using Portly.Authentication.Encryption;
using Portly.Authentication.Handshake;
using Portly.Extensions;
using Portly.Interfaces;
using Portly.Managers;
using Portly.PacketHandling;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Portly
{
    /// <summary>
    /// Represents a TCP-based client responsible for connecting to a server,
    /// performing a Trust-On-First-Use (TOFU) handshake, and sending/receiving packets.
    ///
    /// The client establishes a connection to a remote server, verifies its identity
    /// using <see cref="TrustClient"/> by validating the server's public key fingerprint,
    /// and performs a challenge-response to ensure authenticity.
    ///
    /// After a successful handshake, it continuously listens for incoming packets
    /// using <see cref="PacketHandler"/> and allows sending packets over the connection.
    ///
    /// This implementation does not include encryption but ensures server identity
    /// verification as a foundation for secure communication.
    /// </summary>
    public class PortlyClient
    {
        private readonly TrustClient _trustClient = new();
        private TcpClient? _client;
        private NetworkStream? _stream;
        private int _connected = 0;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private IPacketCrypto? _crypto;

        private readonly KeepAliveManager<PortlyClient> _keepAliveManager = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15),
            async (client) => await client.SendPacketAsync(Packet.Create(PacketType.KeepAlive, Array.Empty<byte>(), false)),
            async (client) => await client.DisconnectAsync());

        /// <summary>
        /// Raised when the client is connected with the server after a succesful handshake.
        /// </summary>
        public event EventHandler? OnConnected;
        /// <summary>
        /// Raised when the client is disconnected from the server.
        /// </summary>
        public event EventHandler? OnDisconnected;

        /// <summary>
        /// Connects asynchronously to a server.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <param name="onPacket"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task ConnectAsync(string host, int port, Func<Packet, Task> onPacket)
        {
            if (Interlocked.CompareExchange(ref _connected, 1, 0) != 0)
                throw new InvalidOperationException("Already connected.");

            TcpClient? client = null;
            NetworkStream? stream = null;
            var cts = new CancellationTokenSource();

            try
            {
                client = new TcpClient();
                await client.ConnectAsync(host, port);
                stream = client.GetStream();

                // --- HANDSHAKE ---
                await PerformHandshakeAsync(stream, host, port);

                // Assign ONLY after successful handshake
                _client = client;
                _stream = stream;
                _cts = cts;

                var token = cts.Token;
                var receiveTask = PacketHandler.ReadPacketsAsync(stream, async packet =>
                {
                    _keepAliveManager.UpdateLastReceived(this);

                    bool flowControl = await OnPacketReceived(packet);
                    if (!flowControl) return;

                    await onPacket(packet);
                }, _crypto, cts.Token);

                // Update initial state
                _keepAliveManager.Register(this);

                var KeepAliveTask = _keepAliveManager.StartAsync(cts.Token);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.WhenAny(receiveTask, KeepAliveTask);
                    }
                    finally
                    {
                        await DisconnectInternalAsync(false);
                    }
                });

                OnConnected?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                try { cts.Cancel(); } catch { }
                try { stream?.Dispose(); } catch { }
                try { client?.Dispose(); } catch { }

                Interlocked.Exchange(ref _connected, 0);
                throw;
            }
        }

        private async Task<bool> OnPacketReceived(Packet packet)
        {
            switch (packet.Identifier.Id)
            {
                case (int)PacketType.KeepAlive:
                    return false;
                case (int)PacketType.Disconnect:
                    await DisconnectInternalAsync(false);
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Sends a packet asynchronously to the connected server.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public async Task SendPacketAsync(Packet packet)
        {
            await SendPacketInternalAsync(_stream, packet);
        }

        /// <summary>
        /// Disconnects asynchronously from the connected server.
        /// </summary>
        /// <returns></returns>
        public async Task DisconnectAsync()
        {
            await DisconnectInternalAsync(true);
        }

        private async Task SendPacketInternalAsync(NetworkStream? stream, Packet packet)
        {
            if (Volatile.Read(ref _connected) == 0)
                throw new InvalidOperationException("Not connected.");

            if (stream == null)
                throw new InvalidOperationException("Stream not available.");

            await _sendLock.WaitAsync();
            try
            {
                await PacketHandler.SendPacketAsync(stream, packet, _crypto);
                _keepAliveManager.UpdateLastSent(this);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task DisconnectInternalAsync(bool sendMessageToServer)
        {
            if (Interlocked.Exchange(ref _connected, 0) == 0)
                return;

            try
            {
                // Try to send a "disconnect" packet first
                if (sendMessageToServer && _stream != null)
                {
                    var disconnectPacket = Packet.Create(PacketType.Disconnect, Array.Empty<byte>(), false);

                    try
                    {
                        await SendPacketAsync(disconnectPacket);
                    }
                    catch
                    {
                        // Ignore send errors — we still want to close
                    }
                }

                if (!sendMessageToServer)
                    Console.WriteLine("You lost connection to the server.");

                // Cancel background tasks (KeepAlive, reading)
                _cts?.Cancel();

                // Dispose the stream and client
                _stream?.Dispose();
                _client?.Dispose();
            }
            finally
            {
                _stream = null;
                _client = null;
                _keepAliveManager.Unregister(this);
                OnDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task PerformHandshakeAsync(NetworkStream stream, string host, int port)
        {
            // 1. Receive server identity public key
            var publicKeyPacket = await PacketHandler.ReceiveSinglePacketAsync(stream, _crypto);
            var publicKey = publicKeyPacket.Payload;

            if (publicKeyPacket.Identifier.Id != (int)PacketType.Handshake || publicKey == null)
                throw new Exception("Invalid handshake packet: " + publicKeyPacket.Identifier.Id);

            if (!_trustClient.VerifyOrTrustServer(host, port, publicKey))
                throw new Exception("Server identity verification failed.");

            // 2. Create ECDH + challenge
            using var keyExchange = new EncryptionKeyExchange();
            byte[] challenge = RandomNumberGenerator.GetBytes(32);

            // 3. Send challenge + client ephemeral key
            var clientHandshake = new ClientHandshake
            {
                Challenge = challenge,
                ClientEphemeralKey = keyExchange.PublicKey
            };

            await SendPacketInternalAsync(stream, Packet<ClientHandshake>.Create(
                PacketType.Handshake,
                clientHandshake,
                false
            ));

            // 4. Receive server response
            var responsePacket = await PacketHandler.ReceiveSinglePacketAsync(stream, _crypto);
            if (responsePacket == null || responsePacket.Identifier.Id != (int)PacketType.Handshake || responsePacket.Payload == null)
                throw new Exception("Invalid handshake response.");

            var response = responsePacket.As<ServerHandshake>();
            if (response.Payload.ServerEphemeralKey.Length == 0)
                throw new Exception("Invalid server key.");

            // 5. Verify signature (binds identity + ECDH)
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);

            byte[] signedData = challenge.Combine(
                keyExchange.PublicKey,
                response.Payload.ServerEphemeralKey
            );

            if (!ecdsa.VerifyData(signedData, response.Payload.Signature, HashAlgorithmName.SHA256))
                throw new Exception("Invalid server signature. Possible MITM attack.");

            // 6. Derive session key
            _crypto = new AesPacketCrypto(keyExchange.DeriveSharedKey(response.Payload.ServerEphemeralKey));
        }
    }
}

using Portly.Core.Authentication.Encryption;
using Portly.Core.Authentication.Handshake;
using Portly.Core.Interfaces;
using Portly.Core.Networking;
using Portly.Core.PacketHandling;
using Portly.Extensions;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Portly.Client
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
    /// using <see cref="PacketProtocol"/> and allows sending packets over the connection.
    ///
    /// This implementation does not include encryption but ensures server identity
    /// verification as a foundation for secure communication.
    /// </summary>
    public abstract class PortlyClientBase : IClient
    {
        private readonly TrustClient _trustClient = new();
        private TcpClient? _client;
        private NetworkStream? _stream;
        private int _connected = 0;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private IPacketCrypto? _crypto;
        private readonly PacketRouter<IClient> _packetRouter = new();

        /// <summary>
        /// The log provider that is used.
        /// </summary>
        public readonly ILogProvider? LogProvider;

        private readonly KeepAliveManager<PortlyClientBase> _keepAliveManager = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15),
            async (client) => await client.SendPacketAsync(Packet.Create(PacketType.KeepAlive, Array.Empty<byte>(), false)),
            async (client) => await client.DisconnectAsync());

        /// <summary>
        /// A router that helps with registering packet handlers to handle packets easily based on their identifiers.
        /// </summary>
        public PacketRouter<IClient> Router => _packetRouter;

        /// <summary>
        /// Raised when a packet is received.
        /// </summary>
        public event EventHandler<Packet>? OnPacketReceived;
        /// <summary>
        /// Raised when the client is connected with the server after a succesful handshake.
        /// </summary>
        public event EventHandler? OnConnected;
        /// <summary>
        /// Raised when the client is disconnected from the server.
        /// </summary>
        public event EventHandler? OnDisconnected;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logProvider"></param>
        internal PortlyClientBase(ILogProvider? logProvider)
        {
            LogProvider = logProvider;
        }

        /// <inheritdoc/>
        public async Task ConnectAsync(string host, int port)
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
                var receiveTask = PacketProtocol.ReadPacketsAsync(stream, async packet =>
                {
                    _keepAliveManager.UpdateLastReceived(this);
                    var task = Router.RouteAsync(this, packet);
                    if (task != null)
                        await task;
                    OnPacketReceived?.Invoke(this, packet);
                }, _crypto, LogProvider, null, cts.Token);

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

        /// <inheritdoc/>
        public async Task SendPacketAsync(Packet packet)
        {
            await SendPacketInternalAsync(_stream, packet);
        }

        /// <inheritdoc/>
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
                await PacketProtocol.SendPacketAsync(stream, packet, _crypto, LogProvider);
                _keepAliveManager.UpdateLastSent(this);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        internal async Task DisconnectInternalAsync(bool sendMessageToServer, string reason = "")
        {
            // Only prevent re-entry at the end, not before sending the packet
            int prev = Interlocked.Exchange(ref _connected, 1); // temporary mark as connected
            if (prev == 0)
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
                {
                    LogProvider?.Log(string.IsNullOrWhiteSpace(reason) ?
                        "You lost connection to the server." : $"You lost connection to the server: {reason}");
                }

                // Cancel background tasks (KeepAlive, reading)
                _cts?.Cancel();

                // Dispose the stream and client
                _stream?.Dispose();
                _client?.Dispose();
            }
            finally
            {
                // Now mark disconnected
                Interlocked.Exchange(ref _connected, 0);

                _stream = null;
                _client = null;
                _keepAliveManager.Unregister(this);
                OnDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task PerformHandshakeAsync(NetworkStream stream, string host, int port)
        {
            // 1. Receive server identity public key
            var publicKeyPacket = await PacketProtocol.ReceiveSinglePacketAsync(stream, _crypto, LogProvider);
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
            var responsePacket = await PacketProtocol.ReceiveSinglePacketAsync(stream, _crypto, LogProvider);
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

using Portly.Core.Authentication.Encryption;
using Portly.Core.Authentication.Handshake;
using Portly.Core.Extensions;
using Portly.Core.Interfaces;
using Portly.Core.Networking;
using Portly.Core.PacketHandling;
using Portly.Core.PacketHandling.Protocols;
using Portly.Core.Serialization;
using Portly.Core.Transports;
using Portly.Core.Utilities;
using System.Security.Cryptography;
using System.Text;

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
    /// using <see cref="IPacketProtocol"/> and allows sending packets over the connection.
    ///
    /// This implementation does not include encryption but ensures server identity
    /// verification as a foundation for secure communication.
    /// </summary>
    public abstract class PortlyClientBase : IClient
    {
        private readonly TrustClient _trustClient = new();
        private readonly IClientTransport _clientTransport;

        private int _connected = 0;     // 0 = disconnected, 1 = connected
        private int _disconnecting = 0; // 0 = not disconnecting, 1 = disconnecting
        private int _cleanupCalled = 0; // 0 = not cleaned, 1 = cleaned

        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        private readonly object _ctsLock = new();

        private CancellationToken Token
        {
            get
            {
                var cts = Volatile.Read(ref _cts);
                if (cts == null)
                    throw new InvalidOperationException("Client not initialized.");
                return cts.Token;
            }
        }

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly PacketRouter<IClient> _packetRouter = new();
        private readonly IPacketProtocol _packetProtocol;
        private readonly Func<byte[], IEncryptionProvider> _encryptionProvider;

        /// <summary>
        /// The log provider that is used.
        /// </summary>
        public readonly ILogProvider? LogProvider;

        private readonly KeepAliveManager<PortlyClientBase> _keepAliveManager = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60),
            async (client) => await client.SendPacketAsync(Packet.Create(PacketType.KeepAlive, Array.Empty<byte>()), false),
            async (client) => await client.DisconnectAsync());

        /// <summary>
        /// A router that helps with registering packet handlers to handle packets easily based on their identifiers.
        /// </summary>
        public PacketRouter<IClient> Router => _packetRouter;

        /// <summary>
        /// Raised when a packet is received.
        /// </summary>
        public event EventHandler<IPacket>? OnPacketReceived;
        /// <summary>
        /// Raised when the client is connected with the server after a succesful handshake.
        /// </summary>
        public event EventHandler? OnConnected;
        /// <summary>
        /// Raised when the client is disconnected from the server.
        /// </summary>
        public event EventHandler? OnDisconnected;

        /// <summary>
        /// Determines if the client is fully connected to the server.
        /// </summary>
        public bool Connected => Volatile.Read(ref _connected) == 1;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="clientTransport"></param>
        /// <param name="packetProtocol"></param>
        /// <param name="packetSerializationProvider"></param>
        /// <param name="encryptionProvider"></param>
        /// <param name="logProvider"></param>
        /// <param name="noDelay">Enable if low latency matters (games, real-time systems, RPC)</param>
        internal PortlyClientBase(
            IClientTransport? clientTransport = null,
            IPacketProtocol? packetProtocol = null,
            IPacketSerializationProvider? packetSerializationProvider = null,
            Func<byte[], IEncryptionProvider>? encryptionProvider = null,
            ILogProvider? logProvider = null, bool noDelay = false)
        {
            var packetSerializer = packetSerializationProvider ?? new MessagePackSerializationProvider();
            _encryptionProvider = encryptionProvider ?? ((sessionKey) => new AESEncryptionProvider(sessionKey));
            _packetProtocol = packetProtocol ?? new LengthPrefixedPacketProtocol(new Core.Configuration.Settings.ConnectionSettings(), packetSerializer, logProvider: logProvider);
            _clientTransport = clientTransport ?? new TcpClientTransport();
            _cts = new CancellationTokenSource();
            LogProvider = logProvider;
        }

        /// <inheritdoc/>
        public async Task ConnectAsync(string host, int port)
        {
            if (Interlocked.CompareExchange(ref _connected, 1, 0) != 0)
                throw new InvalidOperationException("Already connected.");

            var cts = Volatile.Read(ref _cts)!;
            var token = cts.Token;

            try
            {
                await _clientTransport.ConnectAsync(host, port, token);

                await PerformLiteHandshakeAsync();
                await PerformSecureHandshakeAsync(host, port);

                var receiveTask = _packetProtocol.ReadPacketsAsync(_clientTransport.Stream, async packet =>
                {
                    _keepAliveManager.UpdateLastReceived(this);

                    var task = Router.RouteAsync(this, packet);
                    if (task != null)
                        await task;

                    OnPacketReceived?.Invoke(this, packet);
                }, token);

                _keepAliveManager.Register(this);

                var keepAliveTask = _keepAliveManager.StartAsync(token);

                _backgroundTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.WhenAny(receiveTask, keepAliveTask);
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
                Interlocked.Exchange(ref _connected, 0);
                await SafeCleanupAsync();
                throw;
            }
        }

        private async Task SafeCleanupAsync()
        {
            if (Interlocked.Exchange(ref _cleanupCalled, 1) == 1)
                return;

            try
            {
                CancelAndDisposeCts();

                try { await _clientTransport.DisconnectAsync(); } catch { }
                try { await _clientTransport.DisposeAsync(); } catch { }
            }
            catch
            {
                // swallow to guarantee cleanup does not throw
            }
        }

        private void CancelAndDisposeCts()
        {
            var cts = Interlocked.Exchange(ref _cts, null);
            if (cts == null) return;

            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }

        /// <inheritdoc/>
        public async Task SendPacketAsync(IPacket packet, bool encrypt)
        {
            await SendPacketInternalAsync(_clientTransport.Stream, packet, encrypt);
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync()
        {
            await DisconnectInternalAsync(true);
        }

        private async Task SendPacketInternalAsync(Stream? stream, IPacket packet, bool encrypt)
        {
            if (!Connected)
                throw new InvalidOperationException("Not connected.");

            if (stream == null)
                throw new InvalidOperationException("Stream not available.");

            await _sendLock.WaitAsync();
            try
            {
                await _packetProtocol.SendPacketAsync(stream, packet, encrypt, default);
                _keepAliveManager.UpdateLastSent(this);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        internal async Task DisconnectInternalAsync(bool sendMessageToServer, string reason = "")
        {
            if (Interlocked.Exchange(ref _disconnecting, 1) == 1)
                return;

            if (Volatile.Read(ref _connected) == 0)
            {
                Interlocked.Exchange(ref _disconnecting, 0);
                return;
            }

            try
            {
                if (sendMessageToServer)
                {
                    var disconnectPacket = Packet.Create(PacketType.Disconnect, Array.Empty<byte>());

                    try
                    {
                        await SendPacketAsync(disconnectPacket, false);
                    }
                    catch { }
                }
                else
                {
                    LogProvider?.Log(string.IsNullOrWhiteSpace(reason)
                        ? "You lost connection to the server."
                        : $"You lost connection to the server: {reason}");
                }
            }
            finally
            {
                Interlocked.Exchange(ref _connected, 0);

                _keepAliveManager.Unregister(this);

                OnDisconnected?.Invoke(this, EventArgs.Empty);

                await SafeCleanupAsync();

                Interlocked.Exchange(ref _disconnecting, 0);
            }
        }

        private async Task PerformLiteHandshakeAsync()
        {
            // Send protocol version
            await SendPacketInternalAsync(_clientTransport.Stream, Packet.Create(
                PacketType.LiteHandshake,
                new LiteHandshake
                {
                    Protocol = Encoding.UTF8.GetBytes(_packetProtocol.GetType().Name),
                    ProtocolVersion = VersionUtils.ToBytes(_packetProtocol.Version)
                }), false);

            var result = await _packetProtocol.ReceiveSinglePacketAsync(_clientTransport.Stream, default);
            if (result == null || result.Identifier.Id != (int)PacketType.LiteHandshake || result.Payload == null)
                throw new Exception("Invalid lite handshake packet.");

            var payload = ((Packet)result).As<string>().Payload;
            if (payload != "OK")
                throw new Exception(payload ?? "Unable to connect to the server, invalid lite handshake response received.");
        }

        private async Task PerformSecureHandshakeAsync(string host, int port)
        {
            // 1. Receive server identity public key
            var publicKeyPacket = await _packetProtocol.ReceiveSinglePacketAsync(_clientTransport.Stream, default);
            var publicKey = publicKeyPacket.Payload;

            if (publicKeyPacket == null || publicKeyPacket.Identifier.Id != (int)PacketType.SecureHandshake || publicKey == null)
                throw new Exception("Invalid handshake packet.");

            if (!_trustClient.VerifyOrTrustServer(host, port, publicKey))
                throw new Exception("Server identity verification failed.");

            // 2. Create ECDH + challenge
            using var keyExchange = new EncryptionKeyExchange();
            byte[] challenge = RandomNumberGenerator.GetBytes(32);

            // 3. Send challenge + client ephemeral key
            var clientHandshake = new ClientHandshake
            {
                Challenge = challenge,
                ClientEphemeralKey = keyExchange.PublicKey,
                Protocol = Encoding.UTF8.GetBytes(_packetProtocol.GetType().Name),
                ProtocolVersion = VersionUtils.ToBytes(_packetProtocol.Version)
            };

            await SendPacketInternalAsync(_clientTransport.Stream, Packet<ClientHandshake>.Create(
                PacketType.SecureHandshake,
                clientHandshake), false);

            // 4. Receive server response
            var responsePacket = await _packetProtocol.ReceiveSinglePacketAsync(_clientTransport.Stream, default);
            if (responsePacket == null || responsePacket.Identifier.Id != (int)PacketType.SecureHandshake || responsePacket.Payload == null)
                throw new Exception("Invalid handshake response.");

            var response = ((Packet)responsePacket).As<ServerHandshake>();
            if (response.Payload.ServerEphemeralKey.Length == 0)
                throw new Exception("Invalid server key.");

            // 5. Verify signature (binds identity + ECDH)
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);

            byte[] signedData = challenge.Combine(
                publicKey,
                keyExchange.PublicKey,
                response.Payload.ServerEphemeralKey,
                clientHandshake.Protocol,
                clientHandshake.ProtocolVersion
            );

            if (!ecdsa.VerifyData(signedData, response.Payload.Signature, HashAlgorithmName.SHA256))
                throw new Exception("Invalid server signature. Possible MITM attack.");

            // 6. Derive session key
            _packetProtocol.SetEncryptionProvider(_encryptionProvider.Invoke(keyExchange.DeriveSharedKey(response.Payload.ServerEphemeralKey)));
        }
    }
}

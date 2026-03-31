using Portly.Abstractions;
using Portly.Infrastructure;
using Portly.Infrastructure.Configuration;
using Portly.Protocol;
using Portly.Protocol.Processing;
using Portly.Protocol.Serialization;
using Portly.Security.Encryption;
using Portly.Security.Handshake;
using Portly.Security.Trust;
using Portly.Transport;
using Portly.Utilities;
using System.Security.Cryptography;
using System.Text;

namespace Portly.Runtime
{
    /// <summary>
    /// Represents a client responsible for connecting to a server,
    /// performing a Trust-On-First-Use (TOFU) handshake, and sending/receiving packets.
    ///
    /// The client establishes a connection to a remote server, verifies its identity
    /// using <see cref="TrustClient"/> by validating the server's public key fingerprint,
    /// and performs a challenge-response to ensure authenticity.
    ///
    /// After a successful handshake, it continuously listens for incoming packets
    /// using <see cref="IPacketProtocol"/> and allows sending packets over the connection.
    ///
    /// This implementation ensures server identity verification as a foundation for secure communication.
    /// </summary>
    public class PortlyClient : IClient, IAsyncDisposable
    {
        private enum ClientState
        {
            Disconnected,
            Connecting,
            Connected,
            Disconnecting
        }

        private int _state = (int)ClientState.Disconnected;

        private readonly TrustClient _trustClient;
        private IClientTransport? _clientTransport;
        private readonly Func<IClientTransport> _clientTransportFactory;

        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        private Stream? _stream;

        private readonly PacketRouter<IClient> _packetRouter;
        private readonly IPacketProtocol _packetProtocol;
        private readonly Func<byte[], IEncryptionProvider> _encryptionProvider;

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ILogProvider? _logProvider;

        private readonly KeepAliveManager<PortlyClient> _keepAliveManager =
            new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60),
                async (client) => await client.SendPacketAsync(Packet.Create(PacketType.KeepAlive, Array.Empty<byte>()), false),
                async (client) => await client.DisconnectAsync());

        /// <summary>
        /// Router to add automatic packet routing.
        /// </summary>
        public PacketRouter<IClient> Router => _packetRouter;

        /// <summary>
        /// Raised when a packet is received.
        /// </summary>
        public event EventHandler<Packet>? OnPacketReceived;
        /// <summary>
        /// Raised when the client is connected to the server.
        /// </summary>
        public event EventHandler? OnConnected;
        /// <summary>
        /// Raised when the client is disconnected from the server.
        /// </summary>
        public event EventHandler? OnDisconnected;

        /// <summary>
        /// Determines if the client is connected to the server.
        /// </summary>
        public bool IsConnected => Volatile.Read(ref _state) == (int)ClientState.Connected;

        /// <summary>
        /// The id that the server assigned the client.
        /// </summary>
        public Guid ServerClientId { get; private set; }

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

        internal PortlyClient(string? folder = null,
            Func<IClientTransport>? clientTransport = null,
            IPacketProtocol? packetProtocol = null,
            IPacketSerializationProvider? packetSerializationProvider = null,
            Func<byte[], IEncryptionProvider>? encryptionProvider = null,
            ILogProvider? logProvider = null)
        {
            var packetSerializer = packetSerializationProvider ?? new MessagePackSerializationProvider();

            _trustClient = new(folder);
            _encryptionProvider = encryptionProvider ?? (key => new AESEncryptionProvider(key));

            _packetProtocol = packetProtocol ??
                new LengthPrefixedPacketProtocol(
                    new ServerConfiguration(),
                    packetSerializer,
                    logProvider: logProvider);

            _clientTransportFactory = clientTransport ?? (() => new TcpClientTransport());

            _cts = new CancellationTokenSource();

            _logProvider = logProvider;
            _packetRouter = new(logProvider);

            RegisterPredefinedRoutes();
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="clientTransport"></param>
        /// <param name="packetProtocol"></param>
        /// <param name="packetSerializationProvider"></param>
        /// <param name="encryptionProvider"></param>
        /// <param name="logProvider"></param>
        public PortlyClient(
            Func<IClientTransport>? clientTransport = null,
            IPacketProtocol? packetProtocol = null,
            IPacketSerializationProvider? packetSerializationProvider = null,
            Func<byte[], IEncryptionProvider>? encryptionProvider = null,
            ILogProvider? logProvider = null) :
            this(null, clientTransport, packetProtocol, packetSerializationProvider, encryptionProvider, logProvider)
        {

        }

        private void RegisterPredefinedRoutes()
        {
            Router.Register(PacketType.Disconnect, async (client, packet) =>
            {
                string reason = string.Empty;
                if (packet.Payload.Length != 0)
                    reason = ((Packet<string>)packet).Payload;
                await OnServerDisconnectedAsync(reason);
            });
        }

        /// <inheritdoc/>
        public async Task ConnectAsync(string host, int port)
        {
            if (!TryTransition(ClientState.Disconnected, ClientState.Connecting))
            {
                throw new InvalidOperationException("Already connected or connecting.");
            }

            var cts = Volatile.Read(ref _cts)!;
            var token = cts.Token;
            _clientTransport = _clientTransportFactory.Invoke();

            try
            {
                await _clientTransport.ConnectAsync(host, port, token);

                _stream = _clientTransport.Stream;

                await PerformLiteHandshakeAsync(token);
                await PerformSecureHandshakeAsync(host, port, token);

                var receiveTask = _packetProtocol.ReadPacketsAsync(_stream, async packet =>
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
                    catch (Exception ex)
                    {
                        _logProvider?.Log($"Background task error: {ex}");
                    }
                    finally
                    {
                        // Trigger disconnect, but do NOT await it here
                        if (IsConnected)
                            _ = DisconnectInternalAsync(false);
                    }
                });

                Transition(ClientState.Connecting, ClientState.Connected);

                OnConnected?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                await SafeCleanupAsync();

                ResetConnectionState();

                Transition(ClientState.Connecting, ClientState.Disconnected);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync()
        {
            await DisconnectInternalAsync(true);
        }

        /// <summary>
        /// Call this method when the server tells the client to disconnect with a specified reason.
        /// <br>This will properly handle disconnecting async without replying back to the server.</br>
        /// </summary>
        /// <param name="reason"></param>
        /// <returns></returns>
        protected virtual Task OnServerDisconnectedAsync(string reason)
        {
            return DisconnectInternalAsync(false, reason);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (_backgroundTask != null)
            {
                try { await _backgroundTask; } catch { }
            }
            else
            {
                await DisconnectInternalAsync(false);
            }
        }

        private bool TryTransition(ClientState from, ClientState to)
        {
            return Interlocked.CompareExchange(ref _state, (int)to, (int)from) == (int)from;
        }

        private void Transition(ClientState from, ClientState to)
        {
            if (!TryTransition(from, to))
            {
                throw new InvalidOperationException(
                    $"Invalid state transition from {from} to {to}. Current state: {(ClientState)Volatile.Read(ref _state)}");
            }
        }

        internal async Task DisconnectInternalAsync(bool sendMessageToServer, string reason = "")
        {
            var current = (ClientState)Volatile.Read(ref _state);

            if (current == ClientState.Connected)
            {
                if (!TryTransition(ClientState.Connected, ClientState.Disconnecting))
                    return;
            }
            else if (current == ClientState.Connecting)
            {
                if (!TryTransition(ClientState.Connecting, ClientState.Disconnecting))
                    return;
            }
            else
            {
                return;
            }

            try
            {
                if (sendMessageToServer && _stream != null)
                {
                    var packet = Packet.Create(PacketType.Disconnect, Array.Empty<byte>());

                    try
                    {
                        await SendPacketInternalAsync(_stream, packet, false);
                    }
                    catch { }
                }

                if (!sendMessageToServer)
                {
                    _logProvider?.Log(string.IsNullOrWhiteSpace(reason)
                        ? "You lost connection to the server."
                        : $"You lost connection to the server: {reason}");
                }
            }
            finally
            {
                _keepAliveManager.Unregister(this);

                await SafeCleanupAsync();

                ResetConnectionState();

                Transition(ClientState.Disconnecting, ClientState.Disconnected);
                OnDisconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc/>
        public async Task SendPacketAsync(Packet packet, bool encrypt)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Client is not connected.");

            if (_stream == null)
                throw new InvalidOperationException("Stream not available.");

            await _sendLock.WaitAsync();
            try
            {
                await _packetProtocol.SendPacketAsync(_stream, packet, encrypt, Token);
                _keepAliveManager.UpdateLastSent(this);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SendPacketInternalAsync(Stream stream, Packet packet, bool encrypt)
        {
            await _sendLock.WaitAsync();
            try
            {
                await _packetProtocol.SendPacketAsync(stream, packet, encrypt, Token);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task SafeCleanupAsync()
        {
            CancelAndDisposeCts();

            var transport = _clientTransport;
            if (transport != null)
            {
                try { await transport.DisconnectAsync(); } catch { }
                try { await transport.DisposeAsync(); } catch { }
            }
        }

        private void CancelAndDisposeCts()
        {
            var cts = Interlocked.Exchange(ref _cts, null);
            if (cts == null) return;

            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }

        private void ResetConnectionState()
        {
            ServerClientId = Guid.Empty;
            _stream = null;
            _backgroundTask = null;

            // Recreate CTS for next connection
            _cts = new CancellationTokenSource();

            _clientTransport = null!;

            // Reset protocol encryption state
            _packetProtocol.SetEncryptionProvider(null!);
        }

        private async Task PerformLiteHandshakeAsync(CancellationToken token)
        {
            var stream = _stream ?? throw new InvalidOperationException("Stream not initialized.");

            await SendPacketInternalAsync(stream, Packet.Create(
                PacketType.LiteHandshake,
                new LiteHandshake
                {
                    Protocol = Encoding.UTF8.GetBytes(_packetProtocol.GetType().Name),
                    ProtocolVersion = VersionUtils.ToBytes(_packetProtocol.Version)
                }), false);

            var result = await _packetProtocol.ReceiveSinglePacketAsync(stream, token);

            if (result == null || result.Identifier.Id != (int)PacketType.LiteHandshake || result.Payload == null)
                throw new Exception("Invalid lite handshake packet.");

            var payload = result.As<string>().Payload;

            if (payload != "OK")
                throw new Exception(payload ?? "Invalid lite handshake response.");
        }

        private async Task PerformSecureHandshakeAsync(string host, int port, CancellationToken token)
        {
            var stream = _stream ?? throw new InvalidOperationException("Stream not initialized.");

            var publicKeyPacket = await _packetProtocol.ReceiveSinglePacketAsync(stream, token);
            if (publicKeyPacket == null || publicKeyPacket.Identifier.Id != (int)PacketType.SecureHandshake || publicKeyPacket.Payload == null)
                throw new Exception("Invalid handshake packet.");

            var publicKey = publicKeyPacket.Payload;

            if (!await _trustClient.VerifyOrTrustServer(host, port, publicKey))
                throw new Exception("Server identity verification failed.");

            using var keyExchange = new EncryptionKeyExchange();
            byte[] challenge = RandomNumberGenerator.GetBytes(32);

            var clientHandshake = new ClientHandshake
            {
                Challenge = challenge,
                ClientEphemeralKey = keyExchange.PublicKey,
                Protocol = Encoding.UTF8.GetBytes(_packetProtocol.GetType().Name),
                ProtocolVersion = VersionUtils.ToBytes(_packetProtocol.Version)
            };

            await SendPacketInternalAsync(stream, Packet<ClientHandshake>.Create(
                PacketType.SecureHandshake,
                clientHandshake), false);

            var responsePacket = await _packetProtocol.ReceiveSinglePacketAsync(stream, token);

            if (responsePacket == null || responsePacket.Identifier.Id != (int)PacketType.SecureHandshake || responsePacket.Payload == null)
                throw new Exception("Invalid handshake response.");

            var response = responsePacket.As<ServerHandshake>();

            if (response.Payload.ServerEphemeralKey.Length == 0)
                throw new Exception("Invalid server key.");

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
                throw new Exception("Invalid server signature.");

            _packetProtocol.SetEncryptionProvider(
                _encryptionProvider.Invoke(
                    keyExchange.DeriveSharedKey(response.Payload.ServerEphemeralKey)));

            // Assign client id
            ServerClientId = response.Payload.ClientId;
        }
    }
}

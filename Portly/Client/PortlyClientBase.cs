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
    public abstract class PortlyClientBase : IClient, IAsyncDisposable
    {
        private enum ClientState
        {
            Disconnected,
            Connecting,
            Connected,
            Disconnecting
        }

        private int _state = (int)ClientState.Disconnected;

        private readonly TrustClient _trustClient = new();
        private IClientTransport? _clientTransport;
        private readonly Func<IClientTransport> _clientTransportFactory;

        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        private Stream? _stream;

        private readonly PacketRouter<IClient> _packetRouter = new();
        private readonly IPacketProtocol _packetProtocol;
        private readonly Func<byte[], IEncryptionProvider> _encryptionProvider;

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ILogProvider? _logProvider;

        private readonly KeepAliveManager<PortlyClientBase> _keepAliveManager =
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
        public event EventHandler<IPacket>? OnPacketReceived;
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
        public bool Connected => Volatile.Read(ref _state) == (int)ClientState.Connected;

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

        internal PortlyClientBase(
            Func<IClientTransport>? clientTransport = null,
            IPacketProtocol? packetProtocol = null,
            IPacketSerializationProvider? packetSerializationProvider = null,
            Func<byte[], IEncryptionProvider>? encryptionProvider = null,
            ILogProvider? logProvider = null)
        {
            var packetSerializer = packetSerializationProvider ?? new MessagePackSerializationProvider();

            _encryptionProvider = encryptionProvider ?? (key => new AESEncryptionProvider(key));

            _packetProtocol = packetProtocol ??
                new LengthPrefixedPacketProtocol(
                    new Core.Configuration.ServerConfiguration(),
                    packetSerializer,
                    logProvider: logProvider);

            _clientTransportFactory = clientTransport ?? (() => new TcpClientTransport());

            _cts = new CancellationTokenSource();

            _logProvider = logProvider;
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
                        if (Connected)
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
        public async Task SendPacketAsync(IPacket packet, bool encrypt)
        {
            if (!Connected)
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

        private async Task SendPacketInternalAsync(Stream stream, IPacket packet, bool encrypt)
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

            var payload = ((Packet)result).As<string>().Payload;

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

            if (!_trustClient.VerifyOrTrustServer(host, port, publicKey))
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

            var response = ((Packet)responsePacket).As<ServerHandshake>();

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
        }
    }
}

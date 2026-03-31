using Portly.Abstractions;
using Portly.Infrastructure;
using Portly.Infrastructure.Configuration;
using Portly.Infrastructure.Logging;
using Portly.Protocol;
using Portly.Protocol.Processing;
using Portly.Protocol.Serialization;
using Portly.Security.Encryption;
using Portly.Security.Handshake;
using Portly.Security.Trust;
using Portly.Transport;
using Portly.Utilities;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Portly.Tests")]
namespace Portly.Runtime
{
    /// <summary>
    /// Represents a server responsible for accepting client connections,
    /// performing a Trust-On-First-Use (TOFU) handshake, and processing incoming packets.
    ///
    /// The server listens for incoming <see cref="ITransportConnection"/> connections and handles each client
    /// asynchronously. Upon connection, a handshake is performed using <see cref="TrustServer"/> to
    /// establish the server's identity by sending its public key and signing a client-provided challenge.
    ///
    /// After a successful handshake, the server continuously reads and processes packets using
    /// <see cref="IPacketProtocol"/>, enabling efficient, length-prefixed communication over the network.
    ///
    /// This implementation establishes the foundation for secure
    /// communication by verifying server identity during the initial connection phase.
    /// </summary>
    public class PortlyServer
    {
        private readonly IServerTransport _serverTransport;
        private readonly TrustServer _trustServer;
        private readonly CancellationTokenSource _cts;

        private readonly SemaphoreSlim _broadcastSemaphore = new(100);
        private readonly ConcurrentDictionary<Guid, ServerClient> _clients = new();
        private readonly ConcurrentDictionary<IPAddress, int> _connectionsPerIp = new();
        private static readonly HashSet<int> _systemPacketIds = [.. Enum.GetValues<PacketType>().Select(a => (int)a)];
        private static readonly int _highestSystemPacketId = _systemPacketIds.Max();
        private readonly PacketRouter<IServerClient> _packetRouter;

        private readonly KeepAliveManager<ServerClient> _keepAliveManager;
        private readonly IPacketSerializationProvider _packetSerializationProvider;
        private readonly Func<IPacketProtocol> _packetProtocol;
        private readonly Func<byte[], IEncryptionProvider> _encryptionProvider;
        private readonly ILogProvider? _logProvider;

        /// <summary>
        /// A router that helps with registering packet handlers to handle packets easily based on their identifiers.
        /// </summary>
        public PacketRouter<IServerClient> Router => _packetRouter;

        /// <summary>
        /// All connected clients.
        /// </summary>
        public IReadOnlyCollection<IServerClient> ConnectedClients =>
            [.. _clients.Values];

        /// <summary>
        /// Returns the specific client by guid.
        /// </summary>
        /// <param name="clientId"></param>
        /// <returns></returns>
        public IServerClient GetClient(Guid clientId) => _clients[clientId];

        /// <summary>
        /// Populated upon server start.
        /// </summary>
        public EndPoint? LocalEndpoint => _serverTransport.LocalEndPoint;

        /// <summary>
        /// Raised when a packet is received from a client.
        /// </summary>
        public event EventHandler<IServerClient, Packet>? OnPacketReceived;
        /// <summary>
        /// Raised when the server enters a started state.
        /// </summary>
        public event EventHandler? OnServerStarted;
        /// <summary>
        /// Raised when the server enters a stopped state.
        /// </summary>
        public event EventHandler? OnServerStopped;
        /// <summary>
        /// Raised when a client is connected to the server after the handshake is succesful.
        /// </summary>
        public event EventHandler<IServerClient>? OnClientConnected;
        /// <summary>
        /// Raised when a client is disconnected from the server.
        /// </summary>
        public event EventHandler<IServerClient>? OnClientDisconnected;

        /// <summary>
        /// Server configuration
        /// </summary>
        public ServerConfiguration Configuration { get; }

        internal PortlyServer(string? folder = null,
            IServerTransport? serverTransport = null,
            Func<IPacketProtocol>? packetProtocol = null,
            IPacketSerializationProvider? packetSerializationProvider = null,
            Func<byte[], IEncryptionProvider>? encryptionProvider = null,
            ILogProvider? logProvider = null)
        {
            _logProvider = logProvider;
            _packetRouter = new(logProvider);

            Configuration = ServerConfiguration.Load(logProvider: _logProvider, folder: folder);
            Configuration.Validate();

            _packetSerializationProvider = packetSerializationProvider ?? new MessagePackSerializationProvider();
            _encryptionProvider = encryptionProvider ?? ((sessionKey) => new AESEncryptionProvider(sessionKey));
            _packetProtocol = packetProtocol ?? (() => new LengthPrefixedPacketProtocol(Configuration, _packetSerializationProvider, _logProvider));
            _serverTransport = serverTransport ?? new TcpServerTransport(logProvider);
            _trustServer = new TrustServer(folder);
            _cts = new();

            _serverTransport.OnServerStarted += (sender, args) => OnServerStarted?.Invoke(this, EventArgs.Empty);
            _serverTransport.OnServerStopped += (sender, args) => OnServerStopped?.Invoke(this, EventArgs.Empty);

            _keepAliveManager = new(
                TimeSpan.FromSeconds(Configuration.ConnectionSettings.KeepAliveIntervalSeconds),
                TimeSpan.FromSeconds(Configuration.ConnectionSettings.KeepAliveTimeoutSeconds),
                async (serverClient) => await serverClient.SendPacketAsync(Packet.Create(PacketType.KeepAlive, Array.Empty<byte>()), false, _cts.Token),
                async (serverClient) => await serverClient.DisconnectInternalAsync());

            RegisterPredefinedRoutes();
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="serverTransport"></param>
        /// <param name="packetProtocol"></param>
        /// <param name="packetSerializationProvider"></param>
        /// <param name="encryptionProvider"></param>
        /// <param name="logProvider"></param>
        public PortlyServer(
            IServerTransport? serverTransport = null,
            Func<IPacketProtocol>? packetProtocol = null,
            IPacketSerializationProvider? packetSerializationProvider = null,
            Func<byte[], IEncryptionProvider>? encryptionProvider = null,
            ILogProvider? logProvider = null) :
            this(null, serverTransport, packetProtocol, packetSerializationProvider, encryptionProvider, logProvider)
        { }

        private void RegisterPredefinedRoutes()
        {
            Router.Register(PacketType.Disconnect, async (client, packet) => await client.DisconnectAsync(informClient: false));
        }

        /// <summary>
        /// Starts the server asynchronously.
        /// <br>Note: This task runs for as long as the server is running.</br>
        /// </summary>
        /// <param name="ip">The ip used to listen on, if null it will take the IP defined within the server configuration or all interfaces if none defined.</param>
        /// <param name="port">The port to be used, if null it will take the port defined within the server configuration.</param>
        /// <returns></returns>
        public Task StartAsync(IPAddress? ip = null, int? port = null)
        {
            _serverTransport.OnClientAccepted += connection =>
            {
                _ = HandleClientSafeAsync(connection, _cts.Token);
                return Task.CompletedTask;
            };

            port ??= Configuration.ConnectionSettings.Port;

            var ipToUse = ip ?? IPAddress.Any;
            if (ip == null &&
                !string.IsNullOrWhiteSpace(Configuration.ConnectionSettings.IpAddress) &&
                IPAddress.TryParse(Configuration.ConnectionSettings.IpAddress, out var ipAddress))
            {
                ipToUse = ipAddress;
            }

            // Useful logging
            if (IPAddress.Any.Equals(ipToUse) || IPAddress.IPv6Any.Equals(ipToUse))
            {
                _logProvider?.Log($"Server started on all interfaces (port {port})");

                var addresses = Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork);

                foreach (var addr in addresses)
                {
                    _logProvider?.Log($"  -> {addr}:{port}");
                }
            }
            else
            {
                _logProvider?.Log($"Server started on {ipToUse}:{port}");
            }

            _ = _keepAliveManager.StartAsync(_cts.Token);
            return _serverTransport.StartAsync(ipToUse, port.Value, _cts.Token);
        }

        /// <summary>
        /// Stops the server asynchronously.
        /// </summary>
        /// <returns></returns>
        public async Task StopAsync()
        {
            _logProvider?.Log("Stopping server..");
            _cts.Cancel();  // stop accepting new clients

            try
            {
                await _serverTransport.StopAsync();
            }
            catch (Exception ex)
            {
                _logProvider?.Log($"Transport stop error: {ex.Message}", LogLevel.Warning);
            }

            // cancel all client loops
            foreach (var connection in _clients.Values.ToArray())
            {
                // Send disconnection packet before cancel
                try
                {
                    await connection.SendPacketAsync(Packet.Create(PacketType.Disconnect, "Server is shutting down."), false);
                }
                catch (Exception) { }
                connection.Cancellation.Cancel();
            }

            bool graceful = false;
            try
            {
                // Wait for all client tasks to complete, with timeout
                var tasks = _clients.Values
                    .Select(a => a.ClientTask)
                    .Where(t => t is not null)
                    .Cast<Task>()
                    .ToArray();

                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
                graceful = true;
            }
            catch (TimeoutException)
            {
                _logProvider?.Log("Warning: Not all client tasks finished in time, forcing shutdown.", LogLevel.Warning);

                // Forcefully disconnect remaining connections
                foreach (var remaining in _clients.Values.ToArray())
                {
                    try
                    {
                        _logProvider?.Log($"[{remaining.Id}]: Forcibly disconnecting.", LogLevel.Warning);
                        await remaining.DisconnectInternalAsync();
                    }
                    catch (Exception ex)
                    {
                        // ignore exceptions during forced shutdown
                        _logProvider?.Log($"[{remaining.Id}]: Error forcibly disconnecting: {ex.Message}", LogLevel.Error);
                    }
                }
            }

            _clients.Clear();

            _logProvider?.Log(graceful ? "Server stopped gracefully." : "Server stopped.");
        }

        /// <summary>
        /// Sends a packet to all connected clients.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="encrypt"></param>
        /// <returns></returns>
        public async Task SendToClientsAsync(Packet packet, bool encrypt)
        {
            if (IsSystemPacket(packet))
                throw new ArgumentException($"PacketIdentifier \"{packet.Identifier.Id}\" is a reserved id, please use an ID higher than \"{_highestSystemPacketId}\".", nameof(packet));

            var tasks = _clients.Values.ToArray()
                .Select(async client =>
                {
                    await _broadcastSemaphore.WaitAsync();
                    try
                    {
                        await client.SendPacketAsync(packet, encrypt);
                    }
                    catch (Exception)
                    {
                        try { await client.DisconnectInternalAsync(); } catch { }
                    }
                    finally
                    {
                        _broadcastSemaphore.Release();
                    }
                });

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Sends a packet to the specified client.
        /// </summary>
        /// <param name="serverClient"></param>
        /// <param name="packet"></param>
        /// <param name="encrypt"></param>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public async Task SendToClientAsync(IServerClient serverClient, Packet packet, bool encrypt)
        {
            if (IsSystemPacket(packet))
                throw new ArgumentException($"PacketIdentifier \"{packet.Identifier.Id}\" is a reserved id, please use an ID higher than \"{_highestSystemPacketId}\".", nameof(packet));

            if (_clients.TryGetValue(serverClient.Id, out var client))
                await client.SendPacketAsync(packet, encrypt);
            else
                _logProvider?.Log($"[{serverClient.Id}]: Failed to send packet, not connected.", LogLevel.Warning);
        }

        private void OnClientDisconnectedImpl(object? sender, IServerClient connection)
        {
            DecrementConnection(connection.IpAddress);
            _clients.Remove(connection.Id, out _);
            OnClientDisconnected?.Invoke(sender, connection);
        }

        private void DecrementConnection(IPAddress ip)
        {
            while (true)
            {
                if (_connectionsPerIp.TryGetValue(ip, out int current))
                {
                    if (current <= 1)
                    {
                        // Try to remove if count is 0 or 1
                        if (_connectionsPerIp.TryRemove(ip, out _))
                            break; // success
                    }
                    else
                    {
                        // Try to update the value atomically
                        if (_connectionsPerIp.TryUpdate(ip, current - 1, current))
                            break; // success
                    }
                }
                else
                {
                    // No entry exists, nothing to do
                    break;
                }
            }
        }

        private async Task HandleClientAsync(ITransportConnection client, CancellationToken serverToken)
        {
            var connection = new ServerClient(_packetProtocol.Invoke(), Configuration, client, _keepAliveManager, OnClientDisconnectedImpl);
            _clients[connection.Id] = connection;

            _logProvider?.Log($"[{connection.Id}]: Connecting to server..");

            try
            {
                try
                {
                    var (resultLiteHandshake, validReason) = await PerformLiteHandshakeAsync(connection);
                    if (!resultLiteHandshake)
                    {
                        if (validReason != null)
                        {
                            await connection.SendPacketAsync(Packet.Create(PacketType.LiteHandshake, validReason), false, serverToken);
                            await connection.DisconnectInternalAsync();
                            _logProvider?.Log($"[{connection.Id}]: Connection failed ({validReason}).", LogLevel.Warning);
                            return;
                        }
                        else
                        {
                            await connection.DisconnectInternalAsync();
                            _logProvider?.Log($"[{connection.Id}]: Connection failed (handshake rejected).", LogLevel.Warning);
                            return;
                        }
                    }
                    else
                    {
                        await connection.SendPacketAsync(Packet.Create(PacketType.LiteHandshake, "OK"), false);
                    }

                    if (!await PerformSecureHandshakeAsync(connection))
                    {
                        await connection.DisconnectInternalAsync();
                        _logProvider?.Log($"[{connection.Id}]: Connection failed (handshake rejected).", LogLevel.Warning);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    await connection.DisconnectInternalAsync();
                    _logProvider?.Log($"[{connection.Id}]: Connection failed (handshake error: {ex.Message})", LogLevel.Error);
                    return;
                }

                _logProvider?.Log($"[{connection.Id}]: Connected sucessfully.");

                // Start loops
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken, connection.Cancellation.Token);

                connection.ClientTask = Task.Run(async () =>
                {
                    var remoteEndpoint = connection.Connection.RemoteEndPoint;
                    try
                    {
                        // Incrmeent ip connection count
                        _connectionsPerIp.AddOrUpdate(connection.IpAddress, 1, (_, current) => current + 1);
                        // Register connection
                        _keepAliveManager.Register(connection);

                        await connection.PacketProtocol.ReadPacketsAsync(connection.Stream, async packet =>
                            {
                                _keepAliveManager.UpdateLastReceived(connection);
                                if (packet.Identifier.Id != (int)PacketType.KeepAlive)
                                {
                                    await HandlePacketAsync(connection, packet);
                                }
                            }, linkedCts.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logProvider?.Log($"[{connection.Id}]: {ex.Message}", LogLevel.Error);
                    }
                    finally
                    {
                        await connection.DisconnectInternalAsync();
                        _clients.TryRemove(connection.Id, out _);
                        _logProvider?.Log($"[{connection.Id}]: Disconnected.", LogLevel.Info);
                    }
                }, CancellationToken.None);

                OnClientConnected?.Invoke(this, connection);
            }
            catch (Exception ex)
            {
                void PrintException(Exception e, int level = 0)
                {
                    var indent = new string(' ', level * 2);
                    _logProvider?.Log($"{indent}{e.GetType().Name}: {e.Message}", LogLevel.Error);
                    _logProvider?.Log($"{indent}{e.StackTrace}", LogLevel.Error);
                    if (e.InnerException != null)
                        PrintException(e.InnerException, level + 1);
                }

                _logProvider?.Log($"[{connection.Id}]: Exception:", LogLevel.Error);
                PrintException(ex);
            }
        }

        private async Task HandleClientSafeAsync(ITransportConnection connection, CancellationToken token)
        {
            if (_cts.IsCancellationRequested)
            {
                try { await connection.CloseAsync(); } catch { }
                return;
            }

            try
            {
                await HandleClientAsync(connection, token);
            }
            catch (Exception ex)
            {
                _logProvider?.Log($"Unhandled client error: {ex.Message}", LogLevel.Error);

                try { await connection.CloseAsync(); } catch { }
            }
        }

        private (bool allowed, string? reason) ValidateIpAddress(IPAddress ip)
        {
            // If whitelist has entries -> ONLY allow those
            if (Configuration.IpWhitelist.Count > 0)
            {
                if (!Configuration.IpWhitelist.Contains(ip))
                    return (false, $"IP {ip} is not whitelisted.");

                return (true, null);
            }

            // Otherwise use banlist (allow all except banned)
            if (Configuration.IpBlacklist.TryGetValue(ip, out var expireTime))
            {
                if (expireTime > DateTime.UtcNow)
                {
                    return (false, $"IP {ip} is banned.");
                }

                // Remove from blacklist
                Configuration.IpBlacklist.TryRemove(ip, out _);
                Configuration.Save(logProvider: _logProvider);
            }

            return (true, null);
        }

        private async Task<(bool result, string? validReason)> PerformLiteHandshakeAsync(ServerClient connection)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Configuration.ConnectionSettings.ConnectTimeoutSeconds));

            Packet? packet;
            try
            {
                packet = await connection.PacketProtocol.ReceiveSinglePacketAsync(
                    connection.Stream,
                    cts.Token
                );
            }
            catch (OperationCanceledException)
            {
                return (false, "Handshake timed out.");
            }
            catch (Exception ex)
            {
                return (false, $"Handshake error: {ex.Message}");
            }

            if (packet == null || packet.Identifier.Id != (int)PacketType.LiteHandshake || packet.Payload == null)
                return (false, null);

            // Protocol + version check
            var liteHandshake = packet.As<LiteHandshake>();
            var liteHandshakePayload = liteHandshake.Payload;

            var protocol = Encoding.UTF8.GetString(liteHandshakePayload.Protocol);
            if (protocol != connection.PacketProtocol.GetType().Name)
                return (false, $"Protocol mismatch (Received: {protocol} | Expected: {connection.PacketProtocol.GetType().Name}).");
            var protocolVersion = VersionUtils.FromBytes(liteHandshakePayload.ProtocolVersion);
            if (protocolVersion != connection.PacketProtocol.Version)
                return (false, $"Protocol version mismatch (Received: {protocolVersion} | Expected: {connection.PacketProtocol.Version}).");

            // Max connections
            if (_clients.Count >= Configuration.ConnectionSettings.MaxConnections)
                return (false, "Server is full.");

            if (connection.Connection.RemoteEndPoint is not IPEndPoint remoteEndPoint)
                return (false, "Unable to determine client IP.");

            var clientIp = remoteEndPoint.Address.MapToIPv6();

            // Verify IP against whitelist/banlist
            var (ipAllowed, ipReason) = ValidateIpAddress(clientIp);
            if (!ipAllowed)
                return (false, ipReason);

            // Verify max connections per ip
            _connectionsPerIp.TryGetValue(clientIp, out int connectionsFromIp);
            if (connectionsFromIp >= Configuration.ConnectionSettings.MaxConnectionsPerIp)
                return (false, $"Too many connections from {clientIp.MapToIPv4()}");

            return (true, null);
        }

        private async Task<bool> PerformSecureHandshakeAsync(ServerClient connection)
        {
            // 1. Send server identity public key
            byte[] publicKey = _trustServer.GetPublicKey();

            await connection.SendPacketAsync(Packet.Create(PacketType.SecureHandshake, publicKey), false);

            // 2. Receive client handshake
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Configuration.ConnectionSettings.ConnectTimeoutSeconds));

            Packet? requestPacket;
            try
            {
                requestPacket = await connection.PacketProtocol.ReceiveSinglePacketAsync(
                    connection.Stream,
                    cts.Token
                );
            }
            catch (OperationCanceledException)
            {
                return false; // timeout
            }

            if (requestPacket == null || requestPacket.Identifier.Id != (int)PacketType.SecureHandshake || requestPacket.Payload == null)
                return false;

            var request = requestPacket.As<ClientHandshake>();

            if (request.Payload.Challenge == null || request.Payload.Challenge.Length == 0 ||
                request.Payload.ClientEphemeralKey == null || request.Payload.ClientEphemeralKey.Length == 0)
                return false;

            // 3. Create ECDH key exchange
            using var keyExchange = new EncryptionKeyExchange();

            // 4. Build signed data
            byte[] signedData = request.Payload.Challenge.Combine(
                publicKey,
                request.Payload.ClientEphemeralKey,
                keyExchange.PublicKey,
                request.Payload.Protocol,
                request.Payload.ProtocolVersion
            );

            // 5. Sign (binds identity + key exchange)
            byte[] signature = _trustServer.SignChallenge(signedData);

            // 6. Send response
            var response = new ServerHandshake
            {
                ServerEphemeralKey = keyExchange.PublicKey,
                Signature = signature,
                ClientId = connection.Id
            };

            await connection.SendPacketAsync(Packet<ServerHandshake>.Create(
                PacketType.SecureHandshake,
                response
            ), false);

            // 7. Derive session key
            connection.PacketProtocol.SetEncryptionProvider(_encryptionProvider.Invoke(keyExchange.DeriveSharedKey(request.Payload.ClientEphemeralKey)));

            return true;
        }

        private async Task HandlePacketAsync(ServerClient connection, Packet packet)
        {
            // Rate limit non-system packets
            if (!IsSystemPacket(packet) && !connection.ClientRateLimiter.TryConsume(connection.IpAddress, packet.Payload.Length, out bool banned))
            {
                if (banned)
                    _logProvider?.Log($"[{connection.Id}]: IP {connection.IpAddress} has been banned due to repeated violations.", LogLevel.Warning);
                else
                    _logProvider?.Log($"[{connection.Id}]: rate limit exceeded, client was forcibly disconnected.", LogLevel.Warning);

                await connection.DisconnectAsync("Rate limit exceeded.");
                return;
            }

            var task = Router.RouteAsync(connection, packet);
            if (task != null)
                await task;

            OnPacketReceived?.Invoke(connection, packet);
        }

        private static bool IsSystemPacket(Packet packet)
            => _systemPacketIds.Contains(packet.Identifier.Id);
    }
}
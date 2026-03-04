using Portly.Core.Authentication.Encryption;
using Portly.Core.Authentication.Handshake;
using Portly.Core.Configuration;
using Portly.Core.Extensions;
using Portly.Core.Interfaces;
using Portly.Core.Networking;
using Portly.Core.PacketHandling;
using Portly.Core.PacketHandling.Protocols;
using Portly.Core.Utilities;
using Portly.Core.Utilities.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Portly.Server
{
    /// <summary>
    /// Use <see cref="PortlyServer"/> implementation instead.
    /// </summary>
    public abstract class PortlyServerBase
    {
        private readonly TcpListener _listener;
        private readonly TrustServer _trustServer;
        private readonly CancellationTokenSource _cts;

        private readonly SemaphoreSlim _broadcastSemaphore = new(100);
        private readonly ConcurrentDictionary<Guid, ServerClient> _clients = new();
        private readonly ConcurrentDictionary<IPAddress, int> _connectionsPerIp = new();
        private static readonly HashSet<int> _systemPacketIds = [.. Enum.GetValues<PacketType>().Select(a => (int)a)];
        private readonly PacketRouter<IServerClient> _packetRouter = new();

        private readonly KeepAliveManager<ServerClient> _keepAliveManager;

        private readonly int _port;
        private readonly IPacketProtocol _packetProtocol;

        /// <summary>
        /// The log provider that is used.
        /// </summary>
        public readonly ILogProvider? LogProvider;

        /// <summary>
        /// A router that helps with registering packet handlers to handle packets easily based on their identifiers.
        /// </summary>
        public PacketRouter<IServerClient> Router => _packetRouter;

        /// <summary>
        /// All connected clients.
        /// </summary>
        public IReadOnlyCollection<IServerClient> ConnectedClients =>
            _clients.Values.Cast<IServerClient>().ToList().AsReadOnly();

        /// <summary>
        /// Raised when a packet is received from a client.
        /// </summary>
        public event EventHandler<IServerClient, Packet>? OnPacketReceived;
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

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="port"></param>
        /// <param name="configuration"></param>
        /// <param name="packetProtocol"></param>
        /// <param name="logProvider"></param>
        internal PortlyServerBase(int port, ServerConfiguration? configuration = null, IPacketProtocol? packetProtocol = null, ILogProvider? logProvider = null)
        {
            LogProvider = logProvider;
            Configuration = configuration ?? ServerConfiguration.Load(logProvider: LogProvider);
            Configuration.Validate();

            _port = port;
            _packetProtocol = packetProtocol ?? new DefaultPacketProtocol(Configuration.ConnectionSettings, logProvider);
            _listener = new TcpListener(IPAddress.Any, _port);
            _trustServer = new TrustServer();
            _cts = new();

            _keepAliveManager = new(
                TimeSpan.FromSeconds(Configuration.ConnectionSettings.KeepAliveIntervalSeconds),
                TimeSpan.FromSeconds(Configuration.ConnectionSettings.KeepAliveTimeoutSeconds),
                async (serverClient) => await serverClient.SendPacketAsync(Packet.Create(PacketType.KeepAlive, Array.Empty<byte>(), false)),
                async (serverClient) => await serverClient.DisconnectInternalAsync());
        }

        /// <summary>
        /// Starts the server asynchronously.
        /// </summary>
        /// <returns></returns>
        public async Task StartAsync()
        {
            _listener.Start(Configuration.ConnectionSettings.MaxPendingConnectionBacklog);
            LogProvider?.Log($"Server started on port {_port}.");

            _ = Task.Run(async () =>
            {
                try
                {
                    _ = _keepAliveManager.StartAsync(_cts.Token);
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                        if (Configuration.ConnectionSettings.NoTcpDelay)
                            client.NoDelay = true;
                        _ = HandleClientAsync(client, _cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        /// <summary>
        /// Stops the server asynchronously.
        /// </summary>
        /// <returns></returns>
        public async Task StopAsync()
        {
            LogProvider?.Log("Stopping server..");
            _cts.Cancel();  // stop accepting new clients
            _listener.Stop();

            // cancel all client loops
            foreach (var connection in _clients.Values.ToArray())
            {
                // Send disconnection packet before cancel
                try
                {
                    await connection.SendPacketAsync(Packet.Create(PacketType.Disconnect, "Server is shutting down.", false));
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
                LogProvider?.Log("Warning: Not all client tasks finished in time, forcing shutdown.", LogLevel.Warning);

                // Forcefully disconnect remaining connections
                foreach (var remaining in _clients.Values.ToArray())
                {
                    try
                    {
                        LogProvider?.Log($"[{remaining.Id}]: Forcibly disconnecting.", LogLevel.Warning);
                        await remaining.DisconnectInternalAsync();
                    }
                    catch (Exception ex)
                    {
                        // ignore exceptions during forced shutdown
                        LogProvider?.Log($"[{remaining.Id}]: Error forcibly disconnecting: {ex.Message}", LogLevel.Error);
                    }
                }
            }

            _clients.Clear();

            LogProvider?.Log(graceful ? "Server stopped gracefully." : "Server stopped.");
        }

        /// <summary>
        /// Sends a packet to all connected clients.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public async Task SendToClientsAsync(Packet packet)
        {
            var tasks = _clients.Values.ToArray()
                .Select(async client =>
                {
                    await _broadcastSemaphore.WaitAsync();
                    try
                    {
                        await client.SendPacketAsync(packet);
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
        /// Sends a packet with a generic payload object to all connected clients.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public async Task SendToClientsAsync<T>(Packet<T> packet)
        {
            await SendToClientsAsync((Packet)packet);
        }

        /// <summary>
        /// Sends a packet to the specified client.
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="packet"></param>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public async Task SendToClientAsync(Guid clientId, Packet packet)
        {
            if (_clients.TryGetValue(clientId, out var client))
                await client.SendPacketAsync(packet);
            else
                LogProvider?.Log($"[{clientId}]: Failed to send packet, not connected.", LogLevel.Warning);
        }

        /// <summary>
        /// Sends a packet with a generic payload object to the specified client.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="clientId"></param>
        /// <param name="packet"></param>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public async Task SendToClientAsync<T>(Guid clientId, Packet<T> packet)
        {
            await SendToClientAsync(clientId, (Packet)packet);
        }

        private void OnClientDisconnectedImpl(object? sender, IServerClient connection)
        {
            DecrementConnection(connection.IpAddress);
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

        private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
        {
            var connection = new ServerClient(_packetProtocol, Configuration, client, _keepAliveManager, OnClientDisconnectedImpl);
            _clients[connection.Id] = connection;

            LogProvider?.Log($"[{connection.Id}]: Connecting to server..");

            try
            {
                try
                {
                    var (resultLiteHandshake, validReason) = await PerformLiteHandshakeAsync(connection);
                    if (!resultLiteHandshake)
                    {
                        if (validReason != null)
                        {
                            await connection.SendPacketAsync(Packet.Create(PacketType.LiteHandshake, validReason, false));
                            await connection.DisconnectInternalAsync();
                            LogProvider?.Log($"[{connection.Id}]: Connection failed ({validReason}).", LogLevel.Warning);
                            return;
                        }
                        else
                        {
                            await connection.DisconnectInternalAsync();
                            LogProvider?.Log($"[{connection.Id}]: Connection failed (handshake rejected).", LogLevel.Warning);
                            return;
                        }
                    }
                    else
                    {
                        await connection.SendPacketAsync(Packet.Create(PacketType.LiteHandshake, "OK", false));
                    }

                    if (!await PerformSecureHandshakeAsync(connection))
                    {
                        await connection.DisconnectInternalAsync();
                        LogProvider?.Log($"[{connection.Id}]: Connection failed (handshake rejected).", LogLevel.Warning);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    await connection.DisconnectInternalAsync();
                    LogProvider?.Log($"[{connection.Id}]: Connection failed (handshake error: {ex.Message})", LogLevel.Error);
                    return;
                }

                LogProvider?.Log($"[{connection.Id}]: Connected sucessfully.");

                // Start loops
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken, connection.Cancellation.Token);

                var clientTask = Task.Run(async () =>
                {
                    var remoteEndpoint = connection.TcpClient.Client.RemoteEndPoint;
                    try
                    {
                        // Incrmeent ip connection count
                        _connectionsPerIp.AddOrUpdate(connection.IpAddress, 1, (_, current) => current + 1);
                        // Register connection
                        _keepAliveManager.Register(connection);

                        await _packetProtocol.ReadPacketsAsync(connection.Stream, async packet =>
                            {
                                _keepAliveManager.UpdateLastReceived(connection);
                                await HandlePacketAsync(connection, packet);
                            }, linkedCts.Token, connection.EncryptionProvider);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        LogProvider?.Log($"[{connection.Id}]: Error: {ex.Message}", LogLevel.Error);
                    }
                    finally
                    {
                        await connection.DisconnectInternalAsync();
                        _clients.TryRemove(connection.Id, out _);
                        LogProvider?.Log($"[{connection.Id}]: Disconnected.", LogLevel.Info);
                    }
                }, CancellationToken.None);

                OnClientConnected?.Invoke(this, connection);
            }
            catch (Exception ex)
            {
                void PrintException(Exception e, int level = 0)
                {
                    var indent = new string(' ', level * 2);
                    LogProvider?.Log($"{indent}{e.GetType().Name}: {e.Message}", LogLevel.Error);
                    LogProvider?.Log($"{indent}{e.StackTrace}", LogLevel.Error);
                    if (e.InnerException != null)
                        PrintException(e.InnerException, level + 1);
                }

                LogProvider?.Log($"[{connection.Id}]: Exception:", LogLevel.Error);
                PrintException(ex);
            }
        }

        private (bool allowed, string? reason) ValidateIpAddress(IPAddress ip)
        {
            // If whitelist has entries → ONLY allow those
            if (Configuration.IpWhitelist.Count > 0)
            {
                if (!Configuration.IpWhitelist.Contains(ip))
                    return (false, $"IP {ip} is not whitelisted.");

                return (true, null);
            }

            // Otherwise use banlist (allow all except banned)
            if (Configuration.IpBlacklist.Contains(ip))
                return (false, $"IP {ip} is banned.");

            return (true, null);
        }

        private async Task<(bool result, string? validReason)> PerformLiteHandshakeAsync(ServerClient connection)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Configuration.ConnectionSettings.ConnectTimeoutSeconds));

            Packet? packet;
            try
            {
                packet = await _packetProtocol.ReceiveSinglePacketAsync(
                    connection.Stream,
                    cts.Token,
                    connection.EncryptionProvider
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
            var liteHandshake = packet.As<LiteHandshake>().Payload;
            var protocol = Encoding.UTF8.GetString(liteHandshake.Protocol);
            if (protocol != _packetProtocol.GetType().Name)
                return (false, $"Protocol mismatch (Received: {protocol} | Expected: {_packetProtocol.GetType().Name}).");
            var protocolVersion = VersionUtils.FromBytes(liteHandshake.ProtocolVersion);
            if (protocolVersion != _packetProtocol.Version)
                return (false, $"Protocol version mismatch (Received: {protocolVersion} | Expected: {_packetProtocol.Version}).");

            // Max connections
            if (_clients.Count >= Configuration.ConnectionSettings.MaxConnections)
                return (false, "Server is full.");

            // Verify if tcp client
            var remoteEndPoint = connection.TcpClient.Client.RemoteEndPoint as IPEndPoint;
            if (remoteEndPoint == null)
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

            await connection.SendPacketAsync(Packet.Create(PacketType.SecureHandshake, publicKey, false));

            // 2. Receive client handshake
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Configuration.ConnectionSettings.ConnectTimeoutSeconds));

            Packet? requestPacket;
            try
            {
                requestPacket = await _packetProtocol.ReceiveSinglePacketAsync(
                    connection.Stream,
                    cts.Token,
                    connection.EncryptionProvider
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
                Signature = signature
            };

            await connection.SendPacketAsync(Packet<ServerHandshake>.Create(
                PacketType.SecureHandshake,
                response,
                false
            ));

            // 7. Derive session key
            connection.EncryptionProvider = new AESEncryptionProvider(keyExchange.DeriveSharedKey(request.Payload.ClientEphemeralKey));

            return true;
        }

        private async Task HandlePacketAsync(ServerClient connection, Packet packet)
        {
            // Rate limit non-system packets
            if (!IsSystemPacket(packet) && !connection.ClientRateLimiter.TryConsume(packet.Payload.Length))
            {
                LogProvider?.Log($"[{connection.Id}]: rate limit exceeded, client was forcibly disconnected.", LogLevel.Warning);
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
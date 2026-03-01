using Portly.Core.Authentication.Encryption;
using Portly.Core.Authentication.Handshake;
using Portly.Core.Extensions;
using Portly.Core.PacketHandling;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Portly.Core.Server
{
    /// <summary>
    /// Represents a TCP-based server responsible for accepting client connections,
    /// performing a Trust-On-First-Use (TOFU) handshake, and processing incoming packets.
    ///
    /// The server listens for incoming <see cref="TcpClient"/> connections and handles each client
    /// asynchronously. Upon connection, a handshake is performed using <see cref="TrustServer"/> to
    /// establish the server's identity by sending its public key and signing a client-provided challenge.
    ///
    /// After a successful handshake, the server continuously reads and processes packets using
    /// <see cref="PacketHandler"/>, enabling efficient, length-prefixed communication over the network.
    ///
    /// This implementation does not include encryption, but establishes the foundation for secure
    /// communication by verifying server identity during the initial connection phase.
    /// </summary>
    public class PortlyServer
    {
        private readonly TcpListener _listener;
        private readonly TrustServer _trustServer;
        private readonly CancellationTokenSource _cts;
        private readonly int _port;

        private readonly SemaphoreSlim _broadcastSemaphore = new(100);
        private readonly ConcurrentDictionary<Guid, ServerClient> _clients = new();
        private readonly ConcurrentDictionary<Guid, Task> _clientTasks = new();

        /// <summary>
        /// All connected clients.
        /// </summary>
        public IReadOnlyCollection<IServerClient> ConnectedClients =>
            _clients.Values.Cast<IServerClient>().ToList().AsReadOnly();

        public event EventHandler<Guid>? OnClientConnected, OnClientDisconnected;

        public PortlyServer(int port)
        {
            _port = port;
            _listener = new TcpListener(IPAddress.Any, _port);
            _trustServer = new TrustServer();
            _cts = new();
        }

        public async Task StartAsync()
        {
            _listener.Start();
            Console.WriteLine($"Server started on port \'{_port}\'.");

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                        _ = HandleClientAsync(client, _cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        public async Task StopAsync()
        {
            Console.WriteLine("Stopping server..");
            _cts.Cancel();  // stop accepting new clients
            _listener.Stop();

            // cancel all client loops
            foreach (var connection in _clients.Values.ToArray())
            {
                // Send disconnection packet before cancel
                await connection.SendPacketAsync(Packet.Create<byte[]>(new(PacketType.Disconnect), [], false));
                connection.Cancellation.Cancel();
            }

            bool graceful = false;
            try
            {
                // Wait for all client tasks to complete, with timeout
                await Task.WhenAll(_clientTasks.Values).WaitAsync(TimeSpan.FromSeconds(10));
                graceful = true;
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Warning: Not all client tasks finished in time, forcing shutdown.");

                // Forcefully disconnect remaining connections
                foreach (var remaining in _clients.Values.ToArray())
                {
                    try
                    {
                        Console.WriteLine($"Forcibly disconnecting [{(remaining.Client?.Client?.RemoteEndPoint?.ToString() ?? "Unknown")}]");
                        remaining.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        // ignore exceptions during forced shutdown
                        Console.WriteLine($"Error forcibly disconnecting [{(remaining.Client?.Client?.RemoteEndPoint?.ToString() ?? "Unknown")}]: {ex.Message}");
                    }
                }
            }

            _clients.Clear();
            _clientTasks.Clear();

            Console.WriteLine(graceful ? "Server stopped gracefully." : "Server stopped.");
        }

        /// <summary>
        /// Sends a packet to all connected clients.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public async Task SendToClientsAsync(Packet packet)
        {
            var tasks = ConnectedClients
                .Select(async client =>
            {
                await _broadcastSemaphore.WaitAsync();
                try
                {
                    await client.SendPacketAsync(packet);
                }
                catch (Exception)
                {
                    try { client.Disconnect(); } catch { }
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
                throw new KeyNotFoundException($"Client {clientId} not connected.");
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

        private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
        {
            var connection = new ServerClient(client, OnClientDisconnected);
            _clients[connection.Id] = connection;

            var remoteEndpoint = client.Client.RemoteEndPoint;
            Console.WriteLine($"[{remoteEndpoint}]: Client connected.");

            try
            {
                // Handshake
                bool trusted = await PerformHandshakeAsync(connection);
                if (!trusted)
                {
                    Console.WriteLine($"[{remoteEndpoint}]: Handshake failed");
                    connection.Disconnect();
                    return;
                }

                Console.WriteLine($"[{remoteEndpoint}]: Handshake successful");

                // Start loops
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken, connection.Cancellation.Token);

                var clientTask = Task.Run(async () =>
                {
                    var remoteEndpoint = connection.Client.Client.RemoteEndPoint;
                    try
                    {
                        await Task.WhenAny(
                            PacketHandler.ReadPacketsAsync(connection.Stream, async packet =>
                            {
                                connection.LastReceived = DateTime.UtcNow;
                                if (packet.Identifier.Id != (int)PacketType.Heartbeat)
                                    await HandlePacketAsync(connection, packet);
                            }, connection.Crypto, linkedCts.Token),
                            HeartbeatLoop(connection, linkedCts.Token)
                        );
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{remoteEndpoint}]: Error: {ex.Message}");
                    }
                    finally
                    {
                        connection.Disconnect();
                        _clients.TryRemove(connection.Id, out _);
                        _clientTasks.TryRemove(connection.Id, out _);
                        Console.WriteLine($"[{remoteEndpoint}]: Disconnected.");
                    }
                }, CancellationToken.None);

                _clientTasks[connection.Id] = clientTask;

                OnClientConnected?.Invoke(this, connection.Id);
            }
            catch (Exception ex)
            {
                static void PrintException(Exception e, int level = 0)
                {
                    var indent = new string(' ', level * 2);
                    Console.WriteLine($"{indent}{e.GetType().Name}: {e.Message}");
                    Console.WriteLine($"{indent}{e.StackTrace}");
                    if (e.InnerException != null)
                        PrintException(e.InnerException, level + 1);
                }

                Console.WriteLine($"[{remoteEndpoint}] Exception caught:");
                PrintException(ex);
            }
        }

        private async Task<bool> PerformHandshakeAsync(ServerClient connection)
        {
            // 1. Send server identity public key (unchanged)
            byte[] publicKey = _trustServer.GetPublicKey();

            await connection.SendPacketAsync(Packet.Create<byte[]>(new(PacketType.Handshake), publicKey, false));

            // 2. Receive client handshake (UPDATED)
            var requestPacket = await PacketHandler.ReceiveSinglePacketAsync(connection.Stream, connection.Crypto);
            if (requestPacket == null || requestPacket.Identifier.Id != (int)PacketType.Handshake || requestPacket.Payload == null)
                return false;

            var request = requestPacket.As<ClientHandshake>();

            if (request.PayloadObj.Challenge == null || request.PayloadObj.Challenge.Length == 0 ||
                request.PayloadObj.ClientEphemeralKey == null || request.PayloadObj.ClientEphemeralKey.Length == 0)
                return false;

            // 3. Create ECDH key exchange
            using var keyExchange = new EncryptionKeyExchange();

            // 4. Build signed data
            byte[] signedData = request.PayloadObj.Challenge.Combine(
                request.PayloadObj.ClientEphemeralKey,
                keyExchange.PublicKey
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
                new(PacketType.Handshake),
                response,
                false
            ));

            // 7. Derive session key
            connection.Crypto = new AesPacketCrypto(keyExchange.DeriveSharedKey(request.PayloadObj.ClientEphemeralKey));

            return true;
        }

        private static async Task HeartbeatLoop(ServerClient connection, CancellationToken token)
        {
            var interval = TimeSpan.FromSeconds(5);
            var timeout = TimeSpan.FromSeconds(15);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(interval, token);

                    if (DateTime.UtcNow - connection.LastSent > interval)
                    {
                        await connection.SendPacketAsync(Packet.Create<byte[]>(new(PacketType.Heartbeat), [], false));

                        connection.LastSent = DateTime.UtcNow;
                    }

                    if (DateTime.UtcNow - connection.LastReceived > timeout)
                    {
                        Console.WriteLine($"[{(connection.Client?.Client?.RemoteEndPoint?.ToString() ?? "Unknown")}]: Timed out");
                        connection.Disconnect();
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                connection.Disconnect();
            }
        }

        private static async Task HandlePacketAsync(ServerClient connection, Packet packet)
        {
            // Handle system packets
            switch (packet.Identifier.Id)
            {
                case (int)PacketType.Disconnect:
                    connection.Disconnect();
                    break;
            }

            await Task.CompletedTask;
        }
    }
}

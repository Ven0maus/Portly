using Portly.Core.Authentication.Handshake;
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

        private readonly ConcurrentDictionary<TcpClient, ClientConnection> _clients = new();
        private readonly ConcurrentDictionary<ClientConnection, Task> _clientTasks = new();

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

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = HandleClientAsync(client, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
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
                await connection.SendPacketAsync(new Packet
                {
                    Type = PacketType.Disconnect,
                    Payload = []
                });
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
                        Console.WriteLine($"Forcibly disconnecting [{remaining.Client.Client.RemoteEndPoint}]");
                        remaining.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        // ignore exceptions during forced shutdown
                        Console.WriteLine($"Error forcibly disconnecting [{remaining.Client.Client.RemoteEndPoint}]: {ex.Message}");
                    }
                }
            }

            _clients.Clear();
            _clientTasks.Clear();

            Console.WriteLine(graceful ? "Server stopped gracefully." : "Server stopped.");
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
        {
            var connection = new ClientConnection(client);
            _clients.TryAdd(client, connection);

            Console.WriteLine($"[{client.Client.RemoteEndPoint}]: Client connected.");

            try
            {
                // Handshake
                bool trusted = await PerformHandshakeAsync(connection);
                if (!trusted)
                {
                    Console.WriteLine($"[{client.Client.RemoteEndPoint}]: Handshake failed");
                    connection.Disconnect();
                    return;
                }

                Console.WriteLine($"[{client.Client.RemoteEndPoint}]: Handshake successful");

                // Start loops
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken, connection.Cancellation.Token);

                var clientTask = Task.Run(async () =>
                {
                    var endpoint = connection.Client.Client.RemoteEndPoint;
                    try
                    {
                        await Task.WhenAny(
                            PacketHandler.ReadPacketsAsync(connection.Stream, async packet =>
                            {
                                connection.LastReceived = DateTime.UtcNow;
                                if (packet.Type != PacketType.Heartbeat)
                                    await HandlePacketAsync(connection, packet);
                            }, linkedCts.Token),
                            HeartbeatLoop(connection, linkedCts.Token)
                        );
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{endpoint}]: Error: {ex.Message}");
                    }
                    finally
                    {
                        connection.Disconnect();
                        _clients.TryRemove(connection.Client, out _);
                        _clientTasks.TryRemove(connection, out _);
                        Console.WriteLine($"[{endpoint}]: Disconnected.");
                    }
                }, CancellationToken.None);

                _clientTasks[connection] = clientTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{client.Client.RemoteEndPoint}]: Error: {ex.Message}");
            }
        }

        private async Task<bool> PerformHandshakeAsync(ClientConnection connection)
        {
            byte[] publicKey = _trustServer.GetPublicKey();

            await connection.SendPacketAsync(new Packet
            {
                Type = PacketType.Handshake,
                Payload = publicKey
            });

            var challengePacket = await PacketHandler.ReceiveSinglePacketAsync(connection.Stream);
            if (challengePacket.Type != PacketType.Handshake || challengePacket.Payload == null)
                return false;

            var signature = _trustServer.SignChallenge(challengePacket.Payload);

            await connection.SendPacketAsync(new Packet
            {
                Type = PacketType.Handshake,
                Payload = signature
            });

            return true;
        }

        private static async Task HeartbeatLoop(ClientConnection connection, CancellationToken token)
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
                        await connection.SendPacketAsync(new Packet
                        {
                            Type = PacketType.Heartbeat,
                            Payload = []
                        });

                        connection.LastSent = DateTime.UtcNow;
                    }

                    if (DateTime.UtcNow - connection.LastReceived > timeout)
                    {
                        Console.WriteLine($"[{connection.Client.Client.RemoteEndPoint}]: Timed out");
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

        private static async Task HandlePacketAsync(ClientConnection connection, Packet packet)
        {
            switch (packet.Type)
            {
                case PacketType.Disconnect:
                    connection.Disconnect();
                    break;
                default:
                    Console.WriteLine($"[{connection.Client.Client.RemoteEndPoint}]: Received '{packet.Type}' packet.");
                    break;
            }

            await Task.CompletedTask;
        }
    }
}

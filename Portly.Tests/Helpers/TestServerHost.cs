using Portly.Abstractions;
using Portly.PacketHandling;
using Portly.Protocol;
using Portly.Runtime;
using System.Collections.Concurrent;
using System.Net;

namespace Portly.Tests.Helpers
{
    internal sealed class TestServerHost : IAsyncDisposable
    {
        public PortlyServer Server { get; }
        public Task ServerTask { get; private set; } = default!;
        public int Port { get; private set; }

        private readonly ConcurrentDictionary<(Guid ClientId, int PacketId), object> _locks = new();
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<int, Queue<TaskCompletionSource<Packet>>>> _receivePacketWaiters = [];
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<int, Queue<Packet>>> _packetBuffer = [];
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<IServerClient>> _disconnectWaiters = [];
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<IServerClient>> _connectWaiters = new();
        private readonly ConcurrentDictionary<Guid, IServerClient> _clientMap = [];
        private readonly TaskCompletionSource _startedTcs = new();

        public TestServerHost(string folder)
        {
            Server = new PortlyServer(folder, logProvider: new TestLogProvider(false));
            Server.OnServerStarted += (_, _) => _startedTcs.TrySetResult();
            Server.OnPacketReceived += HandleReceivedPacket;
            Server.OnClientConnected += HandleClientConnection;
            Server.OnClientDisconnected += HandleClientDisconnected;
        }

        public async Task StartAsync(int? port = null)
        {
            ServerTask = Server.StartAsync(port: port ?? 0);
            await _startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Port = ((IPEndPoint)Server.LocalEndpoint!).Port;
        }

        public async Task SendAsync(TestClientHost client, Packet packet, bool encrypt)
        {
            await SendAsync(GetServerConnection(client), packet, encrypt);
        }

        public async Task SendAsync(IServerClient client, Packet packet, bool encrypt)
        {
            await Server.SendToClientAsync(client, packet, encrypt);
        }

        public async Task SendAllAsync(Packet packet, bool encrypt)
        {
            await Server.SendToClientsAsync(packet, encrypt);
        }

        public async Task<Packet> WaitForPacketAsync(TestClientHost client, Enum identifier)
        {
            var conn = GetServerConnection(client);
            var packetId = ((PacketIdentifier)identifier).Id;
            var clientId = conn.Id;

            var clientWaiters = _receivePacketWaiters.GetOrAdd(clientId, _ => new ConcurrentDictionary<int, Queue<TaskCompletionSource<Packet>>>());
            var clientBuffers = _packetBuffer.GetOrAdd(clientId, _ => new ConcurrentDictionary<int, Queue<Packet>>());

            var lockObj = _locks.GetOrAdd((clientId, packetId), _ => new object());

            TaskCompletionSource<Packet> tcs;

            lock (lockObj)
            {
                if (clientBuffers.TryGetValue(packetId, out var buffer) && buffer.Count > 0)
                {
                    var value = buffer.Dequeue();

                    if (buffer.Count == 0)
                        clientBuffers.TryRemove(packetId, out _);

                    return value;
                }

                tcs = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);

                var queue = clientWaiters.GetOrAdd(packetId, _ => new Queue<TaskCompletionSource<Packet>>());
                queue.Enqueue(tcs);
            }

            try
            {
                return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Remove timed-out waiter
                lock (lockObj)
                {
                    if (clientWaiters.TryGetValue(packetId, out var queue))
                    {
                        var newQueue = new Queue<TaskCompletionSource<Packet>>();

                        while (queue.Count > 0)
                        {
                            var item = queue.Dequeue();
                            if (!ReferenceEquals(item, tcs))
                                newQueue.Enqueue(item);
                        }

                        if (newQueue.Count > 0)
                            clientWaiters[packetId] = newQueue;
                        else
                            clientWaiters.TryRemove(packetId, out _);
                    }
                }

                throw;
            }
        }

        public async Task<T> WaitForPacketAsync<T>(TestClientHost client, Enum identifier)
        {
            var packet = await WaitForPacketAsync(client, identifier);
            return packet.As<T>().Payload;
        }

        public Task<IServerClient> WaitForClientConnectedAsync(TestClientHost client)
        {
            var clientId = client.Client.ServerClientId;

            if (_clientMap.TryGetValue(clientId, out var existing))
                return Task.FromResult(existing);

            var tcs = new TaskCompletionSource<IServerClient>(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectWaiters[clientId] = tcs;

            if (_clientMap.TryGetValue(clientId, out existing))
            {
                if (_connectWaiters.TryRemove(clientId, out var waiter))
                    waiter.TrySetResult(existing);

                return Task.FromResult(existing);
            }

            return tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async Task WaitForClientDisconnectedAsync(TestClientHost client)
        {
            IServerClient? conn;

            try
            {
                conn = GetServerConnection(client);
            }
            catch (Exception)
            {
                // Already disconnected
                return;
            }

            if (!_clientMap.ContainsKey(client.Client.ServerClientId))
                return;

            var tcs = new TaskCompletionSource<IServerClient>(TaskCreationOptions.RunContinuationsAsynchronously);
            _disconnectWaiters[conn.Id] = tcs;

            if (!_clientMap.ContainsKey(conn.Id))
            {
                if (_disconnectWaiters.TryRemove(conn.Id, out var removed))
                    removed.TrySetResult(conn);

                return;
            }

            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            await Server.StopAsync();

            try { await ServerTask; } catch { }
        }

        private void HandleReceivedPacket(IServerClient conn, Packet packet)
        {
            var clientId = conn.Id;
            var packetId = packet.Identifier.Id;

            var clientWaiters = _receivePacketWaiters.GetOrAdd(clientId, _ => new ConcurrentDictionary<int, Queue<TaskCompletionSource<Packet>>>());
            var clientBuffers = _packetBuffer.GetOrAdd(clientId, _ => new ConcurrentDictionary<int, Queue<Packet>>());

            var lockObj = _locks.GetOrAdd((clientId, packetId), _ => new object());

            lock (lockObj)
            {
                if (clientWaiters.TryGetValue(packetId, out var queue) && queue.Count > 0)
                {
                    var waiter = queue.Dequeue();

                    if (queue.Count == 0)
                        clientWaiters.TryRemove(packetId, out _);

                    waiter.TrySetResult(packet);
                }
                else
                {
                    var buffer = clientBuffers.GetOrAdd(packetId, _ => new Queue<Packet>());
                    buffer.Enqueue(packet);
                }
            }
        }

        private void HandleClientConnection(object? sender, IServerClient client)
        {
            _clientMap[client.Id] = client;

            if (_connectWaiters.TryRemove(client.Id, out var tcs))
            {
                tcs.TrySetResult(client);
            }
        }

        private void HandleClientDisconnected(object? sender, IServerClient client)
        {
            _clientMap.TryRemove(client.Id, out _);

            if (_disconnectWaiters.TryRemove(client.Id, out var disconnectWaiter))
            {
                disconnectWaiter.TrySetResult(client);
            }

            // Fail packet waiters
            if (_receivePacketWaiters.TryRemove(client.Id, out var clientWaiters))
            {
                foreach (var kvp in clientWaiters)
                {
                    var queue = kvp.Value;

                    while (queue.Count > 0)
                    {
                        var waiter = queue.Dequeue();
                        waiter.TrySetException(new Exception("Client disconnected"));
                    }
                }
            }

            // Clear buffers
            _packetBuffer.TryRemove(client.Id, out _);
        }

        private IServerClient GetServerConnection(TestClientHost client)
        {
            if (!_clientMap.TryGetValue(client.Client.ServerClientId, out var serverClient))
                throw new Exception($"No matching server client found on server with guid: {client.Client.ServerClientId}");

            return serverClient;
        }
    }
}
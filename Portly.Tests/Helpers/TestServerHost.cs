using Portly.Abstractions;
using Portly.PacketHandling;
using Portly.Protocol;
using Portly.Runtime;
using System.Collections.Concurrent;
using System.Net;

namespace Portly.Tests.Helpers
{
    /// <summary>
    /// Ready to use server host with awaitable startup and timeout.
    /// </summary>
    internal sealed class TestServerHost : IAsyncDisposable
    {
        public PortlyServer Server { get; }
        public Task ServerTask { get; private set; } = default!;
        public int Port { get; private set; }

        private readonly ConcurrentDictionary<(IServerClient, int), object> _locks = new();
        private readonly ConcurrentDictionary<(IServerClient Client, int PacketId), Queue<TaskCompletionSource<Packet>>> _receivePacketWaiters = [];
        private readonly ConcurrentDictionary<(IServerClient Client, int PacketId), Queue<Packet>> _packetBuffer = [];
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

        public async Task SendAsync(IServerClient client, Packet packet, bool encrypt)
        {
            await Server.SendToClientAsync(client, packet, encrypt);
        }

        public async Task SendAllAsync(Packet packet, bool encrypt)
        {
            await Server.SendToClientsAsync(packet, encrypt);
        }

        /// <summary>
        /// Note: Must await ConnectAsync of client before connection is available.
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public IServerClient GetServerConnection(TestClientHost client)
        {
            if (!_clientMap.TryGetValue(client.Client.ServerClientId, out var serverClient))
                throw new Exception($"No matching server client found on server with guid: {client.Client.ServerClientId}");
            return serverClient;
        }

        public async Task<Packet> WaitForPacketAsync(IServerClient client, Enum identifier)
        {
            var packetId = ((PacketIdentifier)identifier).Id;
            var key = (client, packetId);
            var lockObj = _locks.GetOrAdd(key, _ => new object());

            TaskCompletionSource<Packet> tcs;

            lock (lockObj)
            {
                if (_packetBuffer.TryGetValue(key, out var buffer) &&
                    buffer.Count > 0)
                {
                    var value = buffer.Dequeue();
                    if (buffer.Count == 0)
                        _packetBuffer.TryRemove(key, out _);

                    return value;
                }

                tcs = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);

                if (!_receivePacketWaiters.TryGetValue(key, out var queue))
                {
                    queue = new Queue<TaskCompletionSource<Packet>>();
                    _receivePacketWaiters[key] = queue;
                }

                queue.Enqueue(tcs);
            }

            try
            {
                return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // IMPORTANT: remove timed-out waiter
                lock (lockObj)
                {
                    if (_receivePacketWaiters.TryGetValue(key, out var queue))
                    {
                        var newQueue = new Queue<TaskCompletionSource<Packet>>();

                        while (queue.Count > 0)
                        {
                            var item = queue.Dequeue();
                            if (!ReferenceEquals(item, tcs))
                                newQueue.Enqueue(item);
                        }

                        if (newQueue.Count > 0)
                            _receivePacketWaiters[key] = newQueue;
                        else
                            _receivePacketWaiters.TryRemove(key, out _);
                    }
                }

                throw;
            }
        }

        public async Task<T> WaitForPacketAsync<T>(IServerClient client, Enum identifier)
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

            // Re-check after registration
            if (_clientMap.TryGetValue(clientId, out existing))
            {
                if (_connectWaiters.TryRemove(clientId, out var waiter))
                    waiter.TrySetResult(existing);

                return Task.FromResult(existing);
            }

            return tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public Task<IServerClient> WaitForClientDisconnectedAsync(IServerClient client)
        {
            // 1. Fast path: already disconnected
            if (!_clientMap.ContainsKey(client.Id))
            {
                return Task.FromResult(client);
            }

            var tcs = new TaskCompletionSource<IServerClient>(TaskCreationOptions.RunContinuationsAsynchronously);

            _disconnectWaiters[client.Id] = tcs;

            // 2. Re-check after registering waiter (avoid missed signal)
            if (!_clientMap.ContainsKey(client.Id))
            {
                if (_disconnectWaiters.TryRemove(client.Id, out var removed))
                {
                    removed.TrySetResult(client);
                }

                return Task.FromResult(client);
            }

            return tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            await Server.StopAsync();

            try { await ServerTask; } catch { }
        }

        private void HandleReceivedPacket(IServerClient conn, Packet packet)
        {
            var key = (conn, packet.Identifier.Id);
            var lockObj = _locks.GetOrAdd(key, _ => new object());

            lock (lockObj)
            {
                if (_receivePacketWaiters.TryGetValue(key, out var queue) &&
                    queue.Count > 0)
                {
                    var waiter = queue.Dequeue();

                    if (queue.Count == 0)
                        _receivePacketWaiters.TryRemove(key, out _);

                    // safer
                    waiter.TrySetResult(packet);
                }
                else
                {
                    if (!_packetBuffer.TryGetValue(key, out var buffer))
                    {
                        buffer = new Queue<Packet>();
                        _packetBuffer[key] = buffer;
                    }

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

            // Complete disconnect waiter
            if (_disconnectWaiters.TryRemove(client.Id, out var disconnectWaiter))
            {
                disconnectWaiter.TrySetResult(client);
            }

            // IMPORTANT: fail all packet waiters for this client
            var keys = _receivePacketWaiters.Keys
                .Where(k => k.Client == client)
                .ToList();

            foreach (var key in keys)
            {
                var lockObj = _locks.GetOrAdd(key, _ => new object());

                lock (lockObj)
                {
                    if (_receivePacketWaiters.TryRemove(key, out var queue))
                    {
                        while (queue.Count > 0)
                        {
                            var waiter = queue.Dequeue();
                            waiter.TrySetException(new Exception("Client disconnected"));
                        }
                    }
                }
            }

            // Clear buffers too
            var bufferKeys = _packetBuffer.Keys
                .Where(k => k.Client == client)
                .ToList();

            foreach (var key in bufferKeys)
            {
                _packetBuffer.TryRemove(key, out _);
            }
        }
    }
}

using Portly.Abstractions;
using Portly.PacketHandling;
using Portly.Protocol;
using Portly.Runtime;
using System.Net;

namespace Portly.IntegrationTests.Helpers
{
    /// <summary>
    /// Ready to use server host with awaitable startup and timeout.
    /// </summary>
    internal sealed class TestServerHost : IAsyncDisposable
    {
        public PortlyServer Server { get; }
        public Task ServerTask { get; private set; } = default!;
        public int Port { get; private set; }

        private readonly Lock _lock = new();
        private readonly Dictionary<(IServerClient Client, int PacketId), Queue<TaskCompletionSource<Packet>>> _receivePacketWaiters = [];
        private readonly Dictionary<(IServerClient Client, int PacketId), Queue<Packet>> _packetBuffer = [];
        private readonly Dictionary<Guid, TaskCompletionSource<IServerClient>> _disconnectWaiters = [];
        private readonly Dictionary<Guid, IServerClient> _clientMap = [];
        private readonly TaskCompletionSource _startedTcs = new();

        public TestServerHost()
        {
            Server = new PortlyServer();
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
            lock (_lock)
            {
                if (!_clientMap.TryGetValue(client.Client.ServerClientId, out var serverClient))
                    throw new Exception($"No matching server client found on server with guid: {client.Client.ServerClientId}");
                return serverClient;
            }
        }

        public async Task<Packet> WaitForPacketAsync(IServerClient client, Enum identifier)
        {
            var packetId = ((PacketIdentifier)identifier).Id;

            TaskCompletionSource<Packet> tcs;

            lock (_lock)
            {
                var key = (client, packetId);

                // If packet already arrived, consume it immediately
                if (_packetBuffer.TryGetValue(key, out var buffer) &&
                    buffer.Count > 0)
                {
                    return buffer.Dequeue();
                }

                tcs = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);

                if (!_receivePacketWaiters.TryGetValue(key, out var queue))
                {
                    queue = new Queue<TaskCompletionSource<Packet>>();
                    _receivePacketWaiters[key] = queue;
                }

                queue.Enqueue(tcs);
            }

            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async Task<T> WaitForPacketAsync<T>(IServerClient client, Enum identifier)
        {
            var packet = await WaitForPacketAsync(client, identifier);
            return packet.As<T>().Payload;
        }

        public async Task<IServerClient> WaitForClientDisconnectedAsync(IServerClient client)
        {
            var tcs = new TaskCompletionSource<IServerClient>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock)
            {
                _disconnectWaiters[client.Id] = tcs;
            }

            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            await Server.StopAsync();

            try { await ServerTask; } catch { }
        }

        private void HandleReceivedPacket(IServerClient conn, Packet packet)
        {
            TaskCompletionSource<Packet>? waiter = null;
            var key = (conn, packet.Identifier.Id);

            lock (_lock)
            {
                if (_receivePacketWaiters.TryGetValue(key, out var queue) &&
                    queue.Count > 0)
                {
                    waiter = queue.Dequeue();
                }
                else
                {
                    if (!_packetBuffer.TryGetValue(key, out var buffer))
                    {
                        buffer = new Queue<Packet>();
                        _packetBuffer[key] = buffer;
                    }

                    buffer.Enqueue(packet);
                    return;
                }
            }

            waiter?.TrySetResult(packet);
        }

        private void HandleClientConnection(object? sender, IServerClient client)
        {
            lock (_lock)
            {
                _clientMap[client.Id] = client;
            }
        }

        private void HandleClientDisconnected(object? sender, IServerClient client)
        {
            TaskCompletionSource<IServerClient>? waiter;

            lock (_lock)
            {
                if (_disconnectWaiters.TryGetValue(client.Id, out waiter))
                {
                    _disconnectWaiters.Remove(client.Id);
                }
            }

            waiter?.TrySetResult(client);
        }
    }
}

using Portly.Abstractions;
using Portly.PacketHandling;
using Portly.Protocol;
using Portly.Runtime;

namespace Portly.IntegrationTests.Helpers
{
    /// <summary>
    /// Ready to use server host with awaitable startup and timeout.
    /// </summary>
    internal sealed class TestServerHost : IAsyncDisposable
    {
        public PortlyServer Server { get; }
        public Task ServerTask { get; private set; } = default!;
        public int Port { get; }

        private readonly Lock _lock = new();
        private readonly Dictionary<(IServerClient Client, int PacketId), Queue<TaskCompletionSource<Packet>>> _waiters = [];
        private readonly Dictionary<Guid, IServerClient> _clientMap = [];
        private readonly TaskCompletionSource _startedTcs = new();

        public TestServerHost()
        {
            Server = new PortlyServer();
            Server.OnServerStarted += (_, _) => _startedTcs.TrySetResult();
            Server.OnPacketReceived += HandleReceivedPacket;
            Server.OnClientConnected += HandleClientConnection;
            Port = Tools.GetFreePort();
        }

        public async Task StartAsync()
        {
            ServerTask = Server.StartAsync(port: Port);
            await _startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
                return _clientMap[client.Client.ServerClientId];
            }
        }

        public async Task<Packet> WaitForPacketAsync(IServerClient client, Enum identifier)
        {
            var packetId = ((PacketIdentifier)identifier).Id;

            var tcs = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock)
            {
                var key = (client, packetId);

                if (!_waiters.TryGetValue(key, out var queue))
                {
                    queue = new Queue<TaskCompletionSource<Packet>>();
                    _waiters[key] = queue;
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
                if (_waiters.TryGetValue(key, out var queue) &&
                    queue.Count > 0)
                {
                    waiter = queue.Dequeue();
                }
            }

            waiter?.TrySetResult(packet);
        }

        private void HandleClientConnection(object? sender, IServerClient e)
        {
            lock (_lock)
            {
                _clientMap[e.Id] = e;
            }
        }
    }
}

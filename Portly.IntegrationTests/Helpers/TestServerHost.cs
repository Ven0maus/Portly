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
        private readonly Dictionary<int, Queue<TaskCompletionSource<Packet>>> _packetWaitersById = new();
        private readonly TaskCompletionSource _startedTcs = new();

        public TestServerHost()
        {
            Server = new PortlyServer();
            Server.OnServerStarted += (_, _) => _startedTcs.TrySetResult();
            // Subscribe ONCE
            Server.OnPacketReceived += HandleReceivedPacket;
            Port = Tools.GetFreePort();
        }

        public async Task StartAsync()
        {
            ServerTask = Server.StartAsync(port: Port);
            await _startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async Task<Packet> WaitForPacketAsync(Enum identifier)
        {
            var tcs = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock)
            {
                var packetIdentifier = ((PacketIdentifier)identifier);
                if (!_packetWaitersById.TryGetValue(packetIdentifier.Id, out var queue))
                {
                    queue = new Queue<TaskCompletionSource<Packet>>();
                    _packetWaitersById[packetIdentifier.Id] = queue;
                }

                queue.Enqueue(tcs);
            }

            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async Task<T> WaitForPacketAsync<T>(Enum identifier)
        {
            var packet = await WaitForPacketAsync(identifier);
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

            lock (_lock)
            {
                if (_packetWaitersById.TryGetValue(packet.Identifier.Id, out var queue) &&
                    queue.Count > 0)
                {
                    waiter = queue.Dequeue();
                }
            }

            waiter?.TrySetResult(packet);
        }
    }
}

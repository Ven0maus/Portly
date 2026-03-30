using Portly.PacketHandling;
using Portly.Protocol;
using Portly.Runtime;

namespace Portly.IntegrationTests.Helpers
{
    /// <summary>
    /// Ready to use client host with awaitable connect, sending and timeouts.
    /// </summary>
    internal sealed class TestClientHost : IAsyncDisposable
    {
        public PortlyClient Client { get; }

        private readonly Lock _lock = new();
        private readonly Dictionary<int, Queue<TaskCompletionSource<Packet>>> _waitersById = new();

        public TestClientHost()
        {
            Client = new PortlyClient();
            Client.OnPacketReceived += HandleReceivedPacket;
        }

        public async Task<Packet> WaitForPacketAsync(Enum identifier)
        {
            var packetId = ((PacketIdentifier)identifier).Id;

            var tcs = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock)
            {
                if (!_waitersById.TryGetValue(packetId, out var queue))
                {
                    queue = new Queue<TaskCompletionSource<Packet>>();
                    _waitersById[packetId] = queue;
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

        public async Task ConnectAsync(string host, int port)
        {
            await Client.ConnectAsync(host, port)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async Task SendAsync(Packet packet)
        {
            await Client.SendPacketAsync(packet, encrypt: false)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisconnectAsync();
        }

        private void HandleReceivedPacket(object? sender, Packet packet)
        {
            TaskCompletionSource<Packet>? waiter = null;

            var id = packet.Identifier.Id;

            lock (_lock)
            {
                if (_waitersById.TryGetValue(id, out var queue) &&
                    queue.Count > 0)
                {
                    waiter = queue.Dequeue();
                }
            }

            waiter?.TrySetResult(packet);
        }
    }
}

using Portly.PacketHandling;
using Portly.Protocol;
using Portly.Runtime;

namespace Portly.Tests.Helpers
{
    /// <summary>
    /// Ready to use client host with awaitable connect, sending and timeouts.
    /// </summary>
    internal sealed class TestClientHost : IAsyncDisposable
    {
        public PortlyClient Client { get; }

        private readonly Lock _lock = new();
        private readonly Dictionary<int, Queue<TaskCompletionSource<Packet>>> _waitersById = [];
        private readonly Dictionary<int, Queue<Packet>> _bufferedPackets = [];

        public TestClientHost(string folder)
        {
            Directory.CreateDirectory(folder);
            Client = new PortlyClient(folder);
            Client.OnPacketReceived += HandleReceivedPacket;
        }

        public async Task<Packet> WaitForPacketAsync(Enum identifier)
        {
            var packetId = ((PacketIdentifier)identifier).Id;
            TaskCompletionSource<Packet> tcs;

            lock (_lock)
            {
                // If packet already arrived, consume it immediately
                if (_bufferedPackets.TryGetValue(packetId, out var buffer) &&
                    buffer.Count > 0)
                {
                    var packet = buffer.Dequeue();
                    return packet;
                }

                tcs = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public async Task DisconnectAsync(TestServerHost serverHost)
        {
            var serverClient = serverHost.GetServerConnection(this);
            var disconnectTask = serverHost.WaitForClientDisconnectedAsync(serverClient);

            await Client.DisconnectAsync();
            await disconnectTask;
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
                else
                {
                    if (!_bufferedPackets.TryGetValue(id, out var buffer))
                    {
                        buffer = new Queue<Packet>();
                        _bufferedPackets[id] = buffer;
                    }

                    buffer.Enqueue(packet);
                    return;
                }
            }

            waiter?.TrySetResult(packet);
        }
    }
}

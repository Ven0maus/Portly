using Portly.PacketHandling;
using Portly.Protocol;
using Portly.Runtime;

namespace Portly.Tests.Helpers
{
    internal sealed class TestClientHost : IAsyncDisposable
    {
        public PortlyClient Client { get; }

        private readonly Lock _lock = new();

        private readonly Dictionary<int, Queue<TaskCompletionSource<Packet>>> _waitersById = [];
        private readonly Dictionary<int, Queue<Packet>> _bufferedPackets = [];

        public TestClientHost(string folder)
        {
            Directory.CreateDirectory(folder);
            Client = new PortlyClient(folder, logProvider: new TestLogProvider(true));
            Client.OnPacketReceived += HandleReceivedPacket;
        }

        public async Task<Packet> WaitForPacketAsync(Enum identifier, int? timeout = null)
        {
            var packetId = ((PacketIdentifier)identifier).Id;
            TaskCompletionSource<Packet> tcs;

            lock (_lock)
            {
                if (_bufferedPackets.TryGetValue(packetId, out var buffer) &&
                    buffer.Count > 0)
                {
                    var packet = buffer.Dequeue();

                    if (buffer.Count == 0)
                        _bufferedPackets.Remove(packetId);

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

            try
            {
                return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(timeout ?? 5));
            }
            catch
            {
                // Cleanup timed-out waiter
                lock (_lock)
                {
                    if (_waitersById.TryGetValue(packetId, out var queue))
                    {
                        var newQueue = new Queue<TaskCompletionSource<Packet>>();

                        while (queue.Count > 0)
                        {
                            var item = queue.Dequeue();
                            if (!ReferenceEquals(item, tcs))
                                newQueue.Enqueue(item);
                        }

                        if (newQueue.Count > 0)
                            _waitersById[packetId] = newQueue;
                        else
                            _waitersById.Remove(packetId);
                    }
                }

                throw;
            }
        }

        public async Task<T> WaitForPacketAsync<T>(Enum identifier)
        {
            var packet = await WaitForPacketAsync(identifier);
            return packet.As<T>().Payload;
        }

        public async Task ConnectAsync(string host, int port, int? timeout = null)
        {
            if (Client.IsConnected) return;

            await Client.ConnectAsync(host, port)
                .WaitAsync(TimeSpan.FromSeconds(timeout ?? 5));
        }

        public async Task SendAsync(Packet packet, int? timeout = null)
        {
            if (!Client.IsConnected) return;

            await Client.SendPacketAsync(packet, encrypt: false)
                .WaitAsync(TimeSpan.FromSeconds(timeout ?? 5));
        }

        public async Task DisconnectAsync(TestServerHost serverHost)
        {
            if (!Client.IsConnected) return;

            var serverClient = serverHost.GetServerConnection(this);
            var disconnectTask = serverHost.WaitForClientDisconnectedAsync(serverClient);

            await Client.DisconnectAsync();
            await disconnectTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (Client.IsConnected)
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

                    if (queue.Count == 0)
                        _waitersById.Remove(id);
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
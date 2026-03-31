using Portly.Protocol;

namespace Portly.Tests.Helpers
{
    /// <summary>
    /// Contains a group of <see cref="TestClientHost"/> to simplify simulation of multiple client actions at once.
    /// </summary>
    internal sealed class TestClientGroup : IAsyncDisposable
    {
        public List<TestClientHost> Clients { get; } = [];

        private Dictionary<Guid, TestClientHost>? _clients;
        private readonly TimeSpan _timeout;

        public TestClientGroup(string folder, int count, TimeSpan? timeout = null)
        {
            _timeout = timeout ?? TimeSpan.FromSeconds(5);
            for (int i = 0; i < count; i++)
                Clients.Add(new TestClientHost($"{folder}_{i}"));
        }

        public async Task ConnectAllAsync(string host, int port, TestServerHost serverHost)
        {
            await Task.WhenAll(Clients.Select(c => c.ConnectAsync(host, port, serverHost)))
                .WaitAsync(_timeout);

            // Build lookup
            _clients = Clients.ToDictionary(a => a.Client.ServerClientId);
        }

        public async Task SendAllAsync(Func<int, Packet> packetFactory)
        {
            await Task.WhenAll(Clients.Select((c, i) => c.SendAsync(packetFactory(i))))
                .WaitAsync(_timeout);
        }

        public Task SendAsync(int clientIndex, Packet packet)
        {
            var client = Clients[clientIndex];
            return client.SendAsync(packet)
                .WaitAsync(_timeout);
        }

        public Task SendAsync(Guid clientId, Packet packet)
        {
            var client = _clients![clientId];
            return client.SendAsync(packet)
                .WaitAsync(_timeout);
        }

        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(Clients.Select(c => c.DisposeAsync().AsTask()))
                .WaitAsync(_timeout);
        }
    }
}

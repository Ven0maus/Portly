using Portly.Protocol;

namespace Portly.IntegrationTests.Helpers
{
    /// <summary>
    /// Contains a group of <see cref="TestClientHost"/> to simplify simulation of multiple client actions at once.
    /// </summary>
    internal sealed class TestClientGroup : IAsyncDisposable
    {
        public List<TestClientHost> Clients { get; } = [];

        private readonly TimeSpan _timeout;

        public TestClientGroup(int count, TestServerHost host, TimeSpan? timeout = null)
        {
            _timeout = timeout ?? TimeSpan.FromSeconds(5);
            for (int i = 0; i < count; i++)
                Clients.Add(new TestClientHost(host));
        }

        public async Task ConnectAllAsync(string host, int port)
        {
            await Task.WhenAll(Clients.Select(c => c.ConnectAsync(host, port)))
                .WaitAsync(_timeout);
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

        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(Clients.Select(c => c.DisposeAsync().AsTask()))
                .WaitAsync(_timeout);
        }
    }
}

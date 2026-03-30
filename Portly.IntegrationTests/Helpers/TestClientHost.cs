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

        public TestClientHost(TestServerHost host)
        {
            Client = new PortlyClient();
            host.RegisterPendingClient(this);
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
    }
}

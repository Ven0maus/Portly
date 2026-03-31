using Portly.Tests.Helpers;
using Portly.Tests.Objects;

namespace Portly.Tests.HelperTests
{
    /// <summary>
    /// These should cover all helper tools and objects
    /// </summary>
    internal class BasicHelperTests : BaseTests
    {
        [Test]
        public async Task WaitForPacket_Should_Timeout_When_No_Packet()
        {
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            var client = new TestClientHost(ClientDirectory);
            await client.ConnectAsync(LocalHost, host.Port, host);

            var conn = host.GetServerConnection(client);
            var task = host.WaitForPacketAsync<string>(conn, PacketType.Custom);

            Assert.ThrowsAsync<TimeoutException>(async () => await task);
        }
    }
}

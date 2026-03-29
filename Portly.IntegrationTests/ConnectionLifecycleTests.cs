using Portly.IntegrationTests.Helpers;
using Portly.Protocol;

namespace Portly.IntegrationTests
{
    /// <summary>
    /// Basic server-client connection lifecycle tests
    /// </summary>
    public class ConnectionLifecycleTests
    {
        [Test]
        public async Task Should_Connect_SendPacket_And_Disconnect()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            await using var client = new TestClientHost();

            var receiveTask = host.WaitForPacketAsync<string>(PacketType.Custom);

            await client.ConnectAsync("localhost", host.Port);

            await client.SendAsync(
                Packet.Create(PacketType.Custom, "hello"));

            var received = await receiveTask;

            Assert.That(received, Is.EqualTo("hello"));
        }
    }
}

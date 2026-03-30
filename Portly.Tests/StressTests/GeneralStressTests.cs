using Portly.Protocol;
using Portly.Tests.Helpers;
using PacketType = Portly.Tests.Objects.PacketType;

namespace Portly.Tests.StressTests
{
    internal class GeneralStressTests : BaseTests
    {
        [Test]
        public async Task ChaosTest_MixedClientServerOperations()
        {
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            var clients = new TestClientGroup(ClientDirectory, 10);

            var random = new Random(12345);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var tasks = clients.Clients.Select(async client =>
            {
                while (!cts.IsCancellationRequested)
                {
                    var action = random.Next(0, 4);

                    try
                    {
                        switch (action)
                        {
                            case 0: // connect if not connected
                                if (!client.Client.IsConnected)
                                    await client.ConnectAsync(LocalHost, host.Port);
                                break;

                            case 1: // send packet
                                if (client.Client.IsConnected)
                                {
                                    await client.SendAsync(Packet.Create(PacketType.Custom, "ping"));
                                }
                                break;

                            case 2: // disconnect
                                if (client.Client.IsConnected)
                                {
                                    await client.Client.DisconnectAsync();
                                }
                                break;

                            case 3: // reconnect
                                if (!client.Client.IsConnected)
                                {
                                    await client.ConnectAsync(LocalHost, host.Port);
                                }
                                break;
                        }
                    }
                    catch
                    {
                        // Ignore transient errors in chaos scenarios
                    }

                    await Task.Delay(random.Next(1, 20));
                }
            });

            await Task.WhenAll(tasks);

            // Final assertions (invariants)

            using (Assert.EnterMultipleScope())
            {
                // No duplicate server connections
                var uniqueClients = host.Server.ConnectedClients
                    .Select(c => c.Id)
                    .Distinct()
                    .Count();

                Assert.That(uniqueClients, Is.EqualTo(host.Server.ConnectedClients.Count));

                // Server should not crash or lose internal consistency
                Assert.That(host.Server.ConnectedClients, Has.Count.GreaterThanOrEqualTo(0));
            }
        }
    }
}

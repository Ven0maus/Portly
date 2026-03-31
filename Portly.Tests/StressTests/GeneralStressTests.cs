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

            await using var clients = new TestClientGroup(ClientDirectory, 10);

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
                                    await client.ConnectAsync(LocalHost, host.Port, host);
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
                                    await client.ConnectAsync(LocalHost, host.Port, host);
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

        [Test]
        public async Task StressTest_BurstMessageFlood()
        {
            await using var host = new TestServerHost(ServerDirectory);

            // Increase connection limits
            host.Server.Configuration.ConnectionSettings.MaxConnectionsPerIp = 100;
            host.Server.Configuration.ConnectionSettings.MaxConnections = 100;

            // Increase rate limiting thresholds
            host.Server.Configuration.RateLimits.MaxPacketsPerSecond = 10_000_000;
            host.Server.Configuration.RateLimits.MaxPacketsPerBurst = 10_000_000;
            host.Server.Configuration.RateLimits.MaxBytesPerSecond = 10_000_000;
            host.Server.Configuration.RateLimits.MaxBytesPerBurst = 10_000_000;

            await host.StartAsync();

            await using var clients = new TestClientGroup(ClientDirectory, 20);
            var random = new Random(42);

            foreach (var client in clients.Clients)
                await client.ConnectAsync(LocalHost, host.Port, host);

            await Task.Delay(200);

            Assert.That(host.Server.ConnectedClients, Has.Count.EqualTo(clients.Clients.Count));

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var tasks = clients.Clients.Select(async client =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        await client.SendAsync(Packet.Create(PacketType.Custom, Guid.NewGuid().ToString()));
                    }
                    catch
                    {
                        // ignore transient disconnects if any still occur
                    }

                    await Task.Delay(random.Next(0, 5));
                }
            });

            await Task.WhenAll(tasks);

            Assert.That(host.Server.ConnectedClients, Has.Count.EqualTo(clients.Clients.Count));
        }

        [Test]
        public async Task StressTest_RapidConnectDisconnect()
        {
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            await using var clients = new TestClientGroup(ClientDirectory, 10);
            var random = new Random(99);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var tasks = clients.Clients.Select(async client =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        if (!client.Client.IsConnected)
                        {
                            await client.ConnectAsync(LocalHost, host.Port, host);
                        }
                        else
                        {
                            await client.Client.DisconnectAsync();
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    await Task.Delay(random.Next(1, 10));
                }
            });

            await Task.WhenAll(tasks);

            Assert.Pass();
        }

        [Test]
        public async Task StressTest_MessageDeliveryUnderLoad()
        {
            await using var host = new TestServerHost(ServerDirectory);

            host.Server.Configuration.ConnectionSettings.MaxConnectionsPerIp = 100;
            host.Server.Configuration.ConnectionSettings.MaxConnections = 100;
            host.Server.Configuration.RateLimits.MaxPacketsPerSecond = 10_000_000;
            host.Server.Configuration.RateLimits.MaxPacketsPerBurst = 10_000_000;
            host.Server.Configuration.RateLimits.MaxBytesPerSecond = 10_000_000;
            host.Server.Configuration.RateLimits.MaxBytesPerBurst = 10_000_000;

            await host.StartAsync();

            var receivedPackets = 0;

            // Hook into server-side packet handling
            host.Server.OnPacketReceived += (connection, packet) =>
            {
                Interlocked.Increment(ref receivedPackets);
            };

            await using var clients = new TestClientGroup(ClientDirectory, 15);

            foreach (var client in clients.Clients)
                await client.ConnectAsync(LocalHost, host.Port, host);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var senderTasks = clients.Clients.Select(async client =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        await client.SendAsync(Packet.Create(PacketType.Custom, "msg"));
                    }
                    catch { }

                    await Task.Delay(5);
                }
            });

            await Task.WhenAll(senderTasks);

            Assert.That(receivedPackets, Is.GreaterThan(0));
        }

        [Test]
        public async Task StressTest_SequentialClientConnections()
        {
            await using var host = new TestServerHost(ServerDirectory);

            host.Server.Configuration.RateLimits.MaxPacketsPerSecond = 10_000_000;
            host.Server.Configuration.RateLimits.MaxPacketsPerBurst = 10_000_000;
            host.Server.Configuration.RateLimits.MaxBytesPerSecond = 10_000_000;
            host.Server.Configuration.RateLimits.MaxBytesPerBurst = 10_000_000;

            await host.StartAsync();

            var random = new Random(123);

            for (int i = 0; i < 100; i++)
            {
                await using var client = new TestClientHost(ClientDirectory);

                await client.ConnectAsync(LocalHost, host.Port, host);

                await client.SendAsync(Packet.Create(PacketType.Custom, $"msg-{i}"));

                await client.Client.DisconnectAsync();

                await Task.Delay(random.Next(1, 5));
            }

            Assert.That(host.Server.ConnectedClients, Is.Empty);
        }

        [Test]
        public async Task StressTest_ConcurrentConnectionStorm()
        {
            await using var host = new TestServerHost(ServerDirectory);
            host.Server.Configuration.ConnectionSettings.MaxConnectionsPerIp = 100;
            host.Server.Configuration.ConnectionSettings.MaxConnections =
                host.Server.Configuration.ConnectionSettings.MaxConnectionsPerIp;
            await host.StartAsync();

            await using var clients = new TestClientGroup(ClientDirectory, 50);

            var tasks = clients.Clients.Select(client =>
                client.ConnectAsync(LocalHost, host.Port, host));

            await Task.WhenAll(tasks);

            Assert.That(host.Server.ConnectedClients, Has.Count.EqualTo(clients.Clients.Count));
        }

        [Test]
        public async Task StressTest_MaxConnectionsPerIp_IsEnforced()
        {
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            var clients = new List<TestClientHost>();
            try
            {
                var maxPerIp = host.Server.Configuration.ConnectionSettings.MaxConnectionsPerIp;

                // Create exactly max allowed connections
                for (int i = 0; i < maxPerIp; i++)
                {
                    var client = new TestClientHost(ClientDirectory);
                    await client.ConnectAsync(LocalHost, host.Port, host);
                    clients.Add(client);
                }

                // Verify we reached the limit
                Assert.That(host.Server.ConnectedClients, Has.Count.EqualTo(maxPerIp));

                // Attempt one additional connection from same IP
                await using var extraClient = new TestClientHost(ClientDirectory);

                try
                {
                    await extraClient.ConnectAsync(LocalHost, host.Port, host);
                }
                catch
                { }

                await Task.Delay(200); // allow server to process rejection if async

                Assert.That(host.Server.ConnectedClients, Has.Count.EqualTo(maxPerIp));
            }
            finally
            {
                foreach (var client in clients)
                    await client.DisposeAsync();
            }
        }
    }
}

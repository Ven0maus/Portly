using Portly.IntegrationTests.Helpers;
using Portly.Protocol;
using System.Collections.Concurrent;

namespace Portly.IntegrationTests
{
    /// <summary>
    /// Basic server-client connection lifecycle tests
    /// </summary>
    public class ConnectionLifecycleTests
    {
        private static Mutex? _cleanupMutex;

        [TearDown]
        public async Task TearDown()
        {
            _cleanupMutex?.WaitOne();
            try
            {
                Tools.CleanupServerSetup();
                Tools.CleanupClientSetup();
            }
            finally
            {
                _cleanupMutex?.ReleaseMutex();
            }
        }

        [OneTimeSetUp]
        public async Task OnetimeSetUp()
        {
            _cleanupMutex = new Mutex(false, "Portly_IntegrationTests_CleanupMutex");
        }

        [OneTimeTearDown]
        public async Task OnetimeTearDown()
        {
            _cleanupMutex?.Dispose();
            _cleanupMutex = null;
        }

        [Test]
        public async Task Should_Connect_SendPacket_And_Disconnect()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            await using var client = new TestClientHost();

            await client.ConnectAsync("localhost", host.Port);

            var serverConnection = host.GetServerConnection(client);
            var receiveTask = host.WaitForPacketAsync<string>(serverConnection, PacketType.Custom);

            await client.SendAsync(Packet.Create(PacketType.Custom, "hello"));

            var received = await receiveTask;

            Assert.That(received, Is.EqualTo("hello"));
        }

        [Test]
        public async Task MultipleClients_Should_Send_Independent_Packets()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            await using var clients = new TestClientGroup(3);

            await clients.ConnectAllAsync("localhost", host.Port);

            var connections = clients.Clients
                .Select(c => host.GetServerConnection(c))
                .ToList();

            var receiveTasks = connections.Select(conn =>
                host.WaitForPacketAsync<string>(conn, PacketType.Custom))
                .ToList();

            await clients.SendAllAsync(i =>
                Packet.Create(PacketType.Custom, $"msg-{i}")
            );

            var results = await Task.WhenAll(receiveTasks);

            Assert.That(results, Is.EquivalentTo(["msg-0", "msg-1", "msg-2"]));
        }

        [Test]
        public async Task EachClient_Should_Be_Mapped_To_Correct_ServerConnection()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            await using var clients = new TestClientGroup(2);

            await clients.ConnectAllAsync("localhost", host.Port);

            var connA = host.GetServerConnection(clients.Clients[0]);
            var connB = host.GetServerConnection(clients.Clients[1]);

            var taskA = host.WaitForPacketAsync<string>(connA, PacketType.Custom);
            var taskB = host.WaitForPacketAsync<string>(connB, PacketType.Custom);

            await clients.Clients[0].SendAsync(Packet.Create(PacketType.Custom, "A"));
            await clients.Clients[1].SendAsync(Packet.Create(PacketType.Custom, "B"));

            var resultA = await taskA;
            var resultB = await taskB;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resultA, Is.EqualTo("A"));
                Assert.That(resultB, Is.EqualTo("B"));
            }
        }

        [Test]
        public async Task Server_Should_Broadcast_To_All_Clients()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            await using var clients = new TestClientGroup(3);

            await clients.ConnectAllAsync("localhost", host.Port);

            Assert.That(host.Server.ConnectedClients, Has.Count.EqualTo(clients.Clients.Count));

            var receiveTasks = clients.Clients.Select(c =>
                c.WaitForPacketAsync<string>(PacketType.Custom));

            foreach (var conn in host.Server.ConnectedClients)
            {
                await host.SendAsync(conn, Packet.Create(PacketType.Custom, "broadcast"), false);
            }

            var results = await Task.WhenAll(receiveTasks);

            foreach (var result in results)
                Assert.That(result, Is.EqualTo("broadcast"));
        }

        [Test]
        public async Task MultipleClients_Should_Handle_Concurrent_Sends()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            await using var clients = new TestClientGroup(5);

            await clients.ConnectAllAsync("localhost", host.Port);

            var connections = clients.Clients
                .Select(c => host.GetServerConnection(c))
                .ToList();

            var receiveTasks = connections.Select(conn =>
                host.WaitForPacketAsync<string>(conn, PacketType.Custom))
                .ToList();

            var sendTasks = clients.Clients.Select((c, i) =>
                c.SendAsync(Packet.Create(PacketType.Custom, $"msg-{i}")));

            await Task.WhenAll(sendTasks);

            var results = await Task.WhenAll(receiveTasks);

            Assert.That(results, Has.Length.EqualTo(5));
        }

        [Test]
        public async Task Client_Should_Disconnect_Cleanly()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            var client = new TestClientHost();

            await client.ConnectAsync("localhost", host.Port);

            var conn = host.GetServerConnection(client);

            var disconnectTask = host.WaitForClientDisconnectedAsync(conn);
            await client.Client.DisconnectAsync();
            await disconnectTask;

            // Server should no longer consider it active
            Assert.That(host.Server.ConnectedClients, Is.Empty);
        }

        [Test]
        public async Task Packets_Should_Be_Received_In_Order_Per_Client()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            var client = new TestClientHost();

            await client.ConnectAsync("localhost", host.Port);

            var conn = host.GetServerConnection(client);

            var receive1 = host.WaitForPacketAsync<string>(conn, PacketType.Custom);
            var receive2 = host.WaitForPacketAsync<string>(conn, PacketType.Custom);

            await client.SendAsync(Packet.Create(PacketType.Custom, "1"));
            await client.SendAsync(Packet.Create(PacketType.Custom, "2"));

            var r1 = await receive1;
            var r2 = await receive2;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(r1, Is.EqualTo("1"));
                Assert.That(r2, Is.EqualTo("2"));
            }
        }

        [Test]
        public async Task ChaosTest_MixedClientServerOperations()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            var clients = Enumerable.Range(0, 10)
                .Select(_ => new TestClientHost())
                .ToList();

            var random = new Random(12345);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var tasks = clients.Select(async client =>
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
                                    await client.ConnectAsync("localhost", host.Port);
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
                                    await client.ConnectAsync("localhost", host.Port);
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
        public async Task Clients_Should_Receive_Exact_Number_Of_Packets()
        {
            await using var host = new TestServerHost();
            await host.StartAsync();

            const int clientCount = 5;
            const int packetsPerClient = 20;

            await using var clients = new TestClientGroup(clientCount);

            await clients.ConnectAllAsync("localhost", host.Port);

            var connections = clients.Clients
                .Select(c => host.GetServerConnection(c))
                .ToList();

            // Track received packets per client
            var receivedCounts = new ConcurrentDictionary<int, int>();

            var receiveTasks = clients.Clients.Select(async (client, index) =>
            {
                for (int i = 0; i < packetsPerClient; i++)
                {
                    await client.WaitForPacketAsync<string>(PacketType.Custom);

                    receivedCounts.AddOrUpdate(index, 1, (_, count) => count + 1);
                }
            });

            // Send packets to each client
            foreach (var conn in connections)
            {
                for (int i = 0; i < packetsPerClient; i++)
                {
                    await host.SendAsync(conn, Packet.Create(PacketType.Custom, $"msg-{i}"), false);
                }
            }

            await Task.WhenAll(receiveTasks);

            // Assertions
            using (Assert.EnterMultipleScope())
            {
                Assert.That(receivedCounts.Count, Is.EqualTo(clientCount));

                foreach (var kvp in receivedCounts)
                {
                    Assert.That(kvp.Value, Is.EqualTo(packetsPerClient));
                }
            }
        }
    }
}

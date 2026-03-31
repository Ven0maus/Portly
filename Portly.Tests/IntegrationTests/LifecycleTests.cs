using Portly.Protocol;
using Portly.Tests.Helpers;
using System.Collections.Concurrent;
using PacketType = Portly.Tests.Objects.PacketType;

namespace Portly.Tests.IntegrationTests
{
    /// <summary>
    /// Basic server-client connection lifecycle tests
    /// </summary>
    internal class LifecycleTests : BaseTests
    {
        [Test]
        public async Task Should_Connect_SendPacket_And_Disconnect()
        {
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            await using var client = new TestClientHost(ClientDirectory);

            await client.ConnectAsync(LocalHost, host.Port);

            var serverConnection = host.GetServerConnection(client);
            var receiveTask = host.WaitForPacketAsync<string>(serverConnection, PacketType.Custom);

            await client.SendAsync(Packet.Create(PacketType.Custom, "hello"));

            var received = await receiveTask;

            Assert.That(received, Is.EqualTo("hello"));
        }

        [Test]
        public async Task MultipleClients_Should_Send_Independent_Packets()
        {
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            await using var clients = new TestClientGroup(ClientDirectory, 3);

            await clients.ConnectAllAsync(LocalHost, host.Port);

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
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            await using var clients = new TestClientGroup(ClientDirectory, 2);

            await clients.ConnectAllAsync(LocalHost, host.Port);

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
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            await using var clients = new TestClientGroup(ClientDirectory, 3);

            await clients.ConnectAllAsync(LocalHost, host.Port);

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
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            await using var clients = new TestClientGroup(ClientDirectory, 5);

            await clients.ConnectAllAsync(LocalHost, host.Port);

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
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            var client = new TestClientHost(ClientDirectory);

            await client.ConnectAsync(LocalHost, host.Port);
            await client.DisconnectAsync(host);

            // Server should no longer consider it active
            Assert.That(host.Server.ConnectedClients, Is.Empty);
        }

        [Test]
        public async Task Packets_Should_Be_Received_In_Order_Per_Client()
        {
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            var client = new TestClientHost(ClientDirectory);

            await client.ConnectAsync(LocalHost, host.Port);

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
        public async Task Clients_Should_Receive_Exact_Number_Of_Packets()
        {
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            const int clientCount = 5;
            const int packetsPerClient = 20;

            await using var clients = new TestClientGroup(ClientDirectory, clientCount);

            await clients.ConnectAllAsync(LocalHost, host.Port);

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

        [Test]
        public async Task Packets_Should_Maintain_Order_Under_Concurrent_Clients()
        {
            await using var host = new TestServerHost(ServerDirectory);

            // Adjust rate limits
            host.Server.Configuration.RateLimits.MaxPacketsPerSecond = 10_000_000;
            host.Server.Configuration.RateLimits.MaxPacketsPerBurst = 10_000_000;
            host.Server.Configuration.RateLimits.MaxBytesPerSecond = 10_000_000;
            host.Server.Configuration.RateLimits.MaxBytesPerBurst = 10_000_000;

            await host.StartAsync();

            await using var clients = new TestClientGroup(ClientDirectory, 5);
            await clients.ConnectAllAsync(LocalHost, host.Port);

            var connections = clients.Clients
                .Select(c => host.GetServerConnection(c))
                .ToList();

            // Concurrent sends with interleaving
            var sendTasks = clients.Clients.Select((c, ci) =>
                Task.Run(async () =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        await c.SendAsync(Packet.Create(PacketType.Custom, $"{i}"));
                    }
                }));

            await Task.WhenAll(sendTasks);

            var receiveTasks = connections.Select(conn =>
                Task.Run(async () =>
                {
                    var list = new List<int>();

                    for (int i = 0; i < 10; i++)
                    {
                        var msg = await host.WaitForPacketAsync<string>(conn, PacketType.Custom);
                        list.Add(int.Parse(msg));
                    }

                    return list;
                }))
                .ToList();

            var results = await Task.WhenAll(receiveTasks);

            foreach (var sequence in results)
            {
                Assert.That(sequence, Is.EqualTo(sequence.OrderBy(x => x)));
            }
        }

        [Test]
        public async Task Clients_Should_Handle_Frequent_Connect_Disconnect()
        {
            await using var host = new TestServerHost(ServerDirectory);
            await host.StartAsync();

            await using var client = new TestClientHost(ClientDirectory);

            for (int i = 0; i < 20; i++)
            {
                await client.ConnectAsync(LocalHost, host.Port);
                await client.DisconnectAsync(host);
            }

            Assert.That(host.Server.ConnectedClients, Is.Empty);
        }
    }
}

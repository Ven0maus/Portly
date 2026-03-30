using Portly.IntegrationTests.Helpers;
using Portly.Protocol;

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
    }
}

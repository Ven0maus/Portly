using Portly.Protocol;
using Portly.Tests.Helpers;
using System.Buffers.Binary;
using PacketType = Portly.Tests.Objects.PacketType;

namespace Portly.Tests.FuzzTests
{
    [Parallelizable(ParallelScope.All)]
    internal class LengthPrefixProtocolFuzzTests
    {
        [Test]
        public async Task ReadPackets_Should_Handle_Random_Garbage_Stream()
        {
            var protocol = Tools.CreateProtocol();
            var random = new Random();

            for (int i = 0; i < 100; i++)
            {
                var data = new byte[random.Next(1, 1024)];
                random.NextBytes(data);

                using var stream = new MemoryStream(data);

                try
                {
                    await protocol.ReadPacketsAsync(stream, _ => Task.CompletedTask);
                }
                catch (IOException) { }
                catch (Exception) { }
            }

            Assert.Pass();
        }

        [Test]
        public async Task ReadPackets_Should_Reject_Invalid_Lengths()
        {
            var protocol = Tools.CreateProtocol();

            var invalidLengths = new[]
            {
                -1,
                int.MinValue,
                int.MaxValue,
                10_000_000 // beyond max
            };

            foreach (var len in invalidLengths)
            {
                var buffer = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(buffer, len);

                using var stream = new MemoryStream(buffer);

                Assert.ThrowsAsync<IOException>(async () =>
                {
                    await protocol.ReadPacketsAsync(stream, _ => Task.CompletedTask);
                });
            }
        }

        [Test]
        public async Task ReadPackets_Should_Fail_On_Truncated_Payload()
        {
            var protocol = Tools.CreateProtocol();

            var length = 100;
            var buffer = new byte[4 + 10]; // only partial payload

            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), length);

            using var stream = new MemoryStream(buffer);

            Assert.ThrowsAsync<IOException>(async () =>
            {
                await protocol.ReadPacketsAsync(stream, _ => Task.CompletedTask);
            });
        }

        [Test]
        public async Task ReadPackets_Should_Handle_Fragmented_Stream()
        {
            var protocol = Tools.CreateProtocol();
            var validPacket = Tools.CreateValidSerializedPacket();

            using var stream = new FragmentedStream(validPacket, chunkSize: 1);
            using var cts = new CancellationTokenSource();

            var packets = new List<Packet>();

            await protocol.ReadPacketsAsync(stream, p =>
            {
                packets.Add(p);

                // Stop immediately after first successful reconstruction
                cts.Cancel();

                return Task.CompletedTask;
            }, cts.Token);

            Assert.That(packets.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadPackets_Should_Reject_Replay_Attacks()
        {
            var protocol = Tools.CreateProtocol();

            var packetBytes = Tools.CreateValidSerializedPacket();

            using var stream = new MemoryStream([.. packetBytes, .. packetBytes]);

            // First read succeeds
            await protocol.ReceiveSinglePacketAsync(stream);

            // Second should fail
            Assert.ThrowsAsync<Exception>(async () =>
            {
                await protocol.ReceiveSinglePacketAsync(stream);
            });
        }

        [Test]
        public async Task Send_Receive_Should_Roundtrip_Random_Packets()
        {
            var protocol = Tools.CreateProtocol();
            var random = new Random();

            for (int i = 0; i < 50; i++)
            {
                var payload = new byte[random.Next(1, 512)];
                random.NextBytes(payload);

                var packet = Packet.Create(PacketType.Custom, payload);

                using var stream = new MemoryStream();

                await protocol.SendPacketAsync(stream, packet, encrypted: false);

                stream.Position = 0;

                var received = await protocol.ReceiveSinglePacketAsync(stream);

                Assert.That(received.Payload, Is.EqualTo(packet.Payload));
            }
        }

        [Test]
        public async Task ReadPackets_Should_Handle_KeepAlive_Flood()
        {
            var protocol = Tools.CreateProtocol();

            var keepAlive = new byte[4];
            var data = Enumerable.Repeat(keepAlive, 1000)
                .SelectMany(x => x)
                .ToArray();

            using var stream = new MemoryStream(data);
            using var cts = new CancellationTokenSource();

            int count = 0;

            await protocol.ReadPacketsAsync(stream, _ =>
            {
                count++;

                if (count == 1000)
                    cts.Cancel();

                return Task.CompletedTask;
            }, cts.Token);

            Assert.That(count, Is.EqualTo(1000));
        }

        [Test]
        public async Task Send_Should_Reject_Oversized_Packets()
        {
            var protocol = Tools.CreateProtocol(config =>
            {
                config.ConnectionSettings.MaxRequestSizeBytes = 100;
            });

            var payload = new byte[1000];
            var packet = Packet.Create(PacketType.Custom, payload);

            using var stream = new MemoryStream();

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await protocol.SendPacketAsync(stream, packet, encrypted: false);
            });
        }

        [Test]
        public async Task ReadPackets_Should_Fail_On_Partial_LengthPrefix()
        {
            var protocol = Tools.CreateProtocol();

            var partialHeader = new byte[] { 0x00, 0x00 }; // only 2 bytes

            using var stream = new MemoryStream(partialHeader);

            Assert.ThrowsAsync<IOException>(async () =>
            {
                await protocol.ReadPacketsAsync(stream, _ => Task.CompletedTask);
            });
        }

        [Test]
        public async Task ReadPackets_Should_Handle_Multiple_Valid_Packets()
        {
            var protocol = Tools.CreateProtocol();

            var p1 = Tools.CreateValidSerializedPacket();
            var p2 = Tools.CreateValidSerializedPacket();

            using var stream = new MemoryStream([.. p1, .. p2]);
            using var cts = new CancellationTokenSource();

            int count = 0;

            await protocol.ReadPacketsAsync(stream, _ =>
            {
                count++;

                if (count == 2)
                    cts.Cancel();

                return Task.CompletedTask;
            }, cts.Token);

            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void ReadPackets_Should_Timeout_Mid_Packet()
        {
            var protocol = Tools.CreateProtocol(config =>
            {
                config.ConnectionSettings.IdleTimeoutSeconds = 1;
            });

            var validPacket = Tools.CreateValidSerializedPacket();

            // First few bytes fast, then stall
            var stream = new SlowPartialStreamWithStall(
                validPacket,
                fastBytes: 2,
                slowDelayMs: 2000);

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
            {
                await protocol.ReadPacketsAsync(stream, _ => Task.CompletedTask);
            });
        }

        [Test]
        public async Task ReadPackets_Should_Fail_On_Corrupted_Length()
        {
            var protocol = Tools.CreateProtocol();

            var valid = Tools.CreateValidSerializedPacket();

            // Corrupt the length prefix (first 4 bytes)
            valid[0] = 0xFF;
            valid[1] = 0xFF;
            valid[2] = 0xFF;
            valid[3] = 0xFF;

            using var stream = new MemoryStream(valid);

            Assert.ThrowsAsync<IOException>(async () =>
            {
                await protocol.ReceiveSinglePacketAsync(stream);
            });
        }

        [Test]
        public async Task SendReceive_Should_Work_With_Encryption()
        {
            var protocol = Tools.CreateProtocol();

            var packet = Packet.Create(PacketType.Custom, "secure");

            using var stream = new MemoryStream();

            await protocol.SendPacketAsync(stream, packet, encrypted: true);

            stream.Position = 0;

            var received = await protocol.ReceiveSinglePacketAsync(stream);

            var expectedBytes = System.Text.Encoding.UTF8.GetBytes("secure");

            Assert.That(received.As<string>().Payload, Is.EqualTo(expectedBytes));
        }

        [Test]
        public void ReadPackets_Should_Fail_On_Empty_Stream()
        {
            var protocol = Tools.CreateProtocol();

            using var stream = new MemoryStream([]);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

            Assert.ThrowsAsync<IOException>(async () =>
            {
                await protocol.ReadPacketsAsync(stream, _ => Task.CompletedTask, cts.Token);
            });
        }
    }
}

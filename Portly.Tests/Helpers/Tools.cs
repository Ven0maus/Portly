using Portly.Abstractions;
using Portly.Infrastructure;
using Portly.Infrastructure.Configuration;
using Portly.Protocol;
using Portly.Protocol.Processing;
using Portly.Protocol.Serialization;
using System.Buffers.Binary;
using PacketType = Portly.Tests.Objects.PacketType;

namespace Portly.Tests.Helpers
{
    /// <summary>
    /// Contains a set of useful tools / helper methods.
    /// </summary>
    internal static class Tools
    {
        internal static (string serverDir, string clientDir) CreateIsolatedTestDirectories()
        {
            var mainFolder = Path.Combine("PortlyTests", $"{Guid.NewGuid()}");
            var clientFolder = Path.Combine(mainFolder, "client");
            var serverFolder = Path.Combine(mainFolder, "server");

            Directory.CreateDirectory(clientFolder);
            Directory.CreateDirectory(serverFolder);

            return (serverFolder, clientFolder);
        }

        public static LengthPrefixedPacketProtocol CreateProtocol(
            Action<ServerConfiguration>? configure = null)
        {
            var config = new ServerConfiguration();

            // sensible defaults
            config.ConnectionSettings.MaxRequestSizeBytes = 1024 * 1024; // 1 MB
            config.ConnectionSettings.IdleTimeoutSeconds = 30;
            config.ConnectionSettings.WriteTimeoutSeconds = 30;

            configure?.Invoke(config);

            return new LengthPrefixedPacketProtocol(
                config,
                new MessagePackSerializationProvider(),
                null);
        }

        internal static byte[] CreateValidSerializedPacket(
            Packet? packet = null,
            IPacketSerializationProvider? serializer = null)
        {
            serializer ??= new MessagePackSerializationProvider();

            packet ??= Packet.Create(PacketType.Custom, "test");

            // 1. Serialize outer packet payload
            var payload = serializer.Serialize(packet, CancellationToken.None);

            // 2. Create transport packet with valid nonce + timestamp
            var (nonce, timestamp) = ReplayProtection.CreateNonceWithTimestamp();

            var transportPacket = new TransportPacket
            {
                Payload = payload,
                Nonce = nonce,
                CreationTimestampUtc = timestamp,
                Encrypted = false
            };

            // 3. Serialize transport packet
            var transportBytes = TransportPacketSerializer.Serialize(transportPacket, CancellationToken.None);

            // 4. Add length prefix
            var buffer = new byte[4 + transportBytes.Length];

            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), transportBytes.Length);
            transportBytes.CopyTo(buffer.AsSpan(4));

            return buffer;
        }
    }
}

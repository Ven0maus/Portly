using MessagePack;
using Portly.Core.PacketHandling;

namespace Portly.Core.Serialization
{
    internal static class TransportPacketSerializer
    {
        private static readonly MessagePackSerializerOptions _messagePackSerializerOptions = MessagePackSerializerOptions.Standard
            .WithSecurity(MessagePackSecurity.UntrustedData);

        internal static TransportPacket Deserialize(in ReadOnlyMemory<byte> bytes, CancellationToken token = default)
        {
            return MessagePackSerializer.Deserialize<TransportPacket>(bytes, _messagePackSerializerOptions, cancellationToken: token);
        }

        internal static byte[] Serialize(TransportPacket data, CancellationToken token = default)
        {
            // Default provider serializes to Packet type for message packet
            return MessagePackSerializer.Serialize(data, options: _messagePackSerializerOptions, token);
        }
    }
}

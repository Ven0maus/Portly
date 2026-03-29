using MessagePack;
using Portly.Abstractions;
using Portly.Protocol;

namespace Portly.Protocol.Serialization
{
    /// <summary>
    /// Uses message pack as serialization provider
    /// </summary>
    public class MessagePackSerializationProvider : IPacketSerializationProvider
    {
        private static readonly MessagePackSerializerOptions _messagePackSerializerOptions = MessagePackSerializerOptions.Standard
            .WithSecurity(MessagePackSecurity.UntrustedData);

        /// <inheritdoc/>
        public T Deserialize<T>(in ReadOnlyMemory<byte> bytes, CancellationToken token = default) where T : IPacket
        {
            return MessagePackSerializer.Deserialize<T>(bytes, _messagePackSerializerOptions, cancellationToken: token);
        }

        /// <inheritdoc/>
        public byte[] Serialize<T>(T data, CancellationToken token = default) where T : IPacket
        {
            // Default provider serializes to Packet type for message packet
            if (data is Packet)
                return MessagePackSerializer.Serialize(typeof(Packet), data, options: _messagePackSerializerOptions, token);
            else
                return MessagePackSerializer.Serialize(data, options: _messagePackSerializerOptions, token);
        }
    }
}

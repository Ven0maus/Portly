using MessagePack;

namespace Portly.Core.PacketHandling
{
    [MessagePackObject(SuppressSourceGeneration = true)]
    public class Packet
    {
        [Key(0)]
        public PacketIdentifier Identifier { get; init; }

        [Key(1)]
        public bool Encrypted { get; init; }

        [Key(2)]
        public required byte[] Payload { get; init; } = [];

        /// <summary>
        /// Stores the serialized byte array from the entire packet, for caching purposes when sending to multiple clients.
        /// </summary>
        [IgnoreMember]
        internal byte[]? SerializedPacket { get; set; }

        public T FromPayload<T>()
        {
            return MessagePackSerializer.Deserialize<T>(Payload, MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData));
        }
    }

    public class Packet<T> : Packet
    {
        private T? _payloadObj;

        public T PayloadObj => _payloadObj ??= FromPayload<T>();

        public static Packet<T> FromPayload(T payload)
        {
            try
            {
                return new Packet<T>
                {
                    _payloadObj = payload,
                    Payload = MessagePackSerializer.Serialize(payload, MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData))
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Type '{typeof(T)}' is not MessagePack-serializable", ex);
            }
        }
    }
}

using MessagePack;
using Portly.PacketHandling;

namespace Portly.Protocol
{
    /// <summary>
    /// Base packet implementation
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    public class Packet
    {
        /// <summary>
        /// The unique identifier for this packet.
        /// </summary>
        [Key(0)]
        public PacketIdentifier Identifier { get; init; }

        /// <summary>
        /// The payload of this packet in bytes.
        /// </summary>
        [Key(1)]
        public byte[] Payload { get; set; }

        [SerializationConstructor]
        internal Packet(PacketIdentifier identifier, byte[] payload)
        {
            Identifier = identifier;
            Payload = payload;
        }

        /// <summary>
        /// Converts the packet's payload into a typed payload.
        /// <br>Note: You can use the packet router to register typed packet handlers.</br>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Packet<T> As<T>() => new(Identifier, Payload);

        /// <summary>
        /// Creates a packet of the specified type, any MessagePack supported object can be used.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="identifier"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        public static Packet<T> Create<T>(PacketIdentifier identifier, T payload)
        {
            return Packet<T>.Create(identifier, payload);
        }

        /// <summary>
        /// Creates a packet of the specified type, any MessagePack supported object can be used.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TEnum"></typeparam>
        /// <param name="identifier"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        public static Packet<T> Create<T, TEnum>(TEnum identifier, T payload) where TEnum : Enum
            => Create((PacketIdentifier)identifier, payload);
    }

    /// <summary>
    /// Generic packet implementation
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class Packet<T> : Packet
    {
        private T? _payloadObj;
        /// <summary>
        /// The payload of the packet as a generic typed object.
        /// </summary>
        [IgnoreMember]
        public new T Payload => _payloadObj ??= MessagePackSerializer.Deserialize<T>(base.Payload, MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData));

        internal Packet(PacketIdentifier identifier, byte[] payload)
            : base(identifier, payload)
        { }

        /// <summary>
        /// Creates a new generic typed packet.
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static Packet<T> Create(PacketIdentifier identifier, T payload)
        {
            try
            {
                var serializedPayload = payload is byte[] bytePayload ? bytePayload :
                    MessagePackSerializer.Serialize(payload,
                        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData));

                return new Packet<T>(identifier, serializedPayload)
                {
                    _payloadObj = payload,
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Type '{typeof(T)}' is not MessagePack-serializable", ex);
            }
        }

        /// <summary>
        /// Creates a new generic typed packet.
        /// </summary>
        /// <typeparam name="TEnum"></typeparam>
        /// <param name="identifier"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        public static Packet<T> Create<TEnum>(TEnum identifier, T payload) where TEnum : Enum
            => Create((PacketIdentifier)identifier, payload);
    }
}

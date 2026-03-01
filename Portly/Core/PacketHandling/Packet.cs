using MessagePack;
using Portly.PacketHandling;

namespace Portly.Core.PacketHandling
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

        [IgnoreMember]
        internal byte[] _payloadBackingField = [];

        /// <summary>
        /// The payload of this packet in bytes.
        /// </summary>
        [Key(1)]
        public byte[] Payload
        {
            get => _payloadBackingField;
            init => _payloadBackingField = value; // public can only init
        }

        /// <summary>
        /// Determines if the packet is encrypted or not.
        /// </summary>
        [Key(2)]
        public bool Encrypted { get; init; }

        /// <summary>
        /// Stores the serialized byte array from the entire packet, for caching purposes when sending to multiple clients.
        /// </summary>
        [IgnoreMember]
        internal byte[]? SerializedPacket { get; set; }

        [SerializationConstructor]
        internal Packet(PacketIdentifier identifier, byte[] payload, bool encrypted)
        {
            Identifier = identifier;
            Payload = payload;
            Encrypted = encrypted;
        }

        /// <summary>
        /// Convert to a generic typed packet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Packet<T> As<T>()
        {
            var packet = new Packet<T>(Identifier, Payload, Encrypted)
            {
                SerializedPacket = SerializedPacket
            };
            return packet;
        }

        /// <summary>
        /// Creates a packet of the specified type, any MessagePack supported object can be used.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="identifier"></param>
        /// <param name="payload"></param>
        /// <param name="encrypted"></param>
        /// <returns></returns>
        public static Packet<T> Create<T>(PacketIdentifier identifier, T payload, bool encrypted)
        {
            return Packet<T>.Create(identifier, payload, encrypted);
        }

        /// <summary>
        /// Creates a packet of the specified type, any MessagePack supported object can be used.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TEnum"></typeparam>
        /// <param name="identifier"></param>
        /// <param name="payload"></param>
        /// <param name="encrypted"></param>
        /// <returns></returns>
        public static Packet<T> Create<T, TEnum>(TEnum identifier, T payload, bool encrypted) where TEnum : Enum
            => Create((PacketIdentifier)identifier, payload, encrypted);
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
        public new T Payload => _payloadObj ??= MessagePackSerializer.Deserialize<T>(base.Payload, MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData));

        internal Packet(PacketIdentifier identifier, byte[] payload, bool encrypted)
            : base(identifier, payload, encrypted)
        { }

        /// <summary>
        /// Creates a new generic typed packet.
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="payload"></param>
        /// <param name="encrypted"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static Packet<T> Create(PacketIdentifier identifier, T payload, bool encrypted)
        {
            try
            {
                var serializedPayload = payload is byte[] bytePayload ? bytePayload :
                    MessagePackSerializer.Serialize(payload,
                        MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData));

                return new Packet<T>(identifier, serializedPayload, encrypted)
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
        /// <param name="encrypted"></param>
        /// <returns></returns>
        public static Packet<T> Create<TEnum>(TEnum identifier, T payload, bool encrypted) where TEnum : Enum
            => Create((PacketIdentifier)identifier, payload, encrypted);
    }
}

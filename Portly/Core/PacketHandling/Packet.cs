using MessagePack;
using Portly.Core.Interfaces;
using Portly.Core.Networking;
using Portly.PacketHandling;

namespace Portly.Core.PacketHandling
{
    /// <summary>
    /// Base packet implementation
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    public class Packet : IPacket
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

        [IgnoreMember]
        private bool _encrypted;
        /// <summary>
        /// Determines if the packet is encrypted or not.
        /// </summary>
        [Key(2)]
        public bool Encrypted
        {
            get => _encrypted;
            init
            {
                _encrypted = value;
            }
        }

        /// <summary>
        /// The nonce of the packet.
        /// </summary>
        [Key(3)]
        public string? Nonce { get; init; }

        /// <summary>
        /// The timestamp the packet was created in UTC.
        /// </summary>
        [Key(4)]
        public DateTime? CreationTimestampUtc { get; init; }

        [SerializationConstructor]
        internal Packet(PacketIdentifier identifier, byte[] payload, bool encrypted, string? nonce = null, DateTime? creationTimestampUtc = null)
        {
            Identifier = identifier;
            Payload = payload;
            Encrypted = encrypted;

            if (nonce == null || creationTimestampUtc == null)
            {
                (Nonce, CreationTimestampUtc) = ReplayProtection.CreateNonceWithTimestamp();
            }
            else
            {
                Nonce = nonce;
                CreationTimestampUtc = creationTimestampUtc;
            }
        }

        /// <summary>
        /// Convert to a generic typed packet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Packet<T> As<T>() => new(Identifier, Payload, Encrypted, Nonce, CreationTimestampUtc);

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

        /// <inheritdoc/>
        public void Encrypt(IEncryptionProvider? encryptionProvider)
        {
            if (Encrypted || encryptionProvider == null) return;
            Payload = encryptionProvider.Encrypt(Payload);
            _encrypted = true;
        }

        /// <inheritdoc/>
        public void Decrypt(IEncryptionProvider? encryptionProvider)
        {
            if (!Encrypted || encryptionProvider == null) return;
            Payload = encryptionProvider.Decrypt(Payload);
            _encrypted = false;
        }
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

        internal Packet(PacketIdentifier identifier, byte[] payload, bool encrypted, string? nonce, DateTime? creationTimestampUtc)
            : base(identifier, payload, encrypted, nonce, creationTimestampUtc)
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

                return new Packet<T>(identifier, serializedPayload, false, null, null)
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

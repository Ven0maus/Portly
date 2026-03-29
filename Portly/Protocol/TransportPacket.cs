using MessagePack;
using Portly.Abstractions;

namespace Portly.Protocol
{
    /// <summary>
    /// Internal packet structure used for non-exposed transport metadata per packet.
    /// </summary>
    [MessagePackObject(AllowPrivate = true)]
    internal sealed class TransportPacket
    {
        [Key(0)]
        public required byte[] Payload
        {
            get => _payload ??= [];
            init => _payload = value;
        }

        [Key(1)]
        public string? Nonce { get; init; }

        [Key(2)]
        public DateTime? CreationTimestampUtc { get; init; }

        [Key(3)]
        public bool Encrypted
        {
            get => _encrypted;
            init
            {
                _encrypted = value;
            }
        }

        [IgnoreMember]
        private bool _encrypted;
        [IgnoreMember]
        private byte[]? _payload;

        /// <summary>
        /// Encrypts the payload.
        /// </summary>
        /// <param name="encryptionProvider"></param>
        public void Encrypt(IEncryptionProvider? encryptionProvider)
        {
            if (Encrypted || encryptionProvider == null) return;
            _payload = encryptionProvider.Encrypt(Payload);
            _encrypted = true;
        }

        /// <summary>
        /// Decrypts the payload.
        /// </summary>
        /// <param name="encryptionProvider"></param>
        public void Decrypt(IEncryptionProvider? encryptionProvider)
        {
            if (!Encrypted || encryptionProvider == null) return;
            _payload = encryptionProvider.Decrypt(Payload);
            _encrypted = false;
        }
    }
}

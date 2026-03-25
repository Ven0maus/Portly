using Portly.PacketHandling;

namespace Portly.Core.Interfaces
{
    /// <summary>
    /// Packet implementation
    /// </summary>
    public interface IPacket
    {
        /// <summary>
        /// The unique identifier for this packet.
        /// </summary>
        PacketIdentifier Identifier { get; init; }

        /// <summary>
        /// The payload of this packet in bytes.
        /// </summary>
        public byte[] Payload { get; set; }

        /// <summary>
        /// The nonce of the packet.
        /// </summary>
        string? Nonce { get; init; }

        /// <summary>
        /// The timestamp the packet was created in UTC.
        /// </summary>
        DateTime? CreationTimestampUtc { get; init; }

        /// <summary>
        /// Determines if the packet is encrypted or not.
        /// </summary>
        bool Encrypted { get; init; }

        /// <summary>
        /// Encrypts the payload.
        /// </summary>
        void Encrypt(IEncryptionProvider? encryptionProvider);

        /// <summary>
        /// Decrypts the payload.
        /// </summary>
        void Decrypt(IEncryptionProvider? encryptionProvider);
    }
}

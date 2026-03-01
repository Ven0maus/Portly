using Portly.Core.PacketHandling;
using System.Security.Cryptography;

namespace Portly.Core.Authentication.Encryption
{
    internal sealed class AesPacketCrypto(byte[] key) : IPacketCrypto
    {
        private readonly byte[] _key = key;

        public Packet Encrypt(Packet packet)
        {
            if (!packet.Encrypted)
                return packet;

            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] ciphertext = new byte[packet.Payload.Length];
            byte[] tag = new byte[16];

            using var aes = new AesGcm(_key, tag.Length);
            aes.Encrypt(nonce, packet.Payload, ciphertext, tag);

            byte[] combined = new byte[12 + 16 + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, 12);
            Buffer.BlockCopy(tag, 0, combined, 12, 16);
            Buffer.BlockCopy(ciphertext, 0, combined, 28, ciphertext.Length);

            packet._payloadBackingField = combined;
            packet.SerializedPacket = null; // IMPORTANT

            return packet;
        }

        public Packet Decrypt(Packet packet)
        {
            if (!packet.Encrypted)
                return packet;

            var payload = packet.Payload;

            byte[] nonce = payload[..12];
            byte[] tag = payload[12..28];
            byte[] ciphertext = payload[28..];

            byte[] plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(_key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            packet._payloadBackingField = plaintext;
            return packet;
        }
    }
}

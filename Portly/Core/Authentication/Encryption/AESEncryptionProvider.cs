using Portly.Core.Interfaces;
using System.Security.Cryptography;

namespace Portly.Core.Authentication.Encryption
{
    internal sealed class AESEncryptionProvider(byte[] key) : IEncryptionProvider
    {
        private readonly byte[] _key = key;

        public byte[] Encrypt(byte[] payload)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] ciphertext = new byte[payload.Length];
            byte[] tag = new byte[16];

            using var aes = new AesGcm(_key, tag.Length);
            aes.Encrypt(nonce, payload, ciphertext, tag);

            byte[] combined = new byte[12 + 16 + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, combined, 0, 12);
            Buffer.BlockCopy(tag, 0, combined, 12, 16);
            Buffer.BlockCopy(ciphertext, 0, combined, 28, ciphertext.Length);

            return combined;
        }

        public byte[] Decrypt(byte[] encryptedPayload)
        {
            byte[] nonce = encryptedPayload[..12];
            byte[] tag = encryptedPayload[12..28];
            byte[] ciphertext = encryptedPayload[28..];

            byte[] decryptedPayload = new byte[ciphertext.Length];

            using var aes = new AesGcm(_key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, decryptedPayload);

            return decryptedPayload;
        }
    }
}

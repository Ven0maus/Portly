using System.Security.Cryptography;

namespace Portly.Authentication.Encryption
{
    /// <summary>
    /// Standard EC Diffie hellman key exchange for encryption using AES
    /// </summary>
    internal sealed class EncryptionKeyExchange : IDisposable
    {
        private readonly ECDiffieHellman _ecdh;
        public byte[] PublicKey { get; }

        public EncryptionKeyExchange()
        {
            _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            PublicKey = _ecdh.ExportSubjectPublicKeyInfo();
        }

        public byte[] DeriveSharedKey(byte[] otherPublicKey)
        {
            using var other = ECDiffieHellman.Create();
            other.ImportSubjectPublicKeyInfo(otherPublicKey, out _);

            byte[] sharedSecret = _ecdh.DeriveKeyMaterial(other.PublicKey);

            // Normalize to fixed-size AES key
            return SHA256.HashData(sharedSecret);
        }

        public void Dispose()
        {
            _ecdh.Dispose();
        }
    }
}

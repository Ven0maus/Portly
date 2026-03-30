using System.Security.Cryptography;
using System.Text.Json;

namespace Portly.Security.Trust
{
    /// <summary>
    /// Manages the server’s cryptographic identity and proves it to clients during connection handshakes.
    /// </summary>
    internal class TrustServer
    {
        private const string KEY_STORAGE_PATH = "server_key.json";
        private readonly ECDsa _keyPair;
        private readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };
        private readonly string _folder;

        public TrustServer(string? folder = null)
        {
            _folder = folder ?? string.Empty;
            _keyPair = LoadOrCreateKeyPair();
        }

        private string GetFile(string path)
        {
            return Path.Combine(_folder, path);
        }

        public byte[] GetPublicKey()
            => _keyPair.ExportSubjectPublicKeyInfo();

        public byte[] SignChallenge(byte[] challenge)
            => _keyPair.SignData(challenge, HashAlgorithmName.SHA256);

        private ECDsa LoadOrCreateKeyPair()
        {
            if (File.Exists(GetFile(KEY_STORAGE_PATH)) && TryLoadKeyPair(out var keypair))
                return keypair!;

            keypair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var keyData = new KeyPairData
            {
                PrivateKey = Convert.ToBase64String(keypair.ExportECPrivateKey()),
                PublicKey = Convert.ToBase64String(keypair.ExportSubjectPublicKeyInfo())
            };

            using var writeStream = new FileStream(
                GetFile(KEY_STORAGE_PATH),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            JsonSerializer.Serialize(writeStream, keyData, _serializerOptions);

            return keypair;
        }

        private bool TryLoadKeyPair(out ECDsa? keypair)
        {
            keypair = null;
            using var readStream = new FileStream(
                GetFile(KEY_STORAGE_PATH),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var data = JsonSerializer.Deserialize<KeyPairData>(readStream);
            if (data != null && !string.IsNullOrWhiteSpace(data.PrivateKey))
            {
                var ecdsa = ECDsa.Create();
                ecdsa.ImportECPrivateKey(
                    Convert.FromBase64String(data.PrivateKey),
                    out _
                );

                keypair = ecdsa;
                return true;
            }
            return false;
        }

        private sealed class KeyPairData
        {
            public required string PrivateKey { get; set; }
            public required string PublicKey { get; set; }
        }
    }
}
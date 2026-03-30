using System.Security.Cryptography;
using System.Text.Json;

namespace Portly.Security.Trust
{
    /// <summary>
    /// Manages the server’s cryptographic identity and proves it to clients during connection handshakes.
    /// </summary>
    internal class TrustServer
    {
        // TODO: Make this injectable/changeable so tests can store in their own directories
        // Ideally this would be a directory that can be defined in the top scope, and all files go there not just the trust files.
        private const string KEY_STORAGE_PATH = "server_key.json";
        private readonly ECDsa _keyPair;
        private readonly Lock _lock = new();
        private readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

        public TrustServer()
        {
            _keyPair = LoadOrCreateKeyPair();
        }

        public byte[] GetPublicKey()
            => _keyPair.ExportSubjectPublicKeyInfo();

        public byte[] SignChallenge(byte[] challenge)
            => _keyPair.SignData(challenge, HashAlgorithmName.SHA256);

        private ECDsa LoadOrCreateKeyPair()
        {
            lock (_lock)
            {
                if (File.Exists(KEY_STORAGE_PATH) && TryLoadKeyPair(out var keypair))
                    return keypair!;

                keypair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var keyData = new KeyPairData
                {
                    PrivateKey = Convert.ToBase64String(keypair.ExportECPrivateKey()),
                    PublicKey = Convert.ToBase64String(keypair.ExportSubjectPublicKeyInfo())
                };

                try
                {
                    using var writeStream = new FileStream(
                        KEY_STORAGE_PATH,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);

                    JsonSerializer.Serialize(writeStream, keyData, _serializerOptions);
                }
                catch (IOException) when (File.Exists(KEY_STORAGE_PATH))
                {
                    // Another process created it -> load instead
                    if (TryLoadKeyPair(out var loaded))
                        return loaded!;

                    throw; // if load fails, something else is wrong
                }

                return keypair;
            }
        }

        private static bool TryLoadKeyPair(out ECDsa? keypair)
        {
            keypair = null;
            using var readStream = new FileStream(
                KEY_STORAGE_PATH,
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
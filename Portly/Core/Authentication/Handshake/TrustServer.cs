using System.Security.Cryptography;
using System.Text.Json;

namespace Portly.Core.Authentication.Handshake
{
    /// <summary>
    /// Manages the server’s cryptographic identity and proves it to clients during connection handshakes.
    /// </summary>
    internal class TrustServer
    {
        private const string KEY_STORAGE_PATH = "server_key.json";
        private readonly ECDsa _keyPair;
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
            if (File.Exists(KEY_STORAGE_PATH))
            {
                string json = File.ReadAllText(KEY_STORAGE_PATH);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonSerializer.Deserialize<KeyPairData>(json);

                    if (data != null && !string.IsNullOrWhiteSpace(data.PrivateKey))
                    {
                        var ecdsa = ECDsa.Create();
                        ecdsa.ImportECPrivateKey(
                            Convert.FromBase64String(data.PrivateKey),
                            out _
                        );

                        return ecdsa;
                    }
                }
            }

            var keypair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var keyData = new KeyPairData
            {
                PrivateKey = Convert.ToBase64String(keypair.ExportECPrivateKey()),
                PublicKey = Convert.ToBase64String(keypair.ExportSubjectPublicKeyInfo())
            };

            string newJson = JsonSerializer.Serialize(keyData, _serializerOptions);
            File.WriteAllText(KEY_STORAGE_PATH, newJson);

            return keypair;
        }

        private sealed class KeyPairData
        {
            public required string PrivateKey { get; set; }
            public required string PublicKey { get; set; }
        }
    }
}
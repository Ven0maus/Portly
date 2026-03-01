using System.Security.Cryptography;
using System.Text.Json;

namespace Portly.Authentication.Handshake
{
    /// <summary>
    /// Manages Trust-On-First-Use (TOFU) for servers from the client perspective. Its responsibility is to establish and maintain trust in a server’s identity across connections.
    /// </summary>
    internal class TrustClient
    {
        private const string SERVER_STORAGE_PATH = "known_servers.json";
        private readonly Dictionary<string, ServerInfo> _knownServers;
        private readonly Lock _lock = new();
        private readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

        public TrustClient()
        {
            _knownServers = LoadKnownServers();
        }

        public bool VerifyOrTrustServer(string host, int port, byte[] publicKey)
        {
            string key = $"{host}:{port}";
            string fingerprint = ComputeFingerprint(publicKey);

            if (_knownServers.TryGetValue(key, out var info))
                return info.Fingerprint == fingerprint;

            _knownServers[key] = new ServerInfo
            {
                Host = host,
                Port = port,
                Fingerprint = fingerprint
            };

            SaveKnownServers();
            return true;
        }

        private Dictionary<string, ServerInfo> LoadKnownServers()
        {
            if (!File.Exists(SERVER_STORAGE_PATH))
                return [];

            string json = File.ReadAllText(SERVER_STORAGE_PATH);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            var list = JsonSerializer.Deserialize<List<ServerInfo>>(json, _serializerOptions) ?? [];
            var dict = new Dictionary<string, ServerInfo>();
            foreach (var server in list)
                dict[$"{server.Host}:{server.Port}"] = server;

            return dict;
        }

        private static string ComputeFingerprint(byte[] publicKey)
        {
            var hash = SHA256.HashData(publicKey);
            return BitConverter.ToString(hash).Replace("-", ":");
        }

        private void SaveKnownServers()
        {
            lock (_lock)
            {
                var list = new List<ServerInfo>(_knownServers.Values);
                string json = JsonSerializer.Serialize(list, _serializerOptions);
                File.WriteAllText(SERVER_STORAGE_PATH, json);
            }
        }

        private class ServerInfo
        {
            public string Host { get; set; } = string.Empty; // IP or hostname
            public int Port { get; set; }
            public string Fingerprint { get; set; } = string.Empty;
        }
    }
}

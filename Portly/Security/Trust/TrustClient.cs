using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace Portly.Security.Trust
{
    /// <summary>
    /// Manages Trust-On-First-Use (TOFU) for servers from the client perspective. Its responsibility is to establish and maintain trust in a server’s identity across connections.
    /// </summary>
    internal class TrustClient
    {
        private const string SERVER_STORAGE_PATH = "known_servers.json";
        private readonly ConcurrentDictionary<string, ServerInfo> _knownServers;
        private readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };
        private readonly string _folder;

        public TrustClient(string? folder = null)
        {
            _folder = folder ?? string.Empty;
            _knownServers = LoadKnownServers();
        }

        public async Task<bool> VerifyOrTrustServer(string host, int port, byte[] publicKey)
        {
            string key = $"{host.ToLowerInvariant()}:{port}";
            string fingerprint = ComputeFingerprint(publicKey);

            if (_knownServers.TryGetValue(key, out var info))
            {
                return info.Fingerprint == fingerprint;
            }

            _knownServers[key] = new ServerInfo
            {
                Host = host,
                Port = port,
                Fingerprint = fingerprint
            };

            await SaveKnownServers();

            return true;
        }

        private ConcurrentDictionary<string, ServerInfo> LoadKnownServers()
        {
            if (!File.Exists(GetFile(SERVER_STORAGE_PATH)))
                return [];

            using var stream = new FileStream(
                GetFile(SERVER_STORAGE_PATH),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            if (stream.Length == 0)
                return [];

            var list = JsonSerializer.Deserialize<List<ServerInfo>>(stream, _serializerOptions) ?? [];

            var dict = new ConcurrentDictionary<string, ServerInfo>();
            foreach (var server in list)
                dict[$"{server.Host}:{server.Port}"] = server;

            return dict;
        }

        private static string ComputeFingerprint(byte[] publicKey)
        {
            var hash = SHA256.HashData(publicKey);
            return BitConverter.ToString(hash).Replace("-", ":");
        }

        private async Task SaveKnownServers()
        {
            List<ServerInfo> snapshot = [.. _knownServers.Values];

            using var stream = new FileStream(
                GetFile(SERVER_STORAGE_PATH),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            JsonSerializer.Serialize(stream, snapshot, _serializerOptions);
        }

        private string GetFile(string path)
        {
            return Path.Combine(_folder, path);
        }

        private class ServerInfo
        {
            public string Host { get; set; } = string.Empty; // IP or hostname
            public int Port { get; set; }
            public string Fingerprint { get; set; } = string.Empty;
        }
    }
}

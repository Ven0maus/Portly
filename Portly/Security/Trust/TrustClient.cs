using System.Collections.Concurrent;
using System.Diagnostics;
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
        private readonly Lock _lock = new();
        private readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };
        private static readonly Mutex _fileMutex = new Mutex(false, "Portly_KnownServers_FileMutex");

        public TrustClient()
        {
            _knownServers = LoadKnownServers();
        }

        public async Task<bool> VerifyOrTrustServer(string host, int port, byte[] publicKey)
        {
            string key = $"{host.ToLowerInvariant()}:{port}";
            string fingerprint = ComputeFingerprint(publicKey);

            bool isNewEntry;
            lock (_lock)
            {
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

                isNewEntry = true;
            }

            // Save outside lock (important)
            if (isNewEntry)
                await SaveKnownServers();

            return true;
        }

        private ConcurrentDictionary<string, ServerInfo> LoadKnownServers()
        {
            lock (_lock)
            {
                if (!File.Exists(SERVER_STORAGE_PATH))
                    return [];

                using var stream = new FileStream(
                    SERVER_STORAGE_PATH,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete); // Allows file being replaced after read

                if (stream.Length == 0)
                    return [];

                var list = JsonSerializer.Deserialize<List<ServerInfo>>(stream, _serializerOptions) ?? [];

                var dict = new ConcurrentDictionary<string, ServerInfo>();
                foreach (var server in list)
                    dict[$"{server.Host}:{server.Port}"] = server;

                return dict;
            }
        }

        private static string ComputeFingerprint(byte[] publicKey)
        {
            var hash = SHA256.HashData(publicKey);
            return BitConverter.ToString(hash).Replace("-", ":");
        }

        private async Task SaveKnownServers()
        {
            var tempPath = Path.GetFileNameWithoutExtension(SERVER_STORAGE_PATH) + $"_{Guid.NewGuid()}.tmp";

            List<ServerInfo> snapshot;

            lock (_lock)
            {
                snapshot = [.. _knownServers.Values];
            }

            _fileMutex.WaitOne();
            try
            {
                // write temp + move
                // Write temp file
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    JsonSerializer.Serialize(stream, snapshot, _serializerOptions);
                }

                var timeout = TimeSpan.FromSeconds(2);
                var sw = Stopwatch.StartNew();

                while (true)
                {
                    try
                    {
                        File.Move(tempPath, SERVER_STORAGE_PATH, overwrite: true);
                        return;
                    }
                    catch (IOException) when (sw.Elapsed < timeout)
                    {
                        await Task.Delay(Random.Shared.Next(5, 25)); // jitter
                    }
                }

                throw new IOException("Failed to update known servers file.");
            }
            finally
            {
                _fileMutex.ReleaseMutex();
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

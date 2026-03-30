using Portly.Abstractions;
using Portly.Infrastructure.Configuration.Serializers;
using Portly.Infrastructure.Logging;
using System.Net;

namespace Portly.Infrastructure.Configuration
{
    internal class ConfigurationService(ISerializer? serializer = null, ILogProvider? logProvider = null, string? folder = null)
    {
        private readonly ISerializer _serializer = serializer ?? new XmlProvider();
        private readonly ILogProvider? _logProvider = logProvider;
        private readonly string _folder = folder ?? string.Empty;

        public ServerConfiguration Load()
        {
            var file = LoadOrCreate<ConfigurationFile>(GetFile("server_config"));
            var configuration = new ServerConfiguration
            {
                RateLimits = file.RateLimits,
                IpBlacklist = LoadList(GetFile("ip-blacklist.txt")),
                IpWhitelist = LoadList(GetFile("ip-whitelist.txt"))
            };

            _logProvider?.Log("Loaded configuration files.", LogLevel.Debug);

            // Save for compatibility in newer versions (eg, new defaults added)
            Save(configuration);

            return configuration;
        }

        public void Save(ServerConfiguration config)
        {
            var file = new ConfigurationFile
            {
                RateLimits = config.RateLimits
            };

            _ = SaveOrCreate(GetFile("server_config"), file);

            SaveList(GetFile("ip-blacklist.txt"), config.IpBlacklist);
            SaveList(GetFile("ip-whitelist.txt"), config.IpWhitelist);

            _logProvider?.Log("Saved configuration files.", LogLevel.Debug);
        }

        private string GetFile(string path)
        {
            return Path.Combine(_folder, path);
        }

        private HashSet<IPAddress> LoadList(string fileName)
        {
            if (!File.Exists(fileName))
            {
                File.WriteAllText(fileName, string.Empty);
                return [];
            }

            var set = new HashSet<IPAddress>();

            foreach (var line in File.ReadLines(fileName))
            {
                var trimmed = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                    continue;

                if (IPAddress.TryParse(trimmed, out var ip))
                {
                    set.Add(ip.MapToIPv6());
                }
                else
                {
                    _logProvider?.Log($"Invalid IP in {fileName}: {trimmed}", LogLevel.Warning);
                }
            }

            return set;
        }

        private static void SaveList(string fileName, IEnumerable<IPAddress> values)
        {
            // Undo normalization back to IPv4
            File.WriteAllLines(fileName, values.Select(ip => ip.MapToIPv4().ToString()));
        }

        private T LoadOrCreate<T>(string filePathWithoutExtension) where T : new()
        {
            if (!filePathWithoutExtension.EndsWith(_serializer.FileExtension))
                filePathWithoutExtension += _serializer.FileExtension;

            string json;

            // Create a new configuration file with the default state
            if (!File.Exists(filePathWithoutExtension))
                return SaveOrCreate(filePathWithoutExtension, new T());

            try
            {
                json = File.ReadAllText(filePathWithoutExtension);
            }
            catch (Exception ex)
            {
                _logProvider?.Log($"Unable to read configuration file \"{Path.GetFileName(filePathWithoutExtension)}\": {ex.Message}", LogLevel.Error);

                // Just return a default state, but don't try to overwrite the file
                return new T();
            }

            try
            {
                return _serializer.Deserialize<T>(json) ?? new T();
            }
            catch (Exception ex)
            {
                _logProvider?.Log($"Unable to deserialize configuration file \"{Path.GetFileName(filePathWithoutExtension)}\": {ex.Message}", LogLevel.Error);

                // Overwrite the file with a default state because it is corrupted.
                return SaveOrCreate(filePathWithoutExtension, new T());
            }
        }

        private T SaveOrCreate<T>(string filePathWithoutExtension, T configFile) where T : new()
        {
            if (!filePathWithoutExtension.EndsWith(_serializer.FileExtension))
                filePathWithoutExtension += _serializer.FileExtension;

            string json;

            try
            {
                json = _serializer.Serialize(configFile);
            }
            catch (Exception ex)
            {
                _logProvider?.Log($"Unable to serialize configuration file \"{Path.GetFileName(filePathWithoutExtension)}\": {ex.Message}", LogLevel.Error);
                return configFile;
            }

            try
            {
                File.WriteAllText(filePathWithoutExtension, json);
            }
            catch (Exception ex)
            {
                _logProvider?.Log($"Unable to create configuration file \"{Path.GetFileName(filePathWithoutExtension)}\": {ex.Message}", LogLevel.Error);
                return configFile;
            }

            return configFile;
        }
    }
}

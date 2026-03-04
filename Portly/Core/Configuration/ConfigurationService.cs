using Portly.Core.Configuration.Serializers;
using Portly.Core.Interfaces;
using Portly.Core.Utilities.Logging;
using System.Net;

namespace Portly.Core.Configuration
{
    internal class ConfigurationService(ISerializer? serializer = null, ILogProvider? logProvider = null)
    {
        private readonly ISerializer _serializer = serializer ?? new XmlProvider();
        private readonly ILogProvider? _logProvider = logProvider;

        public ServerConfiguration Load()
        {
            var file = LoadOrCreate<ConfigurationFile>("server_config");
            var configuration = new ServerConfiguration
            {
                RateLimits = file.RateLimits,
                IpBlacklist = LoadList("ip-blacklist.txt"),
                IpWhitelist = LoadList("ip-whitelist.txt")
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

            _ = SaveOrCreate("server_config", file);

            SaveList("ip-blacklist.txt", config.IpBlacklist);
            SaveList("ip-whitelist.txt", config.IpWhitelist);

            _logProvider?.Log("Saved configuration files.", LogLevel.Debug);
        }

        private HashSet<IPAddress> LoadList(string fileName)
        {
            if (!File.Exists(fileName))
            {
                File.WriteAllText(fileName, string.Empty);
                return new HashSet<IPAddress>();
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
                    set.Add(ip);
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
            File.WriteAllLines(fileName, values.Select(ip => ip.ToString()));
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

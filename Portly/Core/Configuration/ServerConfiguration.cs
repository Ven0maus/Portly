using Portly.Core.Configuration.Serializers;
using Portly.Core.Configuration.Settings;
using Portly.Core.Interfaces;

namespace Portly.Core.Configuration
{
    /// <summary>
    /// Server configuration options
    /// </summary>
    public sealed class ServerConfiguration
    {
        /// <summary>
        /// Connection configuration options.
        /// </summary>
        public ConnectionSettings ConnectionSettings { get; set; } = new();
        /// <summary>
        /// Rate limit configuration options.
        /// </summary>
        public RateLimitSettings RateLimits { get; set; } = new();
        /// <summary>
        /// List of ips to be blocked on connection.
        /// </summary>
        public HashSet<string> IpBlacklist { get; set; } = new();
        /// <summary>
        /// Lists of ips to be exclusively allowed in the server, any others are denied on connection.
        /// </summary>
        public HashSet<string> IpWhitelist { get; set; } = new();

        /// <summary>
        /// Loads all configuration from disk.
        /// </summary>
        public static ServerConfiguration Load(ISerializer? serializer = null, ILogProvider? logProvider = null)
            => new ConfigurationService(serializer, logProvider).Load();

        /// <summary>
        /// Saves all configuration to disk.
        /// </summary>
        public void Save(ISerializer? serializer = null, ILogProvider? logProvider = null)
            => new ConfigurationService(serializer, logProvider).Save(this);

        /// <summary>
        /// Executes any validations on set values.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Validate()
        {
            var errors = new List<string>();

            if (ConnectionSettings.ConnectTimeoutSeconds <= 0)
                errors.Add($"ConnectTimeoutSeconds is invalid ({ConnectionSettings.ConnectTimeoutSeconds}), must be > 0");

            if (ConnectionSettings.WriteTimeoutSeconds <= 0)
                errors.Add($"WriteTimeoutSeconds is invalid ({ConnectionSettings.WriteTimeoutSeconds}), must be > 0");

            if (ConnectionSettings.KeepAliveIntervalSeconds <= 0)
                errors.Add($"KeepAliveIntervalSeconds is invalid ({ConnectionSettings.KeepAliveIntervalSeconds}), must be > 0");

            if (ConnectionSettings.KeepAliveTimeoutSeconds < ConnectionSettings.KeepAliveIntervalSeconds)
                errors.Add($"KeepAliveTimeoutSeconds is invalid ({ConnectionSettings.KeepAliveTimeoutSeconds}), must be >= KeepAliveIntervalSeconds ({ConnectionSettings.KeepAliveIntervalSeconds})");

            if (ConnectionSettings.IdleTimeoutSeconds <= 0)
                errors.Add($"IdleTimeoutSeconds is invalid ({ConnectionSettings.IdleTimeoutSeconds}), must be > 0");

            if (ConnectionSettings.IdleTimeoutSeconds < ConnectionSettings.KeepAliveTimeoutSeconds)
                errors.Add($"IdleTimeoutSeconds ({ConnectionSettings.IdleTimeoutSeconds}) should be >= KeepAliveTimeoutSeconds ({ConnectionSettings.KeepAliveTimeoutSeconds})");

            if (errors.Count > 0)
                throw new Exception("Configuration validation failed:\n" + string.Join("\n", errors.Select(a => "- " + a)));
        }
    }
}

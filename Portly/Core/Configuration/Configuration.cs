using Portly.Core.Configuration.Serializers;
using Portly.Core.Configuration.Settings;
using Portly.Core.Interfaces;

namespace Portly.Core.Configuration
{
    /// <summary>
    /// Server configuration options
    /// </summary>
    public sealed class Configuration
    {
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
        public static Configuration Load(ISerializer? serializer = null, ILogProvider? logProvider = null)
            => new ConfigurationService(serializer, logProvider).Load();

        /// <summary>
        /// Saves all configuration to disk.
        /// </summary>
        public void Save(ISerializer? serializer = null, ILogProvider? logProvider = null)
            => new ConfigurationService(serializer, logProvider).Save(this);
    }
}

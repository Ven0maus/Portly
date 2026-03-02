using Portly.Core.Configuration.Settings;

namespace Portly.Core.Configuration
{
    /// <summary>
    /// Used for serialization container for the server_config file. Use <see cref="Configuration"/> instead.
    /// </summary>
    public sealed class ConfigurationFile
    {
        /// <summary>
        /// Use <see cref="Configuration"/> instead.
        /// </summary>
        public RateLimitSettings RateLimits { get; set; } = new();
    }
}

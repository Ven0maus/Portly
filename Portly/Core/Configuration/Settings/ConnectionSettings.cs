namespace Portly.Core.Configuration.Settings
{
    /// <summary>
    /// Connection settings
    /// </summary>
    public class ConnectionSettings
    {
        // TODO: Implement these
        /// <summary>
        /// MaxConnections
        /// </summary>
        public int MaxConnections { get; set; } = 20;

        /// <summary>
        /// MaxConnectionsPerIp
        /// </summary>
        public int MaxConnectionsPerIp { get; set; } = 1;

        /// <summary>
        /// ConnectTimeout
        /// </summary>
        public TimeSpan? ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// IdleTimeout
        /// </summary>
        public TimeSpan? IdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// ReadTimeout
        /// </summary>
        public TimeSpan? ReadTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// WriteTimeout
        /// </summary>
        public TimeSpan? WriteTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// KeepAliveTimeout
        /// </summary>
        public TimeSpan? KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// MaxRequestSizeBytes
        /// </summary>
        public int MaxRequestSizeBytes { get; set; } = 64 * 1024; // 64 kb

        /// <summary>
        /// No TCP Delay
        /// </summary>
        public bool NoTcpDelay { get; set; } = true;
    }
}

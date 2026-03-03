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
        /// Maximum number of incoming connections that can be queued in the pre-handshake (pending) state
        /// before new connections are rejected. This helps absorb short connection spikes while protecting
        /// the server from overload.
        /// </summary>
        public int MaxPendingConnectionBacklog { get; set; } = 100;

        /// <summary>
        /// ConnectTimeout
        /// </summary>
        public int ConnectTimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// How long a read receiver can receive no data, can be 0
        /// </summary>
        public int IdleTimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// WriteTimeout
        /// </summary>
        public int WriteTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// How often keep alive packet is send
        /// </summary>
        public int KeepAliveIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// How long no keepalive is received until connection times out
        /// </summary>
        public int KeepAliveTimeoutSeconds { get; set; } = 60;

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

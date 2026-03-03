namespace Portly.Core.Configuration.Settings
{
    /// <summary>
    /// Connection settings
    /// </summary>
    public class ConnectionSettings
    {
        /// <summary>
        /// Maximum simultaneous connections allowed
        /// </summary>
        public int MaxConnections { get; set; } = 500;

        /// <summary>
        /// Maximum simultaneous connections per single IP
        /// </summary>
        public int MaxConnectionsPerIp { get; set; } = 5;

        /// <summary>
        /// Maximum number of incoming connections queued while waiting for handshake
        /// </summary>
        public int MaxPendingConnectionBacklog { get; set; } = 200;

        /// <summary>
        /// Maximum time allowed for TCP connect before failing
        /// </summary>
        public int ConnectTimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// Maximum idle time without receiving any packets (including KeepAlive)
        /// </summary>
        public int IdleTimeoutSeconds { get; set; } = 180;

        /// <summary>
        /// Maximum time allowed for sending a packet
        /// </summary>
        public int WriteTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Interval at which KeepAlive packets are sent if no other traffic occurs
        /// </summary>
        public int KeepAliveIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// Time without receiving KeepAlive or any packet before disconnecting
        /// </summary>
        public int KeepAliveTimeoutSeconds { get; set; } = 90;

        /// <summary>
        /// Maximum allowed size of a single request/packet
        /// </summary>
        public int MaxRequestSizeBytes { get; set; } = 64 * 1024; // 64 KB

        /// <summary>
        /// TCP_NODELAY to reduce latency at the cost of more packets
        /// </summary>
        public bool NoTcpDelay { get; set; } = true;
    }
}

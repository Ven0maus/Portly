namespace Portly.Core.Configuration.Settings
{
    /// <summary>
    /// Rate limit settings
    /// </summary>
    public class RateLimitSettings
    {
        /// <summary>
        /// The max amount of packets that can be send per second before being rate limited.
        /// </summary>
        public int MaxPacketsPerSecond { get; set; } = 20;

        /// <summary>
        /// The max amount of packets that can be send in a short burst of packets above the sustained rate without being immediately rate limited.
        /// </summary>
        public int MaxPacketsPerBurst { get; set; } = 40;

        /// <summary>
        /// The max amount of bytes that can be send per second before being rate limited.
        /// </summary>
        public int MaxBytesPerSecond { get; set; } = 1000;

        /// <summary>
        /// The max amount of bytes that can be send in a short burst of bytes above the sustained rate without being immediately rate limited.
        /// </summary>
        public int MaxBytesPerBurst { get; set; } = 2000;

        /// <summary>
        /// How long a packet is valid to be handled, if not handled for more than 5 minutes it will be flagged as a possible replay and ignored.
        /// </summary>
        public int RequestsValidForMaxMinutes { get; set; } = 5;
    }
}

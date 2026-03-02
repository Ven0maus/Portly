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
        public int MaxPacketsPerSecond = 20;

        /// <summary>
        /// The max amount of packets that can be send in a short burst of packets above the sustained rate without being immediately rate limited.
        /// </summary>
        public int MaxPacketsPerBurst = 40;

        /// <summary>
        /// The max amount of bytes that can be send per second before being rate limited.
        /// </summary>
        public int MaxBytesPerSecond = 1000;

        /// <summary>
        /// The max amount of bytes that can be send in a short burst of bytes above the sustained rate without being immediately rate limited.
        /// </summary>
        public int MaxBytesPerBurst = 2000;
    }
}

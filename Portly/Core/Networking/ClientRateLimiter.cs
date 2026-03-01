using Portly.Core.Configuration;

namespace Portly.Core.Networking
{
    /// <summary>
    /// A rate limiting implementation that handles both packet and bandwith limiting.
    /// </summary>
    internal sealed class ClientRateLimiter
    {
        private readonly double _packetsPerSecond;
        private readonly double _maxPacketBurst;
        private double _availablePackets;

        private readonly double _bytesPerSecond;
        private readonly double _maxByteBurst;
        private double _availableBytes;

        private long _lastRefillTicks;

        public ClientRateLimiter(ServerSettings.RateLimiting rateLimitSettings)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimitSettings.MaxPacketsPerSecond);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimitSettings.MaxPacketsPerBurst);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimitSettings.MaxBytesPerSecond);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimitSettings.MaxBytesPerBurst);

            _packetsPerSecond = rateLimitSettings.MaxPacketsPerSecond;
            _maxPacketBurst = rateLimitSettings.MaxPacketsPerBurst;
            _availablePackets = rateLimitSettings.MaxPacketsPerBurst;

            _bytesPerSecond = rateLimitSettings.MaxBytesPerSecond;
            _maxByteBurst = rateLimitSettings.MaxBytesPerBurst;
            _availableBytes = rateLimitSettings.MaxBytesPerBurst;

            _lastRefillTicks = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Try consuming 1 packet of size 'bytes'. Returns true if allowed, false if rate limit exceeded.
        /// </summary>
        public bool TryConsume(int bytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

            Refill();

            if (_availablePackets >= 1 && _availableBytes >= bytes)
            {
                _availablePackets -= 1;
                _availableBytes -= bytes;
                return true;
            }

            return false;
        }

        private void Refill()
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var elapsedSec = (nowTicks - Interlocked.Exchange(ref _lastRefillTicks, nowTicks)) / (double)TimeSpan.TicksPerSecond;

            if (elapsedSec <= 0) return;

            _availablePackets = Math.Min(_maxPacketBurst, _availablePackets + elapsedSec * _packetsPerSecond);
            _availableBytes = Math.Min(_maxByteBurst, _availableBytes + elapsedSec * _bytesPerSecond);
        }
    }
}

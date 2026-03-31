using Portly.Abstractions;
using Portly.Infrastructure.Configuration;
using System.Collections.Concurrent;
using System.Net;

namespace Portly.Infrastructure
{
    /// <summary>
    /// A rate limiting implementation that handles both packet and bandwith limiting.
    /// </summary>
    internal sealed class ClientRateLimiter
    {
        private class RateLimitState
        {
            public int Violations;
            public DateTime LastViolation;
        }

        private readonly ConcurrentDictionary<IPAddress, RateLimitState> _rateLimitStates = new();

        private const int MaxViolations = 5;
        private static readonly TimeSpan ViolationWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan BanDuration = TimeSpan.FromDays(7);

        private double _availablePackets;
        private double _availableBytes;
        private long _lastRefillTicks;

        private readonly ServerConfiguration _serverConfiguration;
        private readonly ILogProvider? _logProvider;

        public ClientRateLimiter(ServerConfiguration configuration, ILogProvider? logProvider = null)
        {
            var rateLimitSettings = configuration.RateLimits;

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimitSettings.MaxPacketsPerSecond);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimitSettings.MaxPacketsPerBurst);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimitSettings.MaxBytesPerSecond);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rateLimitSettings.MaxBytesPerBurst);

            _logProvider = logProvider;
            _serverConfiguration = configuration;
            _lastRefillTicks = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Try consuming 1 packet of size 'bytes'. Returns true if allowed, false if rate limit exceeded.
        /// </summary>
        public bool TryConsume(IPAddress ip, int bytes, out bool banned)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

            Refill();

            if (_availablePackets >= 1 && _availableBytes >= bytes)
            {
                _availablePackets -= 1;
                _availableBytes -= bytes;
                banned = false;
                return true;
            }

            banned = RegisterViolation(ip);

            return false;
        }

        private void Refill()
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var elapsedSec = (nowTicks - Interlocked.Exchange(ref _lastRefillTicks, nowTicks)) / (double)TimeSpan.TicksPerSecond;

            if (elapsedSec <= 0) return;

            _availablePackets = Math.Min(_serverConfiguration.RateLimits.MaxPacketsPerBurst, _availablePackets + elapsedSec * _serverConfiguration.RateLimits.MaxPacketsPerSecond);
            _availableBytes = Math.Min(_serverConfiguration.RateLimits.MaxBytesPerBurst, _availableBytes + elapsedSec * _serverConfiguration.RateLimits.MaxBytesPerSecond);
        }

        private void BanIp(IPAddress ip, TimeSpan duration)
        {
            _serverConfiguration.IpBlacklist[ip] = DateTime.UtcNow.Add(duration);
            _serverConfiguration.Save(logProvider: _logProvider);
        }

        private bool RegisterViolation(IPAddress ip)
        {
            var state = _rateLimitStates.GetOrAdd(ip, _ => new RateLimitState());

            lock (state)
            {
                // Reset if outside window
                if (DateTime.UtcNow - state.LastViolation > ViolationWindow)
                {
                    state.Violations = 0;
                }

                state.Violations++;
                state.LastViolation = DateTime.UtcNow;

                if (state.Violations >= MaxViolations)
                {
                    BanIp(ip, BanDuration);
                    return true;
                }
                return false;
            }
        }
    }
}

using Portly.Abstractions;
using Portly.Infrastructure.Configuration;
using System.Collections.Concurrent;
using System.Net;

namespace Portly.Infrastructure
{
    internal sealed class ClientRateLimiter
    {
        private class RateLimitState
        {
            public int Violations;
            public DateTime LastViolation;
            public double AvailablePackets;
            public double AvailableBytes;
            public long LastRefillTicks;
            public readonly Lock Lock = new();
        }

        private readonly ConcurrentDictionary<IPAddress, RateLimitState> _states = new();

        private const int MaxViolations = 5;
        private static readonly TimeSpan ViolationWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan BanDuration = TimeSpan.FromDays(7);

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
        }

        public bool TryConsume(IPAddress ip, int bytes, out bool banned)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

            var state = _states.GetOrAdd(ip, _ =>
            {
                var r = _serverConfiguration.RateLimits;
                return new RateLimitState
                {
                    AvailablePackets = r.MaxPacketsPerBurst,
                    AvailableBytes = r.MaxBytesPerBurst,
                    LastRefillTicks = DateTime.UtcNow.Ticks
                };
            });

            lock (state.Lock)
            {
                Refill(state);

                if (state.AvailablePackets >= 1 && state.AvailableBytes >= bytes)
                {
                    state.AvailablePackets -= 1;
                    state.AvailableBytes -= bytes;

                    banned = false;
                    return true;
                }
            }

            banned = RegisterViolation(ip);
            return false;
        }

        private void Refill(RateLimitState state)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            var elapsedSec = (nowTicks - state.LastRefillTicks) / (double)TimeSpan.TicksPerSecond;

            if (elapsedSec <= 0) return;

            state.LastRefillTicks = nowTicks;

            var r = _serverConfiguration.RateLimits;

            state.AvailablePackets = Math.Min(
                r.MaxPacketsPerBurst,
                state.AvailablePackets + elapsedSec * r.MaxPacketsPerSecond);

            state.AvailableBytes = Math.Min(
                r.MaxBytesPerBurst,
                state.AvailableBytes + elapsedSec * r.MaxBytesPerSecond);
        }

        private void BanIp(IPAddress ip, TimeSpan duration)
        {
            _serverConfiguration.IpBlacklist[ip] = DateTime.UtcNow.Add(duration);
            _serverConfiguration.Save(logProvider: _logProvider);
        }

        private bool RegisterViolation(IPAddress ip)
        {
            var state = _states.GetOrAdd(ip, _ =>
            {
                var r = _serverConfiguration.RateLimits;
                return new RateLimitState
                {
                    AvailablePackets = r.MaxPacketsPerBurst,
                    AvailableBytes = r.MaxBytesPerBurst,
                    LastRefillTicks = DateTime.UtcNow.Ticks
                };
            });

            lock (state.Lock)
            {
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
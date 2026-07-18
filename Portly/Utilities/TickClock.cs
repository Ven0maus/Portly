namespace Portly.Utilities
{
    using System.Diagnostics;

    namespace Portly.Utilities
    {
        /// <summary>
        /// Contains data about server tick.
        /// </summary>
        public sealed class TickClock
        {
            private double _tickRate;
            private double _tickInterval;

            private long _currentTick;

            // Client synchronization data
            private long _serverTick;
            private long _serverTimeOffsetMs;

            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

            private TickClock() { }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="tickRate"></param>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            internal TickClock(double tickRate)
            {
                Configure(tickRate);
            }

            /// <summary>
            /// Current authoritative tick (server)
            /// </summary>
            public long CurrentTick =>
                Interlocked.Read(ref _currentTick);

            /// <summary>
            /// Estimated server tick (client)
            /// </summary>
            public long EstimatedServerTick
            {
                get
                {
                    var elapsedSeconds =
                        _stopwatch.Elapsed.TotalSeconds;

                    var estimatedTicks =
                        elapsedSeconds / _tickInterval;

                    return _serverTick +
                           (long)estimatedTicks;
                }
            }

            /// <summary>
            /// The current tick rate
            /// </summary>
            public double TickRate => _tickRate;

            /// <summary>
            /// The server time offset in milliseconds.
            /// </summary>
            public long ServerTimeOffsetMs =>
                Interlocked.Read(ref _serverTimeOffsetMs);

            internal void Configure(double tickRate)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);

                _tickRate = tickRate;
                _tickInterval = 1.0 / tickRate;
            }

            internal long Advance()
            {
                return Interlocked.Increment(ref _currentTick);
            }

            internal void Overwrite(long tick)
            {
                Interlocked.Exchange(ref _currentTick, tick);
            }

            internal void Synchronize(long serverTick, long serverTimestampMs)
            {
                var localTimestamp =
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Estimated round trip time
                var roundTrip =
                    localTimestamp - serverTimestampMs;

                // Assume symmetric latency
                var latency =
                    roundTrip / 2;

                // Estimate what time it was on the server when we received the packet
                var estimatedServerTimestamp =
                    serverTimestampMs + latency;

                // How far ahead the server clock is compared to local clock
                var clockOffset =
                    estimatedServerTimestamp - localTimestamp;

                // Advance tick by the time elapsed during network transit
                var elapsedTicks =
                    (long)((latency / 1000d) * _tickRate);

                var correctedTick =
                    serverTick + elapsedTicks;

                Interlocked.Exchange(
                    ref _serverTick,
                    correctedTick);

                Interlocked.Exchange(
                    ref _serverTimeOffsetMs,
                    clockOffset);

                _stopwatch.Restart();
            }
        }
    }
}

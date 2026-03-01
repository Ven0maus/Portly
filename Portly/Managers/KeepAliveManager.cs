namespace Portly.Managers
{
    internal class KeepAliveManager<TClient> where TClient : class
    {
        private readonly TimeSpan _interval;
        private readonly TimeSpan _timeout;
        private readonly Func<TClient, Task> _sendKeepAliveAsync;
        private readonly Func<TClient, Task> _disconnectAsync;
        private readonly SortedSet<ScheduledClient> _schedule;
        private readonly Dictionary<TClient, ScheduledClient> _lookup = new();

        private CancellationToken _cancellationToken;

        public KeepAliveManager(
            TimeSpan interval,
            TimeSpan timeout,
            Func<TClient, Task> sendKeepAliveAsync,
            Func<TClient, Task> disconnectAsync)
        {
            if (interval >= timeout)
                throw new ArgumentException("Keepalive interval must be smaller than timeout.");

            _interval = interval;
            _timeout = timeout;
            _sendKeepAliveAsync = sendKeepAliveAsync ?? throw new ArgumentNullException(nameof(sendKeepAliveAsync));
            _disconnectAsync = disconnectAsync ?? throw new ArgumentNullException(nameof(disconnectAsync));
            _schedule = new(new ScheduledClientComparer(_interval, _timeout));
        }

        public void Register(TClient client)
        {
            var now = DateTime.UtcNow;

            lock (_schedule)
            {
                var sc = new ScheduledClient(client, now, now);
                _schedule.Add(sc);
                _lookup[client] = sc;
            }
        }

        public void Unregister(TClient client)
        {
            lock (_schedule)
            {
                if (_lookup.TryGetValue(client, out var sc))
                {
                    _schedule.Remove(sc);
                    _lookup.Remove(client);
                }
            }
        }

        public void UpdateLastSent(TClient client)
        {
            lock (_schedule)
            {
                if (_lookup.TryGetValue(client, out var sc))
                {
                    _schedule.Remove(sc);
                    sc.LastSent = DateTime.UtcNow;
                    _schedule.Add(sc);
                }
            }
        }

        public void UpdateLastReceived(TClient client)
        {
            lock (_schedule)
            {
                if (_lookup.TryGetValue(client, out var sc))
                {
                    _schedule.Remove(sc);
                    sc.LastReceived = DateTime.UtcNow;
                    _schedule.Add(sc);
                }
            }
        }

        public Task StartAsync(CancellationToken ct)
        {
            _cancellationToken = ct;
            return Task.Run(MainLoopAsync, CancellationToken.None);
        }

        private async Task MainLoopAsync()
        {
            try
            {
                while (!_cancellationToken.IsCancellationRequested)
                {
                    ScheduledClient? next;

                    lock (_schedule)
                    {
                        next = _schedule.Count > 0 ? _schedule.Min : null;
                    }

                    if (next == null)
                    {
                        await Task.Delay(50, _cancellationToken);
                        continue;
                    }

                    var now = DateTime.UtcNow;
                    var nextEvent = Min(next.LastSent + _interval, next.LastReceived + _timeout);
                    var delay = nextEvent - now;

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, _cancellationToken);
                        continue;
                    }

                    while (true)
                    {
                        ScheduledClient? sc;

                        lock (_schedule)
                        {
                            if (_schedule.Count == 0)
                                break;

                            sc = _schedule.Min;
                            if (sc == null) break;

                            var nowInner = DateTime.UtcNow;
                            var nextInner = Min(sc.LastSent + _interval, sc.LastReceived + _timeout);

                            if (nextInner > nowInner)
                                break;

                            _schedule.Remove(sc);
                        }

                        var client = sc.Client;

                        if (DateTime.UtcNow - sc.LastReceived >= _timeout)
                        {
                            Unregister(client);

                            _ = Task.Run(async () =>
                            {
                                try { await _disconnectAsync(client); }
                                catch { }
                            });
                            continue;
                        }

                        if (DateTime.UtcNow - sc.LastSent >= _interval)
                        {
                            // Introduced jitter to prevent massive sync spikes
                            sc.LastSent = DateTime.UtcNow + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));

                            _ = Task.Run(async () =>
                            {
                                try { await _sendKeepAliveAsync(client); }
                                catch { }
                            });
                        }

                        lock (_schedule)
                        {
                            _schedule.Add(sc);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"KeepAliveManager loop exception: {ex}");
            }
        }

        private sealed class ScheduledClient(TClient client, DateTime lastSent, DateTime lastReceived)
        {
            public TClient Client { get; } = client;
            public DateTime LastSent { get; set; } = lastSent;
            public DateTime LastReceived { get; set; } = lastReceived;
        }

        private sealed class ScheduledClientComparer(TimeSpan interval, TimeSpan timeout) : IComparer<ScheduledClient>
        {
            public int Compare(ScheduledClient? x, ScheduledClient? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;

                var xNext = Min(x.LastSent + interval, x.LastReceived + timeout);
                var yNext = Min(y.LastSent + interval, y.LastReceived + timeout);

                var cmp = xNext.CompareTo(yNext);
                if (cmp != 0) return cmp;

                return x.Client.GetHashCode().CompareTo(y.Client.GetHashCode());
            }

            private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        }

        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }
}
using Portly.Infrastructure;
using Portly.Tests.Helpers;
using System.Collections.Concurrent;

namespace Portly.Tests.IntegrationTests
{
    /// <summary>
    /// Integration tests for the server tick system.
    /// </summary>
    internal class TickSystemTests : BaseTests
    {
        [Test]
        public async Task TickLoop_ShouldStart_And_InvokeOnTick_WithCorrectContext()
        {
            await using var host = new TestServerHost(ServerDirectory);

            // Configure a 20Hz tick rate (50ms interval)
            host.Server.Configuration.ConnectionSettings.TickRate = 20;

            var tickCounts = new ConcurrentBag<TickContext>();
            host.Server.OnTick += e =>
            {
                tickCounts.Add(e);
                return ValueTask.CompletedTask;
            };

            await host.StartAsync();

            // Wait for at least a few ticks to occur
            await Task.Delay(150);

            Assert.That(tickCounts, Is.Not.Empty);

            var firstTick = tickCounts.OrderBy(x => x.Tick).First();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstTick.Tick, Is.GreaterThan(0));
                Assert.That(firstTick.FixedDeltaTime, Is.EqualTo(0.05).Within(0.01));
                Assert.That(firstTick.ElapsedTime, Is.GreaterThan(0));
            }
        }

        [Test]
        public async Task TickLoop_ShouldNotStart_WhenTickRateIsZero()
        {
            await using var host = new TestServerHost(ServerDirectory);

            // TickRate 0 should disable the tick loop
            host.Server.Configuration.ConnectionSettings.TickRate = 0;

            var tickCounts = new ConcurrentBag<TickContext>();
            host.Server.OnTick += e =>
            {
                tickCounts.Add(e);
                return ValueTask.CompletedTask;
            };

            await host.StartAsync();

            // Wait briefly
            await Task.Delay(100);

            Assert.That(tickCounts, Is.Empty);
        }

        [Test]
        public async Task TickLoop_ShouldStop_WhenServerStops()
        {
            await using var host = new TestServerHost(ServerDirectory);
            host.Server.Configuration.ConnectionSettings.TickRate = 60;

            var tickCounts = new ConcurrentBag<TickContext>();
            host.Server.OnTick += e =>
            {
                tickCounts.Add(e);
                return ValueTask.CompletedTask;
            };

            await host.StartAsync();
            await Task.Delay(50);

            var countBeforeStop = tickCounts.Count;
            Assert.That(countBeforeStop, Is.GreaterThan(0));

            await host.DisposeAsync();

            // Wait briefly to ensure no more ticks are processed
            await Task.Delay(50);

            Assert.That(tickCounts, Has.Count.EqualTo(countBeforeStop));
        }

        [Test]
        public async Task TickLoop_ShouldRunAtConfiguredTickRate()
        {
            await using var host = new TestServerHost(ServerDirectory);

            host.Server.Configuration.ConnectionSettings.TickRate = 20;

            var ticks = new ConcurrentBag<TickContext>();
            host.Server.OnTick += e =>
            {
                ticks.Add(e);
                return ValueTask.CompletedTask;
            };

            await host.StartAsync();

            await Task.Delay(1000);

            // 20Hz should produce approximately 20 ticks.
            // Allow some tolerance for CI machines.
            Assert.That(ticks, Has.Count.InRange(15, 25));
        }

        [Test]
        public async Task TickLoop_ShouldIncrementTickCounterSequentially()
        {
            await using var host = new TestServerHost(ServerDirectory);

            host.Server.Configuration.ConnectionSettings.TickRate = 60;

            var ticks = new ConcurrentBag<TickContext>();
            host.Server.OnTick += e =>
            {
                ticks.Add(e);
                return ValueTask.CompletedTask;
            };

            await host.StartAsync();

            await Task.Delay(200);

            var orderedTicks = ticks.OrderBy(x => x.Tick).ToList();

            Assert.That(orderedTicks, Has.Count.GreaterThan(2));

            for (var i = 1; i < orderedTicks.Count; i++)
            {
                Assert.That(
                    orderedTicks[i].Tick,
                    Is.EqualTo(orderedTicks[i - 1].Tick + 1));
            }
        }

        [Test]
        public async Task TickLoop_ShouldNotifyMultipleSubscribers()
        {
            await using var host = new TestServerHost(ServerDirectory);

            host.Server.Configuration.ConnectionSettings.TickRate = 20;

            var first = 0;
            var second = 0;

            host.Server.OnTick += e =>
            {
                first++;
                return ValueTask.CompletedTask;
            };
            host.Server.OnTick += e =>
            {
                second++;
                return ValueTask.CompletedTask;
            };

            await host.StartAsync();

            await Task.Delay(200);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first, Is.GreaterThan(0));
                Assert.That(second, Is.EqualTo(first));
            }
        }

        [Test]
        public async Task TickLoop_ShouldUseFixedDeltaTime()
        {
            await using var host = new TestServerHost(ServerDirectory);

            host.Server.Configuration.ConnectionSettings.TickRate = 30;

            var ticks = new ConcurrentBag<TickContext>();
            host.Server.OnTick += e =>
            {
                ticks.Add(e);
                return ValueTask.CompletedTask;
            };

            await host.StartAsync();

            await Task.Delay(300);

            Assert.That(ticks, Has.Count.GreaterThan(3));

            foreach (var tick in ticks)
            {
                Assert.That(
                    tick.FixedDeltaTime,
                    Is.EqualTo(1.0 / 30.0).Within(0.001));
            }
        }

        [Test]
        public async Task TickLoop_ShouldTrackRealElapsedTime()
        {
            await using var host = new TestServerHost(ServerDirectory);

            host.Server.Configuration.ConnectionSettings.TickRate = 20;

            var ticks = new ConcurrentBag<TickContext>();
            host.Server.OnTick += e =>
            {
                ticks.Add(e);
                return ValueTask.CompletedTask;
            };

            await host.StartAsync();

            await Task.Delay(250);

            Assert.That(ticks, Is.Not.Empty);

            Assert.That(
                ticks.Any(x => x.ElapsedTime > 0),
                Is.True);
        }

        [Test]
        public async Task TickLoop_ShouldDetectSlowTickHandlers()
        {
            await using var host = new TestServerHost(ServerDirectory);

            host.Server.Configuration.ConnectionSettings.TickRate = 60;
            host.Server.Configuration.ConnectionSettings.TickLagWarningThresholdMs = 10;
            host.Server.Configuration.ConnectionSettings.TickLagWarningCooldown = TimeSpan.Zero;

            var lagDetected = new TaskCompletionSource();

            host.LogProvider.OnLog += (sender, message) =>
            {
                if (message.Contains("behind schedule"))
                {
                    lagDetected.TrySetResult();
                }
            };

            host.Server.OnTick += _ =>
            {
                Thread.Sleep(50);
                return ValueTask.CompletedTask;
            };

            await host.StartAsync();

            await lagDetected.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }

        [Test]
        public async Task TickLoop_ShouldNotContinueAfterStopAsync()
        {
            var host = new TestServerHost(ServerDirectory);

            var tickStarted = new TaskCompletionSource();
            var allowTickFinish = new TaskCompletionSource();

            var tickCount = 0;

            host.Server.OnTick += async _ =>
            {
                Interlocked.Increment(ref tickCount);

                tickStarted.SetResult();

                // Keep the tick running while StopAsync is called
                await allowTickFinish.Task;
            };

            await host.StartAsync();

            // Wait until the tick is actively executing
            await tickStarted.Task;

            var countBeforeStop = tickCount;

            var stopTask = host.Server.StopAsync();

            // If StopAsync waits for the tick loop, this should still be incomplete
            Assert.That(stopTask.IsCompleted, Is.False);

            // Allow the tick handler to finish
            allowTickFinish.SetResult();

            await stopTask;

            var countAfterStop = tickCount;

            // Give the scheduler time to incorrectly run another tick
            await Task.Delay(100);

            Assert.That(tickCount, Is.EqualTo(countAfterStop));
        }
    }
}


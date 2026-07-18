using Portly.Utilities.Portly.Utilities;

namespace Portly.Tests.IntegrationTests
{
    internal class TickClockTests
    {
        [Test]
        public void TickClock_ShouldInitializeWithCorrectTickRate()
        {
            var clock = new TickClock(20);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(clock.TickRate, Is.EqualTo(20));
                Assert.That(clock.CurrentTick, Is.EqualTo(0));
            }
        }

        [Test]
        public void TickClock_Advance_ShouldIncrementTick()
        {
            var clock = new TickClock(60);

            var first = clock.Advance();
            var second = clock.Advance();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first, Is.EqualTo(1));
                Assert.That(second, Is.EqualTo(2));
                Assert.That(clock.CurrentTick, Is.EqualTo(2));
            }
        }

        [Test]
        public void TickClock_Overwrite_ShouldReplaceCurrentTick()
        {
            var clock = new TickClock(60);

            clock.Advance();
            clock.Advance();

            clock.Overwrite(500);

            Assert.That(clock.CurrentTick, Is.EqualTo(500));
        }

        [Test]
        public void TickClock_Synchronize_ShouldSetEstimatedServerTick()
        {
            var clock = new TickClock(20);

            var serverTick = 1000;
            var serverTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            clock.Synchronize(
                serverTick,
                serverTimestamp);

            Assert.That(
                clock.EstimatedServerTick,
                Is.EqualTo(serverTick)
                    .Within(1));
        }

        [Test]
        public void TickClock_Synchronize_ShouldAdvanceTickBasedOnLatency()
        {
            var clock = new TickClock(20);

            /*
             * Simulate server sending a tick sync 100ms ago.
             *
             * 20Hz = 20 ticks/sec
             * 100ms = 0.1 sec
             * Expected advancement:
             *
             * 20 * 0.1 = 2 ticks
             */
            var serverTick = 1000;

            var serverTimestamp =
                DateTimeOffset.UtcNow
                    .AddMilliseconds(-100)
                    .ToUnixTimeMilliseconds();

            clock.Synchronize(
                serverTick,
                serverTimestamp);

            var estimated = clock.EstimatedServerTick;

            Assert.That(
                estimated,
                Is.EqualTo(1002)
                .Within(1));
        }

        [Test]
        public void TickClock_ServerTimeOffset_ShouldBeUpdatedAfterSync()
        {
            var clock = new TickClock(60);

            var serverTimestamp =
                DateTimeOffset.UtcNow
                    .AddMilliseconds(-50)
                    .ToUnixTimeMilliseconds();

            clock.Synchronize(
                500,
                serverTimestamp);

            /*
             * The server timestamp is older than local time,
             * so offset should be negative or close to zero.
             */
            Assert.That(
                clock.ServerTimeOffsetMs,
                Is.LessThanOrEqualTo(0));
        }

        [Test]
        public async Task TickClock_EstimatedServerTick_ShouldProgressOverTime()
        {
            var clock = new TickClock(60);

            var timestamp =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            clock.Synchronize(
                100,
                timestamp);

            var first =
                clock.EstimatedServerTick;

            await Task.Delay(100);

            var second =
                clock.EstimatedServerTick;

            Assert.That(
                second,
                Is.GreaterThan(first));
        }

        [Test]
        public void TickClock_Synchronize_ShouldRespectDifferentTickRates()
        {
            var clock30Hz = new TickClock(30);
            var clock120Hz = new TickClock(120);

            /*
             * Simulate 100ms round trip latency.
             * Synchronize() compensates with half RTT:
             *
             * 100ms RTT -> 50ms one way latency
             *
             * 30Hz:
             * 0.05 * 30 = 1.5 ticks
             *
             * 120Hz:
             * 0.05 * 120 = 6 ticks
             */
            var timestamp =
                DateTimeOffset.UtcNow
                    .AddMilliseconds(-100)
                    .ToUnixTimeMilliseconds();

            clock30Hz.Synchronize(1000, timestamp);
            clock120Hz.Synchronize(1000, timestamp);

            var tick30 =
                clock30Hz.EstimatedServerTick;

            var tick120 =
                clock120Hz.EstimatedServerTick;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    tick30,
                    Is.EqualTo(1001)
                    .Within(1));

                Assert.That(
                    tick120,
                    Is.EqualTo(1006)
                    .Within(1));
            }
        }

        [Test]
        public void TickClock_ShouldRejectInvalidTickRate()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = new TickClock(0);
            });

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = new TickClock(-1);
            });
        }
    }
}

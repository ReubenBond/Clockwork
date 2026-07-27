using System.Diagnostics;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tests.Shims;

/// <summary>
/// Semantic conformance tests for <see cref="DeterministicClock"/> against the .NET 10 clock/timestamp
/// contracts: UTC/local/today, offset variants, timestamp and elapsed conversion, tick wrap, active
/// missing-context failure, and rewritten-but-inactive pass-through.
/// </summary>
public sealed class DeterministicClockTests
{
    [Fact]
    public void UtcNowReturnsVirtualUtcWithUtcKind()
    {
        var clock = ShimTestHarness.CreateClock(new DateTimeOffset(2030, 5, 6, 7, 8, 9, TimeSpan.Zero));
        var env = ShimTestHarness.CreateEnvironment(clock);

        var now = ShimTestHarness.RunInSimulation(env, DeterministicClock.GetUtcNow);

        Assert.Equal(DateTimeKind.Utc, now.Kind);
        Assert.Equal(new DateTime(2030, 5, 6, 7, 8, 9, DateTimeKind.Utc), now);
    }

    [Fact]
    public void NowReturnsVirtualLocalWithLocalKindForNonUtcTimeZone()
    {
        // A fixed +05:00 zone with no DST, so the expected wall-clock is unambiguous.
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-plus5", TimeSpan.FromHours(5), "test+5", "test+5");
        var clock = ShimTestHarness.CreateClock(new DateTimeOffset(2030, 5, 6, 7, 0, 0, TimeSpan.Zero));
        var env = ShimTestHarness.CreateEnvironment(clock, localTimeZone: zone);

        var now = ShimTestHarness.RunInSimulation(env, DeterministicClock.GetNow);

        Assert.Equal(DateTimeKind.Local, now.Kind);
        Assert.Equal(new DateTime(2030, 5, 6, 12, 0, 0), now);
    }

    [Fact]
    public void TodayReturnsVirtualLocalDateAtMidnight()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-plus5", TimeSpan.FromHours(5), "test+5", "test+5");
        var clock = ShimTestHarness.CreateClock(new DateTimeOffset(2030, 5, 6, 21, 0, 0, TimeSpan.Zero));
        var env = ShimTestHarness.CreateEnvironment(clock, localTimeZone: zone);

        var today = ShimTestHarness.RunInSimulation(env, DeterministicClock.GetToday);

        // 21:00 UTC is 02:00 next day local (+5), so Today is the 7th at midnight.
        Assert.Equal(new DateTime(2030, 5, 7), today);
        Assert.Equal(TimeSpan.Zero, today.TimeOfDay);
        Assert.Equal(DateTimeKind.Local, today.Kind);
    }

    [Fact]
    public void OffsetUtcNowHasZeroOffset()
    {
        var clock = ShimTestHarness.CreateClock(new DateTimeOffset(2030, 5, 6, 7, 8, 9, TimeSpan.Zero));
        var env = ShimTestHarness.CreateEnvironment(clock);

        var now = ShimTestHarness.RunInSimulation(env, DeterministicClock.GetOffsetUtcNow);

        Assert.Equal(TimeSpan.Zero, now.Offset);
        Assert.Equal(new DateTimeOffset(2030, 5, 6, 7, 8, 9, TimeSpan.Zero), now);
    }

    [Fact]
    public void OffsetNowCarriesTheLocalZoneOffset()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-minus3", TimeSpan.FromHours(-3), "test-3", "test-3");
        var clock = ShimTestHarness.CreateClock(new DateTimeOffset(2030, 5, 6, 7, 0, 0, TimeSpan.Zero));
        var env = ShimTestHarness.CreateEnvironment(clock, localTimeZone: zone);

        var now = ShimTestHarness.RunInSimulation(env, DeterministicClock.GetOffsetNow);

        Assert.Equal(TimeSpan.FromHours(-3), now.Offset);
        Assert.Equal(new DateTimeOffset(2030, 5, 6, 4, 0, 0, TimeSpan.FromHours(-3)), now);
    }

    [Fact]
    public void TimestampAndElapsedTimeMeasureVirtualDuration()
    {
        var clock = ShimTestHarness.CreateClock();
        var env = ShimTestHarness.CreateEnvironment(clock);

        var elapsed = ShimTestHarness.RunInSimulation(env, () =>
        {
            var start = DeterministicClock.GetTimestamp();
            clock.Advance(TimeSpan.FromMilliseconds(1500));
            return DeterministicClock.GetElapsedTime(start);
        });

        Assert.Equal(TimeSpan.FromMilliseconds(1500), elapsed);
    }

    [Fact]
    public void TimestampIsZeroAtOriginAndScalesWithVirtualTime()
    {
        var clock = ShimTestHarness.CreateClock();
        var env = ShimTestHarness.CreateEnvironment(clock);

        var (atOrigin, afterOneSecond) = ShimTestHarness.RunInSimulation(env, () =>
        {
            var t0 = DeterministicClock.GetTimestamp();
            clock.Advance(TimeSpan.FromSeconds(1));
            return (t0, DeterministicClock.GetTimestamp());
        });

        Assert.Equal(0, atOrigin);
        Assert.Equal(TimeSpan.TicksPerSecond, afterOneSecond);
    }

    [Fact]
    public void TickCount64IsVirtualMillisecondsSinceOrigin()
    {
        var clock = ShimTestHarness.CreateClock();
        var env = ShimTestHarness.CreateEnvironment(clock);

        var ticks = ShimTestHarness.RunInSimulation(env, () =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(4242));
            return DeterministicClock.GetTickCount64();
        });

        Assert.Equal(4242L, ticks);
    }

    [Fact]
    public void TickCountWrapsLikeTheLow32BitsOfTickCount64()
    {
        var clock = ShimTestHarness.CreateClock();
        var env = ShimTestHarness.CreateEnvironment(clock);

        // Advance beyond int.MaxValue milliseconds so the 32-bit tick count must wrap negative,
        // exactly as Environment.TickCount does (it is the low 32 bits of TickCount64).
        var wrapMs = (long)int.MaxValue + 5;
        var (tick32, tick64) = ShimTestHarness.RunInSimulation(env, () =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(wrapMs));
            return (DeterministicClock.GetTickCount(), DeterministicClock.GetTickCount64());
        });

        Assert.Equal(wrapMs, tick64);
        Assert.Equal(unchecked((int)wrapMs), tick32);
        Assert.True(tick32 < 0);
    }

    [Fact]
    public void OutsideSimulationEveryClockShimPassesThroughToTheRealBcl()
    {
        Assert.False(Clockwork.Runtime.Execution.SimulationExecutionContext.IsActive);

        var before = DateTime.UtcNow;
        var shimUtc = DeterministicClock.GetUtcNow();
        var after = DateTime.UtcNow;
        Assert.InRange(shimUtc, before.AddSeconds(-1), after.AddSeconds(1));

        Assert.Equal(DateTimeKind.Local, DeterministicClock.GetNow().Kind);
        Assert.Equal(DateTime.Today, DeterministicClock.GetToday());
        Assert.Equal(TimeSpan.Zero, DeterministicClock.GetOffsetUtcNow().Offset);

        // Real timestamp/elapsed monotonicity, and real tick counts, still work.
        var start = DeterministicClock.GetTimestamp();
        Assert.True(DeterministicClock.GetElapsedTime(start) >= TimeSpan.Zero);
        Assert.True(Stopwatch.GetTimestamp() >= start);
        _ = DeterministicClock.GetTickCount();
        Assert.True(DeterministicClock.GetTickCount64() > 0);
    }

    [Fact]
    public void ActiveSimulationWithoutRegisteredEnvironmentFailsExplicitly()
    {
        ShimTestHarness.RunInSimulationWithoutEnvironment(() =>
        {
            Assert.Throws<SimulationServiceMissingException>(() => DeterministicClock.GetUtcNow());
            Assert.Throws<SimulationServiceMissingException>(() => DeterministicClock.GetNow());
            Assert.Throws<SimulationServiceMissingException>(() => DeterministicClock.GetTickCount64());
            Assert.Throws<SimulationServiceMissingException>(() => DeterministicClock.GetTimestamp());
        });
    }

    [Fact]
    public void ReplayWithSameClockProducesIdenticalReadings()
    {
        DateTime First()
        {
            var clock = ShimTestHarness.CreateClock();
            var env = ShimTestHarness.CreateEnvironment(clock);
            return ShimTestHarness.RunInSimulation(env, () =>
            {
                clock.Advance(TimeSpan.FromMinutes(90));
                return DeterministicClock.GetUtcNow();
            });
        }

        Assert.Equal(First(), First());
    }
}

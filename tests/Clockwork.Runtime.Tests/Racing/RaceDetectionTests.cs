using System.Runtime.CompilerServices;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Strategies;
using Clockwork.Runtime.Tests.Scheduling;

namespace Clockwork.Runtime.Tests.Racing;

/// <summary>Behavioral coverage of logical-location conflict detection and false-positive controls.</summary>
public sealed class RaceDetectionTests
{
    [Fact]
    public void ReadReadAccessesAreClean()
    {
        using ControlledOperationScheduler scheduler = RunPair(
            () => RaceInstrumentation.ReadStatic("Fx.State::Value", "A", 1, "A.cs", 10),
            () => RaceInstrumentation.ReadStatic("Fx.State::Value", "B", 2, "B.cs", 20));

        Assert.False(scheduler.RaceExplorationResult.IsRaceDetected);
        Assert.Null(scheduler.FirstRace);
    }

    [Theory]
    [InlineData(RaceAccessKind.Read, RaceAccessKind.Write)]
    [InlineData(RaceAccessKind.Write, RaceAccessKind.Read)]
    [InlineData(RaceAccessKind.Write, RaceAccessKind.Write)]
    public void ConflictingStaticAccessesAreDetected(RaceAccessKind first, RaceAccessKind second)
    {
        using ControlledOperationScheduler scheduler = RunPair(
            () => AccessStatic(first, "A"),
            () => AccessStatic(second, "B"));

        RaceReport race = Assert.IsType<RaceReport>(scheduler.FirstRace);
        Assert.Equal(RaceExplorationTerminationReason.RaceDetected, scheduler.RaceExplorationResult.Reason);
        Assert.Equal(first, race.FirstAccess.Kind);
        Assert.Equal(second, race.SecondAccess.Kind);
        Assert.Equal("Fx.State::Value", race.FirstAccess.Location.Member);
        Assert.NotEqual(race.FirstAccess.OperationId, race.SecondAccess.OperationId);
        Assert.NotEmpty(race.ScheduleTrace);
    }

    [Fact]
    public void InstanceLocationsDistinguishTargets()
    {
        var first = new object();
        var second = new object();
        using ControlledOperationScheduler scheduler = RunPair(
            () => RaceInstrumentation.WriteInstance(first, "Fx.State::Value", "A", 1, null, -1),
            () => RaceInstrumentation.WriteInstance(second, "Fx.State::Value", "B", 2, null, -1));

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void SameInstanceFieldIsDetected()
    {
        var target = new object();
        using ControlledOperationScheduler scheduler = RunPair(
            () => RaceInstrumentation.WriteInstance(target, "Fx.State::Value", "A", 1, null, -1),
            () => RaceInstrumentation.ReadInstance(target, "Fx.State::Value", "B", 2, null, -1));

        RaceReport race = Assert.IsType<RaceReport>(scheduler.FirstRace);
        Assert.Equal(RaceMemoryLocationKind.InstanceField, race.FirstAccess.Location.Kind);
        Assert.True(race.FirstAccess.Location.ObjectId > 0);
    }

    [Fact]
    public void ArrayElementsAreTrackedIndependently()
    {
        int[] values = new int[2];
        using ControlledOperationScheduler clean = RunPair(
            () => RaceInstrumentation.WriteArray(values, 0, "A", 1, null, -1),
            () => RaceInstrumentation.WriteArray(values, 1, "B", 2, null, -1));
        Assert.Null(clean.FirstRace);

        using ControlledOperationScheduler raced = RunPair(
            () => RaceInstrumentation.WriteArray(values, 0, "A", 1, null, -1),
            () => RaceInstrumentation.ReadArray(values, 0, "B", 2, null, -1));
        RaceReport race = Assert.IsType<RaceReport>(raced.FirstRace);
        Assert.Equal(RaceMemoryLocationKind.ArrayElement, race.FirstAccess.Location.Kind);
        Assert.Equal(0, race.FirstAccess.Location.ElementIndex);
    }

    [Fact]
    public void SharedControlledLockSuppressesProtectedAccesses()
    {
        var synchronization = new object();
        using ControlledOperationScheduler scheduler = RunPair(
            () => ProtectedWrite(synchronization),
            () => ProtectedWrite(synchronization));

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void DifferentLocksDoNotHideRaceAndReportSynchronizationContext()
    {
        var firstLock = new object();
        var secondLock = new object();
        using ControlledOperationScheduler scheduler = RunPair(
            () => ProtectedWrite(firstLock),
            () => ProtectedWrite(secondLock));

        RaceReport race = Assert.IsType<RaceReport>(scheduler.FirstRace);
        Assert.Equal(["sync#1"], race.FirstAccess.SynchronizationContext);
        Assert.Equal(["sync#2"], race.SecondAccess.SynchronizationContext);
        Assert.Contains("synchronization:", race.ToDetailedString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SignalWaitCreatesHappensBeforeEdge()
    {
        var signal = new object();
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule("producer", () =>
        {
            RaceInstrumentation.WriteStatic("Fx.State::Value", "producer", 1, null, -1);
            RaceSynchronization.Signal(signal);
        });
        scheduler.Drain();
        scheduler.Schedule("consumer", () =>
        {
            RaceSynchronization.Wait(signal);
            RaceInstrumentation.ReadStatic("Fx.State::Value", "consumer", 2, null, -1);
        });
        scheduler.Drain();

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void TrackerDoesNotRetainTargetObjectsAfterOperationCompletes()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        WeakReference? weak = null;
        scheduler.Schedule("access", () => weak = AccessTemporaryTarget());
        scheduler.Drain();

        ForceCollection();

        Assert.NotNull(weak);
        Assert.False(weak!.IsAlive);
    }

    [Fact]
    public void SameSeedProducesByteStableFirstRaceReport()
    {
        string first = RunReport(seed: 19);
        string replay = RunReport(seed: 19);
        Assert.Equal(first, replay);
    }

    private static ControlledOperationScheduler RunPair(Action first, Action second)
    {
        var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule("first", first);
        scheduler.Schedule("second", second);
        scheduler.Drain();
        return scheduler;
    }

    private static void AccessStatic(RaceAccessKind kind, string method)
    {
        if (kind == RaceAccessKind.Read)
        {
            RaceInstrumentation.ReadStatic("Fx.State::Value", method, 1, method + ".cs", 10);
        }
        else
        {
            RaceInstrumentation.WriteStatic("Fx.State::Value", method, 1, method + ".cs", 10);
        }
    }

    private static void ProtectedWrite(object synchronization)
    {
        RaceSynchronization.Enter(synchronization);
        try
        {
            RaceInstrumentation.WriteStatic("Fx.State::Value", "protected", 1, null, -1);
        }
        finally
        {
            RaceSynchronization.Exit(synchronization);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AccessTemporaryTarget()
    {
        var target = new object();
        var weak = new WeakReference(target);
        RaceInstrumentation.ReadInstance(target, "Fx.State::Value", "temporary", 1, null, -1);
        return weak;
    }

    private static void ForceCollection()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static string RunReport(int seed)
    {
        using var scheduler = SchedulerTestHarness.NewScheduler(seed: seed);
        scheduler.SchedulingStrategy = new SeededRandomSchedulingStrategy(seed);
        scheduler.Schedule("first", () => AccessStatic(RaceAccessKind.Write, "A"));
        scheduler.Schedule("second", () => AccessStatic(RaceAccessKind.Read, "B"));
        scheduler.Drain();
        return Assert.IsType<RaceReport>(scheduler.FirstRace).ToDetailedString();
    }
}

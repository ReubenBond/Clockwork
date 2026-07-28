using System.Runtime.CompilerServices;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Strategies;
using Clockwork.Runtime.Tests.Scheduling;
using Clockwork.Runtime.Tasks;
using Clockwork.Shims.System.Runtime.CompilerServices;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Racing;

/// <summary>Behavioral coverage of logical-location conflict detection and false-positive controls.</summary>
public sealed class RaceDetectionTests
{
    [Fact]
    public void ReadReadAccessesAreClean()
    {
        using SimulationScheduler scheduler = RunPair(
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
        using SimulationScheduler scheduler = RunPair(
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
        using SimulationScheduler scheduler = RunPair(
            () => RaceInstrumentation.WriteInstance(first, "Fx.State::Value", "A", 1, null, -1),
            () => RaceInstrumentation.WriteInstance(second, "Fx.State::Value", "B", 2, null, -1));

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void SameInstanceFieldIsDetected()
    {
        var target = new object();
        using SimulationScheduler scheduler = RunPair(
            () => RaceInstrumentation.WriteInstance(target, "Fx.State::Value", "A", 1, null, -1),
            () => RaceInstrumentation.ReadInstance(target, "Fx.State::Value", "B", 2, null, -1));

        RaceReport race = Assert.IsType<RaceReport>(scheduler.FirstRace);
        Assert.Equal(RaceMemoryLocationKind.InstanceField, race.FirstAccess.Location.Kind);
        Assert.True(race.FirstAccess.Location.ObjectId > 0);
    }

    [Fact]
    public void ClosedGenericStaticFieldsUseDistinctLocations()
    {
        using SimulationScheduler scheduler = RunPair(
            () => RaceInstrumentation.WriteStaticField(
                typeof(GenericStatic<int>).TypeHandle,
                "GenericStatic`1::Value",
                "A",
                1,
                null,
                -1),
            () => RaceInstrumentation.WriteStaticField(
                typeof(GenericStatic<string>).TypeHandle,
                "GenericStatic`1::Value",
                "B",
                2,
                null,
                -1));

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void ArrayElementsAreTrackedIndependently()
    {
        int[] values = new int[2];
        using SimulationScheduler clean = RunPair(
            () => RaceInstrumentation.WriteArray(values, 0, "A", 1, null, -1),
            () => RaceInstrumentation.WriteArray(values, 1, "B", 2, null, -1));
        Assert.Null(clean.FirstRace);

        using SimulationScheduler raced = RunPair(
            () => RaceInstrumentation.WriteArray(values, 0, "A", 1, null, -1),
            () => RaceInstrumentation.ReadArray(values, 0, "B", 2, null, -1));
        RaceReport race = Assert.IsType<RaceReport>(raced.FirstRace);
        Assert.Equal(RaceMemoryLocationKind.ArrayElement, race.FirstAccess.Location.Kind);
        Assert.Equal(0, race.FirstAccess.Location.ElementIndex);
    }

    [Fact]
    public void MutableCollectionAccessesRaceButConcurrentCollectionPointsDoNot()
    {
        var mutable = new List<int>();
        using SimulationScheduler raced = RunPair(
            () => RaceInstrumentation.WriteCollection(mutable, "List::Add", "A", 1, null, -1),
            () => RaceInstrumentation.ReadCollection(mutable, "List::GetEnumerator", "B", 2, null, -1));
        Assert.Equal(RaceMemoryLocationKind.Collection, Assert.IsType<RaceReport>(raced.FirstRace).FirstAccess.Location.Kind);

        var concurrent = new System.Collections.Concurrent.ConcurrentQueue<int>();
        using SimulationScheduler clean = RunPair(
            () => RaceInstrumentation.InterleaveConcurrentCollection(concurrent, "ConcurrentQueue::Enqueue", "A", 1, null, -1),
            () => RaceInstrumentation.InterleaveConcurrentCollection(concurrent, "ConcurrentQueue::TryDequeue", "B", 2, null, -1));
        Assert.Null(clean.FirstRace);
    }

    [Fact]
    public void SharedControlledLockSuppressesProtectedAccesses()
    {
        var synchronization = new object();
        using SimulationScheduler scheduler = RunPair(
            () => ProtectedWrite(synchronization),
            () => ProtectedWrite(synchronization));

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void DifferentLocksDoNotHideRaceAndReportSynchronizationContext()
    {
        var firstLock = new object();
        var secondLock = new object();
        using SimulationScheduler scheduler = RunPair(
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
    public void ControlledTaskCompletionCreatesHappensBeforeEdge()
    {
        ControlledTaskCompletionSource? completion = null;
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule("producer", () =>
        {
            completion = new ControlledTaskCompletionSource();
            RaceInstrumentation.WriteStatic("Fx.State::Value", "producer", 1, null, -1);
            completion.SetResult();
        });
        scheduler.Drain();
        scheduler.Schedule("consumer", () =>
        {
            ControlledTask.Wait(completion!.Task);
            RaceInstrumentation.ReadStatic("Fx.State::Value", "consumer", 2, null, -1);
        });
        scheduler.Drain();

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void ControlledTaskAwaitConsumesCompletionHappensBeforeEdge()
    {
        ControlledTaskCompletionSource? completion = null;
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule("producer", () =>
        {
            completion = new ControlledTaskCompletionSource();
            RaceInstrumentation.WriteStatic("Fx.State::Value", "producer", 1, null, -1);
            completion.SetResult();
        });
        scheduler.Drain();
        scheduler.Schedule("consumer", () =>
        {
            new ControlledTaskAwaiter(completion!.Task).GetResult();
            RaceInstrumentation.ReadStatic("Fx.State::Value", "consumer", 2, null, -1);
        });
        scheduler.Drain();

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void ControlledTaskProxyPropagatesAntecedentHappensBeforeEdges()
    {
        ControlledTaskCompletionSource? first = null;
        ControlledTaskCompletionSource? second = null;
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule("producer", () =>
        {
            first = new ControlledTaskCompletionSource();
            second = new ControlledTaskCompletionSource();
            RaceInstrumentation.WriteStatic("Fx.State::Value", "producer", 1, null, -1);
            first.SetResult();
            second.SetResult();
        });
        scheduler.Drain();
        scheduler.Schedule("consumer", () =>
        {
            Task proxy = ControlledTask.WhenAll(first!.Task, second!.Task);
            new ControlledTaskAwaiter(proxy).GetResult();
            RaceInstrumentation.ReadStatic("Fx.State::Value", "consumer", 2, null, -1);
        });
        scheduler.Drain();

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void SharedReaderLocksDoNotSuppressWriteRace()
    {
        ReaderWriterLockSlim? rwLock = null;
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule("setup", () => rwLock = ControlledReaderWriterLockSlim.Create());
        scheduler.Drain();
        scheduler.Schedule("first", () => WriteUnderReadLock(rwLock!));
        scheduler.Schedule("second", () => WriteUnderReadLock(rwLock!));
        scheduler.Drain();

        Assert.NotNull(scheduler.FirstRace);
    }

    [Fact]
    public void ReaderReleasesAggregateBeforeWriterAcquires()
    {
        ReaderWriterLockSlim? rwLock = null;
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule("setup", () => rwLock = ControlledReaderWriterLockSlim.Create());
        scheduler.Drain();
        scheduler.Schedule("first-reader", () => ReadUnderReadLock(rwLock!));
        scheduler.Schedule("second-reader", () => ReadUnderReadLock(rwLock!));
        scheduler.Drain();
        scheduler.Schedule("writer", () =>
        {
            ControlledReaderWriterLockSlim.EnterWriteLock(rwLock!);
            try
            {
                RaceInstrumentation.WriteStatic("Fx.State::Value", "writer", 3, null, -1);
            }
            finally
            {
                ControlledReaderWriterLockSlim.ExitWriteLock(rwLock!);
            }
        });
        scheduler.Drain();

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void LockAndSignalOnSameObjectUseDifferentSynchronizationDomains()
    {
        Task synchronization = Task.CompletedTask;
        using SimulationScheduler scheduler = RunPair(
            () =>
            {
                RaceSynchronization.Enter(synchronization);
                try
                {
                    RaceInstrumentation.WriteStatic("Fx.State::Value", "lock", 1, null, -1);
                }
                finally
                {
                    RaceSynchronization.Exit(synchronization);
                }
            },
            () =>
            {
                RaceSynchronization.Wait(synchronization);
                RaceInstrumentation.ReadStatic("Fx.State::Value", "task", 2, null, -1);
            });

        Assert.NotNull(scheduler.FirstRace);
    }

    [Fact]
    public void ImmediateAsyncSemaphoreWaitPropagatesReleaseClock()
    {
        SemaphoreSlim? semaphore = null;
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule("setup", () => semaphore = ControlledSemaphoreSlim.Create(0, 1));
        scheduler.Drain();
        scheduler.Schedule("producer", () =>
        {
            RaceInstrumentation.WriteStatic("Fx.State::Value", "producer", 1, null, -1);
            ControlledSemaphoreSlim.Release(semaphore!);
        });
        scheduler.Drain();
        scheduler.Schedule("consumer", () =>
        {
            Task<bool> wait = ControlledSemaphoreSlim.WaitAsync(semaphore!, Timeout.Infinite);
            Assert.True(new ControlledTaskAwaiter<bool>(wait).GetResult());
            RaceInstrumentation.ReadStatic("Fx.State::Value", "consumer", 2, null, -1);
        });
        scheduler.Drain();

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void SchedulerAndNestedStrandIdentitiesUseDisjointRanges()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        long schedulerStrand = 0;
        long nestedStrand = 0;
        scheduler.Schedule("identity", () =>
        {
            schedulerStrand = SimulationSynchronizationFlow.CurrentId;
            SimulationSynchronizationFlow.RunAsNewStrand(
                () => nestedStrand = SimulationSynchronizationFlow.CurrentId);
        });
        scheduler.Drain();

        Assert.True(schedulerStrand < 0);
        Assert.True(nestedStrand > 0);
    }

    [Fact]
    public void ControlledResourceSignalCreatesHappensBeforeEdge()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var resource = scheduler.CreateResource(
            Clockwork.Runtime.Scheduling.Resources.SimulationResourceKind.ManualResetEvent,
            "race-event");
        scheduler.Schedule("consumer", () =>
        {
            scheduler.WaitOnResource(resource, SimulationPauseReason.ResourceWait("race-event"));
            RaceInstrumentation.ReadStatic("Fx.State::Value", "consumer", 2, null, -1);
        });
        Assert.True(scheduler.RunStep());
        scheduler.Schedule("producer", () =>
        {
            RaceInstrumentation.WriteStatic("Fx.State::Value", "producer", 1, null, -1);
            scheduler.SignalOne(resource);
        });
        scheduler.Drain();

        Assert.Null(scheduler.FirstRace);
    }

    [Fact]
    public void ResourceWaitConsumesClockFromItsResolvingSignal()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.SchedulingStrategy = new Clockwork.Runtime.Scheduling.Strategies.PrioritySchedulingStrategy();
        var resource = scheduler.CreateResource(
            Clockwork.Runtime.Scheduling.Resources.SimulationResourceKind.ManualResetEvent,
            "clocked-event");
        scheduler.Schedule("consumer", () =>
        {
            scheduler.WaitOnResource(resource, SimulationPauseReason.ResourceWait("clocked-event"));
            RaceInstrumentation.ReadStatic("Fx.State::Y", "consumer", 4, null, -1);
        });
        Assert.True(scheduler.RunStep());

        scheduler.Schedule("first-signal", () => scheduler.SignalOne(resource), priority: 5);
        Assert.True(scheduler.RunStep());

        scheduler.Schedule("later-signal", () =>
        {
            RaceInstrumentation.WriteStatic("Fx.State::Y", "later", 3, null, -1);
            scheduler.SignalOne(resource);
        }, priority: 10);
        scheduler.Drain();

        Assert.NotNull(scheduler.FirstRace);
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

    private static SimulationScheduler RunPair(Action first, Action second)
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

    private static void WriteUnderReadLock(ReaderWriterLockSlim rwLock)
    {
        ControlledReaderWriterLockSlim.EnterReadLock(rwLock);
        try
        {
            RaceInstrumentation.WriteStatic("Fx.State::Value", "reader", 1, null, -1);
        }
        finally
        {
            ControlledReaderWriterLockSlim.ExitReadLock(rwLock);
        }
    }

    private static void ReadUnderReadLock(ReaderWriterLockSlim rwLock)
    {
        ControlledReaderWriterLockSlim.EnterReadLock(rwLock);
        try
        {
            RaceInstrumentation.ReadStatic("Fx.State::Value", "reader", 1, null, -1);
        }
        finally
        {
            ControlledReaderWriterLockSlim.ExitReadLock(rwLock);
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

    private static class GenericStatic<T>
    {
    }
}

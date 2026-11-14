using System.Runtime.CompilerServices;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Tests.Scheduling;

namespace Clockwork.Runtime.Tests.Racing;

/// <summary>Runs the memory measurements alone, so no other test's allocations reach the heap they measure.</summary>
[CollectionDefinition(nameof(RaceTrackerMemoryIsolation), DisableParallelization = true)]
public sealed class RaceTrackerMemoryIsolation;

/// <summary>
/// The race detector's memory, measured directly on a chain whose every link forks a helper, which increments a counter
/// under a lock and then reads a field of a fresh object: the shape whose clocks once grew by one entry per link. Each
/// link allocates the same number of bytes however long the chain has run, and the heap the detector retains after a
/// full collection is the same after 10³ links as after 10⁴. The run collects its young garbage every hundred links,
/// as a run which allocates does, so the detector can release the locations of the objects it drops.
/// </summary>
[Collection(nameof(RaceTrackerMemoryIsolation))]
public sealed class RaceTrackerMemoryTests
{
    [Fact]
    public void EachLinkAllocatesTheSameAndRetainedHeapIsFlat()
    {
        // A first run warms up code and statics, whose allocations are not the detector's.
        _ = Measure(links: 1_000);
        Measurement shortRun = Measure(links: 1_000);
        Measurement longRun = Measure(links: 10_000);

        // The first window follows warm-up of the detector's tables; the last ends the run.
        Assert.InRange(longRun.LateBytesPerLink, 0, (longRun.EarlyBytesPerLink * 1.1) + 16);
        Assert.InRange(longRun.RetainedBytes, 0, shortRun.RetainedBytes + (64 * 1024));
        Assert.Equal(shortRun.Statistics.LargestClock, longRun.Statistics.LargestClock);
    }

    private static Measurement Measure(int links)
    {
        using SimulationScheduler scheduler = SchedulerTestHarness.NewScheduler();
        var chain = new Chain(scheduler);
        long baseline = GC.GetTotalMemory(forceFullCollection: true);
        double early = 0;
        double late = 0;
        int window = links / 10;
        for (int link = 0; link < links; link++)
        {
            if (link % 100 == 0)
            {
                GC.Collect(0);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            chain.Link();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (link >= window && link < 2 * window)
            {
                early += allocated;
            }
            else if (link >= links - window)
            {
                late += allocated;
            }
        }

        chain.Finish();
        long retained = GC.GetTotalMemory(forceFullCollection: true) - baseline;
        GC.KeepAlive(chain);
        return new Measurement(early / window, late / window, retained, chain.Tracker.Statistics);
    }

    private sealed record Measurement(
        double EarlyBytesPerLink,
        double LateBytesPerLink,
        long RetainedBytes,
        RaceTrackerStatistics Statistics);

    // Drives the detector with operations the scheduler never registers, so the scheduler retains none of them.
    private sealed class Chain
    {
        private static readonly RaceSourceLocation Source = new("memory", 1, null, -1);

        private readonly SimulationScheduler _scheduler;
        private readonly object _gate = new();
        private readonly RaceTracker.LocationState _counter;
        private SimulationOperation _link;
        private long _nextOperationId;

        public Chain(SimulationScheduler scheduler)
        {
            _scheduler = scheduler;
            _counter = Tracker.ResolveLocation(RaceMemoryLocationKind.StaticField, null, "Memory::Counter", null)!;
            _link = Start(parent: null);
        }

        public RaceTracker Tracker { get; } = new();

        public void Link()
        {
            SimulationOperation helper = Start(_link);
            Increment(helper);
            ReadFreshObject(helper);
            Tracker.CompleteOperation(helper);
            Increment(_link);
            SimulationOperation next = Start(_link);
            Tracker.CompleteOperation(_link);
            _link = next;
        }

        public void Finish() => Tracker.CompleteOperation(_link);

        private SimulationOperation Start(SimulationOperation? parent)
        {
            var operation = new SimulationOperation(
                _scheduler,
                new SimulationOperationId(++_nextOperationId),
                parent?.Id ?? SimulationOperationId.None,
                _scheduler.Runtime,
                node: null,
                default(SimulationLogicalExecutionId),
                "memory",
                static () => { },
                capturedContext: null);
            Tracker.RegisterOperation(operation, parent);
            return operation;
        }

        private void Increment(SimulationOperation operation)
        {
            Tracker.EnterExclusive(operation, _gate);
            Tracker.RecordAccess(operation, RaceAccessKind.Read, _counter, Source, []);
            Tracker.RecordAccess(operation, RaceAccessKind.Write, _counter, Source, []);
            Tracker.ExitExclusive(operation, _gate);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ReadFreshObject(SimulationOperation operation)
        {
            RaceTracker.LocationState location =
                Tracker.ResolveLocation(RaceMemoryLocationKind.InstanceField, new object(), "Memory::Field", null)!;
            Tracker.RecordAccess(operation, RaceAccessKind.Read, location, Source, []);
        }
    }
}

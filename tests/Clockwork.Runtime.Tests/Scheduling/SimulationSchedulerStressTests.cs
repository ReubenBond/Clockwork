using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Resources;

namespace Clockwork.Runtime.Tests.Scheduling;

/// <summary>
/// Stress and teardown coverage for the unified <see cref="SimulationScheduler"/>: the
/// exactly-one-running invariant across many physical threads, deterministic interleaving under
/// yielding, no physical thread leaks, and teardown with paused/parked operations.
/// </summary>
public sealed class SimulationSchedulerStressTests
{
    [Theory]
    [InlineData(8, 50)]
    [InlineData(32, 20)]
    public void ExactlyOneOperationExecutesSutCodeAtATimeUnderYieldingStress(int operationCount, int iterations)
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var concurrent = 0;
        var maxObserved = 0;
        var violations = 0;
        var threadIds = new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();

        for (var i = 0; i < operationCount; i++)
        {
            scheduler.Schedule($"op-{i}", () =>
            {
                threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                for (var iter = 0; iter < iterations; iter++)
                {
                    var now = Interlocked.Increment(ref concurrent);
                    if (now != 1)
                    {
                        Interlocked.Increment(ref violations);
                    }

                    UpdateMax(ref maxObserved, now);
                    Thread.SpinWait(200);
                    Interlocked.Decrement(ref concurrent);
                    scheduler.Yield();
                }
            });
        }

        scheduler.Drain(TestContext.Current.CancellationToken);

        Assert.Equal(0, violations);
        Assert.Equal(1, maxObserved);
        // Each operation ran on its own dedicated physical thread.
        Assert.Equal(operationCount, threadIds.Count);
    }

    [Fact]
    public void YieldingOperationsInterleaveInDeterministicRoundRobinOrder()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var trace = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var name = $"op{i}";
            scheduler.Schedule(name, () =>
            {
                for (var round = 0; round < 3; round++)
                {
                    trace.Add($"{name}:{round}");
                    scheduler.Yield();
                }
            });
        }

        scheduler.Drain(TestContext.Current.CancellationToken);

        // Lowest-id selection means all round-0 slices run (in registration order) before any
        // round-1 slice, and so on: a stable, deterministic round-robin.
        Assert.Equal(
            [
                "op0:0", "op1:0", "op2:0",
                "op0:1", "op1:1", "op2:1",
                "op0:2", "op1:2", "op2:2",
            ],
            trace);
    }

    [Fact]
    public void CompletedOperationsLeaveNoLivePhysicalThreads()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ops = new List<SimulationOperation>();
        for (var i = 0; i < 16; i++)
        {
            ops.Add(scheduler.Schedule($"op-{i}", () => Thread.SpinWait(100)));
        }

        scheduler.Drain(TestContext.Current.CancellationToken);

        foreach (var op in ops)
        {
            Assert.Equal(SimulationOperationState.Completed, op.State);
            Assert.True(op.Thread is null or { IsAlive: false }, $"Operation {op.Id} leaked a live physical thread.");
        }
    }

    [Fact]
    public void CompletedOperationsLeaveActiveSetButRemainInStatusHistory()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        for (var i = 0; i < 100; i++)
        {
            scheduler.Schedule($"op-{i}", () => { });
        }

        scheduler.Drain(TestContext.Current.CancellationToken);

        Assert.Equal(0, scheduler.ActiveOperationCount);
        Assert.Equal(100, scheduler.CaptureStatus().Count);
        Assert.All(
            scheduler.CaptureStatus(),
            status => Assert.Equal(SimulationOperationState.Completed, status.State));
    }

    [Fact]
    public void ManyPausedOperationsKeepDistinctThreadsParkedThenTeardownReclaimsThemAll()
    {
        const int count = 12;
        var scheduler = SchedulerTestHarness.NewScheduler();
        var ops = new List<SimulationOperation>();
        for (var i = 0; i < count; i++)
        {
            ops.Add(scheduler.Schedule($"op-{i}", () =>
                scheduler.Pause(SimulationPauseReason.ResourceWait("never-signaled"))));
        }

        // Drive each operation once so all of them park in the paused state.
        for (var i = 0; i < count; i++)
        {
            Assert.True(scheduler.RunStep(TestContext.Current.CancellationToken));
        }

        Assert.False(scheduler.RunStep(TestContext.Current.CancellationToken));
        Assert.Equal(count, scheduler.PendingOperationCount);

        var threads = new HashSet<Thread>();
        foreach (var op in ops)
        {
            Assert.Equal(SimulationOperationState.Paused, op.State);
            Assert.NotNull(op.Thread);
            Assert.True(op.Thread!.IsAlive, $"Paused operation {op.Id} should keep its thread parked.");
            threads.Add(op.Thread);
        }

        Assert.Equal(count, threads.Count);

        // Teardown must cooperatively unwind and reclaim every parked thread.
        scheduler.Dispose();

        foreach (var op in ops)
        {
            Assert.Equal(SimulationOperationState.Canceled, op.State);
            Assert.True(SpinUntilDead(op.Thread!), $"Operation {op.Id} left a stranded thread after teardown.");
        }
    }

    [Fact]
    public void DisposeIsIdempotentAndSafeWithNoOperations()
    {
        var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Dispose();
        scheduler.Dispose();
    }

    [Fact]
    public void OperationsCanBeUsedAfterEarlierOnesCompleteWithoutLeakingThreads()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        Thread? firstThread = null;
        var first = scheduler.Schedule("first", () => firstThread = Thread.CurrentThread);
        scheduler.Drain(TestContext.Current.CancellationToken);
        Assert.True(SpinUntilDead(firstThread!));

        Thread? secondThread = null;
        var second = scheduler.Schedule("second", () => secondThread = Thread.CurrentThread);
        scheduler.Drain(TestContext.Current.CancellationToken);

        Assert.Equal(SimulationOperationState.Completed, first.State);
        Assert.Equal(SimulationOperationState.Completed, second.State);
        Assert.NotSame(firstThread, secondThread);
        Assert.True(SpinUntilDead(secondThread!));
    }

    [Theory]
    [InlineData(CancellationRace.Signal)]
    [InlineData(CancellationRace.Timeout)]
    [InlineData(CancellationRace.Dispose)]
    public void ExternalCancellationDoesNotDeadlockWaiterCleanup(CancellationRace race)
    {
        for (var iteration = 0; iteration < 25; iteration++)
        {
            var scheduler = SchedulerTestHarness.NewScheduler();
            var resource = scheduler.CreateResource(SimulationResourceKind.Semaphore, "race");
            using var cancellation = new CancellationTokenSource();
            var timeout = race == CancellationRace.Timeout
                ? TimeSpan.FromTicks(1)
                : Timeout.InfiniteTimeSpan;

            var operation = scheduler.Schedule(
                "waiter",
                () => scheduler.WaitOnResource(
                    resource,
                    timeout,
                    SimulationPauseReason.ResourceWait("race"),
                    cancellation.Token));

            Assert.True(scheduler.RunStep(TestContext.Current.CancellationToken));
            Assert.Equal(SimulationOperationState.Paused, operation.State);

            using var start = new Barrier(2);
            Exception? contenderError = null;
            Exception? cancelError = null;
            var contender = new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    switch (race)
                    {
                        case CancellationRace.Signal:
                            scheduler.SignalOne(resource);
                            break;
                        case CancellationRace.Timeout:
                            scheduler.TryAdvanceVirtualTime();
                            break;
                        case CancellationRace.Dispose:
                            scheduler.Dispose();
                            break;
                    }
                }
                catch (Exception exception)
                {
                    contenderError = exception;
                }
            })
            {
                IsBackground = true,
            };
            var canceler = new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    cancellation.Cancel();
                }
                catch (Exception exception)
                {
                    cancelError = exception;
                }
            })
            {
                IsBackground = true,
            };

            contender.Start();
            canceler.Start();

            var completed = contender.Join(TimeSpan.FromSeconds(2))
                && canceler.Join(TimeSpan.FromSeconds(2));
            Assert.True(completed, $"{race} deadlocked against external cancellation on iteration {iteration}.");
            Assert.Null(contenderError);
            Assert.Null(cancelError);

            if (race != CancellationRace.Dispose)
            {
                scheduler.Drain(TestContext.Current.CancellationToken);
                Assert.Empty(scheduler.CapturePendingTimeouts());
                scheduler.Dispose();
            }

            Assert.True(SpinUntilDead(operation.Thread!), $"Operation {operation.Id} leaked after {race} race.");
        }
    }

    private static void UpdateMax(ref int target, int candidate)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
            if (candidate <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, candidate, seen) != seen);
    }

    private static bool SpinUntilDead(Thread thread) =>
        SpinWait.SpinUntil(() => !thread.IsAlive, TimeSpan.FromSeconds(10));

    public enum CancellationRace
    {
        Signal,
        Timeout,
        Dispose,
    }
}

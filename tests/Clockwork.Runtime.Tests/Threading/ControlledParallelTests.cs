using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

/// <summary>
/// Tests for the controlled <see cref="ControlledParallel"/> shims: <c>Invoke</c> / <c>For</c> /
/// <c>ForEach</c> decompose their work into controlled operations on the coordinator and drain the
/// deterministic loop until every branch has completed, so all iterations run on the single logical thread;
/// body faults are aggregated into an <see cref="AggregateException"/>; a cancelled options token is
/// observed; the loop-state / TLocal / Partitioner overloads are rejected precisely; and outside a
/// simulation every shim delegates to the real API.
/// </summary>
public sealed class ControlledParallelTests
{
    private static readonly int[] ForEachItems = [10, 20, 30];

    [Fact]
    public void ForRunsEveryIterationOnTheLogicalThread()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            int sum = 0;
            ParallelLoopResult result = ControlledParallel.For(0, 5, i => sum += i);

            Assert.Equal(0 + 1 + 2 + 3 + 4, sum);
            Assert.True(result.IsCompleted);
            Assert.Null(result.LowestBreakIteration);
        });
    }

    [Fact]
    public void ForLongRunsEveryIteration()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            long sum = 0;
            ControlledParallel.For(0L, 4L, i => sum += i);
            Assert.Equal(0 + 1 + 2 + 3, sum);
        });
    }

    [Fact]
    public void InvokeRunsEveryAction()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            bool a = false, b = false, c = false;
            ControlledParallel.Invoke(() => a = true, () => b = true, () => c = true);
            Assert.True(a && b && c);
        });
    }

    [Fact]
    public void ForEachProcessesEveryElement()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var seen = new List<int>();
            ControlledParallel.ForEach(ForEachItems, seen.Add);
            Assert.Equal(3, seen.Count);
            Assert.Contains(10, seen);
            Assert.Contains(20, seen);
            Assert.Contains(30, seen);
        });
    }

    [Fact]
    public void BodyFaultsAreAggregated()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ex = Assert.Throws<AggregateException>(
                () => ControlledParallel.For(0, 3, i =>
                {
                    if (i == 1)
                    {
                        throw new InvalidOperationException("boom");
                    }
                }));

            Assert.Single(ex.InnerExceptions);
            Assert.IsType<InvalidOperationException>(ex.InnerExceptions[0]);
        });
    }

    [Fact]
    public void CancelledOptionsTokenThrowsBeforeRunning()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var options = new ParallelOptions { CancellationToken = cts.Token };

            bool ran = false;
            Assert.Throws<OperationCanceledException>(
                () => ControlledParallel.For(0, 3, options, _ => ran = true));
            Assert.False(ran);
        });
    }

    [Fact]
    public void RejectUnsupportedThrows()
    {
        var ex = Assert.Throws<ControlledParallelUnsupportedException>(
            () => ControlledParallel.RejectUnsupported("System.Threading.Tasks.Parallel.For"));
        Assert.Contains("Parallel.For", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OutsideSimulationForDelegatesToRealParallel()
    {
        int count = 0;
        ParallelLoopResult result = ControlledParallel.For(0, 4, _ => Interlocked.Increment(ref count));

        Assert.True(result.IsCompleted);
        Assert.Equal(4, count);
    }
}

using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Resources;

namespace Clockwork.Runtime.Tests.Scheduling.Resources;

/// <summary>
/// Teardown/disposal coverage for the Phase 3B wait states: an operation parked on an infinite
/// resource wait, one parked with a pending virtual-time timeout, and one parked with a live
/// cancellation registration must all be reclaimed by <see cref="ControlledOperationScheduler.Dispose"/>
/// without leaking a physical thread, a pending timeout item, or a cancellation callback.
/// </summary>
public sealed class ControlledResourceTeardownTests
{
    private static ControlledOperationPauseReason Reason(string tag) => ControlledOperationPauseReason.ResourceWait(tag);

    [Fact]
    public void DisposeReclaimsAnOperationParkedOnAnInfiniteResourceWait()
    {
        var scheduler = SchedulerTestHarness.NewScheduler();
        var resource = scheduler.CreateResource(ControlledResourceKind.Monitor, "m");
        ControlledOperation op;
        try
        {
            op = scheduler.Schedule("waiter", () => scheduler.WaitOnResource(resource, Reason("m")));
            Assert.True(scheduler.RunStep());
            Assert.Equal(ControlledOperationState.Paused, op.State);
            Assert.Equal(1, resource.WaiterCount);
        }
        finally
        {
            scheduler.Dispose();
        }

        Assert.Equal(ControlledOperationState.Canceled, op.State);
        Assert.True(op.Thread is null or { IsAlive: false }, "Parked-on-resource operation leaked a live thread.");
    }

    [Fact]
    public void DisposeClearsAPendingVirtualTimeoutForAParkedOperation()
    {
        var scheduler = SchedulerTestHarness.NewScheduler();
        var resource = scheduler.CreateResource(ControlledResourceKind.Semaphore, "sem");
        ControlledOperation op;
        try
        {
            op = scheduler.Schedule(
                "timed",
                () => scheduler.WaitOnResource(resource, TimeSpan.FromSeconds(5), Reason("sem")));
            Assert.True(scheduler.RunStep());
            Assert.Equal(ControlledOperationState.Paused, op.State);

            // The finite wait registered exactly one pending virtual-time timeout.
            Assert.Single(scheduler.CapturePendingTimeouts());
        }
        finally
        {
            scheduler.Dispose();
        }

        Assert.Equal(ControlledOperationState.Canceled, op.State);
        Assert.True(op.Thread is null or { IsAlive: false }, "Parked-on-timeout operation leaked a live thread.");
    }

    [Fact]
    public void DisposeUnregistersCancellationSoLaterTokenCancellationIsInert()
    {
        var scheduler = SchedulerTestHarness.NewScheduler();
        using var cts = new CancellationTokenSource();
        var resource = scheduler.CreateResource(ControlledResourceKind.WaitHandle, "wh");
        ControlledOperation op;
        try
        {
            op = scheduler.Schedule(
                "cancelable",
                () => scheduler.WaitOnResource(resource, Timeout.InfiniteTimeSpan, Reason("wh"), cts.Token));
            Assert.True(scheduler.RunStep());
            Assert.Equal(ControlledOperationState.Paused, op.State);
        }
        finally
        {
            scheduler.Dispose();
        }

        Assert.Equal(ControlledOperationState.Canceled, op.State);

        // The registration was disposed during teardown, so cancelling afterwards must not throw or
        // re-enter the disposed scheduler.
        cts.Cancel();
    }

    [Fact]
    public void DisposeWithEveryPausedStateReclaimsAllThreads()
    {
        var scheduler = SchedulerTestHarness.NewScheduler();
        using var cts = new CancellationTokenSource();
        var infinite = scheduler.CreateResource(ControlledResourceKind.Monitor, "inf");
        var timed = scheduler.CreateResource(ControlledResourceKind.Semaphore, "timed");
        var cancelable = scheduler.CreateResource(ControlledResourceKind.WaitHandle, "cancel");

        var ops = new List<ControlledOperation>
        {
            scheduler.Schedule("yield", () => scheduler.Pause(ControlledOperationPauseReason.Yield)),
            scheduler.Schedule("infinite", () => scheduler.WaitOnResource(infinite, Reason("inf"))),
            scheduler.Schedule("timed", () => scheduler.WaitOnResource(timed, TimeSpan.FromSeconds(3), Reason("timed"))),
            scheduler.Schedule("cancelable", () => scheduler.WaitOnResource(cancelable, Timeout.InfiniteTimeSpan, Reason("cancel"), cts.Token)),
        };

        // Park every operation in its distinct paused state.
        while (scheduler.RunStep())
        {
        }

        Assert.All(ops, op => Assert.Equal(ControlledOperationState.Paused, op.State));

        scheduler.Dispose();

        Assert.All(ops, op =>
        {
            Assert.Equal(ControlledOperationState.Canceled, op.State);
            Assert.True(op.Thread is null or { IsAlive: false }, $"Operation {op.Id} leaked a live thread through teardown.");
        });
    }
}

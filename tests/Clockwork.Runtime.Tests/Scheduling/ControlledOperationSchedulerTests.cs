using System.Collections.Concurrent;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Tests.Scheduling;

/// <summary>
/// Behavioral coverage of <see cref="ControlledOperationScheduler"/>: registration/selection,
/// permission gating, ambient identity, pause/resume/yield, terminal outcomes, nested scheduling,
/// and teardown.
/// </summary>
public sealed class ControlledOperationSchedulerTests
{
    [Fact]
    public void RegisterCreatesOperationInCreatedStateWithoutRunningIt()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ran = false;
        var op = scheduler.Register("w", () => ran = true);

        Assert.Equal(ControlledOperationState.Created, op.State);
        Assert.False(ran);
        Assert.False(op.Id.IsNone);
        Assert.True(op.ParentId.IsNone);
    }

    [Fact]
    public void AdmitTransitionsCreatedToRunnable()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var op = scheduler.Register("w", () => { });
        scheduler.Admit(op);
        Assert.Equal(ControlledOperationState.Runnable, op.State);
    }

    [Fact]
    public void DrainRunsAdmittedOperationToCompletion()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ran = false;
        var op = scheduler.Schedule("w", () => ran = true);

        var steps = scheduler.Drain();

        Assert.True(ran);
        Assert.Equal(1, steps);
        Assert.Equal(ControlledOperationState.Completed, op.State);
        Assert.Null(op.TerminalException);
    }

    [Fact]
    public void RunStepReturnsFalseWhenNothingRunnable()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        Assert.False(scheduler.RunStep());
    }

    [Fact]
    public void OperationsRunInDeterministicRegistrationOrder()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var order = new List<long>();
        var a = scheduler.Schedule("a", () => order.Add(1));
        var b = scheduler.Schedule("b", () => order.Add(2));
        var c = scheduler.Schedule("c", () => order.Add(3));

        scheduler.Drain();

        Assert.Equal([1L, 2L, 3L], order);
        Assert.True(a.Id < b.Id && b.Id < c.Id);
    }

    [Fact]
    public void OperationBodyObservesCorrectAmbientRuntimeAndLogicalIdentity()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler(description: "sched");
        SimulationExecutionSnapshot? observed = null;
        long observedLogical = -1;
        var op = scheduler.Schedule("w", () =>
        {
            observed = SimulationExecutionContext.Current;
            observedLogical = observed!.LogicalExecutionId.Value;
        });

        scheduler.Drain();

        Assert.NotNull(observed);
        Assert.Same(scheduler.Runtime, observed!.Runtime);
        Assert.Equal(op.LogicalExecutionId.Value, observedLogical);
        Assert.NotEqual(0, observedLogical);
    }

    [Fact]
    public void LogicalExecutionIdentityIsDistinctFromManagedThreadId()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        long logical = -1;
        int managed = -1;
        var op = scheduler.Schedule("w", () =>
        {
            logical = SimulationExecutionContext.Current!.LogicalExecutionId.Value;
            managed = Environment.CurrentManagedThreadId;
        });

        scheduler.Drain();

        Assert.Equal(op.LogicalExecutionId.Value, logical);
        // The logical identity is a scheduler-assigned value, not the physical thread id.
        Assert.NotEqual(managed, (int)logical);
    }

    [Fact]
    public void OperationBodyObservesNodeScopeWhenProvided()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        string? address = null;
        scheduler.Schedule("w", () => address = SimulationExecutionContext.Current!.Node?.Address, new SimulationNodeIdentity("node-A"));

        scheduler.Drain();

        Assert.Equal("node-A", address);
    }

    [Fact]
    public void CurrentOperationReflectsTheRunningOperation()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        ControlledOperation? seen = null;
        var op = scheduler.Schedule("w", () => seen = scheduler.CurrentOperation);

        scheduler.Drain();

        Assert.Same(op, seen);
        Assert.Null(scheduler.CurrentOperation);
    }

    [Fact]
    public void FaultingBodyTransitionsToFaultedAndCapturesException()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var boom = new InvalidOperationException("boom");
        var op = scheduler.Schedule("w", () => throw boom);

        scheduler.Drain();

        Assert.Equal(ControlledOperationState.Faulted, op.State);
        Assert.Same(boom, op.TerminalException);
    }

    [Fact]
    public void BodyThrowingOperationCanceledTransitionsToCanceled()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var op = scheduler.Schedule("w", () => throw new OperationCanceledException());

        scheduler.Drain();

        Assert.Equal(ControlledOperationState.Canceled, op.State);
        Assert.Null(op.TerminalException);
    }

    [Fact]
    public void PauseYieldsControlAndResumeContinues()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var log = new List<string>();
        ControlledOperation? op = null;
        op = scheduler.Schedule("w", () =>
        {
            log.Add("before");
            scheduler.Pause(ControlledOperationPauseReason.ResourceWait("gate"));
            log.Add("after");
        });

        // First step runs until the pause.
        Assert.True(scheduler.RunStep());
        Assert.Equal(ControlledOperationState.Paused, op!.State);
        Assert.Equal(ControlledOperationPauseKind.ResourceWait, op.PauseReason!.Kind);
        Assert.Equal(["before"], log);

        // Nothing runnable while it is paused.
        Assert.False(scheduler.RunStep());

        // Resume makes it runnable again; the next step continues from the pause point.
        scheduler.Resume(op);
        Assert.Equal(ControlledOperationState.Runnable, op.State);
        Assert.True(scheduler.RunStep());

        Assert.Equal(ControlledOperationState.Completed, op.State);
        Assert.Null(op.PauseReason);
        Assert.Equal(["before", "after"], log);
    }

    [Fact]
    public void YieldKeepsOperationRunnableAndItCompletesOnNextStep()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var iterations = 0;
        var op = scheduler.Schedule("w", () =>
        {
            for (var i = 0; i < 3; i++)
            {
                iterations++;
                scheduler.Yield();
            }
        });

        scheduler.Drain();

        Assert.Equal(3, iterations);
        Assert.Equal(ControlledOperationState.Completed, op.State);
    }

    [Fact]
    public void PauseOutsideAnOperationThrows()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        Assert.Throws<ControlledOperationException>(() => scheduler.Pause(ControlledOperationPauseReason.Yield));
        Assert.Throws<ControlledOperationException>(scheduler.Yield);
    }

    [Fact]
    public void NestedRegistrationRecordsParentChildIdentity()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        ControlledOperation? child = null;
        var parent = scheduler.Schedule("parent", () =>
        {
            child = scheduler.Schedule("child", () => { });
        });

        scheduler.Drain();

        Assert.NotNull(child);
        Assert.Equal(parent.Id, child!.ParentId);
        Assert.Equal(ControlledOperationState.Completed, child.State);
        Assert.True(parent.Id < child.Id);
    }

    [Fact]
    public void CancelCreatedOperationTransitionsToCanceledWithoutRunning()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ran = false;
        var op = scheduler.Register("w", () => ran = true);

        scheduler.Cancel(op);

        Assert.Equal(ControlledOperationState.Canceled, op.State);
        Assert.False(ran);
        Assert.False(scheduler.RunStep());
    }

    [Fact]
    public void CancelRunnableOperationRemovesItFromScheduling()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ran = false;
        var op = scheduler.Schedule("w", () => ran = true);

        scheduler.Cancel(op);

        Assert.Equal(ControlledOperationState.Canceled, op.State);
        Assert.False(scheduler.RunStep());
        Assert.False(ran);
    }

    [Fact]
    public void CancelPausedOperationUnwindsItsParkedThread()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var afterPauseRan = false;
        Thread? bodyThread = null;
        var op = scheduler.Schedule("w", () =>
        {
            bodyThread = Thread.CurrentThread;
            scheduler.Pause(ControlledOperationPauseReason.ResourceWait("never-signaled"));
            afterPauseRan = true;
        });

        Assert.True(scheduler.RunStep());
        Assert.Equal(ControlledOperationState.Paused, op.State);

        scheduler.Cancel(op);

        Assert.Equal(ControlledOperationState.Canceled, op.State);
        Assert.False(afterPauseRan);
        Assert.True(SpinUntil(() => bodyThread is { IsAlive: false }), "The parked thread should have unwound and exited.");
    }

    [Fact]
    public void CancelIsIdempotentOnTerminalOperations()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var op = scheduler.Schedule("w", () => { });
        scheduler.Drain();
        Assert.Equal(ControlledOperationState.Completed, op.State);

        // Canceling an already-completed operation is a no-op, not an illegal transition.
        scheduler.Cancel(op);
        Assert.Equal(ControlledOperationState.Completed, op.State);
    }

    [Fact]
    public void CannotCancelTheRunningOperationFromWithinItsOwnBody()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        ControlledOperation? op = null;
        Exception? caught = null;
        op = scheduler.Schedule("w", () =>
        {
            try
            {
                scheduler.Cancel(op!);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });

        scheduler.Drain();

        Assert.IsType<ControlledOperationException>(caught);
        Assert.Equal(ControlledOperationState.Completed, op!.State);
    }

    [Fact]
    public void ListenerObservesDeterministicTransitionSequence()
    {
        var listener = new RecordingListener();
        using var scheduler = SchedulerTestHarness.NewScheduler(listener);
        scheduler.Schedule("w", () => { });
        scheduler.Drain();

        Assert.Equal(["1:Created", "1:Runnable", "1:Running", "1:Completed"], listener.Formatted);
    }

    [Fact]
    public void ListenerObservesPauseResumeSequence()
    {
        var listener = new RecordingListener();
        using var scheduler = SchedulerTestHarness.NewScheduler(listener);
        var op = scheduler.Schedule("w", () => scheduler.Pause(ControlledOperationPauseReason.Yield));
        scheduler.RunStep();
        scheduler.Resume(op);
        scheduler.RunStep();

        Assert.Equal(
            ["1:Created", "1:Runnable", "1:Running", "1:Paused", "1:Runnable", "1:Running", "1:Completed"],
            listener.Formatted);
    }

    [Fact]
    public void CaptureStatusReturnsOperationsInStableIdOrder()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var a = scheduler.Register("a", () => { });
        var b = scheduler.Register("b", () => { });
        scheduler.Admit(b);

        var status = scheduler.CaptureStatus();

        Assert.Equal(2, status.Count);
        Assert.Equal(a.Id, status[0].Id);
        Assert.Equal(ControlledOperationState.Created, status[0].State);
        Assert.Equal(b.Id, status[1].Id);
        Assert.Equal(ControlledOperationState.Runnable, status[1].State);
    }

    private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5));
}

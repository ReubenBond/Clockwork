using System.Collections.Concurrent;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Tests.Scheduling;

/// <summary>
/// Behavioral coverage of the unified <see cref="SimulationScheduler"/>: registration/selection,
/// permission gating, ambient identity, pause/resume/yield, terminal outcomes, nested scheduling,
/// and teardown.
/// </summary>
public sealed class SimulationSchedulerTests
{
    [Fact]
    public void RegisterCreatesOperationInCreatedStateWithoutRunningIt()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ran = false;
        var op = scheduler.Register("w", () => ran = true);

        Assert.Equal(SimulationOperationState.Created, op.State);
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
        Assert.Equal(SimulationOperationState.Runnable, op.State);
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
        Assert.Equal(SimulationOperationState.Completed, op.State);
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
        SimulationOperation? seen = null;
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

        Assert.Equal(SimulationOperationState.Faulted, op.State);
        Assert.Same(boom, op.TerminalException);
    }

    [Fact]
    public void BodyThrowingOperationCanceledTransitionsToCanceled()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var op = scheduler.Schedule("w", () => throw new OperationCanceledException());

        scheduler.Drain();

        Assert.Equal(SimulationOperationState.Canceled, op.State);
        Assert.Null(op.TerminalException);
    }

    [Fact]
    public void PauseYieldsControlAndResumeContinues()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var log = new List<string>();
        SimulationOperation? op = null;
        op = scheduler.Schedule("w", () =>
        {
            log.Add("before");
            scheduler.Pause(SimulationPauseReason.ResourceWait("gate"));
            log.Add("after");
        });

        // First step runs until the pause.
        Assert.True(scheduler.RunStep());
        Assert.Equal(SimulationOperationState.Paused, op!.State);
        Assert.Equal(SimulationOperationPauseReason.ResourceWait, op.PauseReason!.Kind);
        Assert.Equal(["before"], log);

        // Nothing runnable while it is paused.
        Assert.False(scheduler.RunStep());

        // Resume makes it runnable again; the next step continues from the pause point.
        scheduler.Resume(op);
        Assert.Equal(SimulationOperationState.Runnable, op.State);
        Assert.True(scheduler.RunStep());

        Assert.Equal(SimulationOperationState.Completed, op.State);
        Assert.Null(op.PauseReason);
        Assert.Equal(["before", "after"], log);
    }

    [Fact]
    public void ResumeRejectsAnActiveResourceWaiter()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var resource = scheduler.CreateResource(
            Clockwork.Runtime.Scheduling.Resources.SimulationResourceKind.Semaphore,
            "resource-wait");
        var operation = scheduler.Schedule(
            "waiter",
            () => scheduler.WaitOnResource(
                resource,
                SimulationPauseReason.ResourceWait("resource-wait")));
        Assert.True(scheduler.RunStep());

        var exception = Assert.Throws<SimulationSchedulerException>(() => scheduler.Resume(operation));

        Assert.Contains("active resource waiter", exception.Message, StringComparison.Ordinal);
        Assert.Equal(SimulationOperationState.Paused, operation.State);
        scheduler.SignalOne(resource);
        scheduler.Drain();
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
        Assert.Equal(SimulationOperationState.Completed, op.State);
    }

    [Fact]
    public void PauseOutsideAnOperationThrows()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        Assert.Throws<SimulationSchedulerException>(() => scheduler.Pause(SimulationPauseReason.Yield));
        Assert.Throws<SimulationSchedulerException>(scheduler.Yield);
    }

    [Fact]
    public void NestedRegistrationRecordsParentChildIdentity()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        SimulationOperation? child = null;
        var parent = scheduler.Schedule("parent", () =>
        {
            child = scheduler.Schedule("child", () => { });
        });

        scheduler.Drain();

        Assert.NotNull(child);
        Assert.Equal(parent.Id, child!.ParentId);
        Assert.Equal(SimulationOperationState.Completed, child.State);
        Assert.True(parent.Id < child.Id);
    }

    [Fact]
    public void RemovePendingWorkPreservesReadinessWaitForAnotherNode()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var removedNode = new SimulationNodeIdentity("removed");
        var unrelatedNode = new SimulationNodeIdentity("unrelated");
        var removedRan = false;
        var unrelatedReady = false;
        var unrelatedRan = false;
        _ = scheduler.ScheduleWhenReady(removedNode, static () => false, () => removedRan = true);
        _ = scheduler.ScheduleWhenReady(unrelatedNode, () => unrelatedReady, () => unrelatedRan = true);

        scheduler.RemovePendingWork(removedNode);
        unrelatedReady = true;

        Assert.True(scheduler.RunStep());
        Assert.False(removedRan);
        Assert.True(unrelatedRan);
        Assert.False(scheduler.RunStep());
    }

    [Fact]
    public void ScheduleAfterUsesInheritedNodeForCallbackDiagnosticsAndDetachment()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var node = new SimulationNodeIdentity("inherited");
        SimulationNodeIdentity? callbackNode = null;
        var detachedCallbackRan = false;
        IDisposable? elapsedRegistration = null;
        IDisposable? detachedRegistration = null;
        scheduler.Schedule(
            "parent",
            () =>
            {
                elapsedRegistration = scheduler.ScheduleAfter(
                    "elapsed",
                    () => callbackNode = SimulationExecutionContext.Current!.Node,
                    TimeSpan.FromSeconds(1));
                detachedRegistration = scheduler.ScheduleAfter(
                    "detached",
                    () => detachedCallbackRan = true,
                    TimeSpan.FromSeconds(2));
            },
            node);
        Assert.True(scheduler.RunStep());
        Assert.True(scheduler.TryAdvanceVirtualTime());
        Assert.True(scheduler.RunStep());

        Assert.Equal(node, callbackNode);
        Assert.True(scheduler.HasPendingWork(node));
        scheduler.RemovePendingWork(node);

        Assert.False(scheduler.HasPendingWork(node));
        Assert.Equal(0, scheduler.Drain());
        Assert.False(detachedCallbackRan);
        elapsedRegistration!.Dispose();
        detachedRegistration!.Dispose();
    }

    [Fact]
    public void CancelCreatedOperationTransitionsToCanceledWithoutRunning()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ran = false;
        var op = scheduler.Register("w", () => ran = true);

        scheduler.Cancel(op);

        Assert.Equal(SimulationOperationState.Canceled, op.State);
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

        Assert.Equal(SimulationOperationState.Canceled, op.State);
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
            scheduler.Pause(SimulationPauseReason.ResourceWait("never-signaled"));
            afterPauseRan = true;
        });

        Assert.True(scheduler.RunStep());
        Assert.Equal(SimulationOperationState.Paused, op.State);

        scheduler.Cancel(op);

        Assert.Equal(SimulationOperationState.Canceled, op.State);
        Assert.False(afterPauseRan);
        Assert.True(SpinUntil(() => bodyThread is { IsAlive: false }), "The parked thread should have unwound and exited.");
    }

    [Fact]
    public void CancelIsIdempotentOnTerminalOperations()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var op = scheduler.Schedule("w", () => { });
        scheduler.Drain();
        Assert.Equal(SimulationOperationState.Completed, op.State);

        // Canceling an already-completed operation is a no-op, not an illegal transition.
        scheduler.Cancel(op);
        Assert.Equal(SimulationOperationState.Completed, op.State);
    }

    [Fact]
    public void CannotCancelTheRunningOperationFromWithinItsOwnBody()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        SimulationOperation? op = null;
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

        Assert.IsType<SimulationSchedulerException>(caught);
        Assert.Equal(SimulationOperationState.Completed, op!.State);
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
        var op = scheduler.Schedule("w", () => scheduler.Pause(SimulationPauseReason.Yield));
        scheduler.RunStep();
        scheduler.Resume(op);
        scheduler.RunStep();

        Assert.Equal(
            ["1:Created", "1:Runnable", "1:Running", "1:Paused", "1:Runnable", "1:Running", "1:Completed"],
            listener.Formatted);
    }

    [Fact]
    public void ListenerSerializesPausedBeforeExternalCancellationMakesOperationRunnable()
    {
        var listener = new BlockingPauseListener(TestContext.Current.CancellationToken);
        using var scheduler = SchedulerTestHarness.NewScheduler(listener);
        listener.Scheduler = scheduler;
        using var cancellation = new CancellationTokenSource();
        var resource = scheduler.CreateResource(
            Clockwork.Runtime.Scheduling.Resources.SimulationResourceKind.Semaphore,
            "listener-race");
        scheduler.Schedule(
            "waiter",
            () => scheduler.WaitOnResource(
                resource,
                Timeout.InfiniteTimeSpan,
                SimulationPauseReason.ResourceWait("listener-race"),
                cancellation.Token));

        var driver = new Thread(() => scheduler.RunStep()) { IsBackground = true };
        driver.Start();
        Assert.True(listener.PausedEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        var canceler = new Thread(cancellation.Cancel) { IsBackground = true };
        canceler.Start();
        canceler.Join(TimeSpan.FromMilliseconds(200));

        listener.ReleasePaused.Set();
        Assert.True(canceler.Join(TimeSpan.FromSeconds(5)));
        Assert.True(driver.Join(TimeSpan.FromSeconds(5)));
        scheduler.Drain();

        Assert.Equal(
            [
                SimulationOperationState.Created,
                SimulationOperationState.Runnable,
                SimulationOperationState.Running,
                SimulationOperationState.Paused,
                SimulationOperationState.Runnable,
            ],
            listener.Events.Take(5).Select(e => e.EventState));
        Assert.All(listener.Events.Take(5), e => Assert.Equal(e.EventState, e.SnapshotState));
    }

    [Fact]
    public void TerminalListenerCanDisposeSchedulerAfterHandback()
    {
        var listener = new DisposeOnCompletionListener();
        using var scheduler = SchedulerTestHarness.NewScheduler(listener);
        listener.Scheduler = scheduler;
        var operation = scheduler.Schedule("complete", () => { });

        Assert.True(scheduler.RunStep());

        Assert.Equal(SimulationOperationState.Completed, operation.State);
        Assert.Contains(SimulationOperationState.Completed, listener.Events);
    }

    [Fact]
    public void TerminalListenerDisposeDefersRegistrationCleanupUntilPublicationGateIsReleased()
    {
        using var cancellation = new CancellationTokenSource();
        var listener = new DisposeWithInFlightCancellationListener(
            cancellation,
            TestContext.Current.CancellationToken);
        using var scheduler = SchedulerTestHarness.NewScheduler(listener);
        listener.Scheduler = scheduler;
        var resource = scheduler.CreateResource(
            Clockwork.Runtime.Scheduling.Resources.SimulationResourceKind.Semaphore,
            "pending-cancellation");
        var victim = scheduler.Schedule(
            "victim",
            () => scheduler.WaitOnResource(
                resource,
                Timeout.InfiniteTimeSpan,
                SimulationPauseReason.ResourceWait("pending-cancellation"),
                cancellation.Token));
        Assert.True(scheduler.RunStep());
        Assert.Equal(SimulationOperationState.Paused, victim.State);

        using var probeRegistration = cancellation.Token.Register(listener.CancellationStarted.Set);
        scheduler.Schedule("dispose-trigger", () => { });

        Assert.True(scheduler.RunStep());
        Assert.NotNull(listener.CancellationThread);
        Assert.True(listener.CancellationThread!.Join(TimeSpan.FromSeconds(5)));
        Assert.Equal(SimulationOperationState.Canceled, victim.State);
    }

    [Fact]
    public void ListenerReentrantDrivingIsRejectedInsteadOfDeadlocking()
    {
        var listener = new ReentrantDriveListener();
        using var scheduler = SchedulerTestHarness.NewScheduler(listener);
        listener.Scheduler = scheduler;
        scheduler.Schedule("work", () => { });

        var exception = Assert.IsType<SimulationSchedulerException>(listener.Exception);
        Assert.Contains("reentrantly", exception.Message, StringComparison.Ordinal);
        scheduler.Drain();
    }

    [Fact]
    public void ListenerCannotCancelCurrentOperationBeforeHandback()
    {
        var listener = new CancelCurrentOnPausedListener();
        using var scheduler = SchedulerTestHarness.NewScheduler(listener);
        listener.Scheduler = scheduler;
        var operation = scheduler.Schedule(
            "pause",
            () => scheduler.Pause(SimulationPauseReason.ResourceWait("pause")));

        Assert.True(scheduler.RunStep());

        var exception = Assert.IsType<SimulationSchedulerException>(listener.Exception);
        Assert.Contains("before it hands control back", exception.Message, StringComparison.Ordinal);
        Assert.Equal(SimulationOperationState.Paused, operation.State);
        scheduler.Cancel(operation);
    }

    [Fact]
    public void SignalAllPublishesEachRunnableBeforeReentrantCancellationOfLaterWaiter()
    {
        var listener = new CancelLaterWaiterListener();
        using var scheduler = SchedulerTestHarness.NewScheduler(listener);
        listener.Scheduler = scheduler;
        var resource = scheduler.CreateResource(
            Clockwork.Runtime.Scheduling.Resources.SimulationResourceKind.Semaphore,
            "signal-all");
        var first = scheduler.Schedule(
            "first",
            () => scheduler.WaitOnResource(resource, SimulationPauseReason.ResourceWait("signal-all")));
        var second = scheduler.Schedule(
            "second",
            () => scheduler.WaitOnResource(resource, SimulationPauseReason.ResourceWait("signal-all")));
        Assert.True(scheduler.RunStep());
        Assert.True(scheduler.RunStep());
        listener.TriggerId = first.Id;
        listener.Target = second;

        var woken = scheduler.SignalAll(resource);
        scheduler.Drain();

        Assert.Equal([first], woken);
        Assert.Equal(
            [
                SimulationOperationState.Created,
                SimulationOperationState.Runnable,
                SimulationOperationState.Running,
                SimulationOperationState.Paused,
                SimulationOperationState.Canceled,
            ],
            listener.Events.Where(e => e.Id == second.Id).Select(e => e.EventState));
        Assert.All(listener.Events, e => Assert.Equal(e.EventState, e.SnapshotState));
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
        Assert.Equal(SimulationOperationState.Created, status[0].State);
        Assert.Equal(b.Id, status[1].Id);
        Assert.Equal(SimulationOperationState.Runnable, status[1].State);
    }

    private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5));

    private sealed class BlockingPauseListener(CancellationToken cancellationToken) : ISimulationOperationListener
    {
        private readonly ConcurrentQueue<(SimulationOperationState EventState, SimulationOperationState SnapshotState)> _events = new();

        public ManualResetEventSlim PausedEntered { get; } = new();

        public ManualResetEventSlim ReleasePaused { get; } = new();

        public SimulationScheduler Scheduler { get; set; } = null!;

        public IReadOnlyList<(SimulationOperationState EventState, SimulationOperationState SnapshotState)> Events =>
            _events.ToArray();

        public void OnStateChanged(SimulationOperation operation, SimulationOperationState newState)
        {
            if (newState == SimulationOperationState.Paused)
            {
                PausedEntered.Set();
                ReleasePaused.Wait(cancellationToken);
            }

            var snapshot = Assert.Single(Scheduler.CaptureStatus(), status => status.Id == operation.Id);
            _events.Enqueue((newState, snapshot.State));
        }
    }

    private sealed class DisposeOnCompletionListener : ISimulationOperationListener
    {
        private readonly ConcurrentQueue<SimulationOperationState> _events = new();

        public SimulationScheduler Scheduler { get; set; } = null!;

        public IReadOnlyList<SimulationOperationState> Events => _events.ToArray();

        public void OnStateChanged(SimulationOperation operation, SimulationOperationState newState)
        {
            _events.Enqueue(newState);
            if (newState == SimulationOperationState.Completed)
            {
                Scheduler.Dispose();
            }
        }
    }

    private sealed class DisposeWithInFlightCancellationListener(
        CancellationTokenSource cancellation,
        CancellationToken testCancellation) : ISimulationOperationListener
    {
        public ManualResetEventSlim CancellationStarted { get; } = new();

        public SimulationScheduler Scheduler { get; set; } = null!;

        public Thread? CancellationThread { get; private set; }

        public void OnStateChanged(SimulationOperation operation, SimulationOperationState newState)
        {
            if (newState != SimulationOperationState.Completed ||
                !string.Equals(operation.WorkDescription, "dispose-trigger", StringComparison.Ordinal))
            {
                return;
            }

            CancellationThread = new Thread(cancellation.Cancel) { IsBackground = true };
            CancellationThread.Start();
            Assert.True(CancellationStarted.Wait(TimeSpan.FromSeconds(5), testCancellation));
            Thread.SpinWait(100_000);
            Scheduler.Dispose();
        }
    }

    private sealed class ReentrantDriveListener : ISimulationOperationListener
    {
        public SimulationScheduler Scheduler { get; set; } = null!;

        public Exception? Exception { get; private set; }

        public void OnStateChanged(SimulationOperation operation, SimulationOperationState newState)
        {
            if (newState != SimulationOperationState.Runnable)
            {
                return;
            }

            try
            {
                Scheduler.RunStep();
            }
            catch (Exception exception)
            {
                Exception = exception;
            }
        }
    }

    private sealed class CancelCurrentOnPausedListener : ISimulationOperationListener
    {
        public SimulationScheduler Scheduler { get; set; } = null!;

        public Exception? Exception { get; private set; }

        public void OnStateChanged(SimulationOperation operation, SimulationOperationState newState)
        {
            if (newState != SimulationOperationState.Paused)
            {
                return;
            }

            try
            {
                Scheduler.Cancel(operation);
            }
            catch (Exception exception)
            {
                Exception = exception;
            }
        }
    }

    private sealed class CancelLaterWaiterListener : ISimulationOperationListener
    {
        private readonly ConcurrentQueue<(SimulationOperationId Id, SimulationOperationState EventState, SimulationOperationState SnapshotState)> _events = new();

        public SimulationScheduler Scheduler { get; set; } = null!;

        public SimulationOperationId TriggerId { get; set; }

        public SimulationOperation? Target { get; set; }

        public IReadOnlyList<(SimulationOperationId Id, SimulationOperationState EventState, SimulationOperationState SnapshotState)> Events =>
            _events.ToArray();

        public void OnStateChanged(SimulationOperation operation, SimulationOperationState newState)
        {
            _events.Enqueue((operation.Id, newState, operation.State));
            if (newState == SimulationOperationState.Runnable &&
                operation.Id == TriggerId &&
                Target is not null)
            {
                Scheduler.Cancel(Target);
            }
        }
    }
}

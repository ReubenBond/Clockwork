using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Clockwork.Runtime.Decisions;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling.Resources;
using Clockwork.Runtime.Scheduling.Strategies;

namespace Clockwork.Runtime.Scheduling;

/// <summary>
/// <para>
/// The controlled-operation kernel: the single authority that registers
/// <see cref="ControlledOperation"/>s, chooses exactly one runnable operation at a time, grants and
/// revokes the permission baton, drives every legal state transition, and performs terminal
/// cleanup. This is the foundational scheduling layer future controlled <c>Monitor</c>, semaphore,
/// wait-handle, and synchronous <see cref="Task"/> waits (Phase 3B) will build on. It owns the
/// reusable controlled-resource model, atomic resource waits with virtual-time timeouts, and the
/// modeled clock those timeouts fire against; cancellation-token races and deadlock detection are
/// layered on in later Phase 3B increments.
/// </para>
/// <para>
/// <b>Physical-thread gating.</b> Each operation runs on its own dedicated physical thread, but the
/// scheduler grants a single permission baton to at most one operation at a time and blocks the
/// controlling thread until that operation hands control back (by pausing, yielding, completing, or
/// faulting). The handoff uses counting wait handles - never busy-spinning - so at most one
/// operation ever executes system-under-test code, no matter how many physical threads exist. No
/// operation is ever aborted with an unsafe <c>Thread.Abort</c>; teardown unwinds parked threads
/// cooperatively.
/// </para>
/// <para>
/// <b>Threading contract.</b> The "controlling thread" is whichever thread calls
/// <see cref="RunStep"/>/<see cref="Drain"/>; only one thread may drive the scheduler at a time.
/// <see cref="Register"/>, <see cref="Admit"/>, <see cref="Resume"/>, and <see cref="Cancel"/> take
/// the scheduler lock and may be called from the controlling thread or from within a running
/// operation's body (which is how nested scheduling and cross-operation resume work); while an
/// operation body runs, the controlling thread is blocked inside <see cref="RunStep"/>, so state
/// transitions are always serialized.
/// </para>
/// </summary>
public sealed class ControlledOperationScheduler : IDisposable
{
    /// <summary>
    /// The bounded time the scheduler waits for a torn-down operation's physical thread to unwind
    /// and exit before giving up. A thread that refuses to exit within this window is a
    /// system-under-test bug (e.g. an infinite loop that never parks); the scheduler surfaces that
    /// rather than blocking teardown forever.
    /// </summary>
    public static readonly TimeSpan ThreadJoinTimeout = TimeSpan.FromSeconds(30);

    [ThreadStatic]
    private static ControlledOperation? t_currentOperation;

    private readonly object _gate = new();
    private readonly object _transitionPublicationGate = new();
    private readonly SortedDictionary<ControlledOperationId, ControlledOperation> _operations = new();
    private readonly SortedDictionary<ControlledResourceId, ControlledResource> _resources = new();
    private readonly SemaphoreSlim _handback = new(0, 1);
    private readonly SimulationLogicalExecutionIdSource _logicalIds = new();
    private readonly ControlledVirtualClock _clock = new();
    private readonly List<RaceSchedulingPoint> _raceSchedulingPoints = [];
    private readonly RaceTracker _raceTracker = new();
    private readonly SimulationActivationToken _activationToken;
    private readonly SimulationRuntimeIdentity _runtime;
    private readonly IControlledOperationListener? _listener;

    private IControlledSchedulingStrategy _strategy = new RoundRobinSchedulingStrategy();
    private ISimulationDecisionLog? _decisionLog;
    private SimulationDecisionReplayValidator? _replayValidator;
    private long _nextReplayDecisionId;

    private long _nextOperationId;
    private long _nextResourceId;
    private long _nextWaiterSequence;
    private long _nextRaceSchedulingPointSequence;
    private ControlledOperationId _lastSelected = ControlledOperationId.None;
    private ControlledOperation? _current;
    private int _controlThreadId;
    private bool _controlThreadBusy;
    private bool _disposed;
    private ControlledOperation? _pendingTerminalNotification;
    private int _transitionPublicationDepth;
    private List<CancellationTokenRegistration>? _deferredRegistrationDisposals;

    /// <summary>
    /// Initializes a new instance of the <see cref="ControlledOperationScheduler"/> class.
    /// </summary>
    /// <param name="activationToken">
    /// The simulation host's activation token (see <see cref="SimulationActivationToken"/>). The
    /// scheduler uses it to install ambient <see cref="SimulationExecutionContext"/> on each
    /// operation's physical thread, so operation code and the decision log observe the correct
    /// runtime/node/logical identity.
    /// </param>
    /// <param name="runtime">The simulation runtime identity every operation runs under.</param>
    /// <param name="listener">An optional lifecycle listener for diagnostics.</param>
    public ControlledOperationScheduler(
        SimulationActivationToken activationToken,
        SimulationRuntimeIdentity runtime,
        IControlledOperationListener? listener = null)
    {
        ArgumentNullException.ThrowIfNull(activationToken);
        ArgumentNullException.ThrowIfNull(runtime);
        _activationToken = activationToken;
        _runtime = runtime;
        _listener = listener;
    }

    /// <summary>Gets the runtime identity every operation in this scheduler runs under.</summary>
    public SimulationRuntimeIdentity Runtime => _runtime;

    /// <summary>
    /// Gets or sets the policy that chooses which runnable operation runs next. Defaults to
    /// <see cref="RoundRobinSchedulingStrategy"/> - the Phase 3A behavior - so existing simulations are
    /// unaffected. Assigning a strategy takes effect on the next selection. Set this before driving the
    /// scheduler for a reproducible schedule.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public IControlledSchedulingStrategy SchedulingStrategy
    {
        get
        {
            lock (_gate)
            {
                return _strategy;
            }
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate)
            {
                _strategy = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the decision log that captures every scheduling choice made among two or more
    /// runnable operations, as a <see cref="SimulationDecisionKind.SchedulingOrder"/> decision in the
    /// <see cref="SimulationSeedDomain.Scheduler"/> domain. When <see langword="null"/> (the default),
    /// no scheduling decisions are recorded. Attach a log to capture a schedule for later exact replay
    /// via <see cref="ReplaySchedulingStrategy"/>. Single-candidate steps are never recorded, so
    /// recording is stable against interleavings where only one operation is runnable.
    /// </summary>
    public ISimulationDecisionLog? DecisionLog
    {
        get
        {
            lock (_gate)
            {
                return _decisionLog;
            }
        }

        set
        {
            lock (_gate)
            {
                _decisionLog = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a validator that checks each scheduling choice against a previously recorded run,
    /// throwing <see cref="SimulationDecisionReplayMismatchException"/> at the first divergence. This
    /// is orthogonal to <see cref="DecisionLog"/>: a validator can be attached with or without a live
    /// log, and with or without a <see cref="ReplaySchedulingStrategy"/>, to assert that a re-run's
    /// scheduling decisions reproduce the original exactly.
    /// </summary>
    public SimulationDecisionReplayValidator? ReplayValidator
    {
        get
        {
            lock (_gate)
            {
                return _replayValidator;
            }
        }

        set
        {
            lock (_gate)
            {
                _replayValidator = value;
            }
        }
    }

    /// <summary>
    /// The single logical-owner identity that both the controlling thread and every operation thread
    /// of a scheduler report while a controlled drive is in progress. It is deliberately outside the
    /// range of real <see cref="Environment.CurrentManagedThreadId"/> values (which are always
    /// positive) so a single-threaded guard keyed on <see cref="IsSimulationThread"/> treats a
    /// legitimate baton handoff as reentrant while still flagging genuinely escaped async work on an
    /// unrelated thread.
    /// </summary>
    public const int SimulationLogicalThreadOwnerId = int.MinValue;

    /// <summary>
    /// Gets a value indicating whether the calling physical thread is part of this scheduler's single
    /// logical simulation thread right now: either a thread currently executing one of this
    /// scheduler's operation bodies, or the controlling thread while it drives a step (see
    /// <see cref="EnterControlScope"/>). Intended to build a logical-owner delegate for a
    /// single-threaded guard.
    /// </summary>
    public bool IsSimulationThread =>
        (t_currentOperation is { } op && ReferenceEquals(op.Scheduler, this))
        || Environment.CurrentManagedThreadId == Volatile.Read(ref _controlThreadId);

    /// <summary>
    /// Marks the calling thread as this scheduler's controlling thread until the returned scope is
    /// disposed, so <see cref="IsSimulationThread"/> reports <see langword="true"/> for it. A host
    /// integrating a single-threaded guard wraps the code that drives the scheduler (dequeue plus
    /// <see cref="RunStep"/>) in this scope so the guard treats the controlling thread and the
    /// operation threads as one logical owner.
    /// </summary>
    /// <returns>A disposable that restores the previous controlling-thread marker.</returns>
    public IDisposable EnterControlScope()
    {
        var previous = Interlocked.Exchange(ref _controlThreadId, Environment.CurrentManagedThreadId);
        return new ControlScope(this, previous);
    }

    /// <summary>
    /// Gets the operation the calling thread is currently executing as, or <see langword="null"/> if
    /// the calling thread is not a controlled-operation thread of this scheduler. This is how
    /// pause/yield primitives and nested scheduling discover "who am I".
    /// </summary>
    public ControlledOperation? CurrentOperation =>
        t_currentOperation is { } op && !ReferenceEquals(op.Scheduler, this) ? null : t_currentOperation;

    /// <summary>
    /// Gets the scheduler and operation currently executing on the calling physical thread.
    /// </summary>
    internal static bool TryGetExecutingOperation(
        [NotNullWhen(true)]
        out ControlledOperationScheduler? scheduler,
        [NotNullWhen(true)]
        out ControlledOperation? operation)
    {
        operation = t_currentOperation;
        scheduler = operation?.Scheduler;
        return scheduler is not null;
    }

    /// <summary>Captures the injected race-exploration points reached so far in deterministic order.</summary>
    public IReadOnlyList<RaceSchedulingPoint> CaptureRaceSchedulingPoints()
    {
        lock (_gate)
        {
            return [.. _raceSchedulingPoints];
        }
    }

    /// <summary>Gets the deterministic first race detected in this scheduler, if any.</summary>
    public RaceReport? FirstRace
    {
        get
        {
            lock (_gate)
            {
                return _raceTracker.FirstRace;
            }
        }
    }

    /// <summary>Gets the structured race-specific outcome observed so far.</summary>
    public RaceExplorationResult RaceExplorationResult
    {
        get
        {
            lock (_gate)
            {
                RaceReport? race = _raceTracker.FirstRace;
                return race is null
                    ? new RaceExplorationResult(RaceExplorationTerminationReason.CompletedWithoutRace, null)
                    : new RaceExplorationResult(RaceExplorationTerminationReason.RaceDetected, race);
            }
        }
    }

    /// <summary>
    /// Records an injected scheduling point and yields the current operation through the configured
    /// scheduling strategy and decision/replay pipeline.
    /// </summary>
    internal void ReachRaceSchedulingPoint(
        RaceAccessKind kind,
        RaceMemoryLocationKind? locationKind,
        object? target,
        string member,
        long? elementIndex,
        RaceSourceLocation source)
    {
        ControlledOperation operation = RequireCurrentOperation();
        bool suppressInterleaving;
        lock (_gate)
        {
            RaceMemoryLocation? location = _raceTracker.ResolveLocation(
                locationKind,
                target,
                member,
                elementIndex);
            var point = new RaceSchedulingPoint(
                ++_nextRaceSchedulingPointSequence,
                operation.Id,
                kind,
                location?.ToString() ?? member,
                source);
            _raceSchedulingPoints.Add(point);
            if (location is { } tracked)
            {
                _raceTracker.RecordAccess(operation, kind, tracked, source, _raceSchedulingPoints);
            }

            suppressInterleaving = _raceTracker.HasHeldSynchronization(operation);
        }

        if (!suppressInterleaving)
        {
            Yield();
        }
    }

    internal void EnterRaceSynchronization(object synchronization)
    {
        ControlledOperation operation = RequireCurrentOperation();
        lock (_gate)
        {
            _raceTracker.EnterSynchronization(operation, synchronization);
        }
    }

    internal void ExitRaceSynchronization(object synchronization)
    {
        ControlledOperation operation = RequireCurrentOperation();
        lock (_gate)
        {
            _raceTracker.ExitSynchronization(operation, synchronization);
        }
    }

    internal void SignalRaceSynchronization(object synchronization)
    {
        ControlledOperation operation = RequireCurrentOperation();
        lock (_gate)
        {
            _raceTracker.SignalSynchronization(operation, synchronization);
        }
    }

    internal void WaitRaceSynchronization(object synchronization)
    {
        ControlledOperation operation = RequireCurrentOperation();
        lock (_gate)
        {
            _raceTracker.WaitSynchronization(operation, synchronization);
        }
    }

    /// <summary>
    /// Registers a new operation in the <see cref="ControlledOperationState.Created"/> state without
    /// admitting it for scheduling. The operation's parent is the operation the calling thread is
    /// running as (enabling nested parent/child identity), or <see cref="ControlledOperationId.None"/>
    /// for a root registration.
    /// </summary>
    /// <param name="workDescription">A short, stable description of the work, for diagnostics.</param>
    /// <param name="body">The operation body. Runs exactly once, on the operation's own thread.</param>
    /// <param name="node">The node the operation is scoped to, or <see langword="null"/> for cluster-level work.</param>
    /// <param name="priority">
    /// The operation's scheduling priority (see <see cref="ControlledOperation.Priority"/>). Only the
    /// <see cref="Strategies.PrioritySchedulingStrategy"/> consults it; it is inert under the other
    /// strategies. Defaults to <c>0</c>.
    /// </param>
    /// <returns>The newly created operation.</returns>
    public ControlledOperation Register(string workDescription, Action body, SimulationNodeIdentity? node = null, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(workDescription);
        ArgumentNullException.ThrowIfNull(body);

        var capturedContext = ExecutionContext.Capture();
        ControlledOperation operation;
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                ControlledOperation? parent =
                    t_currentOperation is { } currentParent && ReferenceEquals(currentParent.Scheduler, this)
                        ? currentParent
                        : null;
                var parentId = parent is not null
                    ? parent.Id
                    : ControlledOperationId.None;
                var id = new ControlledOperationId(++_nextOperationId);
                operation = new ControlledOperation(
                    this,
                    id,
                    parentId,
                    _runtime,
                    node,
                    _logicalIds.Next(),
                    workDescription,
                    body,
                    capturedContext,
                    priority);
                _operations.Add(id, operation);
                _raceTracker.RegisterOperation(operation, parent);
            }

            Notify(operation, ControlledOperationState.Created);
        }

        return operation;
    }

    /// <summary>
    /// Registers and immediately admits an operation, so it is <see cref="ControlledOperationState.Runnable"/>
    /// on return. Convenience for the common "create a ready-to-run operation" case.
    /// </summary>
    /// <param name="workDescription">A short, stable description of the work, for diagnostics.</param>
    /// <param name="body">The operation body.</param>
    /// <param name="node">The node the operation is scoped to, or <see langword="null"/>.</param>
    /// <param name="priority">
    /// The operation's scheduling priority (see <see cref="ControlledOperation.Priority"/>); only the
    /// <see cref="Strategies.PrioritySchedulingStrategy"/> consults it. Defaults to <c>0</c>.
    /// </param>
    /// <returns>The newly created, admitted operation.</returns>
    public ControlledOperation Schedule(string workDescription, Action body, SimulationNodeIdentity? node = null, int priority = 0)
    {
        var operation = Register(workDescription, body, node, priority);
        Admit(operation);
        return operation;
    }

    /// <summary>
    /// Creates a new <see cref="ControlledResource"/> owned by this scheduler, with a stable,
    /// monotonically-assigned <see cref="ControlledResourceId"/>. The resource starts unowned, with no
    /// waiters; acquire/release policy and waits are layered on top by callers via the scheduler's
    /// wait/signal primitives and the resource's own bookkeeping fields. This is the entry point a
    /// future controlled primitive (Phase 6/7) uses to obtain the resource backing one sync object.
    /// </summary>
    /// <param name="kind">The diagnostic classification of the primitive the resource models.</param>
    /// <param name="name">A short, stable, human-readable name for diagnostics.</param>
    /// <returns>The newly created resource.</returns>
    public ControlledResource CreateResource(ControlledResourceKind kind, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_gate)
        {
            ThrowIfDisposed();
            var id = new ControlledResourceId(++_nextResourceId);
            var resource = new ControlledResource(this, id, kind, name);
            _resources.Add(id, resource);
            return resource;
        }
    }

    /// <summary>
    /// Captures a deterministic snapshot of every resource currently registered with this scheduler,
    /// in stable <see cref="ControlledResourceId"/> order, for diagnostics and deadlock reporting.
    /// </summary>
    /// <returns>An ordered, immutable list of resources.</returns>
    public IReadOnlyList<ControlledResource> CaptureResources()
    {
        lock (_gate)
        {
            return [.. _resources.Values];
        }
    }


    /// <summary>
    /// Records (or clears) the operation that owns <paramref name="resource"/>. Ownership is the raw
    /// metadata the wait-for graph reads to draw a "waiter -&gt; owner" edge and the hook a future
    /// controlled <c>Monitor</c> or synchronous <c>Task</c> wait sets when it acquires a resource; this
    /// method deliberately performs no acquire/release policy of its own (it does not block or count),
    /// so each primitive composes its own semantics on top. Must be given operations/resources that
    /// belong to this scheduler.
    /// </summary>
    /// <param name="resource">The resource whose owner is being set. Must belong to this scheduler.</param>
    /// <param name="owner">The owning operation, or <see langword="null"/> to mark the resource unowned.</param>
    public void MarkResourceOwner(ControlledResource resource, ControlledOperation? owner)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ValidateResourceOwnership(resource);
        if (owner is not null)
        {
            ValidateOwnership(owner);
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            resource.Owner = owner;
        }
    }

    /// <summary>
    /// Admits a <see cref="ControlledOperationState.Created"/> operation for scheduling, transitioning
    /// it to <see cref="ControlledOperationState.Runnable"/>.
    /// </summary>
    /// <param name="operation">The operation to admit.</param>
    public void Admit(ControlledOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateOwnership(operation);
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                operation.ApplyTransition(ControlledOperationState.Runnable);
            }

            Notify(operation, ControlledOperationState.Runnable);
        }
    }

    /// <summary>
    /// Selects exactly one <see cref="ControlledOperationState.Runnable"/> operation deterministically,
    /// grants it the permission baton, and blocks the controlling thread until that operation hands
    /// control back by pausing, yielding, completing, or faulting. Terminal operations are cleaned up
    /// before returning.
    /// <para>
    /// Selection is a stable round-robin by <see cref="ControlledOperation.Id"/>: the runnable
    /// operation with the smallest id greater than the last-selected one, wrapping to the smallest
    /// runnable id when none is greater. When no operation ever yields (the compatibility case) this
    /// is identical to strict registration order - each operation runs to completion before the next
    /// starts - so legacy deterministic ordering is preserved; when operations yield it gives every
    /// operation a turn instead of starving all but the lowest id. Fairness/priority strategies beyond
    /// this fixed rotation are deferred to Phase 3B.
    /// </para>
    /// </summary>
    /// <returns><see langword="true"/> if an operation ran; <see langword="false"/> if none was runnable.</returns>
    public bool RunStep()
    {
        if (Monitor.IsEntered(_transitionPublicationGate))
        {
            throw new ControlledOperationException(
                "RunStep/Drain cannot be driven reentrantly from an operation listener callback. " +
                "Listeners may inspect state or request non-driving transitions, but the controlling thread must resume scheduler driving after the callback returns.");
        }

        ControlledOperation operation;
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_controlThreadBusy)
                {
                    throw new ControlledOperationException(
                        "The scheduler is already being driven by another thread. RunStep/Drain must be driven by a single controlling thread at a time.");
                }

                var next = SelectRunnable();
                if (next is null)
                {
                    return false;
                }

                operation = next;
                operation.ApplyTransition(ControlledOperationState.Running);
                _current = operation;
                _controlThreadBusy = true;
            }

            Notify(operation, ControlledOperationState.Running);
        }

        EnsureThreadStarted(operation);

        // Grant the baton and block until the operation hands control back. Only the granted
        // operation executes SUT code between here and the handback.
        operation.GrantPermission();
        _handback.Wait();

        ControlledOperationState resulting;
        lock (_gate)
        {
            resulting = operation.State;
            _current = null;
            _controlThreadBusy = false;
        }

        if (ControlledOperation.IsTerminalState(resulting))
        {
            FinalizeTerminal(operation);
        }

        return true;
    }

    /// <summary>
    /// Repeatedly runs steps until no operation is runnable, advancing modeled virtual time to fire
    /// the next due timeout whenever the runnable set is exhausted, and repeating until neither any
    /// operation is runnable nor any timeout is pending. Returns the number of steps executed (time
    /// advances are not steps). A step count that keeps growing without the set of operations
    /// shrinking indicates operations that only ever yield; callers that need a bound should cap
    /// iterations themselves.
    /// </summary>
    /// <returns>The number of steps executed.</returns>
    public int Drain()
    {
        var steps = 0;
        while (true)
        {
            while (RunStep())
            {
                steps++;
            }

            if (!TryAdvanceVirtualTime())
            {
                break;
            }
        }

        return steps;
    }

    /// <summary>
    /// Validates that decision and scheduling replay streams were consumed completely at an explicit
    /// successful end-of-run boundary.
    /// </summary>
    /// <remarks>
    /// <see cref="Drain"/> does not call this automatically because a scheduler is reusable: a quiescent
    /// batch can be followed by more scheduled work whose replay records remain unread. Call this once
    /// the overall run is known to be complete. Partial or aborted runs must omit this call.
    /// </remarks>
    public void ValidateReplayComplete()
    {
        lock (_gate)
        {
            foreach (var operation in _operations.Values)
            {
                if (!operation.IsTerminal)
                {
                    throw new ControlledOperationException(
                        "Replay completion cannot be validated while non-terminal operations remain. " +
                        "Finish the run first, or omit completion validation for a partial or aborted run.");
                }
            }
        }

        _replayValidator?.ValidateComplete();
        if (_strategy is ReplaySchedulingStrategy replay)
        {
            replay.ValidateComplete();
        }
    }

    /// <summary>
    /// Validates complete consumption of decision streams at an explicit terminal boundary which may
    /// retain non-terminal operations, such as a diagnosed deadlock.
    /// </summary>
    public void ValidateReplayDecisionStreamsComplete()
    {
        _replayValidator?.ValidateComplete();
        if (_strategy is ReplaySchedulingStrategy replay)
        {
            replay.ValidateComplete();
        }
    }

    /// <summary>
    /// Advances modeled virtual time to the earliest pending timeout and fires every timeout due at
    /// that instant, resolving each affected waiter as <see cref="ControlledWaitOutcome.TimedOut"/>
    /// and making its operation runnable. Advancing is only legal when no operation is currently
    /// runnable (the controlling thread is idle and nothing can make progress in the present instant);
    /// this is what guarantees a pending signal always precedes a same-instant timeout. It is a no-op
    /// - returning <see langword="false"/> - when an operation is still runnable or no timeout is
    /// pending.
    /// </summary>
    /// <returns><see langword="true"/> if time advanced and at least one waiter timed out; otherwise <see langword="false"/>.</returns>
    public bool TryAdvanceVirtualTime()
    {
        IReadOnlyList<ControlledTimeoutRegistration> due;
        var timedOut = 0;
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_controlThreadBusy)
                {
                    throw new ControlledOperationException(
                        "Cannot advance virtual time while an operation is running; drive RunStep to quiescence first.");
                }

                if (HasRunnableUnderLock() || !_clock.HasPending)
                {
                    return false;
                }

                due = _clock.AdvanceToNextDue();
            }

            foreach (var timeoutRegistration in due)
            {
                ControlledOperation? operation = null;
                CancellationTokenRegistration cancellationRegistration = default;
                lock (_gate)
                {
                    var waiter = timeoutRegistration.Waiter;
                    if (waiter.TryResolve(ControlledWaitOutcome.TimedOut))
                    {
                        RecordWaitResolution(waiter, ControlledWaitOutcome.TimedOut);
                        cancellationRegistration = DetachWaiterRegistrationsUnderLock(waiter);
                        waiter.Resource.RemoveWaiter(waiter);
                        waiter.Operation.ApplyTransition(ControlledOperationState.Runnable);
                        operation = waiter.Operation;
                    }
                }

                if (operation is not null)
                {
                    Notify(operation, ControlledOperationState.Runnable);
                    DisposeRegistration(cancellationRegistration);
                    timedOut++;
                }
            }
        }

        return timedOut > 0;
    }

    private bool HasRunnableUnderLock()
    {
        foreach (var operation in _operations.Values)
        {
            if (operation.State == ControlledOperationState.Runnable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resumes a <see cref="ControlledOperationState.Paused"/> operation, transitioning it back to
    /// <see cref="ControlledOperationState.Runnable"/> so a subsequent <see cref="RunStep"/> can grant
    /// it the baton again. Its physical thread stays parked (not busy-spinning) until then.
    /// </summary>
    /// <param name="operation">The paused operation to resume.</param>
    public void Resume(ControlledOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateOwnership(operation);
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (operation.Waiter is not null)
                {
                    throw new ControlledOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Cannot resume operation {operation.Id} directly while it has an active resource waiter. Resolve it through SignalOne/SignalAll, timeout, or cancellation so the wait receives exactly one outcome."));
                }

                operation.ApplyTransition(ControlledOperationState.Runnable);
            }

            Notify(operation, ControlledOperationState.Runnable);
        }
    }

    /// <summary>
    /// Cancels a non-running operation (<see cref="ControlledOperationState.Created"/>,
    /// <see cref="ControlledOperationState.Runnable"/>, or <see cref="ControlledOperationState.Paused"/>),
    /// transitioning it to <see cref="ControlledOperationState.Canceled"/>. If the operation has a
    /// parked physical thread, it is unwound cooperatively and joined. Cancellation is idempotent for
    /// already-terminal operations. The currently-running operation cannot be canceled from outside
    /// its own body (it holds the baton); it should observe cooperative cancellation instead.
    /// </summary>
    /// <param name="operation">The operation to cancel.</param>
    public void Cancel(ControlledOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateOwnership(operation);

        bool needsUnwind;
        CancellationTokenRegistration registration;
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                if (operation.IsTerminal)
                {
                    return;
                }

                if (ReferenceEquals(operation, _current))
                {
                    throw new ControlledOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Cannot externally cancel in-flight operation {operation.Id} before it hands control back to the scheduler; retry after RunStep returns or use cooperative cancellation from within its body."));
                }

                operation.RequestTermination();
                registration = DetachWaiterUnderLock(operation);
                operation.ApplyTransition(ControlledOperationState.Canceled);
                needsUnwind = operation.Thread is not null;
            }

            Notify(operation, ControlledOperationState.Canceled);
        }

        DisposeRegistration(registration);
        if (needsUnwind)
        {
            UnwindParkedThread(operation);
        }

    }

    /// <summary>
    /// Pauses the calling operation, releasing the permission baton with the given reason and parking
    /// the calling thread until the operation is resumed and re-granted the baton. Must be called from
    /// within a running operation body of this scheduler. When the operation is later torn down while
    /// paused, this method's park unwinds cooperatively.
    /// </summary>
    /// <param name="reason">Why the operation is pausing. Recorded for diagnostics and Phase 3B resource waits.</param>
    public void Pause(ControlledOperationPauseReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var operation = RequireCurrentOperation();
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                operation.ApplyTransition(ControlledOperationState.Paused, pauseReason: reason);
            }

            Notify(operation, ControlledOperationState.Paused);
        }

        HandBackAndPark(operation);
    }

    /// <summary>
    /// Yields the permission baton from the calling operation, giving the scheduler a chance to run
    /// another operation, while remaining immediately <see cref="ControlledOperationState.Runnable"/>.
    /// Must be called from within a running operation body of this scheduler.
    /// </summary>
    public void Yield()
    {
        var operation = RequireCurrentOperation();
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                operation.ApplyTransition(ControlledOperationState.Runnable);
            }

            Notify(operation, ControlledOperationState.Runnable);
        }

        HandBackAndPark(operation);
    }

    /// <summary>
    /// <para>
    /// Blocks the calling operation on <paramref name="resource"/> until another operation signals it
    /// (see <see cref="SignalOne"/>/<see cref="SignalAll"/>). This is the reusable wait primitive the
    /// later <c>Monitor</c>/<c>SemaphoreSlim</c>/wait-handle/synchronous-<c>Task</c>-wait shims are
    /// built on; this overload waits indefinitely (an infinite timeout, no cancellation).
    /// </para>
    /// <para>
    /// The transition is atomic under the scheduler lock: the operation is enqueued onto the
    /// resource's deterministic FIFO wait queue and moved <c>Running -&gt; Paused</c> in one critical
    /// section before the baton is yielded, so a signal delivered by the very next operation cannot be
    /// lost. An operation can be parked on at most one resource at a time (it is paused), which is why
    /// duplicate queue entries are impossible; a resolved waiter is skipped by every subsequent signal,
    /// which is why stale wakeups cannot occur.
    /// </para>
    /// </summary>
    /// <param name="resource">The resource to wait on. Must belong to this scheduler.</param>
    /// <param name="reason">The stable pause reason recorded for diagnostics and the wait-for graph.</param>
    /// <returns>
    /// The outcome of the wait. For this overload that is always <see cref="ControlledWaitOutcome.Signaled"/>
    /// - the wait only ends when the operation is signaled.
    /// </returns>
    public ControlledWaitOutcome WaitOnResource(ControlledResource resource, ControlledOperationPauseReason reason) =>
        WaitOnResourceCore(resource, Timeout.InfiniteTimeSpan, reason, CancellationToken.None);

    /// <summary>
    /// Blocks the calling operation on <paramref name="resource"/> until it is signaled or the
    /// modeled (virtual) timeout elapses, whichever happens first, resolving the race
    /// deterministically. Timeouts are driven purely by Clockwork's virtual time, never by a
    /// real-time timer, so the wait is fully replayable.
    /// <para>
    /// <b>Timeout semantics.</b> A <see cref="TimeSpan.Zero"/> timeout never parks: it resolves
    /// <see cref="ControlledWaitOutcome.TimedOut"/> immediately (a signal cannot arrive synchronously
    /// to the still-running operation). <see cref="Timeout.InfiniteTimeSpan"/> registers no timeout
    /// and behaves exactly like the signal-only overload. A finite, strictly positive timeout
    /// registers a virtual-time deadline; the deadline can only fire while no operation is runnable
    /// (see <see cref="TryAdvanceVirtualTime"/>), so a pending signal at the same instant always wins.
    /// </para>
    /// </summary>
    /// <param name="resource">The resource to wait on. Must belong to this scheduler.</param>
    /// <param name="timeout">
    /// The modeled wait budget: <see cref="TimeSpan.Zero"/> to poll, <see cref="Timeout.InfiniteTimeSpan"/>
    /// to wait forever, or any strictly positive span for a virtual-time deadline.
    /// </param>
    /// <param name="reason">The stable pause reason recorded for diagnostics and the wait-for graph.</param>
    /// <returns>
    /// <see cref="ControlledWaitOutcome.Signaled"/> if signaled first, otherwise
    /// <see cref="ControlledWaitOutcome.TimedOut"/>.
    /// </returns>
    public ControlledWaitOutcome WaitOnResource(ControlledResource resource, TimeSpan timeout, ControlledOperationPauseReason reason) =>
        WaitOnResourceCore(resource, timeout, reason, CancellationToken.None);

    /// <summary>
    /// Blocks the calling operation on <paramref name="resource"/> until it is signaled, the modeled
    /// (virtual) timeout elapses, or <paramref name="cancellationToken"/> is canceled - whichever
    /// happens first - resolving the three-way race deterministically. Cancellation is observed
    /// <b>synchronously</b>: the token's registration runs on the thread that cancels the token, under
    /// the scheduler lock, so there is no thread-pool hop and no <c>CancelAsync</c>. The registration
    /// is always disposed when the wait ends, so no callback leaks past it.
    /// <para>
    /// <b>Race resolution.</b> The waiter carries a single-assignment resolution; the first of
    /// signal/timeout/cancel to claim it under the lock wins and the terminal reason is exactly that
    /// outcome. An already-canceled token resolves <see cref="ControlledWaitOutcome.Canceled"/>
    /// immediately without parking (cancellation takes precedence over a zero timeout). Timeout still
    /// fires only during virtual-time advance, so a signal or cancellation delivered while the
    /// operation set is still making progress always precedes a same-instant timeout.
    /// </para>
    /// </summary>
    /// <param name="resource">The resource to wait on. Must belong to this scheduler.</param>
    /// <param name="timeout">
    /// The modeled wait budget: <see cref="TimeSpan.Zero"/> to poll, <see cref="Timeout.InfiniteTimeSpan"/>
    /// to wait until signaled/canceled, or any strictly positive span for a virtual-time deadline.
    /// </param>
    /// <param name="reason">The stable pause reason recorded for diagnostics and the wait-for graph.</param>
    /// <param name="cancellationToken">A token whose synchronous cancellation ends the wait.</param>
    /// <returns>The deterministic terminal outcome of the wait.</returns>
    public ControlledWaitOutcome WaitOnResource(
        ControlledResource resource,
        TimeSpan timeout,
        ControlledOperationPauseReason reason,
        CancellationToken cancellationToken) =>
        WaitOnResourceCore(resource, timeout, reason, cancellationToken);

    /// <summary>
    /// The shared implementation behind every <c>WaitOnResource</c> overload. Validates ownership and
    /// the timeout, resolves the non-parking fast paths (already-canceled token, then zero timeout),
    /// then atomically enqueues the waiter (with an optional virtual-time timeout and synchronous
    /// cancellation registration) and parks the operation under the scheduler lock before yielding the
    /// baton, so no signal, timeout, or cancellation can be lost.
    /// </summary>
    private ControlledWaitOutcome WaitOnResourceCore(
        ControlledResource resource,
        TimeSpan timeout,
        ControlledOperationPauseReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(reason);
        ValidateResourceOwnership(resource);
        var operation = RequireCurrentOperation();

        var infinite = timeout == Timeout.InfiniteTimeSpan;
        if (!infinite && timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "A resource wait timeout must be non-negative, TimeSpan.Zero, or Timeout.InfiniteTimeSpan.");
        }

        // Already-canceled token: resolve without parking. Cancellation takes precedence over a zero
        // timeout so the terminal reason is exactly Canceled.
        if (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
            }

            return ControlledWaitOutcome.Canceled;
        }

        // Zero timeout: never park. A signal cannot arrive synchronously to the running operation, so
        // the deterministic outcome is an immediate timeout.
        if (!infinite && timeout == TimeSpan.Zero)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
            }

            return ControlledWaitOutcome.TimedOut;
        }

        ControlledResourceWaiter waiter;
        CancellationTokenRegistration detachedRegistration = default;
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                waiter = new ControlledResourceWaiter(operation, resource, ++_nextWaiterSequence, reason);
                resource.EnqueueWaiter(waiter);
                operation.Waiter = waiter;
                if (!infinite)
                {
                    waiter.Timeout = _clock.Schedule(timeout, waiter);
                }

                operation.ApplyTransition(ControlledOperationState.Paused, pauseReason: reason);
            }

            Notify(operation, ControlledOperationState.Paused);

            // The publication gate keeps external signal/cancel transitions behind the Paused event.
            // Register can still invoke synchronously on this thread if cancellation arrived after the
            // fast-path check; that callback re-enters the publication gate and publishes Runnable only
            // after Paused. Assign the returned registration under the scheduler gate, or dispose it
            // afterward if a listener reentrantly detached the waiter during Paused publication.
            if (cancellationToken.CanBeCanceled)
            {
                var registration = cancellationToken.Register(
                    static state =>
                    {
                        var (scheduler, canceledWaiter) = ((ControlledOperationScheduler, ControlledResourceWaiter))state!;
                        scheduler.OnWaiterCanceled(canceledWaiter);
                    },
                    (this, waiter));
                lock (_gate)
                {
                    if (ReferenceEquals(operation.Waiter, waiter))
                    {
                        waiter.CancellationRegistration = registration;
                    }
                    else
                    {
                        detachedRegistration = registration;
                    }
                }
            }
        }

        DisposeRegistration(detachedRegistration);
        HandBackAndPark(operation);

        ControlledWaitOutcome outcome = FinishWait(operation, waiter);
        if (outcome == ControlledWaitOutcome.Signaled)
        {
            lock (_gate)
            {
                if (waiter.RaceReleaseClock is { } releaseClock)
                {
                    _raceTracker.ConsumeRelease(operation, releaseClock);
                }
            }
        }

        return outcome;
    }

    /// <summary>
    /// The synchronous cancellation callback: resolves <paramref name="waiter"/> as
    /// <see cref="ControlledWaitOutcome.Canceled"/> (if it has not already been signaled or timed out),
    /// cancels its virtual-time timeout, removes it from the resource queue, and makes its operation
    /// runnable so it observes the cancellation. Runs on the thread that cancels the token, under the
    /// scheduler lock; never touches the thread pool. The cancellation registration itself is disposed
    /// later in <see cref="FinishWait"/> (never from inside its own callback).
    /// </summary>
    private void OnWaiterCanceled(ControlledResourceWaiter waiter)
    {
        ControlledOperation? runnable = null;
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (!waiter.TryResolve(ControlledWaitOutcome.Canceled))
                {
                    return;
                }

                RecordWaitResolution(waiter, ControlledWaitOutcome.Canceled);
                if (waiter.Timeout is { } timeout)
                {
                    timeout.IsCanceled = true;
                }

                waiter.Resource.RemoveWaiter(waiter);
                if (waiter.Operation.State == ControlledOperationState.Paused)
                {
                    waiter.Operation.ApplyTransition(ControlledOperationState.Runnable);
                    runnable = waiter.Operation;
                }
            }

            if (runnable is not null)
            {
                Notify(runnable, ControlledOperationState.Runnable);
            }
        }
    }

    /// <summary>
    /// Wakes the earliest-enqueued unresolved waiter on <paramref name="resource"/> (deterministic
    /// FIFO order), resolving it as <see cref="ControlledWaitOutcome.Signaled"/> and transitioning its
    /// operation back to <see cref="ControlledOperationState.Runnable"/>. This is the building block a
    /// monitor pulse / semaphore release / auto-reset-event set is composed from. Safe to call from a
    /// running operation body or the controlling thread.
    /// </summary>
    /// <param name="resource">The resource to signal. Must belong to this scheduler.</param>
    /// <returns>The operation that was woken, or <see langword="null"/> if no waiter was pending.</returns>
    public ControlledOperation? SignalOne(ControlledResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ValidateResourceOwnership(resource);

        ControlledOperation? woken = null;
        CancellationTokenRegistration registration = default;
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                var next = SelectResourceWaiter(resource);
                if (next is not null && next.TryResolve(ControlledWaitOutcome.Signaled))
                {
                    if (t_currentOperation is { } signaler && ReferenceEquals(signaler.Scheduler, this))
                    {
                        next.RaceReleaseClock = _raceTracker.CaptureRelease(signaler);
                    }

                    RecordWaitResolution(next, ControlledWaitOutcome.Signaled);
                    registration = DetachWaiterRegistrationsUnderLock(next);
                    resource.RemoveWaiter(next);
                    next.Operation.ApplyTransition(ControlledOperationState.Runnable);
                    woken = next.Operation;
                }
            }

            if (woken is not null)
            {
                Notify(woken, ControlledOperationState.Runnable);
            }
        }

        DisposeRegistration(registration);
        return woken;
    }

    /// <summary>
    /// Wakes every unresolved waiter on <paramref name="resource"/>, in deterministic FIFO order,
    /// resolving each as <see cref="ControlledWaitOutcome.Signaled"/> and making its operation
    /// runnable. This is the building block a monitor <c>PulseAll</c> / manual-reset-event set is
    /// composed from.
    /// </summary>
    /// <param name="resource">The resource to signal. Must belong to this scheduler.</param>
    /// <returns>The woken operations, in the deterministic order they were signaled.</returns>
    public IReadOnlyList<ControlledOperation> SignalAll(ControlledResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ValidateResourceOwnership(resource);

        List<ControlledOperation> woken = [];
        ControlledResourceWaiter[] pending;
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                pending = resource.SnapshotPendingWaiters();
            }

            foreach (var waiter in pending)
            {
                ControlledOperation? operation = null;
                CancellationTokenRegistration registration = default;
                lock (_gate)
                {
                    if (waiter.TryResolve(ControlledWaitOutcome.Signaled))
                    {
                        if (t_currentOperation is { } signaler && ReferenceEquals(signaler.Scheduler, this))
                        {
                            waiter.RaceReleaseClock = _raceTracker.CaptureRelease(signaler);
                        }

                        RecordWaitResolution(waiter, ControlledWaitOutcome.Signaled);
                        registration = DetachWaiterRegistrationsUnderLock(waiter);
                        resource.RemoveWaiter(waiter);
                        waiter.Operation.ApplyTransition(ControlledOperationState.Runnable);
                        operation = waiter.Operation;
                    }
                }

                if (operation is not null)
                {
                    woken.Add(operation);
                    Notify(operation, ControlledOperationState.Runnable);
                    DisposeRegistration(registration);
                }
            }
        }

        return woken;
    }

    /// <summary>
    /// Completes a wait once the operation has been granted the baton again: reads the deterministic
    /// resolution assigned while it was parked, disposes any timeout/cancellation registrations (so a
    /// cancellation callback that resolved the waiter without disposing its own registration cannot
    /// leak), detaches the waiter from the resource and the operation, and returns the outcome. The
    /// resolution is always present here because an operation is only ever made runnable again after
    /// its waiter has been resolved.
    /// </summary>
    private ControlledWaitOutcome FinishWait(ControlledOperation operation, ControlledResourceWaiter waiter)
    {
        ControlledWaitOutcome resolution;
        CancellationTokenRegistration registration;
        lock (_gate)
        {
            resolution = waiter.Resolution ?? throw new ControlledOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Controlled operation {operation.Id} resumed from a resource wait with no resolution recorded."));
            registration = DetachWaiterRegistrationsUnderLock(waiter);
            waiter.Resource.RemoveWaiter(waiter);
            if (ReferenceEquals(operation.Waiter, waiter))
            {
                operation.Waiter = null;
            }
        }

        DisposeRegistration(registration);
        return resolution;
    }

    /// <summary>
    /// Detaches a paused operation's waiter (if any) during cancellation/teardown: resolves it as
    /// <see cref="ControlledWaitOutcome.Canceled"/> so a later signal never tries to wake a terminal
    /// operation, cancels its timeout, captures its cancellation registration for disposal after the
    /// scheduler lock is released, removes it from the resource queue, and clears the operation's waiter
    /// slot. Caller must hold the lock.
    /// </summary>
    private static CancellationTokenRegistration DetachWaiterUnderLock(ControlledOperation operation)
    {
        var waiter = operation.Waiter;
        if (waiter is null)
        {
            return default;
        }

        waiter.TryResolve(ControlledWaitOutcome.Canceled);
        var registration = DetachWaiterRegistrationsUnderLock(waiter);
        waiter.Resource.RemoveWaiter(waiter);
        operation.Waiter = null;
        return registration;
    }

    /// <summary>
    /// Cancels and detaches the timeout and cancellation registrations attached to a waiter. The caller
    /// disposes the returned cancellation registration only after releasing the scheduler lock, since
    /// disposal can wait for an in-flight callback that is itself waiting to acquire that lock.
    /// </summary>
    private static CancellationTokenRegistration DetachWaiterRegistrationsUnderLock(ControlledResourceWaiter waiter)
    {
        if (waiter.Timeout is { } timeout)
        {
            timeout.IsCanceled = true;
            waiter.Timeout = null;
        }

        var registration = waiter.CancellationRegistration;
        waiter.CancellationRegistration = default;
        return registration;
    }

    private void DisposeRegistrations(List<CancellationTokenRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            DisposeRegistration(registration);
        }
    }

    private void DisposeRegistration(CancellationTokenRegistration registration)
    {
        if (registration.Equals(default(CancellationTokenRegistration)))
        {
            return;
        }

        if (Monitor.IsEntered(_transitionPublicationGate))
        {
            (_deferredRegistrationDisposals ??= []).Add(registration);
            return;
        }

        registration.Dispose();
    }

    private void ValidateResourceOwnership(ControlledResource resource)
    {
        if (!ReferenceEquals(resource.Scheduler, this))
        {
            throw new ControlledOperationException("The resource belongs to a different scheduler.");
        }
    }

    /// <summary>
    /// Captures a deterministic snapshot of every registered operation's identity and status, in
    /// stable <see cref="ControlledOperation.Id"/> order. Intended for diagnostics and for folding
    /// controlled-operation state into higher-level pending-work summaries.
    /// </summary>
    /// <returns>An ordered, immutable list of operation statuses.</returns>
    public IReadOnlyList<ControlledOperationStatus> CaptureStatus()
    {
        lock (_gate)
        {
            var result = new List<ControlledOperationStatus>(_operations.Count);
            foreach (var operation in _operations.Values)
            {
                result.Add(new ControlledOperationStatus(
                    operation.Id,
                    operation.ParentId,
                    operation.State,
                    operation.PauseReason,
                    operation.LogicalExecutionId.Value,
                    operation.Node?.Address,
                    operation.WorkDescription));
            }

            return result;
        }
    }

    /// <summary>
    /// Gets the number of operations that are not in a terminal state (Created/Runnable/Running/Paused).
    /// </summary>
    public int PendingOperationCount
    {
        get
        {
            lock (_gate)
            {
                var count = 0;
                foreach (var operation in _operations.Values)
                {
                    if (!operation.IsTerminal)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <summary>
    /// Gets the current modeled (virtual) time, as a monotonic offset from simulation start. Advanced
    /// only by <see cref="TryAdvanceVirtualTime"/>; never tied to wall-clock time.
    /// </summary>
    public TimeSpan VirtualTime
    {
        get
        {
            lock (_gate)
            {
                return _clock.Now;
            }
        }
    }

    /// <summary>
    /// Gets a deterministic snapshot of the timed waits currently pending in virtual time, ordered by
    /// due time then registration sequence, for diagnostics and deadlock reports.
    /// </summary>
    /// <returns>An ordered, immutable list of pending timeout descriptors.</returns>
    public IReadOnlyList<ControlledPendingTimeoutInfo> CapturePendingTimeouts()
    {
        lock (_gate)
        {
            return _clock.SnapshotPending();
        }
    }

    /// <summary>
    /// Analyzes the current wait-for graph and classifies whether outstanding work can still make
    /// progress, distinguishing a genuine resource-ownership deadlock from a paused-until-time state,
    /// an externally-completable wait, an idle-with-pending-work state, or quiescence. Every detected
    /// cycle is reported deterministically (operations and resources by stable id/name, owners, waiter
    /// order, and originating pause reason), so a stuck simulation explains itself instead of hanging.
    /// <para>
    /// Only <b>indefinite</b> waits contribute deadlock edges: a wait with a pending virtual-time
    /// timeout can always be broken by advancing modeled time, so it never counts as a true cycle.
    /// </para>
    /// </summary>
    /// <returns>A deterministic deadlock/liveness report.</returns>
    public ControlledDeadlockReport DetectDeadlock()
    {
        lock (_gate)
        {
            var edges = new Dictionary<ControlledOperationId, DeadlockEdge>();
            var runnable = 0;
            var blocked = 0;
            var nonTerminal = 0;
            foreach (var operation in _operations.Values)
            {
                if (!operation.IsTerminal)
                {
                    nonTerminal++;
                }

                if (operation.State == ControlledOperationState.Runnable)
                {
                    runnable++;
                }

                if (operation.State != ControlledOperationState.Paused || operation.Waiter is not { } waiter)
                {
                    continue;
                }

                blocked++;

                // Only an indefinite wait (no pending timeout) can form a true deadlock: a timed wait
                // is always broken by advancing virtual time.
                if (waiter.Timeout is not null)
                {
                    continue;
                }

                if (waiter.Resource.Owner is { } owner && !owner.IsTerminal)
                {
                    edges[operation.Id] = new DeadlockEdge(owner.Id, waiter);
                }
            }

            var cycles = FindWaitForCyclesUnderLock(edges);
            var pendingTimeouts = _clock.PendingCount;
            var liveness = ClassifyLiveness(nonTerminal, runnable, cycles.Count, pendingTimeouts);
            return new ControlledDeadlockReport(liveness, cycles, runnable, blocked, pendingTimeouts);
        }
    }

    /// <summary>
    /// Produces a deterministic, multi-line human-readable diagnostic that folds the operation-status
    /// snapshot (<see cref="CaptureStatus"/>) together with the <see cref="DetectDeadlock"/> liveness
    /// classification and any wait-for cycles. This is the summary intended to be surfaced by
    /// higher-level execution diagnostics when a simulation appears stuck.
    /// </summary>
    /// <returns>A stable, replayable multi-line description of scheduler liveness.</returns>
    public string DescribeLiveness()
    {
        ControlledDeadlockReport report;
        List<(ControlledOperationId Id, ControlledOperationState State, string Work, ControlledOperationPauseReason? Reason)> operations;
        TimeSpan now;
        lock (_gate)
        {
            report = DetectDeadlock();
            now = _clock.Now;
            operations = new List<(ControlledOperationId, ControlledOperationState, string, ControlledOperationPauseReason?)>(_operations.Count);
            foreach (var operation in _operations.Values)
            {
                operations.Add((operation.Id, operation.State, operation.WorkDescription, operation.PauseReason));
            }
        }

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"Liveness: {report.Liveness} (virtual-time {now}, runnable {report.RunnableCount}, blocked {report.BlockedCount}, pending-timeouts {report.PendingTimeoutCount})");
        foreach (var (id, state, work, reason) in operations)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}  {id} [{state}] {work}");
            if (reason is not null && state == ControlledOperationState.Paused)
            {
                builder.Append(CultureInfo.InvariantCulture, $" waiting: {reason}");
            }
        }

        for (var i = 0; i < report.Cycles.Count; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}  Deadlock cycle {i + 1}:");
            foreach (var entry in report.Cycles[i].Entries)
            {
                builder.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}    {entry.OperationId} '{entry.WorkDescription}' waits on {entry.ResourceId} '{entry.ResourceName}' (seq {entry.EnqueueSequence}, {entry.Reason}) owned by {entry.OwnerId}");
            }
        }

        return builder.ToString();
    }

    private List<ControlledWaitCycle> FindWaitForCyclesUnderLock(Dictionary<ControlledOperationId, DeadlockEdge> edges)
    {
        var cycles = new List<ControlledWaitCycle>();
        var globallyVisited = new HashSet<ControlledOperationId>();

        // The wait-for graph is functional here (each paused operation waits on exactly one resource,
        // hence has at most one successor), so following the single successor chain from each start
        // node detects every cycle deterministically. _operations is id-sorted, so starts are ordered.
        foreach (var operation in _operations.Values)
        {
            var start = operation.Id;
            if (!edges.ContainsKey(start) || globallyVisited.Contains(start))
            {
                continue;
            }

            var path = new List<ControlledOperationId>();
            var indexInPath = new Dictionary<ControlledOperationId, int>();
            var current = start;
            while (true)
            {
                if (globallyVisited.Contains(current))
                {
                    break;
                }

                if (indexInPath.TryGetValue(current, out var index))
                {
                    cycles.Add(BuildCycle(path.GetRange(index, path.Count - index), edges));
                    break;
                }

                if (!edges.TryGetValue(current, out var edge))
                {
                    break;
                }

                indexInPath[current] = path.Count;
                path.Add(current);
                current = edge.OwnerId;
            }

            foreach (var id in path)
            {
                globallyVisited.Add(id);
            }
        }

        // Order cycles deterministically by their (already smallest-first) leading operation id.
        cycles.Sort(static (a, b) => a.Entries[0].OperationId.CompareTo(b.Entries[0].OperationId));
        return cycles;
    }

    private ControlledWaitCycle BuildCycle(List<ControlledOperationId> cycleIds, Dictionary<ControlledOperationId, DeadlockEdge> edges)
    {
        // Rotate so the cycle starts at its smallest operation id for a stable representation.
        var minIndex = 0;
        for (var i = 1; i < cycleIds.Count; i++)
        {
            if (cycleIds[i] < cycleIds[minIndex])
            {
                minIndex = i;
            }
        }

        var entries = new List<ControlledWaitCycleEntry>(cycleIds.Count);
        for (var i = 0; i < cycleIds.Count; i++)
        {
            var id = cycleIds[(minIndex + i) % cycleIds.Count];
            var edge = edges[id];
            var waiter = edge.Waiter;
            entries.Add(new ControlledWaitCycleEntry(
                id,
                _operations[id].WorkDescription,
                waiter.Resource.Id,
                waiter.Resource.Name,
                edge.OwnerId,
                waiter.EnqueueSequence,
                waiter.Reason.ToString()));
        }

        return new ControlledWaitCycle(entries);
    }

    private static ControlledLivenessState ClassifyLiveness(int nonTerminal, int runnable, int cycleCount, int pendingTimeouts)
    {
        if (nonTerminal == 0)
        {
            return ControlledLivenessState.Quiescent;
        }

        if (runnable > 0)
        {
            return ControlledLivenessState.Progressing;
        }

        // A live deadline means the scheduler can still advance modeled time. Preserve any cycle in
        // the report for diagnostics, but do not classify it as terminal until all finite escape paths
        // have been exhausted.
        if (pendingTimeouts > 0)
        {
            return ControlledLivenessState.PausedUntilTime;
        }

        if (cycleCount > 0)
        {
            return ControlledLivenessState.Deadlocked;
        }

        return ControlledLivenessState.ExternallyCompletable;
    }

    /// <summary>A single wait-for edge used during deadlock analysis: the owner to follow and the waiter that drew it.</summary>
    private readonly record struct DeadlockEdge(ControlledOperationId OwnerId, ControlledResourceWaiter Waiter);

    /// <summary>
    /// Tears the scheduler down: cancels every non-terminal operation, cooperatively unwinds and
    /// joins their parked physical threads (bounded by <see cref="ThreadJoinTimeout"/>), and releases
    /// all wait handles. No operation is aborted unsafely; no thread is left stranded. Idempotent.
    /// </summary>
    public void Dispose()
    {
        List<ControlledOperation> victims;
        List<CancellationTokenRegistration> registrations;
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                victims = new List<ControlledOperation>();
                registrations = new List<CancellationTokenRegistration>();
                foreach (var operation in _operations.Values)
                {
                    if (operation.IsTerminal)
                    {
                        continue;
                    }

                    operation.RequestTermination();

                    // Detach any resource wait first so a resolved-Canceled waiter is removed from its
                    // resource queue and never wakes a terminal operation.
                    registrations.Add(DetachWaiterUnderLock(operation));

                    // Force every non-terminal operation to Canceled. Running is not expected here
                    // because Dispose must not race an in-flight step; the Created/Runnable/Paused ->
                    // Canceled and Running -> Canceled edges are all legal, so a single transition call
                    // handles every non-terminal source state.
                    operation.ApplyTransition(ControlledOperationState.Canceled);
                    victims.Add(operation);
                }

                // Drop any pending virtual-time timeouts; their waiters were just detached and canceled.
                _clock.Clear();
            }

            foreach (var victim in victims)
            {
                Notify(victim, ControlledOperationState.Canceled);
            }
        }

        DisposeRegistrations(registrations);
        foreach (var victim in victims)
        {
            if (victim.Thread is not null)
            {
                UnwindParkedThread(victim);
            }
        }

        foreach (var operation in SnapshotOperations())
        {
            operation.DisposeSignals();
        }

        _handback.Dispose();

    }

    private ControlledOperation? SelectRunnable()
    {
        // Collect the runnable operations in ascending id order (_operations is a SortedDictionary),
        // then delegate the choice to the pluggable strategy. The default RoundRobinSchedulingStrategy
        // reproduces the Phase 3A behavior exactly.
        List<ControlledOperation>? runnable = null;
        foreach (var operation in _operations.Values)
        {
            if (operation.State != ControlledOperationState.Runnable)
            {
                continue;
            }

            (runnable ??= new List<ControlledOperation>()).Add(operation);
        }

        if (runnable is null)
        {
            return null;
        }

        ControlledOperation chosen;
        if (runnable.Count == 1)
        {
            // No real choice - do not consult the strategy's hidden state or record a decision, so
            // single-candidate steps never perturb a seeded/replayed schedule.
            chosen = runnable[0];
        }
        else
        {
            chosen = _strategy.ChooseNext(new ControlledSchedulingContext(runnable, _lastSelected));
            RecordSelection(chosen, runnable);
        }

        _lastSelected = chosen.Id;
        return chosen;
    }

    /// <summary>
    /// Records the scheduling choice among two-or-more candidates as a
    /// <see cref="SimulationDecisionKind.SchedulingOrder"/> decision and/or validates it against a
    /// recorded run. Called under <see cref="_gate"/> on the controlling thread, so the runtime
    /// identity is taken directly from <see cref="_runtime"/> rather than the ambient context (the
    /// controlling thread has no operation scope installed).
    /// </summary>
    private void RecordSelection(ControlledOperation chosen, List<ControlledOperation> runnable)
    {
        if (_decisionLog is null && _replayValidator is null)
        {
            return;
        }

        var request = new SimulationDecisionRequest(
            SimulationSeedDomain.Scheduler,
            SimulationDecisionKind.SchedulingOrder,
            GetDecisionSourceId(),
            FormatCandidateIds(runnable),
            ReplaySchedulingStrategy.FormatId(chosen.Id),
            _runtime.Id,
            NodeId: null,
            SimulationLogicalExecutionId.None);

        var record = _decisionLog is not null
            ? _decisionLog.Record(request)
            : new SimulationDecisionRecord(
                new SimulationDecisionId(_nextReplayDecisionId++),
                request.Domain,
                request.Kind,
                request.SourceId,
                request.InputMetadata,
                request.SelectedResult,
                request.RuntimeId,
                request.NodeId,
                request.LogicalExecutionId);

        _replayValidator?.Validate(record);
    }

    private ControlledResourceWaiter? SelectResourceWaiter(ControlledResource resource)
    {
        ControlledResourceWaiter[] pending = resource.SnapshotPendingWaiters();
        if (pending.Length <= 1)
        {
            return pending.Length == 0 ? null : pending[0];
        }

        var waiterInfos = new ControlledResourceWaiterInfo[pending.Length];
        for (var index = 0; index < pending.Length; index++)
        {
            waiterInfos[index] = pending[index].ToInfo();
        }

        var selectedIndex = _strategy switch
        {
            SeededRandomSchedulingStrategy random => random.ChooseIndex(pending.Length),
            ReplaySchedulingStrategy replay => replay.ChooseResourceWaiter(waiterInfos),
            _ => 0,
        };

        ControlledResourceWaiter selected = pending[selectedIndex];
        RecordResourceWinner(selected, pending);
        return selected;
    }

    private void RecordResourceWinner(
        ControlledResourceWaiter selected,
        ControlledResourceWaiter[] pending)
    {
        if (_decisionLog is null && _replayValidator is null)
        {
            return;
        }

        var candidateIds = new string[pending.Length];
        for (var index = 0; index < pending.Length; index++)
        {
            candidateIds[index] = ReplaySchedulingStrategy.FormatId(pending[index].Operation.Id);
        }

        var request = new SimulationDecisionRequest(
            SimulationSeedDomain.Scheduler,
            SimulationDecisionKind.ResourceWinner,
            GetDecisionSourceId(),
            string.Create(
                CultureInfo.InvariantCulture,
                $"resource={selected.Resource.Id.Value};waiters={string.Join(",", candidateIds)}"),
            ReplaySchedulingStrategy.FormatId(selected.Operation.Id),
            _runtime.Id,
            selected.Operation.Node?.Address,
            selected.Operation.LogicalExecutionId);

        var record = _decisionLog is not null
            ? _decisionLog.Record(request)
            : new SimulationDecisionRecord(
                new SimulationDecisionId(_nextReplayDecisionId++),
                request.Domain,
                request.Kind,
                request.SourceId,
                request.InputMetadata,
                request.SelectedResult,
                request.RuntimeId,
                request.NodeId,
                request.LogicalExecutionId);

        _replayValidator?.Validate(record);
    }

    private string GetDecisionSourceId() =>
        _strategy is ReplaySchedulingStrategy replay
            ? replay.LastDecisionSourceId ?? _strategy.Name
            : _strategy.Name;

    private void RecordWaitResolution(ControlledResourceWaiter waiter, ControlledWaitOutcome outcome)
    {
        if (_decisionLog is null && _replayValidator is null)
        {
            return;
        }

        var request = new SimulationDecisionRequest(
            SimulationSeedDomain.Scheduler,
            SimulationDecisionKind.Choice,
            "resource-wait-resolution",
            string.Create(
                CultureInfo.InvariantCulture,
                $"operation={waiter.Operation.Id.Value};resource={waiter.Resource.Id.Value};outcomes=Signaled,TimedOut,Canceled"),
            outcome.ToString(),
            _runtime.Id,
            waiter.Operation.Node?.Address,
            waiter.Operation.LogicalExecutionId);

        var record = _decisionLog is not null
            ? _decisionLog.Record(request)
            : new SimulationDecisionRecord(
                new SimulationDecisionId(_nextReplayDecisionId++),
                request.Domain,
                request.Kind,
                request.SourceId,
                request.InputMetadata,
                request.SelectedResult,
                request.RuntimeId,
                request.NodeId,
                request.LogicalExecutionId);

        _replayValidator?.Validate(record);
    }

    private static string FormatCandidateIds(List<ControlledOperation> runnable)
    {
        var ids = new string[runnable.Count];
        for (var i = 0; i < runnable.Count; i++)
        {
            ids[i] = ReplaySchedulingStrategy.FormatId(runnable[i].Id);
        }

        return string.Join(",", ids);
    }

    private void EnsureThreadStarted(ControlledOperation operation)
    {
        if (operation.Thread is not null)
        {
            return;
        }

        var thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = string.Create(CultureInfo.InvariantCulture, $"ControlledOperation-{operation.Id.Value}"),
        };
        operation.Thread = thread;
        thread.Start(operation);
    }

    private void WorkerLoop(object? state)
    {
        var operation = (ControlledOperation)state!;
        try
        {
            // Park until first granted the baton. If canceled before ever running, this throws the
            // internal unwind signal and the thread exits without touching scheduler state.
            operation.WaitForPermission();
        }
        catch (ControlledOperationAbortSignal)
        {
            return;
        }

        RunBody(operation);
    }

    private void RunBody(ControlledOperation operation)
    {
        try
        {
            var context = operation.CapturedContext;
            if (context is not null)
            {
                ExecutionContext.Run(context, static s => ((BodyClosure)s!).Run(), new BodyClosure(this, operation));
            }
            else
            {
                RunBodyScoped(operation);
            }

            HandOffTerminal(operation, ControlledOperationState.Completed, terminalException: null);
        }
        catch (ControlledOperationAbortSignal)
        {
            // Teardown/cancellation initiated the unwind; the scheduler already owns the Canceled
            // transition and joins this thread directly. Do not touch the handback signal.
        }
        catch (OperationCanceledException oce)
        {
            HandOffTerminal(operation, ControlledOperationState.Canceled, oce);
        }
        catch (Exception ex)
        {
            HandOffTerminal(operation, ControlledOperationState.Faulted, ex);
        }
    }

    private void RunBodyScoped(ControlledOperation operation)
    {
        var previous = t_currentOperation;
        t_currentOperation = operation;
        try
        {
            using (SimulationExecutionContext.EnterRuntime(_activationToken, _runtime))
            using (operation.Node is { } node ? SimulationExecutionContext.EnterNode(node) : null)
            using (SimulationExecutionContext.EnterLogicalExecution(operation.LogicalExecutionId))
            {
                Threading.ControlledSynchronizationFlow.RunAsStrand(
                    -operation.Id.Value,
                    operation.InvokeBody);
            }
        }
        finally
        {
            t_currentOperation = previous;
        }
    }

    /// <summary>
    /// Hands control back to the waiting controlling thread and parks the operation's physical thread
    /// until it is granted the baton again (or torn down). Called from a running operation's own
    /// thread after it has transitioned itself out of <see cref="ControlledOperationState.Running"/>.
    /// </summary>
    private void HandBackAndPark(ControlledOperation operation)
    {
        _handback.Release();
        operation.WaitForPermission();
        // Resumed: the scheduler transitioned us back to Running before granting the baton.
    }

    private void HandOffTerminal(ControlledOperation operation, ControlledOperationState terminal, Exception? terminalException)
    {
        using (EnterTransitionPublicationScope())
        {
            lock (_gate)
            {
                // Only apply if a controlling thread is still waiting for this operation (it holds the
                // baton). If teardown already forced a terminal state, skip.
                if (operation.IsTerminal)
                {
                    _handback.Release();
                    return;
                }

                operation.ApplyTransition(terminal, terminalException: terminalException);
                _pendingTerminalNotification = operation;
            }
        }

        _handback.Release();
    }

    private void FinalizeTerminal(ControlledOperation operation)
    {
        // The worker thread released the handback and is returning from its loop; join it so no
        // physical thread outlives the operation, then release its signal and publish the deferred
        // terminal transition from the controlling thread.
        operation.Thread?.Join(ThreadJoinTimeout);
        operation.DisposeSignals();
        using (EnterTransitionPublicationScope(waitForPendingTerminal: false))
        {
            if (!ReferenceEquals(_pendingTerminalNotification, operation))
            {
                throw new ControlledOperationException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Controlled operation {operation.Id} reached {operation.State} without its terminal listener notification being pending."));
            }

            _pendingTerminalNotification = null;
            try
            {
                Notify(operation, operation.State);
            }
            finally
            {
                Monitor.PulseAll(_transitionPublicationGate);
            }
        }
    }

    private TransitionPublicationScope EnterTransitionPublicationScope(bool waitForPendingTerminal = true)
    {
        Monitor.Enter(_transitionPublicationGate);
        try
        {
            if (waitForPendingTerminal)
            {
                WaitForPendingTerminalNotificationUnderLock();
            }

            _transitionPublicationDepth++;
            return new TransitionPublicationScope(this);
        }
        catch
        {
            Monitor.Exit(_transitionPublicationGate);
            throw;
        }
    }

    private void ExitTransitionPublicationScope()
    {
        List<CancellationTokenRegistration>? deferred = null;
        _transitionPublicationDepth--;
        if (_transitionPublicationDepth == 0 && _deferredRegistrationDisposals is { Count: > 0 })
        {
            deferred = _deferredRegistrationDisposals;
            _deferredRegistrationDisposals = null;
        }

        Monitor.Exit(_transitionPublicationGate);
        if (deferred is not null)
        {
            foreach (var registration in deferred)
            {
                registration.Dispose();
            }
        }
    }

    private void WaitForPendingTerminalNotificationUnderLock()
    {
        while (_pendingTerminalNotification is not null)
        {
            Monitor.Wait(_transitionPublicationGate);
        }
    }

    private sealed class TransitionPublicationScope(ControlledOperationScheduler scheduler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                scheduler.ExitTransitionPublicationScope();
            }
        }
    }

    private static void UnwindParkedThread(ControlledOperation operation)
    {
        var thread = operation.Thread;
        if (thread is null)
        {
            return;
        }

        // Unpark the thread; because termination was requested, its next WaitForPermission throws the
        // internal unwind signal and the body stack unwinds cooperatively. No unsafe abort.
        operation.GrantPermission();
        if (!thread.Join(ThreadJoinTimeout))
        {
            throw new ControlledOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Controlled operation {operation.Id} did not unwind within {ThreadJoinTimeout.TotalSeconds:0}s of teardown. Its body is likely stuck in a loop that never parks - this is a system-under-test bug, not a kernel one."));
        }
    }

    private ControlledOperation RequireCurrentOperation()
    {
        var operation = t_currentOperation;
        if (operation is null || !ReferenceEquals(operation.Scheduler, this))
        {
            throw new ControlledOperationException(
                "Pause/Yield may only be called from within a running controlled operation of this scheduler.");
        }

        return operation;
    }

    private void ValidateOwnership(ControlledOperation operation)
    {
        if (!ReferenceEquals(operation.Scheduler, this))
        {
            throw new ControlledOperationException("The operation belongs to a different scheduler.");
        }
    }

    private ControlledOperation[] SnapshotOperations()
    {
        lock (_gate)
        {
            var array = new ControlledOperation[_operations.Count];
            _operations.Values.CopyTo(array, 0);
            return array;
        }
    }

    private void Notify(ControlledOperation operation, ControlledOperationState state) =>
        _listener?.OnStateChanged(operation, state);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class BodyClosure(ControlledOperationScheduler scheduler, ControlledOperation operation)
    {
        public void Run() => scheduler.RunBodyScoped(operation);
    }

    private sealed class ControlScope(ControlledOperationScheduler scheduler, int previousControlThreadId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Volatile.Write(ref scheduler._controlThreadId, previousControlThreadId);
            }
        }
    }
}

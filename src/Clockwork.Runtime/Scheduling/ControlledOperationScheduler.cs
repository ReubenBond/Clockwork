using System.Diagnostics;
using System.Globalization;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Scheduling;

/// <summary>
/// <para>
/// The controlled-operation kernel: the single authority that registers
/// <see cref="ControlledOperation"/>s, chooses exactly one runnable operation at a time, grants and
/// revokes the permission baton, drives every legal state transition, and performs terminal
/// cleanup. This is the foundational scheduling layer future controlled <c>Monitor</c>, semaphore,
/// wait-handle, and synchronous <see cref="Task"/> waits (Phase 3B) will build on; it deliberately
/// contains no resource model, no timeouts, and no deadlock detection yet.
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
    private readonly SortedDictionary<ControlledOperationId, ControlledOperation> _operations = new();
    private readonly SemaphoreSlim _handback = new(0, 1);
    private readonly SimulationLogicalExecutionIdSource _logicalIds = new();
    private readonly SimulationActivationToken _activationToken;
    private readonly SimulationRuntimeIdentity _runtime;
    private readonly IControlledOperationListener? _listener;

    private long _nextOperationId;
    private ControlledOperation? _current;
    private bool _controlThreadBusy;
    private bool _disposed;

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
    /// Gets the operation the calling thread is currently executing as, or <see langword="null"/> if
    /// the calling thread is not a controlled-operation thread of this scheduler. This is how
    /// pause/yield primitives and nested scheduling discover "who am I".
    /// </summary>
    public ControlledOperation? CurrentOperation =>
        t_currentOperation is { } op && !ReferenceEquals(op.Scheduler, this) ? null : t_currentOperation;

    /// <summary>
    /// Registers a new operation in the <see cref="ControlledOperationState.Created"/> state without
    /// admitting it for scheduling. The operation's parent is the operation the calling thread is
    /// running as (enabling nested parent/child identity), or <see cref="ControlledOperationId.None"/>
    /// for a root registration.
    /// </summary>
    /// <param name="workDescription">A short, stable description of the work, for diagnostics.</param>
    /// <param name="body">The operation body. Runs exactly once, on the operation's own thread.</param>
    /// <param name="node">The node the operation is scoped to, or <see langword="null"/> for cluster-level work.</param>
    /// <returns>The newly created operation.</returns>
    public ControlledOperation Register(string workDescription, Action body, SimulationNodeIdentity? node = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(workDescription);
        ArgumentNullException.ThrowIfNull(body);

        var capturedContext = ExecutionContext.Capture();
        ControlledOperation operation;
        lock (_gate)
        {
            ThrowIfDisposed();
            var parentId = t_currentOperation is { } parent && ReferenceEquals(parent.Scheduler, this)
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
                capturedContext);
            _operations.Add(id, operation);
        }

        Notify(operation, ControlledOperationState.Created);
        return operation;
    }

    /// <summary>
    /// Registers and immediately admits an operation, so it is <see cref="ControlledOperationState.Runnable"/>
    /// on return. Convenience for the common "create a ready-to-run operation" case.
    /// </summary>
    /// <param name="workDescription">A short, stable description of the work, for diagnostics.</param>
    /// <param name="body">The operation body.</param>
    /// <param name="node">The node the operation is scoped to, or <see langword="null"/>.</param>
    /// <returns>The newly created, admitted operation.</returns>
    public ControlledOperation Schedule(string workDescription, Action body, SimulationNodeIdentity? node = null)
    {
        var operation = Register(workDescription, body, node);
        Admit(operation);
        return operation;
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
        lock (_gate)
        {
            ThrowIfDisposed();
            operation.ApplyTransition(ControlledOperationState.Runnable);
        }

        Notify(operation, ControlledOperationState.Runnable);
    }

    /// <summary>
    /// Selects exactly one <see cref="ControlledOperationState.Runnable"/> operation (deterministically,
    /// the one with the lowest <see cref="ControlledOperation.Id"/>), grants it the permission baton,
    /// and blocks the controlling thread until that operation hands control back by pausing, yielding,
    /// completing, or faulting. Terminal operations are cleaned up before returning.
    /// </summary>
    /// <returns><see langword="true"/> if an operation ran; <see langword="false"/> if none was runnable.</returns>
    public bool RunStep()
    {
        ControlledOperation operation;
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
    /// Repeatedly runs steps until no operation is runnable, returning the number of steps executed.
    /// A step count that keeps growing without the set of operations shrinking indicates operations
    /// that only ever yield; callers that need a bound should cap iterations themselves.
    /// </summary>
    /// <returns>The number of steps executed.</returns>
    public int Drain()
    {
        var steps = 0;
        while (RunStep())
        {
            steps++;
        }

        return steps;
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
        lock (_gate)
        {
            ThrowIfDisposed();
            operation.ApplyTransition(ControlledOperationState.Runnable);
        }

        Notify(operation, ControlledOperationState.Runnable);
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
        lock (_gate)
        {
            if (operation.IsTerminal)
            {
                return;
            }

            if (ReferenceEquals(operation, _current) && operation.State == ControlledOperationState.Running)
            {
                throw new ControlledOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"Cannot externally cancel the currently-running operation {operation.Id}; it must observe cooperative cancellation from within its own body."));
            }

            operation.RequestTermination();
            operation.ApplyTransition(ControlledOperationState.Canceled);
            needsUnwind = operation.Thread is not null;
        }

        if (needsUnwind)
        {
            UnwindParkedThread(operation);
        }

        Notify(operation, ControlledOperationState.Canceled);
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
        lock (_gate)
        {
            operation.ApplyTransition(ControlledOperationState.Paused, pauseReason: reason);
        }

        Notify(operation, ControlledOperationState.Paused);
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
        lock (_gate)
        {
            operation.ApplyTransition(ControlledOperationState.Runnable);
        }

        Notify(operation, ControlledOperationState.Runnable);
        HandBackAndPark(operation);
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
    /// Tears the scheduler down: cancels every non-terminal operation, cooperatively unwinds and
    /// joins their parked physical threads (bounded by <see cref="ThreadJoinTimeout"/>), and releases
    /// all wait handles. No operation is aborted unsafely; no thread is left stranded. Idempotent.
    /// </summary>
    public void Dispose()
    {
        List<ControlledOperation> victims;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            victims = new List<ControlledOperation>();
            foreach (var operation in _operations.Values)
            {
                if (operation.IsTerminal)
                {
                    continue;
                }

                operation.RequestTermination();

                // Force every non-terminal operation to Canceled. Running is not expected here
                // because Dispose must not race an in-flight step; the Created/Runnable/Paused ->
                // Canceled and Running -> Canceled edges are all legal, so a single transition call
                // handles every non-terminal source state.
                operation.ApplyTransition(ControlledOperationState.Canceled);
                victims.Add(operation);
            }
        }

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

        foreach (var victim in victims)
        {
            Notify(victim, ControlledOperationState.Canceled);
        }
    }

    private ControlledOperation? SelectRunnable()
    {
        foreach (var operation in _operations.Values)
        {
            if (operation.State == ControlledOperationState.Runnable)
            {
                return operation;
            }
        }

        return null;
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
                operation.InvokeBody();
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
        }

        _handback.Release();
    }

    private void FinalizeTerminal(ControlledOperation operation)
    {
        // The worker thread released the handback and is returning from its loop; join it so no
        // physical thread outlives the operation, then release its signal and notify.
        operation.Thread?.Join(ThreadJoinTimeout);
        operation.DisposeSignals();
        Notify(operation, operation.State);
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
}

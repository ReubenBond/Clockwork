using System.Diagnostics;
using System.Globalization;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Scheduling;

/// <summary>
/// <para>
/// One independently-schedulable strand of controlled work owned by a
/// <see cref="ControlledOperationScheduler"/>. A controlled operation is the unit the kernel grants
/// the single permission baton to: while it is <see cref="ControlledOperationState.Running"/> it is
/// the one operation allowed to execute system-under-test code, regardless of how many physical
/// threads exist.
/// </para>
/// <para>
/// An operation carries a stable identity (<see cref="Id"/>), its creator/parent
/// (<see cref="ParentId"/>), the simulation runtime and node it runs under, a logical execution
/// identity distinct from any physical thread id (<see cref="LogicalExecutionId"/>), stable
/// originating-work metadata (<see cref="WorkDescription"/>), its current
/// <see cref="ControlledOperationState"/>, why it is paused (<see cref="PauseReason"/>), and its
/// terminal outcome (<see cref="TerminalException"/> / cancellation).
/// </para>
/// <para>
/// State is only ever mutated by the owning scheduler, under the scheduler's lock, via the internal
/// <see cref="ApplyTransition"/> method which validates every edge against
/// <see cref="CanTransition"/>. Callers observe state but never set it - that is what makes the
/// "exactly one running" and legal-transition invariants enforceable in one place.
/// </para>
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The owning ControlledOperationScheduler exclusively controls this operation's lifetime and disposes its signal via DisposeSignals during terminal cleanup/teardown; a public Dispose would invite callers to break the scheduler's ownership invariant.")]
public sealed class ControlledOperation
{
    private readonly Action _body;

    // Released (Release count -> 1) by the scheduler to grant the permission baton to this
    // operation's physical thread; waited on by that thread. A binary semaphore, so a spurious
    // double-grant would throw rather than silently over-count.
    private readonly SemaphoreSlim _permission = new(0, 1);

    private ControlledOperationState _state = ControlledOperationState.Created;

    internal ControlledOperation(
        ControlledOperationScheduler scheduler,
        ControlledOperationId id,
        ControlledOperationId parentId,
        SimulationRuntimeIdentity runtime,
        SimulationNodeIdentity? node,
        SimulationLogicalExecutionId logicalExecutionId,
        string workDescription,
        Action body,
        ExecutionContext? capturedContext)
    {
        Scheduler = scheduler;
        Id = id;
        ParentId = parentId;
        Runtime = runtime;
        Node = node;
        LogicalExecutionId = logicalExecutionId;
        WorkDescription = workDescription;
        _body = body;
        CapturedContext = capturedContext;
    }

    /// <summary>Gets the scheduler that owns this operation.</summary>
    internal ControlledOperationScheduler Scheduler { get; }

    /// <summary>Gets this operation's stable, scheduler-assigned identity.</summary>
    public ControlledOperationId Id { get; }

    /// <summary>
    /// Gets the identity of the operation that created this one, or <see cref="ControlledOperationId.None"/>
    /// for a root operation registered directly by the simulation host.
    /// </summary>
    public ControlledOperationId ParentId { get; }

    /// <summary>Gets the simulation runtime this operation executes under.</summary>
    public SimulationRuntimeIdentity Runtime { get; }

    /// <summary>
    /// Gets the node this operation is scoped to, or <see langword="null"/> for cluster-level work.
    /// </summary>
    public SimulationNodeIdentity? Node { get; }

    /// <summary>
    /// Gets this operation's logical execution identity - the ambient "logical thread" it runs as,
    /// deliberately distinct from whatever <see cref="Environment.CurrentManagedThreadId"/> its
    /// physical thread reports. The scheduler installs this into
    /// <see cref="SimulationExecutionContext"/> while the operation runs.
    /// </summary>
    public SimulationLogicalExecutionId LogicalExecutionId { get; }

    /// <summary>
    /// Gets a short, stable description of the work this operation was created to perform, used in
    /// deterministic diagnostics. Never embeds non-deterministic data.
    /// </summary>
    public string WorkDescription { get; }

    /// <summary>Gets the operation's current lifecycle state.</summary>
    public ControlledOperationState State => _state;

    /// <summary>
    /// Gets why the operation is paused, or <see langword="null"/> if it is not paused. Set together
    /// with a transition into <see cref="ControlledOperationState.Paused"/> and cleared on resume.
    /// </summary>
    public ControlledOperationPauseReason? PauseReason { get; private set; }

    /// <summary>
    /// Gets the exception that faulted the operation (<see cref="ControlledOperationState.Faulted"/>),
    /// or <see langword="null"/> otherwise. The kernel's internal teardown-unwind signal is never
    /// surfaced here.
    /// </summary>
    public Exception? TerminalException { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this operation reached a terminal state
    /// (<see cref="ControlledOperationState.Completed"/>, <see cref="ControlledOperationState.Faulted"/>,
    /// or <see cref="ControlledOperationState.Canceled"/>).
    /// </summary>
    public bool IsTerminal => IsTerminalState(_state);

    // --- Internal mechanics owned by the scheduler ---

    /// <summary>The physical thread carrying this operation, created lazily when first granted the baton.</summary>
    internal Thread? Thread { get; set; }

    /// <summary>The caller's captured execution context, restored on the operation thread so ambient flows in.</summary>
    internal ExecutionContext? CapturedContext { get; }

    /// <summary>
    /// Set by the scheduler during teardown so the next park (or the current one, once unparked)
    /// throws the internal unwind signal instead of continuing. Never reset.
    /// </summary>
    internal bool TerminationRequested { get; private set; }

    /// <summary>Invokes the operation body. Only called on the operation's own physical thread.</summary>
    internal void InvokeBody() => _body();

    /// <summary>Grants the permission baton to this operation's physical thread.</summary>
    internal void GrantPermission() => _permission.Release();

    /// <summary>
    /// Parks the calling (operation) thread until the scheduler grants it the permission baton.
    /// Throws the internal unwind signal if teardown has been requested, so a torn-down operation's
    /// thread unwinds instead of resuming its body.
    /// </summary>
    internal void WaitForPermission()
    {
        _permission.Wait();
        if (TerminationRequested)
        {
            throw new ControlledOperationAbortSignal(Id);
        }
    }

    /// <summary>Requests teardown-driven unwinding of this operation's thread.</summary>
    internal void RequestTermination() => TerminationRequested = true;

    /// <summary>
    /// Validates and applies a state transition. Only the owning scheduler calls this, and only
    /// while holding its lock; every rejected edge throws
    /// <see cref="InvalidControlledOperationTransitionException"/> rather than silently no-op'ing.
    /// </summary>
    /// <param name="to">The target state.</param>
    /// <param name="pauseReason">The pause reason when transitioning to <see cref="ControlledOperationState.Paused"/>; otherwise ignored.</param>
    /// <param name="terminalException">The fault when transitioning to <see cref="ControlledOperationState.Faulted"/>; otherwise ignored.</param>
    internal void ApplyTransition(
        ControlledOperationState to,
        ControlledOperationPauseReason? pauseReason = null,
        Exception? terminalException = null)
    {
        var from = _state;
        if (!CanTransition(from, to))
        {
            throw new InvalidControlledOperationTransitionException(Id, from, to);
        }

        if (to == ControlledOperationState.Paused)
        {
            PauseReason = pauseReason ?? throw new ArgumentNullException(nameof(pauseReason), "A pause reason is required when pausing.");
        }
        else
        {
            PauseReason = null;
        }

        if (to == ControlledOperationState.Faulted)
        {
            TerminalException = terminalException ?? throw new ArgumentNullException(nameof(terminalException), "A terminal exception is required when faulting.");
        }

        _state = to;
    }

    /// <summary>
    /// The fixed legality table for the operation state machine. See
    /// <see cref="ControlledOperationState"/> for the full rationale of each edge.
    /// </summary>
    /// <param name="from">The current state.</param>
    /// <param name="to">The proposed next state.</param>
    /// <returns><see langword="true"/> if the transition is permitted.</returns>
    public static bool CanTransition(ControlledOperationState from, ControlledOperationState to) => (from, to) switch
    {
        (ControlledOperationState.Created, ControlledOperationState.Runnable) => true,
        (ControlledOperationState.Created, ControlledOperationState.Canceled) => true,
        (ControlledOperationState.Runnable, ControlledOperationState.Running) => true,
        (ControlledOperationState.Runnable, ControlledOperationState.Canceled) => true,
        (ControlledOperationState.Running, ControlledOperationState.Paused) => true,
        (ControlledOperationState.Running, ControlledOperationState.Runnable) => true,
        (ControlledOperationState.Running, ControlledOperationState.Completed) => true,
        (ControlledOperationState.Running, ControlledOperationState.Faulted) => true,
        (ControlledOperationState.Running, ControlledOperationState.Canceled) => true,
        (ControlledOperationState.Paused, ControlledOperationState.Runnable) => true,
        (ControlledOperationState.Paused, ControlledOperationState.Canceled) => true,
        _ => false,
    };

    /// <summary>
    /// Gets a value indicating whether the given state is terminal (no legal transition leaves it).
    /// </summary>
    /// <param name="state">The state to classify.</param>
    /// <returns><see langword="true"/> if the state is terminal.</returns>
    public static bool IsTerminalState(ControlledOperationState state) =>
        state is ControlledOperationState.Completed or ControlledOperationState.Faulted or ControlledOperationState.Canceled;

    private bool _signalsDisposed;

    internal void DisposeSignals()
    {
        if (_signalsDisposed)
        {
            return;
        }

        _signalsDisposed = true;
        _permission.Dispose();
    }

    private string DebuggerDisplay => ToString();

    /// <inheritdoc />
    public override string ToString()
    {
        var reason = PauseReason is { } r ? $" reason={r}" : string.Empty;
        var node = Node is { } n ? $" node={n.Address}" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Id} [{_state}]{reason}{node} work='{WorkDescription}' logical={LogicalExecutionId.Value} parent={ParentId.Value}");
    }
}

namespace Clockwork.Runtime.Scheduling;

/// <summary>
/// <para>
/// The lifecycle state of a <see cref="ControlledOperation"/> as tracked by its owning
/// <see cref="ControlledOperationScheduler"/>. The scheduler - not arbitrary callers - is the only
/// component that drives these transitions, and it validates every transition against a fixed
/// legality table (see <see cref="ControlledOperation.CanTransition"/>); an illegal transition
/// fails loudly with an <see cref="InvalidControlledOperationTransitionException"/> rather than
/// silently corrupting scheduler state.
/// </para>
/// <para>
/// The legal transitions are:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Created"/> -&gt; <see cref="Runnable"/> (admitted for scheduling).</description></item>
///   <item><description><see cref="Created"/> -&gt; <see cref="Canceled"/> (canceled before it ever ran).</description></item>
///   <item><description><see cref="Runnable"/> -&gt; <see cref="Running"/> (granted the permission baton).</description></item>
///   <item><description><see cref="Runnable"/> -&gt; <see cref="Canceled"/> (canceled while waiting to run).</description></item>
///   <item><description><see cref="Running"/> -&gt; <see cref="Paused"/> (voluntarily yielded the baton to wait).</description></item>
///   <item><description><see cref="Running"/> -&gt; <see cref="Runnable"/> (yielded the baton but immediately re-runnable).</description></item>
///   <item><description><see cref="Running"/> -&gt; <see cref="Completed"/> (body returned normally).</description></item>
///   <item><description><see cref="Running"/> -&gt; <see cref="Faulted"/> (body threw a non-control exception).</description></item>
///   <item><description><see cref="Running"/> -&gt; <see cref="Canceled"/> (terminated / observed cancellation).</description></item>
///   <item><description><see cref="Paused"/> -&gt; <see cref="Runnable"/> (resumed).</description></item>
///   <item><description><see cref="Paused"/> -&gt; <see cref="Canceled"/> (canceled / torn down while paused).</description></item>
/// </list>
/// <para>
/// <see cref="Completed"/>, <see cref="Faulted"/>, and <see cref="Canceled"/> are terminal: no
/// transition leaves them.
/// </para>
/// </summary>
public enum ControlledOperationState
{
    /// <summary>
    /// The operation has been registered but has not yet been admitted for scheduling. It holds no
    /// permission baton and its physical thread (if any) has not started running its body.
    /// </summary>
    Created,

    /// <summary>
    /// The operation is eligible to run and is waiting for the scheduler to grant it the single
    /// permission baton. It is not currently executing any SUT code.
    /// </summary>
    Runnable,

    /// <summary>
    /// The operation currently holds the permission baton and is the one operation permitted to
    /// execute SUT code. At most one operation per scheduler is ever in this state at a time.
    /// </summary>
    Running,

    /// <summary>
    /// The operation has voluntarily released the permission baton to wait for something (its
    /// reason is recorded in <see cref="ControlledOperation.PauseReason"/>) and is not eligible to
    /// run until something transitions it back to <see cref="Runnable"/> via
    /// <see cref="ControlledOperationScheduler.Resume"/>. Its physical thread is parked on a wait
    /// handle - it is not busy-spinning.
    /// </summary>
    Paused,

    /// <summary>
    /// Terminal: the operation's body returned normally.
    /// </summary>
    Completed,

    /// <summary>
    /// Terminal: the operation's body threw a non-control exception, captured in
    /// <see cref="ControlledOperation.TerminalException"/>.
    /// </summary>
    Faulted,

    /// <summary>
    /// Terminal: the operation was canceled or torn down before completing normally.
    /// </summary>
    Canceled,
}

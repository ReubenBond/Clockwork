using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Tasks;

/// <summary>
/// <para>
/// The deterministic task-scheduling service a simulation host provides to the controlled async/task
/// machinery (the compiler builders, awaiters, and <c>Task</c>/<c>ValueTask</c> shims in
/// <c>Clockwork.Runtime.Tasks</c> and <c>Clockwork.Runtime.Tasks.CompilerServices</c>). A host
/// registers exactly one coordinator per active runtime with <see cref="SimulationTaskCoordination"/>;
/// the controlled machinery resolves it from the ambient <c>SimulationExecutionContext</c> and routes
/// every asynchronous continuation and synchronous wait through it so that <c>async</c>/<c>await</c> -
/// including <c>ConfigureAwait(false)</c> - stays on the simulation's single logical thread instead of
/// escaping to the physical thread pool.
/// </para>
/// <para>
/// The controlled machinery never relies on the real Task infrastructure's continuation threading (it
/// never captures a <see cref="System.Threading.SynchronizationContext"/> and never lets the thread
/// pool run a continuation). Instead an incomplete await registers a <em>readiness</em> continuation
/// with <see cref="ScheduleWhenReady"/>; the coordinator re-checks readiness as it pumps and runs the
/// continuation deterministically once the awaited work has completed. This is what makes "await pauses
/// the logical operation; completion makes it runnable" a single-threaded, replayable transition rather
/// than a real-time callback race.
/// </para>
/// </summary>
public interface ISimulationTaskCoordinator
{
    /// <summary>
    /// Enqueues <paramref name="continuation"/> to run on the simulation's logical thread for
    /// <paramref name="node"/>, deterministically ordered after any work already pending. The controlled
    /// analogue of <see cref="System.Threading.SynchronizationContext.Post"/>: it never runs the
    /// continuation inline and never hops to a physical thread pool thread. Used for continuations that
    /// are immediately runnable, such as <c>Task.Yield</c>.
    /// </summary>
    /// <param name="node">The node the continuation is scoped to, or <see langword="null"/> for cluster-level work.</param>
    /// <param name="continuation">The continuation to schedule. Never <see langword="null"/>.</param>
    void Schedule(SimulationNodeIdentity? node, Action continuation);

    /// <summary>
    /// Registers <paramref name="continuation"/> to run once <paramref name="isReady"/> returns
    /// <see langword="true"/>. The coordinator evaluates <paramref name="isReady"/> as it pumps and, when
    /// it holds, enqueues the continuation deterministically. This backs an <c>await</c> of an incomplete
    /// controlled task: the awaiting state machine's <c>MoveNext</c> is the continuation and
    /// <paramref name="isReady"/> is "the awaited task has completed".
    /// </summary>
    /// <param name="node">The node the continuation is scoped to, or <see langword="null"/> for cluster-level work.</param>
    /// <param name="isReady">The readiness predicate, typically <c>() =&gt; awaitedTask.IsCompleted</c>.</param>
    /// <param name="continuation">The continuation to run once ready.</param>
    void ScheduleWhenReady(SimulationNodeIdentity? node, Func<bool> isReady, Action continuation);

    /// <summary>
    /// Cooperatively pumps ready work on the calling logical thread for <paramref name="node"/> until
    /// <paramref name="completed"/> returns <see langword="true"/>. This backs both the host's top-level
    /// drive of controlled work and controlled synchronous waits (<c>task.Wait()</c>, <c>task.Result</c>,
    /// <c>Task.WaitAll</c>): rather than blocking a physical thread - which nothing in the single-threaded
    /// simulation would ever unblock - the wait drives the deterministic work loop so the awaited work
    /// makes progress on the waiting thread itself.
    /// </summary>
    /// <param name="node">The node the wait is scoped to, or <see langword="null"/> for cluster-level work.</param>
    /// <param name="completed">The predicate that ends the wait. Evaluated before pumping and again after each unit of work.</param>
    /// <exception cref="ControlledSynchronousWaitDeadlockException">
    /// Thrown when no ready work remains and no registered readiness can advance, yet
    /// <paramref name="completed"/> is still <see langword="false"/> - the deterministic signature of a
    /// wait that can never be satisfied (a deadlock).
    /// </exception>
    void DrainUntil(SimulationNodeIdentity? node, Func<bool> completed);
}

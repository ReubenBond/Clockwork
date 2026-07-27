namespace Clockwork.Runtime.Scheduling.Resources;

/// <summary>
/// <para>
/// A coarse classification of the concurrency primitive a <see cref="ControlledResource"/> models.
/// This drives <em>diagnostics and deadlock classification only</em> - it deliberately does not
/// change the mechanics of the reusable wait/wake core (that is identical for every kind). The
/// per-primitive acquire/release <em>semantics</em> (mutual exclusion, counting, manual/auto reset,
/// reentrancy) are composed on top of the core by callers and, where a primitive needs behaviour the
/// core does not model, an <see cref="ControlledResource.SpecializationTag"/> hook - never by baking
/// one-size-fits-all rules into the core.
/// </para>
/// <para>
/// The set is intentionally open-ended (<see cref="Custom"/> covers anything not yet worth a
/// dedicated member) so that adding a future primitive never requires a breaking enum change first.
/// None of these implies the corresponding BCL shim exists yet - shims are Phase 6/7 work; this is
/// only how the scheduler labels a resource for humans reading a deadlock report.
/// </para>
/// </summary>
public enum ControlledResourceKind
{
    /// <summary>
    /// A mutual-exclusion monitor (lock): at most one owner at a time, reentrant for the owner, with
    /// a wait set that a <c>Pulse</c>/<c>PulseAll</c>-style signal wakes. See
    /// <see cref="ControlledResource.Owner"/> and <see cref="ControlledResource.RecursionCount"/>.
    /// </summary>
    Monitor,

    /// <summary>
    /// A counting semaphore: <see cref="ControlledResource.CurrentCount"/> permits out of
    /// <see cref="ControlledResource.MaximumCount"/>; acquirers that find no permit wait, releasers
    /// return permits and wake waiters. No single owner concept.
    /// </summary>
    Semaphore,

    /// <summary>
    /// A manual-reset event: a latch that, once set, stays set and wakes every current and future
    /// waiter until explicitly reset. Modeled as an ownerless resource whose set/reset state a
    /// caller tracks via <see cref="ControlledResource.IsSignaled"/>.
    /// </summary>
    ManualResetEvent,

    /// <summary>
    /// An auto-reset event: setting it wakes exactly one waiter and then automatically resets, so it
    /// behaves like a single-permit semaphore with event ergonomics.
    /// </summary>
    AutoResetEvent,

    /// <summary>
    /// A generic wait handle not otherwise classified (e.g. a future mutex/handle shim). Uses the
    /// same wait/wake core; the label just tells a reader which primitive produced the wait.
    /// </summary>
    WaitHandle,

    /// <summary>
    /// A synchronous wait for a <see cref="System.Threading.Tasks.Task"/> to complete (e.g. a
    /// blocking <c>task.Wait()</c>/<c>.Result</c>): the resource is "owned" by whatever operation
    /// will complete the task, and is externally completable rather than a mutual-exclusion lock.
    /// </summary>
    TaskCompletion,

    /// <summary>
    /// A pure timer/delay wait with no signaling counterpart - it can only be resolved by virtual
    /// time advancing (or cancellation). Such a wait is never part of a resource deadlock cycle.
    /// </summary>
    Timer,

    /// <summary>Any resource whose primitive is not covered by the other kinds.</summary>
    Custom,
}

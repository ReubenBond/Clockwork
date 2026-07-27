namespace Clockwork.Instrumentation.Rules.BuiltIn;

/// <summary>
/// The API family a built-in <see cref="RewriteRule"/> belongs to. Families are the unit of granular
/// include/exclude selection for the built-in deterministic BCL rule set: a caller can opt a whole
/// family in or out, but never edit individual signatures, so the shipped inventory stays coherent.
/// </summary>
public enum BuiltInRuleFamily
{
    /// <summary>
    /// Wall-clock and monotonic time: <see cref="System.DateTime"/>, <see cref="System.DateTimeOffset"/>,
    /// <see cref="System.Diagnostics.Stopwatch"/> static timestamp APIs, and
    /// <see cref="System.Environment"/> tick counters.
    /// </summary>
    Clock,

    /// <summary>Identity: <see cref="System.Guid"/> factory methods (<c>NewGuid</c>, <c>CreateVersion7</c>).</summary>
    Identity,

    /// <summary>General-purpose pseudo-randomness: <see cref="System.Random"/> shared instance and constructors.</summary>
    Random,

    /// <summary>
    /// Cryptographic randomness: the static <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
    /// APIs and factories that draw operating-system entropy. Controlled to the policy shim, which rejects
    /// by default and only serves deterministic-insecure bytes under an explicit test-only opt-in.
    /// </summary>
    Crypto,

    /// <summary>
    /// Task combinators: the static <see cref="System.Threading.Tasks.Task.WhenAll(System.Threading.Tasks.Task[])"/>
    /// and <c>WhenAny</c> family, redirected to controlled equivalents whose completion order is a
    /// deterministic function of when the antecedents complete on the logical thread.
    /// </summary>
    TaskCombinators,

    /// <summary>
    /// Task synchronous waits: <c>Task.Wait()</c>, <c>Task.WaitAll</c>, and <c>Task.WaitAny</c>, redirected
    /// to controlled waits that pump the deterministic loop instead of blocking a physical thread (a
    /// never-satisfiable wait surfaces as a precise deadlock diagnostic).
    /// </summary>
    TaskSynchronization,

    /// <summary>
    /// Task continuations: <c>Task.ContinueWith</c>, redirected so the continuation is scheduled on the
    /// controlled coordinator and runs on the logical thread after the antecedent.
    /// </summary>
    TaskContinuations,

    /// <summary>
    /// Task surfaces deferred to later phases: <c>Task.Delay</c> (virtual timers, Phase 8) and
    /// <c>Task.Run</c> (thread-pool offload, Phase 6B). Classified <c>Rejected</c>:
    /// the shim fails the call with a precise diagnostic under simulation rather than silently using wall
    /// time or a real thread-pool thread, and runs the real BCL API unchanged outside simulation.
    /// </summary>
    TaskDeferred,
}

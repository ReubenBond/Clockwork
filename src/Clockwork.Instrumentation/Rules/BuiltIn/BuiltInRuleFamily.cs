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
    /// Task surfaces deferred to later phases: <c>Task.Delay</c> (virtual timers, Phase 8). Classified
    /// <c>Rejected</c>: the shim fails the call with a precise diagnostic under simulation rather than
    /// silently using wall time, and runs the real BCL API unchanged outside simulation.
    /// </summary>
    TaskDeferred,

    /// <summary>
    /// Task thread-pool scheduling: the <c>Task.Run</c> family (every <c>Action</c>/<c>Func&lt;TResult&gt;</c>/
    /// <c>Func&lt;Task&gt;</c>/<c>Func&lt;Task&lt;TResult&gt;&gt;</c> overload, with and without a
    /// <see cref="System.Threading.CancellationToken"/>). Redirected to controlled equivalents (Phase 6B)
    /// that queue the body as a fresh controlled operation on the simulation coordinator - it runs on the
    /// single logical thread interleaved with all other controlled work instead of on an uncontrolled
    /// physical thread-pool thread - preserving unwrap, cancellation, and fault semantics.
    /// </summary>
    TaskScheduling,

    /// <summary>
    /// Compiler-generated async machinery: the <c>SubstituteType</c> rules that retarget an
    /// <c>async</c> state machine's builder and awaiter types
    /// (<see cref="System.Runtime.CompilerServices.AsyncTaskMethodBuilder"/>, <c>TaskAwaiter</c>,
    /// <c>ConfiguredTaskAwaitable</c>/<c>YieldAwaitable</c> and their awaiters, generic and non-generic)
    /// onto Clockwork's controlled equivalents, plus the <c>Task.Yield()</c> redirect. Applied by the
    /// member-aware substitution pass so every awaited continuation is scheduled through the simulation
    /// coordinator instead of the thread pool, while <c>ConfigureAwait(false)</c> stays controlled.
    /// </summary>
    AsyncMachinery,

    /// <summary>
    /// Compiler-generated <see cref="System.Threading.Tasks.ValueTask"/> machinery: the
    /// <c>SubstituteType</c> rules that retarget an <c>async ValueTask</c>/<c>async ValueTask&lt;T&gt;</c>
    /// state machine's builder and awaiter types
    /// (<see cref="System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder"/>, <c>ValueTaskAwaiter</c>,
    /// <c>ConfiguredValueTaskAwaitable</c> and their awaiters, generic and non-generic) onto Clockwork's
    /// controlled equivalents, so awaiting a <see cref="System.Threading.Tasks.ValueTask"/> is scheduled
    /// through the coordinator and <c>ConfigureAwait(false)</c> stays controlled.
    /// </summary>
    ValueTaskMachinery,

    /// <summary>
    /// <see cref="System.Threading.Tasks.TaskFactory"/> / <see cref="System.Threading.Tasks.TaskFactory{TResult}"/>
    /// scheduling: <c>StartNew</c> offloads work onto a task scheduler (the thread pool by default).
    /// Classified <c>Controlled</c> (Phase 6B) - the shim queues the delegate body as a fresh controlled
    /// operation on the coordinator (honouring state, cancellation, options with controlled meaning, and
    /// results) instead of escaping onto a physical thread. The complete .NET 10 overload set is classified;
    /// custom schedulers and unsupported option semantics are rejected, and every form runs the real BCL API
    /// unchanged outside simulation.
    /// </summary>
    TaskFactory,

    /// <summary>
    /// <see cref="System.Threading.Thread"/> surface: construction and the <c>Start</c>/<c>Join</c>
    /// instance members are <c>Controlled</c> (Phase 6B) - a controlled thread is a real thread object
    /// whose body is scheduled as a fresh controlled operation on the coordinator instead of running on a
    /// physical OS thread, and <c>Join</c> pumps the deterministic loop; the static <c>Sleep</c>,
    /// <c>Yield</c>, and <c>SpinWait</c> hints yield cooperatively without blocking or using real time.
    /// The OS-specific surface (<c>Priority</c>, apartment state, <c>Interrupt</c>) is classified
    /// <c>Rejected</c>: it cannot be modelled faithfully by the cooperative scheduler, so the rewritten
    /// call site fails precisely under simulation and runs the real API unchanged outside one.
    /// </summary>
    Thread,

    /// <summary>
    /// <see cref="System.Threading.ThreadPool"/> queueing surface. The <c>QueueUserWorkItem</c> /
    /// <c>UnsafeQueueUserWorkItem</c> family is classified <c>Controlled</c> (Phase 6B) - the shim queues
    /// the callback as a fresh controlled operation on the coordinator instead of dispatching it to a
    /// physical thread-pool thread, preserving the safe-vs-unsafe <see cref="System.Threading.ExecutionContext"/>
    /// flow distinction (safe variants capture and flow the caller's context; unsafe variants do not).
    /// The native-I/O surface (<c>UnsafeQueueNativeOverlapped</c>) and, until Phase 7 provides controlled
    /// wait handles, the registered-wait surface (<c>RegisterWaitForSingleObject</c> and its unsafe
    /// variant) are <c>Rejected</c>: they cannot be modelled by the deterministic scheduler, so the
    /// rewritten call site fails precisely under simulation. Outside a simulation every shim delegates to
    /// the real BCL API unchanged. This goes beyond Coyote, which routes thread-pool work through its
    /// controlled task types.
    /// </summary>
    ThreadPool,

    /// <summary>
    /// <see cref="System.Threading.Tasks.Parallel"/> surface. The simple-body overloads -
    /// <c>Invoke</c>, <c>For(int/long, ..., Action&lt;int/long&gt;)</c>, and
    /// <c>ForEach&lt;TSource&gt;(IEnumerable&lt;TSource&gt;, Action&lt;TSource&gt;)</c>, each with and without a
    /// <see cref="System.Threading.Tasks.ParallelOptions"/> - are classified <c>Controlled</c> (Phase 6B):
    /// the shim queues each branch as a fresh controlled operation on the coordinator and drains the
    /// deterministic loop until all branches complete, aggregating faults into an
    /// <see cref="System.AggregateException"/> and observing the options' cancellation token. The overloads
    /// whose body receives a <see cref="System.Threading.Tasks.ParallelLoopState"/> (break/stop), the
    /// thread-local (<c>TLocal</c>) overloads, and the <c>Partitioner</c> overloads are <c>Rejected</c>:
    /// they cannot be modelled without constructing framework types that have no public surface, so the
    /// rewritten call site fails precisely under simulation. Outside a simulation every shim delegates to
    /// the real BCL API unchanged.
    /// </summary>
    Parallel,

    /// <summary>
    /// Process control and abrupt-termination surfaces that cannot be modelled by the deterministic
    /// scheduler: the static <see cref="System.Diagnostics.Process"/> <c>Start</c> family (spawning an
    /// uncontrolled OS process), the <see cref="System.Diagnostics.Process"/> instance <c>Kill</c> /
    /// <c>WaitForExit</c> / <c>WaitForExitAsync</c> members (killing or blocking on an uncontrolled
    /// process), and <see cref="System.Environment"/> <c>Exit</c> / <c>FailFast</c> (tearing the host
    /// process down out from under the simulation). Classified <c>Rejected</c>: the rewritten call site
    /// throws a precise uncontrolled-invocation diagnostic that names the exact API and IL offset, so a
    /// rewritten assembly can never escape the deterministic model by launching, killing, waiting on, or
    /// terminating a real process. This mirrors Coyote's uncontrolled-invocation pass, which rewrites
    /// such call sites to throw rather than run.
    /// </summary>
    UncontrolledInvocation,

    /// <summary>
    /// <see cref="System.Threading.Monitor"/> surface, and therefore every C# <c>lock (object)</c>
    /// statement (which the compiler lowers to <c>Monitor.Enter(obj, ref bool)</c> +
    /// <c>finally Monitor.Exit(obj)</c>). Classified <c>Controlled</c> (Phase 7A): the shim models each
    /// monitored object's ownership, recursion count, and condition-variable wait set on the cooperative
    /// logical thread. <c>Enter</c>/<c>TryEnter</c> acquire (a contended acquire pumps the deterministic
    /// loop until the owner releases), <c>Exit</c> unwinds one recursion level, <c>Wait</c> atomically
    /// releases the full recursion count and re-acquires it after being pulsed, and <c>Pulse</c>/
    /// <c>PulseAll</c> move waiters to the ready set with replayable ordering. A never-satisfiable acquire
    /// or wait surfaces as the loop-model deadlock diagnostic. Zero timeouts are faithful non-blocking
    /// tries; finite positive timeouts use deterministic simulated deadlines. Ownership/argument/timeout
    /// errors throw exactly as the BCL. Outside a simulation every
    /// shim delegates to the real <see cref="System.Threading.Monitor"/>.
    /// </summary>
    Monitor,

    /// <summary>
    /// <see cref="System.Threading.Lock"/> (the .NET 9+ dedicated lock type) and its C# <c>lock (Lock)</c>
    /// lowering (<c>Lock.Scope scope = obj.EnterScope(); try { ... } finally { scope.Dispose(); }</c>).
    /// Classified <c>Controlled</c> (Phase 7A) via <c>SubstituteType</c>: every reference to
    /// <see cref="System.Threading.Lock"/> and its nested <c>Scope</c> ref struct is retargeted onto the
    /// controlled equivalents, so <c>Enter</c>/<c>Exit</c>/<c>EnterScope</c>/<c>TryEnter</c>/
    /// <c>IsHeldByCurrentThread</c> and the scope's <c>Dispose</c> run on the controlled monitor kernel
    /// with the same reentrancy and mutual-exclusion semantics. Outside a simulation the controlled type
    /// delegates to a real wrapped <see cref="System.Threading.Lock"/>.
    /// </summary>
    Lock,

    /// <summary>
    /// <see cref="System.Threading.SemaphoreSlim"/> surface: construction, <c>CurrentCount</c>, the
    /// synchronous <c>Wait</c> overloads, the asynchronous <c>WaitAsync</c> overloads, <c>Release</c>, and
    /// <c>Dispose</c>. Classified <c>Controlled</c> (Phase 7A): the permit count and waiter set are
    /// modelled on the cooperative logical thread. A synchronous <c>Wait</c> with no permit pumps the loop
    /// until a permit is released (a never-satisfiable wait surfaces as the loop-model deadlock diagnostic);
    /// <c>WaitAsync</c> returns a task completed when a permit is released; <c>Release</c> enforces the
    /// maximum count (<see cref="System.Threading.SemaphoreFullException"/>) and serves waiters in a
    /// deterministic, replayable order; cancellation is honoured on the logical thread. Zero timeouts are
    /// faithful non-blocking tries; finite positive timeouts use deterministic simulated deadlines.
    /// <c>AvailableWaitHandle</c> depends on the Phase 7B wait-handle surface and is
    /// classified <c>Rejected</c>: the rewritten call site fails precisely under simulation until then.
    /// Outside a simulation every shim delegates to the real <see cref="System.Threading.SemaphoreSlim"/>.
    /// </summary>
    Semaphore,
}

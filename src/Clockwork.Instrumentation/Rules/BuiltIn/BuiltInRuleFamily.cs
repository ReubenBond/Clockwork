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
    /// operation on the coordinator (honouring the factory's/call's cancellation token) instead of
    /// escaping onto a physical thread, rejects the unsupported <c>AttachedToParent</c> creation option,
    /// and runs the real BCL API unchanged outside simulation.
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
    /// tries; finite positive timeouts are modelled as infinite under simulation (virtual-time timeouts
    /// are Phase 8). Ownership/argument/timeout errors throw exactly as the BCL. Outside a simulation every
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
    /// faithful non-blocking tries; finite positive timeouts are modelled as infinite (virtual-time
    /// timeouts are Phase 8). <c>AvailableWaitHandle</c> is completed by the Phase 7B controlled
    /// wait-handle bridge: it returns a controlled <see cref="System.Threading.ManualResetEvent"/> whose
    /// signaled state tracks the permit count and disposal. Outside a simulation every shim delegates to
    /// the real <see cref="System.Threading.SemaphoreSlim"/>.
    /// </summary>
    Semaphore,

    /// <summary>
    /// <see cref="System.Threading.Interlocked"/> atomic surface: the full .NET 10 overload set of
    /// <c>Increment</c>/<c>Decrement</c>/<c>Add</c>/<c>And</c>/<c>Or</c>/<c>Exchange</c>/
    /// <c>CompareExchange</c> (every primitive, native-int, floating-point, reference, and generic
    /// reference overload), <c>Read</c>, and the memory barriers. Classified <c>Controlled</c> (Phase 7B):
    /// each call site is redirected to a shim with the identical <c>ref</c>-first signature. Clockwork's
    /// cooperative single-logical-thread scheduler makes every interlocked read-modify-write an indivisible
    /// step - it is never split and never interleaved mid-operation - so the shim delegates to the real
    /// primitive, preserving exact atomic return, overflow, and reference-write semantics inside and
    /// outside a simulation. The documented exploration policy adds no mid-operation scheduling point (the
    /// natural points remain the surrounding await/yield boundaries), and the single delegation site is
    /// where a future Phase 9 race-access hook attaches without ever splitting an atomic operation.
    /// </summary>
    Interlocked,

    /// <summary>
    /// <see cref="System.Threading.Volatile"/> acquire/release surface: the full .NET 10 overload set of
    /// <c>Read</c>/<c>Write</c> (every primitive, native-int, floating-point, and generic reference
    /// overload) plus <c>ReadBarrier</c>/<c>WriteBarrier</c>. Classified <c>Controlled</c> (Phase 7B): each
    /// call site is redirected to a shim with the identical <c>ref</c>-first signature that delegates to the
    /// real primitive, preserving the exact value read/written and the acquire (read) / release (write)
    /// fence intent. The single delegation site is where a future Phase 9 race-access hook attaches.
    /// </summary>
    Volatile,

    /// <summary>
    /// <see cref="System.Threading.SpinWait"/> busy-spin surface, retargeted by <c>SubstituteType</c> onto
    /// the controlled equivalent (the struct mirrors <c>Count</c>, <c>NextSpinWillYield</c>, <c>Reset</c>,
    /// both <c>SpinOnce</c> overloads, and the three static <c>SpinUntil</c> overloads). Classified
    /// <c>Controlled</c> (Phase 7B): a controlled spin yields cooperatively to the deterministic scheduler
    /// instead of burning CPU, <c>SpinUntil</c> pumps the loop until its predicate holds, and its finite
    /// overload uses a virtual-time deadline so a spin timeout consumes modelled - never real - time.
    /// Outside a simulation the controlled struct delegates to a real wrapped <see cref="System.Threading.SpinWait"/>.
    /// </summary>
    SpinWait,

    /// <summary>
    /// The controlled event / wait-handle surface (Phase 7B): <see cref="System.Threading.AutoResetEvent"/>,
    /// <see cref="System.Threading.ManualResetEvent"/>, <see cref="System.Threading.EventWaitHandle"/>, and
    /// the shared <see cref="System.Threading.WaitHandle"/> operations (<c>WaitOne</c>, <c>WaitAny</c>,
    /// <c>WaitAll</c>, <c>SignalAndWait</c>). Classified <c>Controlled</c> (Phase 7B): each event's signaled
    /// state and deterministic FIFO waiter set are modelled on the cooperative logical thread. A
    /// <c>WaitOne</c> with no signal pumps the loop until <c>Set</c> (a never-satisfiable wait surfaces as
    /// the loop-model deadlock diagnostic); an auto-reset event wakes and consumes exactly one eligible
    /// waiter while a manual-reset event releases all and stays signaled; finite timeouts use virtual time;
    /// <c>WaitAny</c>/<c>WaitAll</c> register across multiple handles with no lost signals. Named/cross-process
    /// event APIs cannot be modelled in a single simulated process and are rejected precisely. Outside a
    /// simulation every shim delegates to the real BCL primitive.
    /// </summary>
    WaitHandle,
}

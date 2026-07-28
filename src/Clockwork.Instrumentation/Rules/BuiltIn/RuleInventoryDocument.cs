using System.Collections.Immutable;
using System.Text;
using Clockwork.Runtime.Policy;

namespace Clockwork.Instrumentation.Rules.BuiltIn;

/// <summary>
/// Renders the deterministic BCL rule inventory (<see cref="BuiltInRuleSets.DeterministicBclInventory"/>)
/// as stable Markdown. The output is committed to <c>docs/rule-inventory.md</c> and verified against
/// this renderer by a test, so the published inventory can never silently drift from the shipped rule
/// set. The rendering is deterministic: families and rules follow their canonical declared order.
/// </summary>
public static class RuleInventoryDocument
{
    /// <summary>Renders the full inventory document.</summary>
    /// <returns>The Markdown text, using <c>\n</c> line endings and a trailing newline.</returns>
    public static string Render()
    {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');

        Line("# Built-in rewrite rule inventory");
        Line();
        Line("<!-- Generated from Clockwork.Instrumentation.Rules.BuiltIn.RuleInventoryDocument.Render().");
        Line("     Do not edit by hand; a test verifies this file matches the shipped rule set. -->");
        Line();
        Line(
            "This is the exact, exhaustive surface the built-in rule sets redirect. Every other API is " +
            "**not** rewritten. Instrumented closure binaries are simulation/test artifacts: every Controlled " +
            "entry point requires an active Clockwork simulation, and an active simulation with no registered " +
            "runtime service fails explicitly rather than use real time, randomness, or an uncontrolled task. " +
            "Uninstrumented production binaries retain ordinary BCL behavior.");
        Line();

        RenderSet(
            sb,
            "Deterministic BCL rule set",
            BuiltInRuleSets.DeterministicBclId,
            BuiltInRuleSets.DeterministicBclVersion,
            BuiltInRuleSets.DeterministicBclInventory);

        RenderSet(
            sb,
            "Controlled task rule set",
            BuiltInRuleSets.ControlledTasksId,
            BuiltInRuleSets.ControlledTasksVersion,
            BuiltInRuleSets.ControlledTasksInventory);

        Line("# Race exploration instrumentation inventory");
        Line();
        Line("This inventory is enabled only when instrumentation mode is `RaceExploration`; `Controlled` mode injects none of these calls.");
        Line();
        Line("| Surface | Instrumentation | Tracked identity |");
        Line("| --- | --- | --- |");
        Line("| `ldfld` / `stfld` on reference types | Read/write scheduling point | Weak object identity + field member |");
        Line("| `ldsfld` / `stsfld` | Read/write scheduling point | Static field member |");
        Line("| volatile field access | Schedule-only point before the `volatile.` prefix | Not race-tracked |");
        Line("| `ldelem.*` / `stelem.*` vector arrays | Read/write scheduling point | Weak array identity + element index |");
        Line("| field-address, indirect/object, and `ldelema` access | Schedule-only point | Not race-tracked |");
        Line("| `brtrue` / `brfalse` | Control-flow scheduling point | n/a |");
        Line("| `List<T>`, `Dictionary<TKey,TValue>`, `HashSet<T>` direct concrete members | Read/write/iteration point after the original call | Weak collection identity |");
        Line("| `ConcurrentBag<T>`, `ConcurrentDictionary<TKey,TValue>`, `ConcurrentQueue<T>`, `ConcurrentStack<T>` direct concrete members | Interleaving point after the original call | Not reported as a race |");
        Line();
        Line("Limits: constructors and property accessors are excluded; generated `MoveNext` methods are visited, with generated value-type state fields schedule-only. Multidimensional arrays, interface-typed and tail-prefixed collection calls, reflection/dynamic dispatch, spans, unmanaged memory, and arbitrary pointer offsets are not assigned tracked locations.");
        Line();

        Line("## Documented holes (not rewritten in these rule sets)");
        Line();
        Line("These nondeterministic or entropy-drawing surfaces are intentionally **not** covered and");
        Line("remain real BCL calls even under simulation:");
        Line();
        Line("- `Stopwatch` instance APIs (`Start`/`Stop`/`Restart`/`Elapsed`/`ElapsedMilliseconds`/`ElapsedTicks`) remain uncontrolled because their mutable lifecycle would require whole-type substitution. `GetElapsedTime(long, long)` is intentionally not rewritten or analyzed: it is deterministic arithmetic over caller-supplied timestamps (use controlled `GetTimestamp()` values).");
        Line("- Generic cryptographic helpers `RandomNumberGenerator.GetItems<T>` and `Shuffle<T>`, and any `GetString`/`GetHexString` overloads beyond those listed above.");
        Line("- `DateTime`/`DateTimeOffset` parsing/formatting and any culture-, timezone-, or kind-conversion helpers other than the `Now`/`UtcNow`/`Today` clocks above.");
        Line("- Synchronous blocking on `ValueTask`/`ValueTask<T>` (`.Result`/`.GetResult()` outside an awaiter): a value task may be consumed only once, so a blocking drain is unsafe. `await` is the supported controlled path.");
        Line("- Named/cross-process synchronization (named `EventWaitHandle`/`Mutex`/`Semaphore` and their `OpenExisting`/`TryOpenExisting` APIs): a single-process simulation cannot model kernel-object sharing, so these are rejected.");
        Line("- Custom `TimeProvider` implementations are rejected by timer-consuming controlled APIs unless Clockwork explicitly recognizes them. `System.Timers.Timer` rejects non-null `SynchronizingObject` and designer `Site` integration because those paths can marshal callbacks to uncontrolled UI or native threads. `Timer.Dispose(WaitHandle)` accepts controlled event handles only.");
        Line();
        Line("Determinism is claimed **only** for the exact rules tabulated above.");

        return sb.ToString();
    }

    private static void RenderSet(
        StringBuilder sb,
        string title,
        string id,
        string version,
        ImmutableArray<(BuiltInRuleFamily Family, RewriteRule Rule)> inventory)
    {
        void Line(string text = "") => sb.Append(text).Append('\n');

        Line($"# {title}");
        Line();
        Line($"Rule set id: `{id}`");
        Line($"Version: `{version}`");
        Line($"Shim assembly: `{BuiltInRuleSets.ShimAssemblyName}`");
        Line();

        foreach (BuiltInRuleFamily family in BuiltInRuleSets.AllFamilies)
        {
            ImmutableArray<RewriteRule> rules =
                [.. inventory.Where(e => e.Family == family).Select(e => e.Rule)];
            if (rules.IsEmpty)
            {
                continue;
            }

            Line($"## {family} family");
            Line();
            Line($"Policy: **{DescribePolicy(rules)}**. {DescribeFamily(family)}");
            Line();
            Line("| Rule id | BCL target | Shim | Policy |");
            Line("| --- | --- | --- | --- |");
            foreach (RewriteRule rule in rules)
            {
                string target = rule.Operation == RewriteOperationKind.RedirectNewObj
                    ? "new " + rule.Target.DeclaringTypeFullName + ParamSuffix(rule.Target.ParameterTypeFullNames)
                    : rule.Target.ToCanonicalString();
                Line($"| `{rule.Id}` | `{target}` | `{rule.Replacement.ToCanonicalString()}` | {rule.Policy} |");
            }

            Line();
        }
    }

    private static string DescribePolicy(ImmutableArray<RewriteRule> rules) =>
        rules.All(r => r.Policy == SimulationApiPolicy.Rejected) ? "Rejected" : "Controlled";

    private static string DescribeFamily(BuiltInRuleFamily family) => family switch
    {
        BuiltInRuleFamily.Clock =>
            "Wall-clock, offset-clock, monotonic timestamp, and tick-counter reads dispatch to the node's " +
            "simulated clock. Local-time APIs honour the configured simulation time zone; tick counters wrap " +
            "with correct `int`/`long` semantics.",
        BuiltInRuleFamily.Identity =>
            "GUIDs draw deterministic bytes while preserving RFC 4122 variant and version. `CreateVersion7` " +
            "encodes the simulated UTC millisecond timestamp in the first 48 bits; repeated calls at the same " +
            "simulated instant share that timestamp (no monotonicity guarantee beyond the BCL contract).",
        BuiltInRuleFamily.Random =>
            "`Random.Shared` and unseeded `new Random()` become per-node deterministic streams isolated from " +
            "the scheduler/network/Buggify seed domains; explicitly seeded `new Random(int)` preserves the " +
            "caller's seed exactly. Stable seed derivation uses `SimulationStableHash`.",
        BuiltInRuleFamily.Crypto =>
            "Static entropy APIs are redirected to `ControlledRandomNumberGenerator`. The default under " +
            "simulation is a precise rejected-call diagnostic; a test-only opt-in can serve bytes from " +
            "`SimulationInsecureRandomNumberGenerator`. Uninstrumented production binaries retain ordinary " +
            "cryptographic BCL behavior.",
        BuiltInRuleFamily.TaskCombinators =>
            "`Task.WhenAll`/`WhenAny` (the non-generic `Task[]`, `IEnumerable<Task>`, .NET 9+ params " +
            "`ReadOnlySpan<Task>`, and two-argument overloads, plus their generic `Task<TResult>` " +
            "counterparts) and the `TaskExtensions.Unwrap` extension methods redirect to controlled " +
            "combinators. Completion and the returned winner become a deterministic function of when the " +
            "antecedents complete on the logical thread instead of a physical thread-pool race.",
        BuiltInRuleFamily.TaskSynchronization =>
            "Blocking `Task.Wait()`, `Task.WaitAll`, and `Task.WaitAny` redirect to controlled waits that pump " +
            "the deterministic loop rather than blocking a physical thread; a never-satisfiable wait surfaces " +
            "as a precise deadlock diagnostic instead of hanging the scheduler.",
        BuiltInRuleFamily.TaskContinuations =>
            "`Task.ContinueWith(Action<Task>)`, `Task<T>.ContinueWith(Action<Task<T>>)`, and the result-producing " +
            "`Task<T>.ContinueWith<TNewResult>(Func<Task<T>,TNewResult>)` redirect so the continuation is " +
            "scheduled on the controlled coordinator and runs on the logical thread after the antecedent completes.",
        BuiltInRuleFamily.TaskTime =>
            "`Task.Delay` and `Task.WaitAsync` use controlled virtual deadlines, preserve cancellation and " +
            "terminal task state, and never consume wall-clock time.",
        BuiltInRuleFamily.TaskScheduling =>
            "`Task.Run` (all `Action`/`Func<TResult>`/`Func<Task>`/`Func<Task<TResult>>` overloads, with and " +
            "without a `CancellationToken`) redirects thread-pool scheduling into controlled operations. " +
            "Each overload redirects to a controlled equivalent that schedules the delegate as a controlled " +
            "operation on the simulation coordinator, preserving cancellation and unwrap semantics.",
        BuiltInRuleFamily.AsyncMachinery =>
            "The compiler-generated builder and awaiter types of an `async` state machine " +
            "(`AsyncTaskMethodBuilder`, `TaskAwaiter`, `ConfiguredTaskAwaitable`/`YieldAwaitable` and their " +
            "awaiters, generic and non-generic) are substituted onto Clockwork's controlled equivalents by " +
            "the member-aware pass, and `Task.Yield()` redirects to the controlled yield. Every awaited " +
            "continuation is scheduled through the simulation coordinator instead of the thread pool, and " +
            "`ConfigureAwait(false)` stays controlled.",
        BuiltInRuleFamily.ValueTaskMachinery =>
            "The compiler-generated builder and awaiter types of an `async ValueTask`/`async ValueTask<T>` " +
            "state machine (`AsyncValueTaskMethodBuilder`, `ValueTaskAwaiter`, `ConfiguredValueTaskAwaitable` " +
            "and their awaiters, generic and non-generic) are substituted onto Clockwork's controlled " +
            "equivalents by the member-aware pass, so every awaited value-task continuation is scheduled " +
            "through the simulation coordinator. `ConfigureAwait(false)` stays controlled. Synchronous " +
            "blocking on a value task is not rewritten " +
            "(a value task may be consumed only once); `await` is the supported controlled path.",
        BuiltInRuleFamily.TaskFactory =>
            "All 24 .NET 10 `TaskFactory.StartNew` and `TaskFactory<T>.StartNew` overloads are classified, " +
            "including state-carrying delegates and the full cancellation/options/scheduler forms. Each " +
            "redirects to a controlled equivalent that schedules the delegate as a fresh logical strand while " +
            "preserving state, cancellation, and results. Non-default schedulers and creation options whose " +
            "semantics cannot be preserved are rejected precisely.",
        BuiltInRuleFamily.Thread =>
            "`Thread` construction (`ThreadStart`/`ParameterizedThreadStart`, with and without a stack size), " +
            "`Start`, `Join` (all overloads), `Sleep`, `Yield`, and `SpinWait` redirect to a controlled thread " +
            "that maps each thread to a controlled operation on the simulation coordinator; `Join`/`Sleep` " +
            "yield the logical thread via the deterministic loop rather than blocking a physical thread or " +
            "consuming real time. OS-specific priority, apartment-state, and `Interrupt` operations cannot be " +
            "modelled faithfully and are rejected with a precise diagnostic.",
        BuiltInRuleFamily.ThreadPool =>
            "`ThreadPool.QueueUserWorkItem` (the `WaitCallback`, `WaitCallback`+state, and generic " +
            "`Action<TState>`+state+preferLocal forms) and `UnsafeQueueUserWorkItem` (the `WaitCallback`+state, " +
            "`IThreadPoolWorkItem`, and generic forms) queue the callback as a controlled operation on the " +
            "simulation coordinator; the safe variants flow `ExecutionContext` while the unsafe variants do " +
            "not, matching the BCL. The registered-wait APIs (`RegisterWaitForSingleObject`/" +
            "`UnsafeRegisterWaitForSingleObject`, across the `uint`/`int`/`long`/`TimeSpan` timeout overloads) " +
            "run as passive, event-driven controlled waits on the target handle's modelled signalled state: " +
            "the callback fires with `timedOut: false` on a signal (an auto-reset handle consumes exactly one) " +
            "or `timedOut: true` on the virtual-time deadline, honouring `executeOnlyOnce`, re-arming " +
            "otherwise, and flowing `ExecutionContext` for the safe family only; the returned " +
            "`RegisteredWaitHandle` is substituted with the controlled handle so `Unregister` stops the wait " +
            "and signals its completion event. `UnsafeQueueNativeOverlapped` depends on native I/O and is " +
            "rejected with a precise diagnostic.",
        BuiltInRuleFamily.Timers =>
            "`System.Threading.Timer`, `System.Timers.Timer`, and `PeriodicTimer` are substituted with " +
            "controlled virtual-time implementations. `TimeProvider.System` and `CreateTimer` bridge to " +
            "the same scheduler; unsupported provider and designer marshaling paths reject precisely.",
        BuiltInRuleFamily.CancellationTimers =>
            "`CancellationTokenSource` timed constructors and `CancelAfter` use resettable virtual deadlines. " +
            "Manual cancellation, reset, and disposal remove stale registrations before they can fire.",
        BuiltInRuleFamily.Parallel =>
            "`Parallel.Invoke`, `Parallel.For` (`int`/`long`, with and without `ParallelOptions`), and " +
            "`Parallel.ForEach(IEnumerable<T>)` run their bodies as controlled operations on the simulation " +
            "coordinator, preserving results, cancellation, and exception aggregation. The `ParallelLoopState` " +
            "break/stop overloads cannot be modelled deterministically yet and are rejected with a precise " +
            "diagnostic.",
        BuiltInRuleFamily.Monitor =>
            "The complete .NET 10 `Monitor` surface is classified: synchronization and C# `lock (object)` " +
            "lowering are controlled with deterministic virtual-time deadlines, while the process-wide " +
            "`LockContentionCount` metric is rejected because it has no per-simulation meaning.",
        BuiltInRuleFamily.Lock =>
            "The .NET 9+ `System.Threading.Lock` type and nested `Scope` are substituted onto controlled " +
            "equivalents, covering the dedicated C# lock lowering in Debug and Release builds.",
        BuiltInRuleFamily.Semaphore =>
            "The .NET 10 `SemaphoreSlim` constructors, counts, waits, releases, and disposal are controlled; " +
            "`AvailableWaitHandle` returns a controlled manual-reset bridge whose signal tracks whether the " +
            "permit count is positive and which composes with the controlled wait-handle surface.",
        BuiltInRuleFamily.UncontrolledInvocation =>
            "Process control and abrupt-termination APIs (`Process.Start`/`Start` instance/`Kill`/`WaitForExit`/" +
            "`WaitForExitAsync`, `Environment.Exit`/`FailFast`) cannot be modelled inside a single simulated " +
            "process at all. A throwing guard is injected before each call site so a rewritten assembly can " +
            "never launch, kill, wait on, or terminate a real OS process; unlike the controlled shims the " +
            "rejection is unconditional (it fires whether or not a simulation is active).",
        BuiltInRuleFamily.Interlocked =>
            "The full .NET 10 `Interlocked` surface - `Increment`/`Decrement`/`Add`/`And`/`Or` (`int`/`long`/" +
            "`uint`/`ulong`), `Exchange`/`CompareExchange` (every primitive, native-int, floating-point, " +
            "reference, and generic reference overload), `Read` (`long`/`ulong`), and the memory barriers - " +
            "redirects each call site to a shim with the identical `ref`-first signature. Clockwork's " +
            "cooperative single-logical-thread scheduler makes every read-modify-write an indivisible step " +
            "(never split, never interleaved mid-operation), so the shim delegates to the real primitive and " +
            "preserves exact atomic return, overflow, and reference-write semantics under the active " +
            "simulation. The exploration policy injects no mid-operation scheduling point; the single " +
            "delegation site is the race-exploration access-tracking attachment point.",
        BuiltInRuleFamily.Volatile =>
            "The full .NET 10 `Volatile` surface - `Read`/`Write` (every primitive, native-int, " +
            "floating-point, and generic reference overload) and the `ReadBarrier`/`WriteBarrier` fences - " +
            "redirects each call site to a shim with the identical `ref`-first signature. Under the " +
            "cooperative single-logical-thread scheduler a volatile access is an indivisible step, so the " +
            "shim delegates to the real primitive and preserves the exact value read/written together with " +
            "the acquire (read) / release (write) fence intent. The single delegation site is the " +
            "race-exploration access-tracking attachment point.",
        BuiltInRuleFamily.SpinWait =>
            "`System.Threading.SpinWait` is a value type retargeted by whole-type substitution (like " +
            "`System.Threading.Lock`): every local/field/parameter typed `SpinWait`, each `new SpinWait()`/" +
            "`default`, the instance members (`Count`, `NextSpinWillYield`, `Reset`, both `SpinOnce` " +
            "overloads) and the static `SpinUntil` overloads remap onto the controlled struct. Inside a " +
            "simulation a spin never burns CPU or consumes real time: `SpinOnce` is a cooperative no-op that " +
            "only advances the observable spin count, and `SpinUntil` pumps the deterministic loop until its " +
            "predicate holds (a never-satisfiable predicate surfaces as the loop-model deadlock diagnostic). " +
            "The finite `SpinUntil` overloads use a first-winner virtual-time deadline.",
        BuiltInRuleFamily.WaitHandle =>
            "The controlled event / wait-handle surface - `AutoResetEvent`, `ManualResetEvent`, " +
            "`EventWaitHandle`, and the shared `WaitHandle` operations. Each concrete event is a sealed BCL " +
            "class, so the real object is retained as an identity handle while its signaled state and a " +
            "deterministic FIFO waiter set live in a side table; every `new` redirects to a `Create` factory " +
            "and each instance member is a receiver-first shim. `WaitOne` (all five overloads) pumps the " +
            "deterministic loop until the event is signaled - a never-satisfiable wait surfaces as the " +
            "loop-model deadlock diagnostic rather than hanging - and `Set`/`Reset` model exact reset-mode " +
            "semantics: an auto-reset `Set` wakes and consumes exactly one eligible waiter (or leaves the " +
            "event signaled until the next `WaitOne` consumes it), while a manual-reset `Set` releases every " +
            "waiter and stays signaled until `Reset`. The static multi-handle operations `WaitAny` (returns " +
            "the lowest-index signaled handle) and `WaitAll` (waits until every handle is simultaneously " +
            "signaled, then consumes them atomically so an auto-reset handle is never partially consumed) " +
            "register across all handles with no lost signals, validating null/empty/over-64 arrays and - " +
            "for `WaitAll` - duplicate handles; `SignalAndWait` atomically signals the first handle then " +
            "waits on the second. Finite timeouts use a first-winner virtual-time deadline (zero polls, " +
            "infinite never times out); `Dispose`/`Close` mark the modelled state disposed. Named / " +
            "cross-process APIs (named constructors, `OpenExisting`, `TryOpenExisting`) and the raw " +
            "native-handle accessors (`Handle`, `SafeWaitHandle`) cannot be modelled in a single simulated " +
            "process and are rejected with a precise diagnostic.",
        BuiltInRuleFamily.ReaderWriterLockSlim =>
            "Every public .NET 10 `ReaderWriterLockSlim` constructor, property, enter/try-enter/exit overload, " +
            "and `Dispose` member redirects to receiver-first controlled shims. The real BCL instance is only " +
            "an identity key; logical-strand ownership, recursion, wait queues, and deadlines are modelled " +
            "without blocking a physical thread.",
        BuiltInRuleFamily.ManualResetEventSlim =>
            "Every public .NET 10 `ManualResetEventSlim` constructor, property, set/reset/wait overload, and " +
            "`Dispose` redirects to receiver-first controlled shims. Signal state, waiters, cancellation, " +
            "deadlines, and the exposed wait-handle bridge are modelled in side state.",
        BuiltInRuleFamily.Mutex =>
            "Unnamed `Mutex` construction and `ReleaseMutex` are controlled through the wait-handle kernel. " +
            "Named constructors (including null-name forms that the shim conditionally treats as unnamed) and " +
            "`OpenExisting`/`TryOpenExisting` are classified Rejected because a non-null name is cross-process " +
            "kernel state. Ownership and recursion are logical-strand state; owner exit without `ReleaseMutex` " +
            "leaves the mutex owned so a later indefinite wait reports the controlled deadlock diagnostic " +
            "rather than simulating `AbandonedMutexException`.",
        BuiltInRuleFamily.KernelSemaphore =>
            "The unnamed kernel `Semaphore` constructor and both `Release` overloads are controlled through " +
            "the wait-handle kernel. Named constructors and `OpenExisting`/`TryOpenExisting` are Rejected " +
            "because cross-process semaphore state cannot be represented by one simulation.",
        BuiltInRuleFamily.SpinLock =>
            "`SpinLock` is wholly substituted with `ControlledSpinLock`, preserving its value-type surface " +
            "while replacing CPU spinning with deterministic scheduler pumping.",
        BuiltInRuleFamily.ExecutionContext =>
            "`ExecutionContext` capture, run, flow-control, copy, and disposal members redirect to controlled " +
            "shims. The legacy `GetObjectData` serialization surface is Rejected before it can invoke BCL " +
            "serialization behavior.",
        BuiltInRuleFamily.SynchronizationContext =>
            "`SynchronizationContext` ambient-context and callback-dispatch members redirect to controlled " +
            "shims. `Post` queues through the coordinator and `Send` runs on the current logical strand; custom " +
            "context dispatch is not invoked. Its raw native-handle `Wait` member is Rejected before it can " +
            "block a physical thread.",
        BuiltInRuleFamily.Barrier =>
            "`Barrier` is wholly substituted with `ControlledBarrier`, including generic occurrences such as " +
            "`Action<Barrier>`, so participant state and post-phase callbacks remain under simulation.",
        BuiltInRuleFamily.CountdownEvent =>
            "`CountdownEvent` is wholly substituted with `ControlledCountdownEvent`, so all count updates, " +
            "waits, bridge handles, and disposal run under the deterministic scheduler.",
        _ => string.Empty,
    };

    private static string ParamSuffix(ImmutableArray<string> parameters) =>
        parameters.IsDefault ? "(*)" : "(" + string.Join(",", parameters) + ")";
}

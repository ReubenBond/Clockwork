using System.Collections.Immutable;
using System.Text;
using Clockwork.Runtime.Policy;

namespace Clockwork.Instrumentation.Rules.BuiltIn;

/// <summary>
/// Renders the built-in rule inventories (<see cref="BuiltInRuleSets.DeterministicBclInventory"/> and
/// <see cref="BuiltInRuleSets.ControlledTasksInventory"/>) and the members of their targeted types that stay BCL calls
/// (<see cref="UnrewrittenMembers"/>) as stable Markdown. The output is committed to <c>docs/rule-inventory.md</c> and
/// verified against this renderer by a test, so the published inventory can never silently drift from the shipped rule
/// set. The rendering is deterministic: families and rules follow their canonical declared order, and listed members
/// are grouped by declaring type in ordinal order, then by reason.
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

        RenderUnrewrittenMembers(sb);

        Line("# Documented holes (not rewritten in these rule sets)");
        Line();
        Line("These nondeterministic or entropy-drawing surfaces are intentionally **not** covered and");
        Line("remain real BCL calls even under simulation:");
        Line();
        string[] holeReasons = [.. Enum.GetValues<UnrewrittenReason>().Where(IsDocumentedHole).Select(reason => Code(reason.ToString()))];
        Line($"- Every member listed above with the {string.Join(", ", holeReasons[..^1])}, or {holeReasons[^1]} reason.");
        Line("- A delegate created from a member, such as `Func<bool> responds = process.CloseMainWindow`, calls the BCL member: the rules rewrite `call`, `callvirt`, and `newobj` sites, and a delegate is created with `ldftn` or `ldvirtftn`.");
        Line("- Blocking `ValueTask<T>.Result` reads: the accessor is not rewritten, so it would block the logical thread on an incomplete value task. `await`, or `GetAwaiter().GetResult()` through the controlled awaiter (which pumps until the value task completes), is the supported controlled path.");
        Line("- Continuations registered by uninstrumented code (an assembly excluded from rewriting, or the framework itself) follow the BCL's dispatch, so a source which runs its continuations asynchronously runs them on the thread pool.");
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
        Line($"Rule set id: {Code(id)}");
        Line($"Version: {Code(version)}");
        Line($"Supported runtime: {Code(BuiltInRuleSets.SupportedRuntime.ToString())}");
        Line($"Shim assembly: {Code(BuiltInRuleSets.ShimAssemblyName)}");
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
                    : Display(rule.Target);
                Line($"| {Code(rule.Id)} | {Code(target)} | {Code(Display(rule.Replacement))} | {rule.Policy} |");
            }

            Line();
        }
    }

    private static string DescribePolicy(ImmutableArray<RewriteRule> rules) =>
        rules.All(r => r.Policy == SimulationApiPolicy.Rejected) ? "Rejected" : "Controlled";

    private static void RenderUnrewrittenMembers(StringBuilder sb)
    {
        void Line(string text = "") => sb.Append(text).Append('\n');

        Line("# Members left as BCL calls");
        Line();
        Line(
            "Every public and protected member of a type the built-in rules target is controlled or rejected by a rule " +
            "above, or is listed below with the reason a call to it stays a BCL call. A type is targeted when a rule " +
            "names or substitutes it, or when a member's signature mentions a class, struct, or interface of " +
            "`System.Diagnostics.Process.dll`. A test classifies each targeted type against the reference assemblies of " +
            "the supported runtime, so a member a new runtime adds fails that test until it is classified. Snapshots of " +
            "the reference pack's assemblies and of the public types in the namespaces of the targeted types fail when a " +
            "runtime adds or removes one.");
        Line();
        Line("| Reason | Behavior | Documented hole |");
        Line("| --- | --- | --- |");
        foreach (UnrewrittenReason reason in Enum.GetValues<UnrewrittenReason>())
        {
            Line($"| {Code(reason.ToString())} | {DescribeReason(reason)} | {(IsDocumentedHole(reason) ? "Yes" : "No")} |");
        }

        Line();
        foreach (IGrouping<string, UnrewrittenMember> typeMembers in UnrewrittenMembers.All
            .GroupBy(member => member.DeclaringTypeFullName)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Line($"## {Code(typeMembers.Key)}");
            Line();
            foreach (IGrouping<UnrewrittenReason, UnrewrittenMember> reasonMembers in typeMembers
                .GroupBy(member => member.Reason)
                .OrderBy(group => group.Key))
            {
                Line($"- {Code(reasonMembers.Key.ToString())}: " + string.Join(", ", reasonMembers.Select(member => Code(member.MemberKey))));
            }

            Line();
        }
    }

    // A documented hole is a member whose BCL behavior can differ between runs or hosts, or escape the coordinator.
    private static bool IsDocumentedHole(UnrewrittenReason reason) => reason switch
    {
        UnrewrittenReason.Value or UnrewrittenReason.Dispatch or UnrewrittenReason.Fence or UnrewrittenReason.RuntimeAsync
            or UnrewrittenReason.AlwaysThrows or UnrewrittenReason.SubstitutedTypeProtected => false,
        UnrewrittenReason.CultureOrTimeZone or UnrewrittenReason.HostState or UnrewrittenReason.Process
            or UnrewrittenReason.RealClock or UnrewrittenReason.ProcessWideId or UnrewrittenReason.CurrentThread
            or UnrewrittenReason.IdentityHandle or UnrewrittenReason.ConstructorCall or UnrewrittenReason.Blocks
            or UnrewrittenReason.Schedules => true,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private static string DescribeReason(UnrewrittenReason reason) => reason switch
    {
        UnrewrittenReason.Value =>
            "Computes its result from the receiver and its arguments, or creates, configures, or releases the receiver.",
        UnrewrittenReason.CultureOrTimeZone =>
            "Uses or sets the current culture, or converts local time with the host's time zone, so its result follows " +
            "the host's configuration.",
        UnrewrittenReason.HostState =>
            "Reads or changes state of the host process or machine (environment variables, paths, identity, privileges, " +
            "and processor, memory, and thread-pool statistics), so a simulation observes the host's values.",
        UnrewrittenReason.Process =>
            "Looks up, configures, reads, or releases an OS process or its handle, so a simulation observes the live " +
            "process. The members that start, signal, wait on, reschedule, or terminate a process are rejected " +
            "(`UncontrolledInvocation` family).",
        UnrewrittenReason.Dispatch =>
            "Runs on the receiver. `new Random()`, `Random.Shared`, `RandomNumberGenerator.Create`, and " +
            "`TimeProvider.System` return instances that draw from the simulation's seeded streams and virtual clock.",
        UnrewrittenReason.RealClock =>
            "Reads the host's monotonic clock. `Stopwatch.GetTimestamp()` and `Stopwatch.GetElapsedTime(long)` are " +
            "controlled.",
        UnrewrittenReason.ProcessWideId =>
            "Returns an identifier drawn from a process-wide counter, so its value depends on the tasks and threads the " +
            "process created before.",
        UnrewrittenReason.CurrentThread =>
            "Reads or changes the calling thread's state (its `Thread` object, thread-local data slots, principal, and " +
            "affinity hints), which belongs to the physical thread that runs every logical thread or flows with the " +
            "`ExecutionContext` as in the BCL.",
        UnrewrittenReason.IdentityHandle =>
            "Reads the BCL `Thread` object that a controlled thread keeps as its identity. That object never runs, so a " +
            "controlled thread reports itself unstarted.",
        UnrewrittenReason.ConstructorCall =>
            "A rule rewrites `new` of the type, but a constructor `call` to this one (a derived type's constructor " +
            "chaining to it, or a value type constructed in place) runs the BCL constructor, so the instance keeps BCL " +
            "state, such as an unseeded generator, a real timer, or a named kernel object.",
        UnrewrittenReason.Fence =>
            "Performs one memory access or fence, which the cooperative scheduler runs as an indivisible step. Race " +
            "exploration does not track it.",
        UnrewrittenReason.Blocks =>
            "Blocks the calling physical thread as in the BCL, which stalls the simulation until it returns. Use `await` " +
            "or a controlled overload.",
        UnrewrittenReason.Schedules =>
            "Runs its work through a BCL task scheduler, the thread pool, or a new physical thread rather than the " +
            "simulation's coordinator.",
        UnrewrittenReason.RuntimeAsync =>
            "Runtime-async intrinsic, which suspends its caller through the runtime's own continuation machinery. " +
            "Rewriting a method compiled with runtime async fails with `CWR0013`.",
        UnrewrittenReason.AlwaysThrows =>
            "Throws on .NET 11: `PlatformNotSupportedException`, or `InvalidOperationException` for " +
            "`GetCompressedStack` and `SetCompressedStack`.",
        UnrewrittenReason.SubstitutedTypeProtected =>
            "Protected member of a substituted type, which only a derived type can call. Rewriting a type derived from " +
            "a substituted type fails.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    // A Markdown code span whose delimiter is longer than any backtick run in the text, as Cecil names carry backticks
    // (Task`1, GetItems``1).
    private static string Code(string text)
    {
        int longestRun = 0;
        int run = 0;
        foreach (char c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }

        if (longestRun == 0)
        {
            return "`" + text + "`";
        }

        string fence = new('`', longestRun + 1);
        return fence + " " + text + " " + fence;
    }

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
            "caller's seed exactly. Stable seed derivation uses `SimulationStableHash`. Every instance member, " +
            "including the generic `NextInteger<T>` and `NextBinaryFloat<T>`, draws from the instance's stream.",
        BuiltInRuleFamily.Crypto =>
            "Static random APIs are redirected to `ControlledRandomNumberGenerator` and draw from a " +
            "deterministic, per-node non-cryptographic stream. Uninstrumented production binaries retain " +
            "ordinary BCL behavior.",
        BuiltInRuleFamily.TaskCombinators =>
            "`Task.WhenAll`/`WhenAny` (the non-generic `Task[]`, `IEnumerable<Task>`, .NET 9+ params " +
            "`ReadOnlySpan<Task>`, and two-argument overloads, plus their generic `Task<TResult>` " +
            "counterparts) and the `TaskExtensions.Unwrap` extension methods redirect to controlled " +
            "combinators which register nothing on their antecedents. A result whose antecedents have already " +
            "completed is computed by the BCL synchronously; otherwise the coordinator polls the antecedents and " +
            "a controlled operation completes the result, with the BCL's argument validation, result, fault " +
            "aggregation, and cancellation. `WhenAny` returns the first antecedent, in argument order, among those " +
            "observed complete; the non-generic `WhenAll` aggregates faults in the order its antecedents are " +
            "observed complete, with antecedents observed in the same scheduling step in argument order. " +
            "`Task.WhenEach` (array, span, and enumerable, generic and non-generic) yields the tasks in the order the " +
            "coordinator observes them complete, from the call on, through an enumerator whose `MoveNextAsync` a " +
            "controlled operation completes, with the BCL's validation, single enumeration, and cancellation.",
        BuiltInRuleFamily.TaskSynchronization =>
            "Blocking `Task.Wait()`, `Task.WaitAll`, and `Task.WaitAny` redirect to controlled waits that pump " +
            "the deterministic loop rather than blocking a physical thread; a never-satisfiable wait surfaces " +
            "as a precise deadlock diagnostic instead of hanging the scheduler. " +
            "`TaskAsyncEnumerableExtensions.ToBlockingEnumerable` redirects to an enumerable that pumps the loop " +
            "until each `MoveNextAsync` and the final `DisposeAsync` complete, with the BCL's token, early-exit " +
            "disposal, and exception semantics.",
        BuiltInRuleFamily.TaskContinuations =>
            "All 40 .NET 11 `Task.ContinueWith` and `Task<T>.ContinueWith` overloads (with and without state, a " +
            "`CancellationToken`, `TaskContinuationOptions`, and a `TaskScheduler`) redirect. The result is a genuine " +
            "BCL continuation task of a private gate, so its status, options, state, cancellation (including " +
            "`LazyCancellation`), `NotOn*`/`OnlyOn*` filtering, and exceptions are the BCL's. The coordinator observes " +
            "the antecedent complete and a controlled operation completes the gate, running the continuation inline on " +
            "the logical thread (`ExecuteSynchronously` is a hint). The overloads without a scheduler use " +
            "`TaskScheduler.Default`; an explicit `TaskScheduler.Default` or the simulation's own `SimulationTaskScheduler` " +
            "is accepted. `LongRunning`, `AttachedToParent`, and any other scheduler cannot be preserved and are rejected.",
        BuiltInRuleFamily.TaskCompletionSource =>
            "Rewritten code keeps the real `TaskCompletionSource` and `TaskCompletionSource<TResult>`, with their real " +
            "creation options. The completion members (`SetResult`, `SetException`, `SetCanceled`, `SetFromTask`, and " +
            "their `TrySet*` forms) redirect to shims which complete the source through the same BCL member and " +
            "publish the race-detection release edge an await, wait, combinator, or continuation acquires. Outside a " +
            "simulation (in a finalizer, or a callback from uninstrumented code) the shims call the BCL member alone.",
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
            "`ConfigureAwait(false)` stays controlled. `AsyncHelpers.HandleAsyncEntryPoint`, through which an async " +
            "`Main` returns, waits for its task through the controlled awaiter. A method compiled with runtime async has " +
            "no state machine and resumes through the runtime's own continuation machinery, so rewriting one fails " +
            "with `CWR0013`; the runtime-async `AsyncHelpers.Await` intrinsics taking a configured task awaitable are " +
            "rejected.",
        BuiltInRuleFamily.ValueTaskMachinery =>
            "The compiler-generated builder and awaiter types of an `async ValueTask`/`async ValueTask<T>` " +
            "state machine (`AsyncValueTaskMethodBuilder`, `ValueTaskAwaiter`, `ConfiguredValueTaskAwaitable` " +
            "and their awaiters, generic and non-generic) are substituted onto Clockwork's controlled " +
            "equivalents by the member-aware pass, so every awaited value-task continuation is scheduled " +
            "through the simulation coordinator. `ConfigureAwait(false)` stays controlled. The configured async " +
            "disposable and enumerable that `await using` and `await foreach` consume " +
            "(`ConfiguredAsyncDisposable`, `ConfiguredCancelableAsyncEnumerable<T>` and its `Enumerator`) are " +
            "substituted with them, and the `TaskAsyncEnumerableExtensions.ConfigureAwait`/`WithCancellation` " +
            "extensions redirect to factories for the controlled types, so iteration and dispose awaits resume " +
            "through the coordinator and the configured token reaches `GetAsyncEnumerator` as in the BCL. " +
            "The runtime-async `AsyncHelpers.Await` intrinsics taking a configured value-task awaitable are " +
            "rejected. Synchronous blocking on a value task is not rewritten " +
            "(a value task may be consumed only once); `await` is the supported controlled path.",
        BuiltInRuleFamily.ValueTaskConversions =>
            "`ValueTask.AsTask`, `ValueTask<T>.AsTask`, `ValueTask.Preserve`, and `ValueTask<T>.Preserve` redirect. A " +
            "value task backed by a pending `IValueTaskSource` is polled through the source's status by the " +
            "coordinator, with no completion callback registered on the source, and a controlled operation consumes " +
            "the completed source once through the BCL's own conversion. Any other value task is converted by the " +
            "BCL itself, synchronously.",
        BuiltInRuleFamily.TaskFactory =>
            "All 24 .NET 11 `TaskFactory.StartNew` and `TaskFactory<T>.StartNew` overloads are classified, " +
            "including state-carrying delegates and the full cancellation/options/scheduler forms. Each " +
            "redirects to a controlled equivalent that schedules the delegate as a fresh logical strand while " +
            "preserving state, cancellation, and results. Non-default schedulers and creation options whose " +
            "semantics cannot be preserved are rejected precisely.",
        BuiltInRuleFamily.TaskFactoryContinuations =>
            "All 48 .NET 11 `TaskFactory` and `TaskFactory<T>` `ContinueWhenAll` and `ContinueWhenAny` overloads " +
            "redirect. The result is a genuine BCL continuation task of a private gate, with the BCL's argument " +
            "validation, creation options, state (the continuation delegate), and cancellation, including a task canceled " +
            "at creation for a canceled token. The coordinator observes the antecedents complete and a controlled operation " +
            "completes the gate, running the continuation inline on the logical thread with a defensive copy of the " +
            "antecedents, or with the first antecedent, in argument order, among those observed complete. The overloads " +
            "without a scheduler use the factory's or `TaskScheduler.Default`; `LongRunning`, `AttachedToParent`, and " +
            "schedulers other than `TaskScheduler.Default` and the simulation's own are rejected, as for `ContinueWith`.",
        BuiltInRuleFamily.TaskFactoryFromAsync =>
            "Every `TaskFactory.FromAsync` and `TaskFactory<T>.FromAsync` overload is rejected at the call site: the task " +
            "completes when an `IAsyncResult` does, on a thread of its implementation's choosing, outside Clockwork's " +
            "control. Await a task-returning API instead.",
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
            "coordinator, preserving results, cancellation, and exception aggregation. The overloads whose body takes a " +
            "`ParallelLoopState` (break/stop) or thread-local state (`TLocal`), and the `Partitioner`/" +
            "`OrderablePartitioner` overloads, cannot be modelled deterministically yet and are rejected with a precise " +
            "diagnostic.",
        BuiltInRuleFamily.Monitor =>
            "The complete .NET 11 `Monitor` surface is classified: synchronization and C# `lock (object)` " +
            "lowering are controlled with deterministic virtual-time deadlines, while the process-wide " +
            "`LockContentionCount` metric is rejected because it has no per-simulation meaning.",
        BuiltInRuleFamily.Lock =>
            "The .NET 9+ `System.Threading.Lock` type and nested `Scope` are substituted onto controlled " +
            "equivalents, covering the dedicated C# lock lowering in Debug and Release builds.",
        BuiltInRuleFamily.Semaphore =>
            "The .NET 11 `SemaphoreSlim` constructors, counts, waits, releases, and disposal are controlled; " +
            "`AvailableWaitHandle` returns a controlled manual-reset bridge whose signal tracks whether the " +
            "permit count is positive and which composes with the controlled wait-handle surface.",
        BuiltInRuleFamily.UncontrolledInvocation =>
            "Process control and abrupt-termination APIs (`Process.Start`/`Run`/`RunAsync`/`RunAndCaptureText`/" +
            "`RunAndCaptureTextAsync`/`StartAndForget`, the `Start` instance/`Kill`/`Signal` members, the " +
            "`WaitForExit`/`WaitForExitAsync`/`WaitForExitStatus`/`TryWaitForExitStatus`/`WaitForExitStatusAsync` " +
            "waits, the `ReadAllBytes`/`ReadAllText`/`ReadAllLines` reads (and their async forms) that wait for the " +
            "process to close its output, the `WaitForInputIdle` waits, the `Responding` getter, `CloseMainWindow`, the `EnableRaisingEvents` " +
            "setter and `BeginOutputReadLine`/`BeginErrorReadLine` (which raise events on threads of the BCL's choosing as " +
            "the process exits or writes), the `Process` priority, processor-affinity, and working-set setters and the " +
            "`ProcessThread` scheduling setters, the `SafeProcessHandle` `Start`/`Signal`/`Resume`/`Kill` and wait members, the " +
            "`WindowsProcessStartArguments`/`UnixProcessStartArguments` `Start` launchers, and `Environment.Exit`/" +
            "`FailFast`) cannot be modelled inside a single " +
            "simulated process at all. A throwing guard is injected before each call site so a rewritten assembly can " +
            "never launch, signal, wait on, reschedule, or terminate a real OS process; unlike the controlled shims the " +
            "rejection is unconditional (it fires whether or not a simulation is active).",
        BuiltInRuleFamily.Interlocked =>
            "The full .NET 11 `Interlocked` surface - `Increment`/`Decrement`/`Add` (`int`/`long`/`uint`/`ulong`), " +
            "`And`/`Or` (`int`/`long`/`uint`/`ulong` and the generic integer or enum overload), `Exchange`/" +
            "`CompareExchange` (every primitive, native-int, floating-point, reference, and generic overload), " +
            "`Read` (`long`/`ulong`), and the memory barriers - " +
            "redirects each call site to a shim with the identical `ref`-first signature. Clockwork's " +
            "cooperative single-logical-thread scheduler makes every read-modify-write an indivisible step " +
            "(never split, never interleaved mid-operation), so the shim delegates to the real primitive and " +
            "preserves exact atomic return, overflow, and reference-write semantics under the active " +
            "simulation. The exploration policy injects no mid-operation scheduling point; the single " +
            "delegation site is the race-exploration access-tracking attachment point.",
        BuiltInRuleFamily.Volatile =>
            "The full .NET 11 `Volatile` surface - `Read`/`Write` (every primitive, native-int, " +
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
            "Every public .NET 11 `ReaderWriterLockSlim` constructor, property, enter/try-enter/exit overload, " +
            "and `Dispose` member redirects to receiver-first controlled shims. The real BCL instance is only " +
            "an identity key; logical-strand ownership, recursion, wait queues, and deadlines are modelled " +
            "without blocking a physical thread.",
        BuiltInRuleFamily.ManualResetEventSlim =>
            "Every public .NET 11 `ManualResetEventSlim` constructor, property, set/reset/wait overload, and " +
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

    // ToCanonicalString is the signature-hashing encoding; the inventory shows the readable member form.
    private static string Display(MemberSignature signature) =>
        signature.MemberName is null
            ? signature.DeclaringTypeFullName
            : signature.DeclaringTypeFullName + "::" + signature.MemberName + ParamSuffix(signature.ParameterTypeFullNames);

    private static string Display(RewriteReplacement replacement) =>
        replacement.AssemblyName + "!" + (replacement.MemberName is null
            ? replacement.DeclaringTypeFullName
            : replacement.DeclaringTypeFullName + "::" + replacement.MemberName + ParamSuffix(replacement.ParameterTypeFullNames));
}

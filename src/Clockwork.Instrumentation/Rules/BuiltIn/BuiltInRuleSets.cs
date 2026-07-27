using System.Collections.Immutable;
using Clockwork.Runtime.Policy;

namespace Clockwork.Instrumentation.Rules.BuiltIn;

/// <summary>
/// The catalogue of Clockwork's built-in, versioned rewrite rule sets. These ship with the
/// instrumentation package so MSBuild and CLI users can turn on deterministic BCL behaviour without
/// hand-authoring JSON signatures. The only rule set today is
/// <see cref="DeterministicBclId"/> - the first production deterministic BCL shim set - which
/// redirects the direct static time / identity / random surface to the Cecil-free runtime shims in
/// the <c>Clockwork.Runtime</c> assembly (namespace <c>Clockwork.Runtime.Shims</c>).
/// </summary>
/// <remarks>
/// <para>
/// Rules are grouped into <see cref="BuiltInRuleFamily"/> values so a caller can include or exclude a
/// whole family (the granular, safe unit) while the exact per-signature mapping stays fixed and
/// coherent. The rule set's <em>content</em> changes when the selected families change, so its
/// <see cref="RewriteRuleSet.ComputeSignature"/> - and therefore the incremental build key - reflects
/// the selection even though the id and version are stable.
/// </para>
/// <para>
/// The clock, identity, and random families are <see cref="SimulationApiPolicy.Controlled"/>
/// redirections. The crypto family is classified <see cref="SimulationApiPolicy.Rejected"/> - the
/// operation is still a redirect to <c>DeterministicCryptoRandom</c>, but the shim rejects the call by
/// default at runtime and only serves deterministic-insecure bytes under an explicit test-only opt-in.
/// Outside a simulation every shim runs the real BCL API unchanged.
/// </para>
/// </remarks>
public static class BuiltInRuleSets
{
    /// <summary>The stable id of the first production deterministic BCL rule set.</summary>
    public const string DeterministicBclId = "clockwork.bcl.deterministic";

    /// <summary>The version of the deterministic BCL rule set.</summary>
    public const string DeterministicBclVersion = "1.0.0";

    /// <summary>The stable id of the controlled task / async machinery rule set (Phase 6A).</summary>
    public const string ControlledTasksId = "clockwork.tasks.controlled";

    /// <summary>The version of the controlled task rule set.</summary>
    public const string ControlledTasksVersion = "1.0.0";

    /// <summary>The simple name of the assembly declaring every built-in shim.</summary>
    public const string ShimAssemblyName = "Clockwork.Runtime";

    private const string ClockShim = "Clockwork.Runtime.Shims.DeterministicClock";
    private const string GuidShim = "Clockwork.Runtime.Shims.DeterministicGuid";
    private const string RandomShim = "Clockwork.Runtime.Shims.DeterministicRandom";
    private const string CryptoShim = "Clockwork.Runtime.Shims.DeterministicCryptoRandom";
    private const string TaskShim = "Clockwork.Runtime.Tasks.ControlledTask";
    private const string TaskFactoryShim = "Clockwork.Runtime.Tasks.ControlledTaskFactory";

    // Cecil full names for the exact overload parameters (from the net10 reference assemblies).
    private const string Int32 = "System.Int32";
    private const string Int64 = "System.Int64";
    private const string String = "System.String";
    private const string Boolean = "System.Boolean";
    private const string DateTimeOffset = "System.DateTimeOffset";
    private const string SpanByte = "System.Span`1<System.Byte>";
    private const string SpanChar = "System.Span`1<System.Char>";
    private const string ReadOnlySpanChar = "System.ReadOnlySpan`1<System.Char>";

    // Cecil full names for the controlled-task overload parameters.
    private const string Task = "System.Threading.Tasks.Task";
    private const string TaskArray = "System.Threading.Tasks.Task[]";
    private const string IEnumerableTask = "System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>";
    private const string ReadOnlySpanTask = "System.ReadOnlySpan`1<System.Threading.Tasks.Task>";
    private const string ActionOfTask = "System.Action`1<System.Threading.Tasks.Task>";
    private const string Action = "System.Action";

    // Cecil full names for the generic Task<TResult> combinator overload parameters. At a call site the
    // C# compiler emits these as a GenericInstanceMethod whose element parameters reference the method
    // generic parameter, which Cecil renders positionally as `!!0` - so the *target* signatures use the
    // `!!0` form. The controlled shim is matched against its *definition*, where Cecil renders the same
    // generic parameter by name (`TResult`), so the *replacement* signatures use the `TResult` form.
    private const string TaskT = "System.Threading.Tasks.Task`1<!!0>";
    private const string TaskTArray = "System.Threading.Tasks.Task`1<!!0>[]";
    private const string IEnumerableTaskT = "System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<!!0>>";
    private const string ReadOnlySpanTaskT = "System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<!!0>>";
    private const string TaskTDecl = "System.Threading.Tasks.Task`1<TResult>";
    private const string TaskTArrayDecl = "System.Threading.Tasks.Task`1<TResult>[]";
    private const string IEnumerableTaskTDecl = "System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<TResult>>";
    private const string ReadOnlySpanTaskTDecl = "System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<TResult>>";

    // Cecil full names for the TaskExtensions.Unwrap(this Task<Task>) / Unwrap<TResult>(this Task<Task<TResult>>)
    // extension methods. Unwrap is a static extension whose receiver is the first parameter, so the shim is a
    // direct static-to-static redirect. The generic overload uses the `!!0` target / `TResult` replacement split.
    private const string TaskExtensionsType = "System.Threading.Tasks.TaskExtensions";
    private const string TaskOfTask = "System.Threading.Tasks.Task`1<System.Threading.Tasks.Task>";
    private const string TaskOfTaskT = "System.Threading.Tasks.Task`1<System.Threading.Tasks.Task`1<!!0>>";
    private const string TaskOfTaskTDecl = "System.Threading.Tasks.Task`1<System.Threading.Tasks.Task`1<TResult>>";

    // Cecil full names for the TaskFactory scheduling surface (rejected under simulation). Task.Factory
    // is the non-generic TaskFactory; its StartNew(Func<TResult>) is a generic method (`!!0` target,
    // `TResult` replacement), whereas TaskFactory`1's StartNew(Func<TResult>) is a non-generic method
    // over the type parameter (`!0` target).
    private const string TaskFactoryType = "System.Threading.Tasks.TaskFactory";
    private const string TaskFactoryTType = "System.Threading.Tasks.TaskFactory`1";
    private const string TaskFactoryTOfResultDecl = "System.Threading.Tasks.TaskFactory`1<TResult>";
    private const string FuncOfMethodResult = "System.Func`1<!!0>";
    private const string FuncOfTypeResult = "System.Func`1<!0>";
    private const string FuncOfResultDecl = "System.Func`1<TResult>";

    // Cecil full names for the compiler-generated async machinery (BCL) and their controlled substitutes.
    // Nested awaiter types use Cecil's '/' separator; generic arities carry the backtick.
    private const string CompilerNs = "System.Runtime.CompilerServices.";
    private const string ControlledNs = "Clockwork.Runtime.Tasks.CompilerServices.";
    private const string BclBuilder = CompilerNs + "AsyncTaskMethodBuilder";
    private const string BclBuilderT = CompilerNs + "AsyncTaskMethodBuilder`1";
    private const string BclTaskAwaiter = CompilerNs + "TaskAwaiter";
    private const string BclTaskAwaiterT = CompilerNs + "TaskAwaiter`1";
    private const string BclConfigured = CompilerNs + "ConfiguredTaskAwaitable";
    private const string BclConfiguredT = CompilerNs + "ConfiguredTaskAwaitable`1";
    private const string BclConfiguredAwaiter = CompilerNs + "ConfiguredTaskAwaitable/ConfiguredTaskAwaiter";
    private const string BclConfiguredAwaiterT = CompilerNs + "ConfiguredTaskAwaitable`1/ConfiguredTaskAwaiter";
    private const string BclYieldAwaitable = CompilerNs + "YieldAwaitable";
    private const string BclYieldAwaiter = CompilerNs + "YieldAwaitable/YieldAwaiter";
    private const string BclValueBuilder = CompilerNs + "AsyncValueTaskMethodBuilder";
    private const string BclValueBuilderT = CompilerNs + "AsyncValueTaskMethodBuilder`1";
    private const string BclValueAwaiter = CompilerNs + "ValueTaskAwaiter";
    private const string BclValueAwaiterT = CompilerNs + "ValueTaskAwaiter`1";
    private const string BclConfiguredValue = CompilerNs + "ConfiguredValueTaskAwaitable";
    private const string BclConfiguredValueT = CompilerNs + "ConfiguredValueTaskAwaitable`1";
    private const string BclConfiguredValueAwaiter = CompilerNs + "ConfiguredValueTaskAwaitable/ConfiguredValueTaskAwaiter";
    private const string BclConfiguredValueAwaiterT = CompilerNs + "ConfiguredValueTaskAwaitable`1/ConfiguredValueTaskAwaiter";
    private const string ControlledBuilder = ControlledNs + "ControlledAsyncTaskMethodBuilder";
    private const string ControlledBuilderT = ControlledNs + "ControlledAsyncTaskMethodBuilder`1";
    private const string ControlledTaskAwaiter = ControlledNs + "ControlledTaskAwaiter";
    private const string ControlledTaskAwaiterT = ControlledNs + "ControlledTaskAwaiter`1";
    private const string ControlledConfigured = ControlledNs + "ControlledConfiguredTaskAwaitable";
    private const string ControlledConfiguredT = ControlledNs + "ControlledConfiguredTaskAwaitable`1";
    private const string ControlledConfiguredAwaiter = ControlledNs + "ControlledConfiguredTaskAwaiter";
    private const string ControlledConfiguredAwaiterT = ControlledNs + "ControlledConfiguredTaskAwaiter`1";
    private const string ControlledYieldAwaitable = ControlledNs + "ControlledYieldAwaitable";
    private const string ControlledYieldAwaiter = ControlledNs + "ControlledYieldAwaiter";
    private const string ControlledValueBuilder = ControlledNs + "ControlledAsyncValueTaskMethodBuilder";
    private const string ControlledValueBuilderT = ControlledNs + "ControlledAsyncValueTaskMethodBuilder`1";
    private const string ControlledValueAwaiter = ControlledNs + "ControlledValueTaskAwaiter";
    private const string ControlledValueAwaiterT = ControlledNs + "ControlledValueTaskAwaiter`1";
    private const string ControlledConfiguredValue = ControlledNs + "ControlledConfiguredValueTaskAwaitable";
    private const string ControlledConfiguredValueT = ControlledNs + "ControlledConfiguredValueTaskAwaitable`1";
    private const string ControlledConfiguredValueAwaiter = ControlledNs + "ControlledConfiguredValueTaskAwaiter";
    private const string ControlledConfiguredValueAwaiterT = ControlledNs + "ControlledConfiguredValueTaskAwaiter`1";

    private static readonly ImmutableArray<BuiltInRuleEntry> DeterministicBcl = BuildDeterministicBclEntries();
    private static readonly ImmutableArray<BuiltInRuleEntry> ControlledTasks = BuildControlledTasksEntries();

    /// <summary>Gets the ids of every built-in rule set that can be enabled by name.</summary>
    public static ImmutableArray<string> AvailableIds { get; } = [DeterministicBclId, ControlledTasksId];

    /// <summary>Gets every rule family in canonical (declared) order.</summary>
    public static ImmutableArray<BuiltInRuleFamily> AllFamilies { get; } =
    [
        BuiltInRuleFamily.Clock,
        BuiltInRuleFamily.Identity,
        BuiltInRuleFamily.Random,
        BuiltInRuleFamily.Crypto,
        BuiltInRuleFamily.TaskCombinators,
        BuiltInRuleFamily.TaskSynchronization,
        BuiltInRuleFamily.TaskContinuations,
        BuiltInRuleFamily.TaskDeferred,
        BuiltInRuleFamily.AsyncMachinery,
        BuiltInRuleFamily.ValueTaskMachinery,
        BuiltInRuleFamily.TaskFactory,
    ];

    /// <summary>Gets the (family, rule) entries of the deterministic BCL rule set, for documentation and inventory generation.</summary>
    public static ImmutableArray<(BuiltInRuleFamily Family, RewriteRule Rule)> DeterministicBclInventory { get; } =
        [.. DeterministicBcl.Select(e => (e.Family, e.Rule))];

    /// <summary>Gets the (family, rule) entries of the controlled task rule set, for documentation and inventory generation.</summary>
    public static ImmutableArray<(BuiltInRuleFamily Family, RewriteRule Rule)> ControlledTasksInventory { get; } =
        [.. ControlledTasks.Select(e => (e.Family, e.Rule))];

    /// <summary>Gets a value indicating whether <paramref name="id"/> names a known built-in rule set.</summary>
    public static bool IsKnownId(string id) => AvailableIds.Contains(id, StringComparer.Ordinal);

    /// <summary>Parses a case-insensitive family name (e.g. <c>Clock</c>, <c>crypto</c>).</summary>
    public static bool TryParseFamily(string text, out BuiltInRuleFamily family) =>
        Enum.TryParse(text, ignoreCase: true, out family) && Enum.IsDefined(family);

    /// <summary>
    /// Builds the deterministic BCL rule set restricted to <paramref name="families"/>, preserving the
    /// canonical family and rule order so the result is stable across runs.
    /// </summary>
    /// <param name="families">The families to include. An empty set produces an empty rule set.</param>
    /// <returns>The versioned rule set.</returns>
    public static RewriteRuleSet BuildDeterministicBcl(IEnumerable<BuiltInRuleFamily> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        var selected = new HashSet<BuiltInRuleFamily>(families);
        IEnumerable<RewriteRule> rules = DeterministicBcl
            .Where(e => selected.Contains(e.Family))
            .Select(e => e.Rule);
        return new RewriteRuleSet(DeterministicBclId, DeterministicBclVersion, rules);
    }

    /// <summary>
    /// Builds the controlled task rule set restricted to <paramref name="families"/>, preserving the
    /// canonical family and rule order so the result is stable across runs.
    /// </summary>
    /// <param name="families">The families to include. An empty set produces an empty rule set.</param>
    /// <returns>The versioned rule set.</returns>
    public static RewriteRuleSet BuildControlledTasks(IEnumerable<BuiltInRuleFamily> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        var selected = new HashSet<BuiltInRuleFamily>(families);
        IEnumerable<RewriteRule> rules = ControlledTasks
            .Where(e => selected.Contains(e.Family))
            .Select(e => e.Rule);
        return new RewriteRuleSet(ControlledTasksId, ControlledTasksVersion, rules);
    }

    private static ImmutableArray<BuiltInRuleEntry> BuildControlledTasksEntries()
    {
        var builder = ImmutableArray.CreateBuilder<BuiltInRuleEntry>();

        // ---- Combinators: WhenAll / WhenAny non-generic overloads -> controlled equivalents ----
        // Completion order becomes a deterministic function of logical-thread completion instead of the
        // physical thread-pool race. The generic Task<TResult> overloads and Result<T> accessor below are
        // bound through the call-site pass's generic-arity substitution (method and declaring-type args).
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenall.array",
            MemberSignature.Method(Task, "WhenAll", TaskArray), Shim(TaskShim, "WhenAll", TaskArray));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenall.span",
            MemberSignature.Method(Task, "WhenAll", ReadOnlySpanTask), Shim(TaskShim, "WhenAll", ReadOnlySpanTask));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenall.enumerable",
            MemberSignature.Method(Task, "WhenAll", IEnumerableTask), Shim(TaskShim, "WhenAll", IEnumerableTask));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenany.array",
            MemberSignature.Method(Task, "WhenAny", TaskArray), Shim(TaskShim, "WhenAny", TaskArray));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenany.span",
            MemberSignature.Method(Task, "WhenAny", ReadOnlySpanTask), Shim(TaskShim, "WhenAny", ReadOnlySpanTask));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenany.pair",
            MemberSignature.Method(Task, "WhenAny", Task, Task), Shim(TaskShim, "WhenAny", Task, Task));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenany.enumerable",
            MemberSignature.Method(Task, "WhenAny", IEnumerableTask), Shim(TaskShim, "WhenAny", IEnumerableTask));

        // ---- Generic combinators: WhenAll<TResult> / WhenAny<TResult> overloads -> controlled equivalents.
        // Each call site is a GenericInstanceMethod, so the call-site pass carries its TResult argument onto
        // the controlled generic shim; the parameter signatures use Cecil's positional `!!0` rendering.
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenall.generic.array",
            MemberSignature.Method(Task, "WhenAll", TaskTArray), Shim(TaskShim, "WhenAll", TaskTArrayDecl));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenall.generic.span",
            MemberSignature.Method(Task, "WhenAll", ReadOnlySpanTaskT), Shim(TaskShim, "WhenAll", ReadOnlySpanTaskTDecl));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenall.generic.enumerable",
            MemberSignature.Method(Task, "WhenAll", IEnumerableTaskT), Shim(TaskShim, "WhenAll", IEnumerableTaskTDecl));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenany.generic.array",
            MemberSignature.Method(Task, "WhenAny", TaskTArray), Shim(TaskShim, "WhenAny", TaskTArrayDecl));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenany.generic.span",
            MemberSignature.Method(Task, "WhenAny", ReadOnlySpanTaskT), Shim(TaskShim, "WhenAny", ReadOnlySpanTaskTDecl));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenany.generic.pair",
            MemberSignature.Method(Task, "WhenAny", TaskT, TaskT), Shim(TaskShim, "WhenAny", TaskTDecl, TaskTDecl));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.whenany.generic.enumerable",
            MemberSignature.Method(Task, "WhenAny", IEnumerableTaskT), Shim(TaskShim, "WhenAny", IEnumerableTaskTDecl));

        // ---- Task extension methods: TaskExtensions.Unwrap -> controlled equivalents. Unwrap is a static
        // extension whose receiver is the first parameter, so this is a direct static-to-static redirect; the
        // unwrapped proxy completes on the logical thread, so delegating to the real API stays deterministic.
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.unwrap",
            MemberSignature.Method(TaskExtensionsType, "Unwrap", TaskOfTask), Shim(TaskShim, "Unwrap", TaskOfTask));
        TaskRule(builder, BuiltInRuleFamily.TaskCombinators, "clockwork.tasks.unwrap.generic",
            MemberSignature.Method(TaskExtensionsType, "Unwrap", TaskOfTaskT), Shim(TaskShim, "Unwrap", TaskOfTaskTDecl));

        // ---- Synchronization: blocking waits -> controlled waits that pump the deterministic loop ----
        // The receiver of the instance Wait() is already on the IL stack, so a redirect to the static
        // shim taking the antecedent as its first parameter is stack-balanced.
        TaskRule(builder, BuiltInRuleFamily.TaskSynchronization, "clockwork.tasks.wait.instance",
            MemberSignature.Method(Task, "Wait"), Shim(TaskShim, "Wait", Task));
        TaskRule(builder, BuiltInRuleFamily.TaskSynchronization, "clockwork.tasks.waitall.array",
            MemberSignature.Method(Task, "WaitAll", TaskArray), Shim(TaskShim, "WaitAll", TaskArray));
        TaskRule(builder, BuiltInRuleFamily.TaskSynchronization, "clockwork.tasks.waitany.array",
            MemberSignature.Method(Task, "WaitAny", TaskArray), Shim(TaskShim, "WaitAny", TaskArray));

        // Blocking Task<TResult>.Result getter -> controlled drain. The getter is a non-generic member on
        // the closed generic Task`1<T>, so the call-site pass binds the shim's TResult from the receiver's
        // declaring type; the drain pumps the loop until the task completes instead of blocking a thread.
        TaskRule(builder, BuiltInRuleFamily.TaskSynchronization, "clockwork.tasks.result.generic",
            MemberSignature.Method("System.Threading.Tasks.Task`1", "get_Result"), Shim(TaskShim, "Result", TaskTDecl));

        // ---- Continuations: ContinueWith -> controlled scheduling on the logical thread ----
        TaskRule(builder, BuiltInRuleFamily.TaskContinuations, "clockwork.tasks.continuewith.action",
            MemberSignature.Method(Task, "ContinueWith", ActionOfTask), Shim(TaskShim, "ContinueWith", Task, ActionOfTask));

        // ---- Deferred: Task.Delay (Phase 8 timers) and Task.Run (Phase 6B thread-pool) rejected ----
        // The shim rejects under simulation with a precise diagnostic and runs the real BCL API outside.
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.TaskDeferred, RewriteRule.RedirectCall(
            "clockwork.tasks.delay.milliseconds",
            MemberSignature.Method(Task, "Delay", Int32),
            Shim(TaskShim, "Delay", Int32),
            SimulationApiPolicy.Rejected)));
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.TaskDeferred, RewriteRule.RedirectCall(
            "clockwork.tasks.run.action",
            MemberSignature.Method(Task, "Run", Action),
            Shim(TaskShim, "Run", Action),
            SimulationApiPolicy.Rejected)));

        // ---- Async machinery: retarget the compiler-generated builder/awaiter types of an async state
        // machine onto controlled equivalents (member-aware SubstituteType), plus the Task.Yield redirect.
        Sub(builder, "clockwork.tasks.builder.task", BclBuilder, ControlledBuilder);
        Sub(builder, "clockwork.tasks.builder.task.generic", BclBuilderT, ControlledBuilderT);
        Sub(builder, "clockwork.tasks.awaiter.task", BclTaskAwaiter, ControlledTaskAwaiter);
        Sub(builder, "clockwork.tasks.awaiter.task.generic", BclTaskAwaiterT, ControlledTaskAwaiterT);
        Sub(builder, "clockwork.tasks.configured.awaitable", BclConfigured, ControlledConfigured);
        Sub(builder, "clockwork.tasks.configured.awaitable.generic", BclConfiguredT, ControlledConfiguredT);
        Sub(builder, "clockwork.tasks.configured.awaiter", BclConfiguredAwaiter, ControlledConfiguredAwaiter);
        Sub(builder, "clockwork.tasks.configured.awaiter.generic", BclConfiguredAwaiterT, ControlledConfiguredAwaiterT);
        Sub(builder, "clockwork.tasks.yield.awaitable", BclYieldAwaitable, ControlledYieldAwaitable);
        Sub(builder, "clockwork.tasks.yield.awaiter", BclYieldAwaiter, ControlledYieldAwaiter);
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.AsyncMachinery, RewriteRule.RedirectCall(
            "clockwork.tasks.yield.call",
            MemberSignature.Method(Task, "Yield"),
            Shim(TaskShim, "Yield"))));

        // ---- ValueTask machinery: retarget the compiler-generated builder/awaiter types of an
        // async ValueTask / async ValueTask<T> state machine onto controlled equivalents. The awaiter
        // source calls (ValueTask.GetAwaiter / ValueTask.ConfigureAwait) are recognised by the
        // member-aware pass, which is why no explicit call redirect is needed for them.
        Sub(builder, BuiltInRuleFamily.ValueTaskMachinery, "clockwork.tasks.builder.valuetask", BclValueBuilder, ControlledValueBuilder);
        Sub(builder, BuiltInRuleFamily.ValueTaskMachinery, "clockwork.tasks.builder.valuetask.generic", BclValueBuilderT, ControlledValueBuilderT);
        Sub(builder, BuiltInRuleFamily.ValueTaskMachinery, "clockwork.tasks.awaiter.valuetask", BclValueAwaiter, ControlledValueAwaiter);
        Sub(builder, BuiltInRuleFamily.ValueTaskMachinery, "clockwork.tasks.awaiter.valuetask.generic", BclValueAwaiterT, ControlledValueAwaiterT);
        Sub(builder, BuiltInRuleFamily.ValueTaskMachinery, "clockwork.tasks.configured.valuetask.awaitable", BclConfiguredValue, ControlledConfiguredValue);
        Sub(builder, BuiltInRuleFamily.ValueTaskMachinery, "clockwork.tasks.configured.valuetask.awaitable.generic", BclConfiguredValueT, ControlledConfiguredValueT);
        Sub(builder, BuiltInRuleFamily.ValueTaskMachinery, "clockwork.tasks.configured.valuetask.awaiter", BclConfiguredValueAwaiter, ControlledConfiguredValueAwaiter);
        Sub(builder, BuiltInRuleFamily.ValueTaskMachinery, "clockwork.tasks.configured.valuetask.awaiter.generic", BclConfiguredValueAwaiterT, ControlledConfiguredValueAwaiterT);

        // ---- TaskFactory scheduling: StartNew offloads onto a task scheduler (the thread pool by
        // default). Rejected under simulation - the shim throws a precise diagnostic and runs the real
        // BCL API outside. Task.Factory.StartNew(Func<TResult>) is a generic method (`!!0`); the generic
        // TaskFactory`1's StartNew(Func<TResult>) is a non-generic method over the type parameter (`!0`).
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.TaskFactory, RewriteRule.RedirectCall(
            "clockwork.tasks.factory.startnew.action",
            MemberSignature.Method(TaskFactoryType, "StartNew", Action),
            Shim(TaskFactoryShim, "StartNew", TaskFactoryType, Action),
            SimulationApiPolicy.Rejected)));
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.TaskFactory, RewriteRule.RedirectCall(
            "clockwork.tasks.factory.startnew.func",
            MemberSignature.Method(TaskFactoryType, "StartNew", FuncOfMethodResult),
            Shim(TaskFactoryShim, "StartNew", TaskFactoryType, FuncOfResultDecl),
            SimulationApiPolicy.Rejected)));
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.TaskFactory, RewriteRule.RedirectCall(
            "clockwork.tasks.factory.generic.startnew.func",
            MemberSignature.Method(TaskFactoryTType, "StartNew", FuncOfTypeResult),
            Shim(TaskFactoryShim, "StartNew", TaskFactoryTOfResultDecl, FuncOfResultDecl),
            SimulationApiPolicy.Rejected)));

        return builder.ToImmutable();
    }

    private static void Sub(
        ImmutableArray<BuiltInRuleEntry>.Builder builder,
        string id,
        string targetTypeFullName,
        string controlledTypeFullName) =>
        Sub(builder, BuiltInRuleFamily.AsyncMachinery, id, targetTypeFullName, controlledTypeFullName);

    private static void Sub(
        ImmutableArray<BuiltInRuleEntry>.Builder builder,
        BuiltInRuleFamily family,
        string id,
        string targetTypeFullName,
        string controlledTypeFullName)
    {
        builder.Add(new BuiltInRuleEntry(family, RewriteRule.SubstituteType(
            id, targetTypeFullName, RewriteReplacement.Type(ShimAssemblyName, controlledTypeFullName))));
    }

    private static void TaskRule(
        ImmutableArray<BuiltInRuleEntry>.Builder builder,
        BuiltInRuleFamily family,
        string id,
        MemberSignature target,
        RewriteReplacement replacement)
    {
        builder.Add(new BuiltInRuleEntry(family, RewriteRule.RedirectCall(id, target, replacement)));
    }

    private static ImmutableArray<BuiltInRuleEntry> BuildDeterministicBclEntries()
    {
        var builder = ImmutableArray.CreateBuilder<BuiltInRuleEntry>();

        // ---- Clock family: wall-clock, offset clock, monotonic timestamp, tick counters ----
        Clock(builder, "clockwork.bcl.datetime.now", "System.DateTime", "get_Now", "GetNow");
        Clock(builder, "clockwork.bcl.datetime.utcnow", "System.DateTime", "get_UtcNow", "GetUtcNow");
        Clock(builder, "clockwork.bcl.datetime.today", "System.DateTime", "get_Today", "GetToday");
        Clock(builder, "clockwork.bcl.datetimeoffset.now", "System.DateTimeOffset", "get_Now", "GetOffsetNow");
        Clock(builder, "clockwork.bcl.datetimeoffset.utcnow", "System.DateTimeOffset", "get_UtcNow", "GetOffsetUtcNow");
        Clock(builder, "clockwork.bcl.stopwatch.gettimestamp", "System.Diagnostics.Stopwatch", "GetTimestamp", "GetTimestamp");
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.Clock, RewriteRule.RedirectCall(
            "clockwork.bcl.stopwatch.getelapsedtime",
            MemberSignature.Method("System.Diagnostics.Stopwatch", "GetElapsedTime", Int64),
            Shim(ClockShim, "GetElapsedTime", Int64))));
        Clock(builder, "clockwork.bcl.environment.tickcount", "System.Environment", "get_TickCount", "GetTickCount");
        Clock(builder, "clockwork.bcl.environment.tickcount64", "System.Environment", "get_TickCount64", "GetTickCount64");

        // ---- Guid family: deterministic identity bytes with preserved RFC variant/version ----
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.Identity, RewriteRule.RedirectCall(
            "clockwork.bcl.guid.newguid",
            MemberSignature.Method("System.Guid", "NewGuid"),
            Shim(GuidShim, "NewGuid"))));
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.Identity, RewriteRule.RedirectCall(
            "clockwork.bcl.guid.createversion7",
            MemberSignature.Method("System.Guid", "CreateVersion7"),
            Shim(GuidShim, "CreateVersion7"))));
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.Identity, RewriteRule.RedirectCall(
            "clockwork.bcl.guid.createversion7.timestamp",
            MemberSignature.Method("System.Guid", "CreateVersion7", DateTimeOffset),
            Shim(GuidShim, "CreateVersion7", DateTimeOffset))));

        // ---- Random family: shared instance and both constructors ----
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.Random, RewriteRule.RedirectCall(
            "clockwork.bcl.random.shared",
            MemberSignature.Method("System.Random", "get_Shared"),
            Shim(RandomShim, "GetShared"))));
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.Random, RewriteRule.RedirectNewObj(
            "clockwork.bcl.random.ctor.unseeded",
            MemberSignature.Constructor("System.Random"),
            Shim(RandomShim, "CreateUnseeded"))));
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.Random, RewriteRule.RedirectNewObj(
            "clockwork.bcl.random.ctor.seeded",
            MemberSignature.Constructor("System.Random", Int32),
            Shim(RandomShim, "CreateSeeded", Int32))));

        // ---- Crypto family: rejected-by-default policy shims for OS-entropy statics ----
        Crypto(builder, "clockwork.bcl.rng.create", "Create", Shim(CryptoShim, "Create"),
            MemberSignature.Method("System.Security.Cryptography.RandomNumberGenerator", "Create"));
        Crypto(builder, "clockwork.bcl.rng.create.named", "Create", Shim(CryptoShim, "Create", String),
            MemberSignature.Method("System.Security.Cryptography.RandomNumberGenerator", "Create", String));
        Crypto(builder, "clockwork.bcl.rng.fill", "Fill", Shim(CryptoShim, "Fill", SpanByte),
            MemberSignature.Method("System.Security.Cryptography.RandomNumberGenerator", "Fill", SpanByte));
        Crypto(builder, "clockwork.bcl.rng.getbytes.count", "GetBytes", Shim(CryptoShim, "GetBytes", Int32),
            MemberSignature.Method("System.Security.Cryptography.RandomNumberGenerator", "GetBytes", Int32));
        Crypto(builder, "clockwork.bcl.rng.getint32.exclusive", "GetInt32", Shim(CryptoShim, "GetInt32", Int32),
            MemberSignature.Method("System.Security.Cryptography.RandomNumberGenerator", "GetInt32", Int32));
        Crypto(builder, "clockwork.bcl.rng.getint32.range", "GetInt32", Shim(CryptoShim, "GetInt32", Int32, Int32),
            MemberSignature.Method("System.Security.Cryptography.RandomNumberGenerator", "GetInt32", Int32, Int32));
        Crypto(builder, "clockwork.bcl.rng.gethexstring.span", "GetHexString", Shim(CryptoShim, "GetHexString", SpanChar, Boolean),
            MemberSignature.Method("System.Security.Cryptography.RandomNumberGenerator", "GetHexString", SpanChar, Boolean));
        Crypto(builder, "clockwork.bcl.rng.gethexstring.length", "GetHexString", Shim(CryptoShim, "GetHexString", Int32, Boolean),
            MemberSignature.Method("System.Security.Cryptography.RandomNumberGenerator", "GetHexString", Int32, Boolean));
        Crypto(builder, "clockwork.bcl.rng.getstring", "GetString", Shim(CryptoShim, "GetString", ReadOnlySpanChar, Int32),
            MemberSignature.Method("System.Security.Cryptography.RandomNumberGenerator", "GetString", ReadOnlySpanChar, Int32));

        return builder.ToImmutable();
    }

    private static void Clock(
        ImmutableArray<BuiltInRuleEntry>.Builder builder,
        string id,
        string declaringType,
        string member,
        string shimMember)
    {
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.Clock, RewriteRule.RedirectCall(
            id,
            MemberSignature.Method(declaringType, member),
            Shim(ClockShim, shimMember))));
    }

    private static void Crypto(
        ImmutableArray<BuiltInRuleEntry>.Builder builder,
        string id,
        string member,
        RewriteReplacement replacement,
        MemberSignature target)
    {
        _ = member;
        builder.Add(new BuiltInRuleEntry(BuiltInRuleFamily.Crypto, RewriteRule.RedirectCall(
            id, target, replacement, SimulationApiPolicy.Rejected)));
    }

    // A collection expression always yields a non-default ImmutableArray - even when empty - so the
    // resolver treats the replacement as having an exact parameter constraint and picks the intended
    // overload deterministically (never the metadata-order first match).
    private static RewriteReplacement Shim(string declaringType, string member, params string[] parameterTypes) =>
        new(ShimAssemblyName, declaringType, member, [.. parameterTypes]);

    private readonly record struct BuiltInRuleEntry(BuiltInRuleFamily Family, RewriteRule Rule);
}

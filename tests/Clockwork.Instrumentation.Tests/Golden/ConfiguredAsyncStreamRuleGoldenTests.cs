using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>
/// Golden tests for the configured async disposable and enumerable substitutions, and for the rewrite-time check
/// that fails a rewrite whose substituted member signature cannot bind against its declaring type.
/// </summary>
public sealed class ConfiguredAsyncStreamRuleGoldenTests
{
    private const string Fixture = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Fx
        {
            public static class Streams
            {
                public static async Task Using(IAsyncDisposable resource)
                {
                    await using (resource.ConfigureAwait(false))
                    {
                    }
                }

                public static async Task<int> Sum(IAsyncEnumerable<int> source, CancellationToken cancellationToken)
                {
                    int sum = 0;
                    await foreach (int item in source.ConfigureAwait(false))
                    {
                        sum += item;
                    }

                    await foreach (int item in source.WithCancellation(cancellationToken))
                    {
                        sum += item;
                    }

                    await foreach (int item in source.WithCancellation(cancellationToken).ConfigureAwait(true))
                    {
                        sum += item;
                    }

                    await foreach (int item in source.ConfigureAwait(false).WithCancellation(cancellationToken))
                    {
                        sum += item;
                    }

                    return sum;
                }

                public static async IAsyncEnumerable<int> Produce([EnumeratorCancellation] CancellationToken cancellationToken = default)
                {
                    await Task.Yield();
                    yield return 1;
                }
            }
        }
        """;

    private static readonly string[] ConfiguredRules =
    [
        "clockwork.tasks.configured.asyncdisposable",
        "clockwork.tasks.configured.asyncenumerable",
        "clockwork.tasks.configured.asyncenumerator",
        "clockwork.tasks.asyncdisposable.configureawait",
        "clockwork.tasks.asyncenumerable.configureawait",
        "clockwork.tasks.asyncenumerable.withcancellation",
    ];

    private static string RuntimeAssemblyPath =>
        typeof(ControlledConfiguredAsyncDisposable).Assembly.Location;

    [Fact]
    public void ConfiguredAsyncStreamsAreRetargetedAndEveryReferenceBinds()
    {
        using var context = RewriteTestContext.Create();
        RewriteResult result = Rewrite(context, "Fx.ConfiguredStreams", BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies));
        result.EnsureSuccess();

        using ModuleDefinition module = context.LoadModule(Path.Combine(context.Directory, "Fx.ConfiguredStreams.rewritten.dll"));
        MemberReference[] members = ReferencedMembers(module);
        string[] targets = [.. members.Select(member => member.FullName)];

        Assert.DoesNotContain(targets, target => target.Contains("System.Runtime.CompilerServices.Configured", StringComparison.Ordinal));
        Assert.DoesNotContain(targets, target => target.Contains("System.Threading.Tasks.TaskAsyncEnumerableExtensions", StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Contains(
            "ControlledTaskAsyncEnumerableExtensions::ConfigureAwait(System.IAsyncDisposable,System.Boolean)", StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Contains(
            "ControlledTaskAsyncEnumerableExtensions::ConfigureAwait<System.Int32>(", StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Contains(
            "ControlledTaskAsyncEnumerableExtensions::WithCancellation<System.Int32>(", StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Contains(
            "ControlledConfiguredAsyncDisposable::DisposeAsync()", StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Contains(
            "ControlledConfiguredCancelableAsyncEnumerable`1<System.Int32>::ConfigureAwait(System.Boolean)", StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Contains(
            "ControlledConfiguredCancelableAsyncEnumerable`1<System.Int32>::WithCancellation(System.Threading.CancellationToken)", StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Contains(
            "ControlledConfiguredCancelableAsyncEnumerable`1/Enumerator<System.Int32>::MoveNextAsync()", StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Contains(
            "ControlledConfiguredCancelableAsyncEnumerable`1/Enumerator<System.Int32>::DisposeAsync()", StringComparison.Ordinal));
        Assert.All(members, member => Assert.True(
            member.Resolve() is not null,
            $"Rewritten reference '{member.FullName}' does not bind."));

        string[] applied = [.. result.Manifest.Transformations.Select(transformation => transformation.RuleId).Distinct(StringComparer.Ordinal)];
        Assert.All(ConfiguredRules, rule => Assert.Contains(rule, applied));
    }

    [Fact]
    public void SubstitutionWhoseFrameworkMemberCannotBindFailsTheRewrite()
    {
        // The configured value-task awaitables without the configured disposable and enumerable is the rule set
        // that produced ConfiguredAsyncDisposable.DisposeAsync() returning a controlled awaitable.
        RewriteRuleSet broken = new(
            "clockwork.tasks.controlled.broken",
            "1.0",
            BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies).Rules
                .Where(rule => !ConfiguredRules.Contains(rule.Id, StringComparer.Ordinal)));
        using var context = RewriteTestContext.Create();

        RewriteResult result = Rewrite(context, "Fx.BrokenStreams", broken);

        Assert.False(result.Succeeded);
        Assert.False(result.WasWritten);
        AssertUnbound(result, "System.Runtime.CompilerServices.ConfiguredAsyncDisposable::DisposeAsync()");
        AssertUnbound(result, "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1/Enumerator<System.Int32>::MoveNextAsync()");
        AssertUnbound(result, "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1/Enumerator<System.Int32>::DisposeAsync()");
    }

    public static TheoryData<string, string, string?> BindingCases => new()
    {
        // A sibling assembly is rewritten with the same rule set in a closure, so its signature is substituted too.
        { "Sibling", "public static string Call(LegacyMarker marker) => MarkerSink.Describe(marker);", null },

        // A replacement assembly is never rewritten, so the substituted signature does not exist there.
        { "Replacement", "public static string Call(LegacyMarker marker) => MarkerShim.Describe(marker);", "MarkerShim::Describe" },
        { "ReplacementGeneric", "public static LegacyMarker Call(LegacyMarker marker) => MarkerShim.Make(marker);", "MarkerShim::Make" },

        // A type excluded from rewriting keeps its original signature.
        { "Excluded", "public static string Call(LegacyMarker marker) => Excluded.Describe(marker);", "Fx.Excluded::Describe" },
    };

    [Theory]
    [MemberData(nameof(BindingCases))]
    public void SubstitutedSignatureBindsOnlyWhereItsDeclaringTypeIsRewritten(string name, string call, string? unbound)
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture($"Fx.Binding{name}", $$"""
            using ClockworkFixtures.Api;
            using ClockworkFixtures.Shims;

            namespace Fx
            {
                public static class Excluded
                {
                    public static string Describe(LegacyMarker marker) => marker.ToString();
                }

                public static class Caller
                {
                    {{call}}
                }
            }
            """);
        var ruleSet = new RewriteRuleSet(
            "clockwork.fixtures.binding",
            "1.0",
            [
                RewriteRule.SubstituteType(
                    "substitute-marker",
                    "ClockworkFixtures.Api.LegacyMarker",
                    RewriteReplacement.Type(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.ModernMarker")),
            ]);
        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = [context.ShimPath],
            ReferenceSearchDirectories = [context.Directory],
            ExcludedTypeFullNames = ["Fx.Excluded"],
        };

        RewriteResult result = context.Rewrite(fixturePath, ruleSet, options);

        if (unbound is null)
        {
            result.EnsureSuccess();
            using ModuleDefinition module = context.LoadModule(Path.Combine(context.Directory, $"Fx.Binding{name}.rewritten.dll"));
            Assert.Contains(
                CecilInspect.CallTargets(CecilInspect.GetMethod(module, "Fx.Caller", "Call")),
                target => target.Contains("MarkerSink::Describe(ClockworkFixtures.Shims.ModernMarker)", StringComparison.Ordinal));
            return;
        }

        Assert.False(result.Succeeded);
        RewriteDiagnostic error = Assert.Single(result.Errors, diagnostic => diagnostic.Message.Contains(unbound, StringComparison.Ordinal));
        Assert.Equal(RewriteDiagnosticIds.UnresolvedReplacement, error.Id);
        Assert.Equal("Fx.Caller.Call", error.Method);
        Assert.Contains("substitute-marker", error.Message, StringComparison.Ordinal);
    }

    private static RewriteResult Rewrite(RewriteTestContext context, string assemblyName, RewriteRuleSet ruleSet)
    {
        string fixturePath = context.CompileFixture(assemblyName, Fixture);
        string outputPath = Path.Combine(context.Directory, assemblyName + ".rewritten.dll");
        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = [RuntimeAssemblyPath],
            ReferenceSearchDirectories = [context.Directory, Path.GetDirectoryName(RuntimeAssemblyPath)!],
        };

        return RewriteEngine.Rewrite(new RewriteRequest(fixturePath, outputPath, ruleSet, options));
    }

    private static void AssertUnbound(RewriteResult result, string member) =>
        Assert.Contains(
            result.Errors,
            diagnostic => diagnostic.Id == RewriteDiagnosticIds.UnresolvedReplacement
                && diagnostic.Message.Contains("'clockwork.tasks.configured.valuetask.awaitable", StringComparison.Ordinal)
                && diagnostic.Message.Contains(member, StringComparison.Ordinal));

    private static MemberReference[] ReferencedMembers(ModuleDefinition module) =>
        [
            .. module.GetTypes()
                .SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Select(instruction => instruction.Operand)
                .OfType<MemberReference>()
                .Where(member => member is MethodReference or FieldReference),
        ];
}

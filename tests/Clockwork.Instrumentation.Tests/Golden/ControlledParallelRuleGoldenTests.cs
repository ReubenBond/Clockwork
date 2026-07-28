using System.Collections.Immutable;
using Clockwork.Instrumentation.Manifest;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Clockwork.Runtime.Policy;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>
/// End-to-end golden tests for the shipped controlled <see cref="System.Threading.Tasks.Parallel"/> rule
/// family: they rewrite real <c>Parallel.Invoke</c> / <c>For</c> / <c>ForEach</c> call sites in a compiled
/// fixture against the real <c>Clockwork</c> assembly and assert on the rewritten IL and
/// manifest. This proves the static signatures declared in <c>BuiltInRuleSets</c> line up with the actual
/// <see cref="Clockwork.Shims.System.Threading.Tasks.ControlledParallel"/> members, and that the break/stop
/// (<c>ParallelLoopState</c>) overloads are rejected at the call site.
/// </summary>
public sealed class ControlledParallelRuleGoldenTests
{
    private const string Fixture = """
        using System.Collections.Generic;
        using System.Threading.Tasks;

        namespace Fx
        {
            public static class ParallelUser
            {
                public static void Invoke() => Parallel.Invoke(() => { }, () => { });
                public static void InvokeOptions(ParallelOptions o) => Parallel.Invoke(o, () => { });
                public static ParallelLoopResult ForInt() => Parallel.For(0, 3, i => { });
                public static ParallelLoopResult ForIntOptions(ParallelOptions o) => Parallel.For(0, 3, o, i => { });
                public static ParallelLoopResult ForLong() => Parallel.For(0L, 3L, i => { });
                public static ParallelLoopResult ForLongOptions(ParallelOptions o) => Parallel.For(0L, 3L, o, i => { });
                public static ParallelLoopResult ForEach(IEnumerable<int> s) => Parallel.ForEach(s, i => { });
                public static ParallelLoopResult ForEachOptions(IEnumerable<int> s, ParallelOptions o) => Parallel.ForEach(s, o, i => { });

                public static ParallelLoopResult ForLoopState() => Parallel.For(0, 3, (i, state) => state.Break());
                public static ParallelLoopResult ForEachLoopState(IEnumerable<int> s) => Parallel.ForEach(s, (i, state) => state.Break());
                public static ParallelLoopResult ForEachLoopStateIndex(IEnumerable<int> s) => Parallel.ForEach(s, (i, state, idx) => state.Break());
            }
        }
        """;

    private static string RuntimeAssemblyPath =>
        typeof(Clockwork.Shims.System.Threading.Tasks.ControlledParallel).Assembly.Location;

    private static RewriteResult RewriteFixture(RewriteTestContext context, string assemblyName)
    {
        string fixturePath = context.CompileFixture(assemblyName, Fixture);
        string outputPath = Path.Combine(
            context.Directory, Path.GetFileNameWithoutExtension(fixturePath) + ".rewritten.dll");

        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = [RuntimeAssemblyPath],
            ReferenceSearchDirectories = [context.Directory, Path.GetDirectoryName(RuntimeAssemblyPath)!],
        };

        RewriteRuleSet ruleSet = BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies);
        var result = RewriteEngine.Rewrite(new RewriteRequest(fixturePath, outputPath, ruleSet, options));
        result.EnsureSuccess();
        return result;
    }

    [Fact]
    public void SimpleBodyOverloadsAreRedirectedToControlledShim()
    {
        using var context = RewriteTestContext.Create();
        var result = RewriteFixture(context, "Fx.ParallelControlled");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.ParallelControlled.rewritten.dll"));

        var expected = new (string Method, string Shim)[]
        {
            ("Invoke", "ControlledParallel::Invoke"),
            ("InvokeOptions", "ControlledParallel::Invoke"),
            ("ForInt", "ControlledParallel::For"),
            ("ForIntOptions", "ControlledParallel::For"),
            ("ForLong", "ControlledParallel::For"),
            ("ForLongOptions", "ControlledParallel::For"),
            ("ForEach", "ControlledParallel::ForEach"),
            ("ForEachOptions", "ControlledParallel::ForEach"),
        };

        foreach (var (method, shim) in expected)
        {
            MethodDefinition definition = CecilInspect.GetMethod(module, "Fx.ParallelUser", method);
            Assert.True(CecilInspect.CallsAnyContaining(definition, shim), $"{method} should call {shim}");
            Assert.False(CecilInspect.CallsAnyContaining(definition, "Tasks.Parallel::"), $"{method} should not call the real Parallel");
        }

        ImmutableArray<ManifestTransformation> transformations = result.Manifest.Transformations;
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.parallel.foreach" && t.Policy == SimulationApiPolicy.Controlled);
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.parallel.for.int64" && t.Policy == SimulationApiPolicy.Controlled);
    }

    [Fact]
    public void LoopStateOverloadsAreRejectedAtTheCallSite()
    {
        using var context = RewriteTestContext.Create();
        var result = RewriteFixture(context, "Fx.ParallelRejected");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.ParallelRejected.rewritten.dll"));

        foreach (var method in new[] { "ForLoopState", "ForEachLoopState", "ForEachLoopStateIndex" })
        {
            MethodDefinition definition = CecilInspect.GetMethod(module, "Fx.ParallelUser", method);
            Assert.True(CecilInspect.CallsAnyContaining(definition, "ControlledParallel::RejectUnsupported"), $"{method} should be rejected");
        }

        ImmutableArray<ManifestTransformation> transformations = result.Manifest.Transformations;
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.parallel.for.int32.loopstate" && t.Policy == SimulationApiPolicy.Rejected);
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.parallel.foreach.loopstate.index" && t.Policy == SimulationApiPolicy.Rejected);
    }
}

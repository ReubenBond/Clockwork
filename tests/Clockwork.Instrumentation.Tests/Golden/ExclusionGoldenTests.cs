using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>
/// Golden tests for exclusion behaviour: a type excluded via options is not rewritten, is recorded in
/// the manifest, and produces a <c>CWR0006</c> diagnostic - while other types are still rewritten.
/// </summary>
public sealed class ExclusionGoldenTests
{
    private const string Fixture = """
        using ClockworkFixtures.Api;

        namespace Fx
        {
            public static class Skipped
            {
                public static long Ticks() => RealClock.UtcNowTicks();
            }

            public static class Kept
            {
                public static long Ticks() => RealClock.UtcNowTicks();
            }
        }
        """;

    [Fact]
    public void ExcludedTypeIsNotRewrittenButOthersAre()
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.Exclusion", Fixture);
        string outputPath = Path.Combine(context.Directory, "Fx.Exclusion.rewritten.dll");

        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = [context.ShimPath],
            ReferenceSearchDirectories = [context.Directory],
            ExcludedTypeFullNames = ["Fx.Skipped"],
        };

        var result = context.Rewrite(fixturePath, outputPath, RewriteTestContext.StandardRuleSet(), options);
        result.EnsureSuccess();

        Assert.Contains(result.Manifest.Exclusions, e => e.TypeFullName == "Fx.Skipped");
        Assert.Contains(result.Diagnostics, d => d.Id == RewriteDiagnosticIds.TypeExcluded);

        using ModuleDefinition module = context.LoadModule(outputPath);
        MethodDefinition skipped = CecilInspect.GetMethod(module, "Fx.Skipped", "Ticks");
        MethodDefinition kept = CecilInspect.GetMethod(module, "Fx.Kept", "Ticks");

        Assert.True(CecilInspect.CallsAnyContaining(skipped, "RealClock::UtcNowTicks"));
        Assert.False(CecilInspect.CallsAnyContaining(skipped, "ClockShim::UtcNowTicks"));
        Assert.True(CecilInspect.CallsAnyContaining(kept, "ClockShim::UtcNowTicks"));

        Assert.All(result.Manifest.Transformations, t => Assert.Contains("Fx.Kept", t.Method, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Manifest.Transformations, t => t.Method.Contains("Fx.Skipped", StringComparison.Ordinal));
    }
}

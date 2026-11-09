using System.Reflection;
using System.Runtime.Loader;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>
/// Golden tests for the rewrite-time check that fails a rewrite (<c>CWR0001</c>) instead of emitting a member reference
/// that does not bind: a substituted signature binds only where the declaring type is rewritten too. The closure-level
/// cases, where the declaring type is in another assembly of the closure, are in <c>ClosureBindingTests</c>.
/// </summary>
public sealed class SubstitutionBindingGoldenTests
{
    public static TheoryData<string, string, string> UnboundCalls => new()
    {
        // Without a closure, no other assembly is known to be rewritten, so a sibling keeps its original signature.
        { "Sibling", "public static string Call(LegacyMarker marker) => MarkerSink.Describe(marker);", "MarkerSink::Describe" },

        // A replacement assembly is never rewritten, so the substituted signature does not exist there.
        { "Replacement", "public static string Call(LegacyMarker marker) => MarkerShim.Describe(marker);", "MarkerShim::Describe" },
        { "ReplacementGeneric", "public static LegacyMarker Call(LegacyMarker marker) => MarkerShim.Make(marker);", "MarkerShim::Make" },

        // A type excluded from rewriting keeps its original signature.
        { "Excluded", "public static string Call(LegacyMarker marker) => Excluded.Describe(marker);", "Fx.Excluded::Describe" },
    };

    [Theory]
    [MemberData(nameof(UnboundCalls))]
    public void SubstitutedSignatureFailsWhereItsDeclaringTypeIsNotRewritten(string name, string call, string unbound)
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

        RewriteResult result = context.Rewrite(fixturePath, MarkerRuleSet(), MarkerOptions(context, excluded: "Fx.Excluded"));

        RewriteDiagnostic error = AssertUnbound(result, unbound);
        Assert.Equal("Fx.Caller.Call", error.Method);
    }

    [Fact]
    public void MultidimensionalArrayOfASubstitutedTypeBinds()
    {
        // The runtime provides an array type's constructor and accessors, so they bind with the substituted element type.
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.Arrays", """
            using ClockworkFixtures.Api;

            namespace Fx
            {
                public static class Arrays
                {
                    public static string Describe()
                    {
                        var markers = new LegacyMarker[1, 1];
                        markers[0, 0] = new LegacyMarker();
                        return markers[0, 0].ToString();
                    }
                }
            }
            """);

        context.Rewrite(fixturePath, MarkerRuleSet(), MarkerOptions(context)).EnsureSuccess();

        string rewrittenPath = Path.Combine(context.Directory, "Fx.Arrays.rewritten.dll");
        using (ModuleDefinition module = context.LoadModule(rewrittenPath))
        {
            List<string> targets = CecilInspect.CallTargets(CecilInspect.GetMethod(module, "Fx.Arrays", "Describe"));
            Assert.Contains(targets, target => target.Contains("ClockworkFixtures.Shims.ModernMarker[", StringComparison.Ordinal)
                && target.EndsWith("]::Get(System.Int32,System.Int32)", StringComparison.Ordinal));
            Assert.DoesNotContain(targets, target => target.Contains("LegacyMarker", StringComparison.Ordinal));
        }

        var loadContext = new DirectoryLoadContext(context.Directory);
        try
        {
            Type arrays = loadContext.LoadFromAssemblyPath(rewrittenPath).GetType("Fx.Arrays", throwOnError: true)!;
            Assert.Equal("modern", arrays.GetMethod("Describe")!.Invoke(null, null));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static RewriteDiagnostic AssertUnbound(RewriteResult result, string unbound)
    {
        Assert.False(result.Succeeded, "The rewrite succeeded: " + string.Join(" | ", result.Diagnostics));
        Assert.False(result.WasWritten);
        RewriteDiagnostic error = Assert.Single(result.Errors);
        Assert.Equal(RewriteDiagnosticIds.UnresolvedReplacement, error.Id);
        Assert.Contains("'substitute-marker'", error.Message, StringComparison.Ordinal);
        Assert.Contains(unbound, error.Message, StringComparison.Ordinal);
        return error;
    }

    private static RewriteRuleSet MarkerRuleSet() =>
        new(
            "clockwork.fixtures.binding",
            "1.0",
            [
                RewriteRule.SubstituteType(
                    "substitute-marker",
                    "ClockworkFixtures.Api.LegacyMarker",
                    RewriteReplacement.Type(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.ModernMarker")),
            ]);

    private static RewriteOptions MarkerOptions(RewriteTestContext context, string? excluded = null) =>
        new()
        {
            ReplacementAssemblyPaths = [context.ShimPath],
            ReferenceSearchDirectories = [context.Directory],
            ExcludedTypeFullNames = excluded is null ? [] : [excluded],
        };

    private sealed class DirectoryLoadContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}

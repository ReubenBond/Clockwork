using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Tests.Infrastructure;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>
/// Golden tests for strict resolution: a targeted member whose replacement cannot be resolved is a
/// hard failure (<c>CWR0001</c>) - the engine never silently skips a targeted call.
/// </summary>
public sealed class ResolutionGoldenTests
{
    private const string Fixture = """
        using ClockworkFixtures.Api;

        namespace Fx
        {
            public static class Resolve
            {
                public static long Ticks() => RealClock.UtcNowTicks();
            }
        }
        """;

    [Fact]
    public void UnresolvableTargetedReplacementFails()
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.Resolve", Fixture);

        var ruleSet = new RewriteRuleSet(
            "clockwork.fixtures.bad",
            "1.0",
            [
                RewriteRule.RedirectCall(
                    "redirect-missing",
                    MemberSignature.Method("ClockworkFixtures.Api.RealClock", "UtcNowTicks"),
                    RewriteReplacement.Method(
                        FixtureSources.ShimAssemblyName,
                        "ClockworkFixtures.Shims.ClockShim",
                        "ThisMethodDoesNotExist")),
            ]);

        var result = context.Rewrite(fixturePath, ruleSet);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, d => d.Id == RewriteDiagnosticIds.UnresolvedReplacement);
        Assert.False(result.WasWritten);
    }
}

using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>
/// Golden tests for the assembly-level idempotence marker: a first rewrite stamps the signature,
/// re-running with the same rule set is a verified no-op, and re-running with an incompatible rule-set
/// version fails clearly rather than double-rewriting.
/// </summary>
public sealed class IdempotenceGoldenTests
{
    private const string Fixture = """
        using ClockworkFixtures.Api;

        namespace Fx
        {
            public static class Idem
            {
                public static long Ticks() => RealClock.UtcNowTicks();
            }
        }
        """;

    [Fact]
    public void FirstRewriteStampsSignature()
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.Idem1", Fixture);
        string outputPath = Path.Combine(context.Directory, "Fx.Idem1.rewritten.dll");

        var result = context.Rewrite(fixturePath, outputPath, RewriteTestContext.StandardRuleSet());
        result.EnsureSuccess();

        Assert.True(result.WasWritten);
        Assert.False(result.WasNoOp);

        using ModuleDefinition module = context.LoadModule(outputPath);
        Assert.True(CecilInspect.HasRewriteSignature(module));
    }

    [Fact]
    public void ReRewriteWithSameRulesIsVerifiedNoOp()
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.Idem2", Fixture);
        string firstOut = Path.Combine(context.Directory, "Fx.Idem2.rewritten.dll");
        context.Rewrite(fixturePath, firstOut, RewriteTestContext.StandardRuleSet()).EnsureSuccess();

        string secondOut = Path.Combine(context.Directory, "Fx.Idem2.twice.dll");
        var second = context.Rewrite(firstOut, secondOut, RewriteTestContext.StandardRuleSet());
        second.EnsureSuccess();

        Assert.True(second.WasNoOp);
        Assert.True(second.Succeeded);
        Assert.Empty(second.Manifest.Transformations);
        Assert.Contains(second.Diagnostics, d => d.Id == RewriteDiagnosticIds.AlreadyRewritten);
    }

    [Fact]
    public void ReRewriteWithIncompatibleVersionFails()
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.Idem3", Fixture);
        string firstOut = Path.Combine(context.Directory, "Fx.Idem3.rewritten.dll");
        context.Rewrite(fixturePath, firstOut, RewriteTestContext.StandardRuleSet("1.0")).EnsureSuccess();

        string secondOut = Path.Combine(context.Directory, "Fx.Idem3.twice.dll");
        var second = context.Rewrite(firstOut, secondOut, RewriteTestContext.StandardRuleSet("2.0"));

        Assert.False(second.Succeeded);
        Assert.Contains(second.Errors, d => d.Id == RewriteDiagnosticIds.IncompatibleRewriteVersion);
        Assert.False(second.WasWritten);
    }
}

using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Tests.Infrastructure;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>Proves malformed JSON replacement contracts fail before invalid IL is emitted.</summary>
public sealed class ReplacementContractGoldenTests
{
    private const string Fixture = """
        using ClockworkFixtures.Api;

        namespace Fx
        {
            public static class Contracts
            {
                public static long Ticks() => RealClock.UtcNowTicks();
                public static int Instance() => new Service(1).GetValue();
                public static Widget Create() => new Widget(1);
                public static int Generic() => GenericOps.Echo(1);
                public static int Wrap() => new Meterable().Measure();
                public static void Reject() => Forbidden.DangerousWrite("x");
            }
        }
        """;

    public static TheoryData<string, RewriteRule, string> MalformedRules => new()
    {
        {
            "instance replacement",
            RewriteRule.RedirectCall(
                "instance",
                MemberSignature.Method("ClockworkFixtures.Api.RealClock", "UtcNowTicks"),
                RewriteReplacement.Method(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.InvalidShim", "InstanceTicks")),
            RewriteDiagnosticIds.ReplacementContractMismatch
        },
        {
            "receiver mismatch",
            RewriteRule.RedirectCall(
                "receiver",
                MemberSignature.Method("ClockworkFixtures.Api.Service", "GetValue"),
                RewriteReplacement.Method(
                    FixtureSources.ShimAssemblyName,
                    "ClockworkFixtures.Shims.InvalidShim",
                    "WrongReceiver",
                    "System.String")),
            RewriteDiagnosticIds.ReplacementContractMismatch
        },
        {
            "return mismatch",
            RewriteRule.RedirectCall(
                "return",
                MemberSignature.Method("ClockworkFixtures.Api.RealClock", "UtcNowTicks"),
                RewriteReplacement.Method(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.InvalidShim", "WrongReturn")),
            RewriteDiagnosticIds.ReplacementContractMismatch
        },
        {
            "constructor return mismatch",
            RewriteRule.RedirectNewObj(
                "factory",
                MemberSignature.Constructor("ClockworkFixtures.Api.Widget", "System.Int32"),
                RewriteReplacement.Method(
                    FixtureSources.ShimAssemblyName,
                    "ClockworkFixtures.Shims.InvalidShim",
                    "WrongFactory",
                    "System.Int32")),
            RewriteDiagnosticIds.ReplacementContractMismatch
        },
        {
            "generic arity mismatch",
            RewriteRule.RedirectCall(
                "generic",
                new MemberSignature("ClockworkFixtures.Api.GenericOps", "Echo"),
                RewriteReplacement.Method(
                    FixtureSources.ShimAssemblyName,
                    "ClockworkFixtures.Shims.InvalidShim",
                    "GenericMismatch")),
            RewriteDiagnosticIds.ReplacementContractMismatch
        },
        {
            "post-call mismatch",
            RewriteRule.WrapAfterCall(
                "wrapper",
                MemberSignature.Method("ClockworkFixtures.Api.Meterable", "Measure"),
                RewriteReplacement.Method(
                    FixtureSources.ShimAssemblyName,
                    "ClockworkFixtures.Shims.InvalidShim",
                    "WrongWrapper",
                    "System.String")),
            RewriteDiagnosticIds.ReplacementContractMismatch
        },
        {
            "rejection mismatch",
            RewriteRule.InjectRejection(
                "rejection",
                MemberSignature.Method("ClockworkFixtures.Api.Forbidden", "DangerousWrite", "System.String"),
                RewriteReplacement.Method(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.InvalidShim", "WrongReject")),
            RewriteDiagnosticIds.ReplacementContractMismatch
        },
        {
            "ambiguous same-name replacement",
            RewriteRule.RedirectCall(
                "ambiguous",
                MemberSignature.Method("ClockworkFixtures.Api.RealClock", "UtcNowTicks"),
                RewriteReplacement.Method(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.InvalidShim", "Ambiguous")),
            RewriteDiagnosticIds.UnresolvedReplacement
        },
    };

    [Theory]
    [MemberData(nameof(MalformedRules))]
    public void MalformedJsonRuleFailsWithoutWritingInvalidIl(
        string name,
        RewriteRule authoredRule,
        string expectedDiagnostic)
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.Contracts." + name.Replace(' ', '.'), Fixture);
        string outputPath = fixturePath + ".rewritten.dll";
        RewriteRuleSet parsed = RuleSetJson.Parse(RuleSetJson.Write(
            new RewriteRuleSet("clockwork.contracts", "1.0", [authoredRule])));

        RewriteResult result = context.Rewrite(fixturePath, outputPath, parsed);

        Assert.False(result.Succeeded);
        Assert.False(result.WasWritten);
        Assert.False(File.Exists(outputPath));
        Assert.Contains(result.Errors, diagnostic => diagnostic.Id == expectedDiagnostic);
    }
}

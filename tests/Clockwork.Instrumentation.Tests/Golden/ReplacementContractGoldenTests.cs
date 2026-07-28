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

    [Fact]
    public void ValueTypeInstanceReceiverRequiresManagedPointerReplacement()
    {
        const string source = """
            using ClockworkFixtures.Api;
            namespace Fx
            {
                public static class ValueReceiver
                {
                    public static int Run()
                    {
                        var value = new StructProbe { N = 3 };
                        return value.Probe();
                    }
                }
            }
            """;
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.ValueReceiver", source);
        var ruleSet = new RewriteRuleSet(
            "clockwork.value-receiver",
            "1.0",
            [
                RewriteRule.RedirectCall(
                    "value-receiver",
                    MemberSignature.Method("ClockworkFixtures.Api.StructProbe", "Probe"),
                    RewriteReplacement.Method(
                        FixtureSources.ShimAssemblyName,
                        "ClockworkFixtures.Shims.ClockShim",
                        "GetProbe",
                        "ClockworkFixtures.Api.StructProbe&")),
            ]);

        RewriteResult result = context.Rewrite(fixturePath, ruleSet);

        result.EnsureSuccess();
        using Mono.Cecil.ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.ValueReceiver.rewritten.dll"));
        Assert.True(CecilInspect.CallsAnyContaining(
            CecilInspect.GetMethod(module, "Fx.ValueReceiver", "Run"),
            "ClockShim::GetProbe"));
    }

    [Fact]
    public void NestedGenericShapeWithForeignParameterOwnerRewrites()
    {
        const string source = """
            using ClockworkFixtures.Api;
            namespace Fx
            {
                public static class RecursiveContract<T>
                {
                    public static RecursiveContainer<T>.Element Read(
                        GenericBox<RecursiveContainer<T>.Element> box) => box.Value;
                }
            }
            """;
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.RecursiveContract", source);
        var ruleSet = RecursiveBoxRuleSet("ReadBox");

        RewriteResult result = context.Rewrite(fixturePath, ruleSet);

        result.EnsureSuccess();
        using Mono.Cecil.ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.RecursiveContract.rewritten.dll"));
        Assert.True(CecilInspect.CallsAnyContaining(
            CecilInspect.GetMethod(module, "Fx.RecursiveContract`1", "Read"),
            "ClockShim::ReadBox"));
    }

    [Fact]
    public void IncompatibleNestedGenericShapeReportsContractMismatch()
    {
        const string source = """
            using ClockworkFixtures.Api;
            namespace Fx
            {
                public static class RecursiveContract<T>
                {
                    public static RecursiveContainer<T>.Element Read(
                        GenericBox<RecursiveContainer<T>.Element> box) => box.Value;
                }
            }
            """;
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.RecursiveContract.Invalid", source);

        RewriteResult result = context.Rewrite(fixturePath, RecursiveBoxRuleSet("ReadBoxWrong"));

        Assert.False(result.Succeeded);
        Assert.False(result.WasWritten);
        RewriteDiagnostic diagnostic = Assert.Single(
            result.Errors,
            error => error.Id == RewriteDiagnosticIds.ReplacementContractMismatch);
        Assert.Contains("RecursiveContainer`1/Element<!0>", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("System.Collections.Generic.List`1", diagnostic.Message, StringComparison.Ordinal);
    }

    private static RewriteRuleSet RecursiveBoxRuleSet(string replacementMethod) =>
        new(
            "clockwork.recursive-contract",
            "1.0",
            [
                RewriteRule.RedirectCall(
                    "recursive-box-value",
                    new MemberSignature("ClockworkFixtures.Api.GenericBox`1", "get_Value"),
                    RewriteReplacement.Method(
                        FixtureSources.ShimAssemblyName,
                        "ClockworkFixtures.Shims.ClockShim",
                        replacementMethod)),
            ]);
}

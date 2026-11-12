using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;

namespace Clockwork.Instrumentation.Tests.Rules;

public sealed class RuleSetRuntimeSupportTests
{
    private static readonly Version Net10 = new(10, 0);
    private static readonly Version Net11 = new(11, 0);
    private static readonly Version Net12 = new(12, 0);

    [Fact]
    public void RuleWithoutAnAlternativeForTheTargetIsUnsupported()
    {
        RewriteRule perRuntime10 = Rule("now.net10", "get_Now", new RuntimeVersionRange(Net10, Net10));
        RewriteRule perRuntime11 = Rule("now.net11", "get_Now", new RuntimeVersionRange(Net11, Net12));
        RewriteRule open = Rule("today", "get_Today", RuntimeVersionRange.AtLeast(Net10));
        RewriteRule older = Rule("utcnow.net10", "get_UtcNow", new RuntimeVersionRange(Net10, Net10));
        var ruleSet = new RewriteRuleSet("fixture", "1.0", [perRuntime10, perRuntime11, open, older]);

        RuleSetRuntimeSupport support = RuleSetRuntimeSupport.Evaluate(ruleSet, Net11);

        Assert.False(support.IsSupported);
        Assert.Equal(older, Assert.Single(support.UnsupportedRules));
        Assert.Equal(new RuntimeVersionRange(Net11, Net12), support.SupportedRuntimes);
    }

    [Fact]
    public void BuiltInRulesSupportOnlyTheirRuntimeAndFailOtherTargetsOnce()
    {
        RewriteRuleSet ruleSet = BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies);

        RuleSetRuntimeSupport supported = RuleSetRuntimeSupport.Evaluate(ruleSet, BuiltInRuleSets.SupportedRuntime);
        RuleSetRuntimeSupport unsupported = RuleSetRuntimeSupport.Evaluate(ruleSet, Net12);
        RewriteDiagnostic diagnostic = unsupported.ToDiagnostic(ruleSet, "configured");

        Assert.True(supported.IsSupported);
        Assert.Equal(
            new RuntimeVersionRange(BuiltInRuleSets.SupportedRuntime, BuiltInRuleSets.SupportedRuntime),
            supported.SupportedRuntimes);
        Assert.Equal(ruleSet.Rules.Length, unsupported.UnsupportedRules.Length);
        Assert.Equal(RewriteDiagnosticIds.RuntimeOutOfRange, diagnostic.Id);
        Assert.True(diagnostic.IsError);
        Assert.Contains("Target runtime 12.0 (configured)", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains($"11.0 ({ruleSet.Rules.Length} rules)", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10, 0, 2)]
    [InlineData(11, 0, 0)]
    public void PatchVersionsAreRejected(int major, int minor, int build)
    {
        var ruleSet = new RewriteRuleSet("fixture", "1.0", [Rule("now", "get_Now", RuntimeVersionRange.All)]);
        var patched = new RewriteRuleSet(
            "fixture",
            "1.0",
            [Rule("now", "get_Now", RuntimeVersionRange.AtMost(new Version(major, minor, build)))]);

        Assert.Throws<ArgumentException>(() => RuleSetRuntimeSupport.Evaluate(ruleSet, new Version(major, minor, build)));
        Assert.Throws<ArgumentException>(() => RuleSetRuntimeSupport.Evaluate(patched, Net11));
    }

    private static RewriteRule Rule(string id, string member, RuntimeVersionRange supportedRuntimes) =>
        RewriteRule.RedirectCall(
            id,
            MemberSignature.Method("System.DateTime", member),
            RewriteReplacement.Method("Shims", "Shims.Clock", member)) with
        {
            SupportedRuntimes = supportedRuntimes,
        };
}

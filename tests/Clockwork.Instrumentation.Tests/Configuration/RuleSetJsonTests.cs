using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Rules;
using Clockwork.Runtime.Policy;

namespace Clockwork.Instrumentation.Tests.Configuration;

/// <summary>
/// Verifies the strict rule-set JSON loader: valid documents parse to the expected rule model,
/// round-tripping is signature-stable, and every class of malformed document fails with a
/// <see cref="RuleSetFormatException"/> rather than silently producing a degenerate rule set.
/// </summary>
public sealed class RuleSetJsonTests
{
    private const string ValidDocument = """
        {
          "schemaVersion": 1,
          "id": "clockwork.example",
          "version": "1.0",
          "rules": [
            {
              "id": "redirect-utcnow",
              "operation": "RedirectCall",
              "target": { "type": "System.DateTime", "member": "get_UtcNow", "parameters": [] },
              "replacement": { "assembly": "Shims", "type": "Shims.Clock", "member": "UtcNow", "parameters": [] },
              "policy": "Controlled",
              "fallback": "Fail",
              "supportedRuntimes": { "min": "10.0", "max": null },
              "description": "redirect the clock"
            },
            {
              "id": "substitute-marker",
              "operation": "SubstituteType",
              "target": { "type": "App.LegacyMarker" },
              "replacement": { "assembly": "Shims", "type": "Shims.ModernMarker" }
            }
          ]
        }
        """;

    [Fact]
    public void ParsesValidDocument()
    {
        RewriteRuleSet ruleSet = RuleSetJson.Parse(ValidDocument);

        Assert.Equal("clockwork.example", ruleSet.Id);
        Assert.Equal("1.0", ruleSet.Version);
        Assert.Equal(2, ruleSet.Rules.Length);

        RewriteRule redirect = ruleSet.Rules[0];
        Assert.Equal("redirect-utcnow", redirect.Id);
        Assert.Equal(RewriteOperationKind.RedirectCall, redirect.Operation);
        Assert.Equal("System.DateTime", redirect.Target.DeclaringTypeFullName);
        Assert.Equal("get_UtcNow", redirect.Target.MemberName);
        Assert.False(redirect.Target.ParameterTypeFullNames.IsDefault);
        Assert.Empty(redirect.Target.ParameterTypeFullNames);
        Assert.Equal("Shims", redirect.Replacement.AssemblyName);
        Assert.Equal(new Version(10, 0), redirect.SupportedRuntimes.Minimum);

        RewriteRule substitute = ruleSet.Rules[1];
        Assert.Equal(RewriteOperationKind.SubstituteType, substitute.Operation);
        Assert.True(substitute.Target.IsTypeOnly);
        Assert.True(substitute.Replacement.IsTypeOnly);
        Assert.Equal(SimulationApiPolicy.Controlled, substitute.Policy);
    }

    [Fact]
    public void OmittedParametersMatchAnyOverload()
    {
        const string doc = """
            {
              "id": "s", "version": "1",
              "rules": [{
                "id": "r", "operation": "RedirectCall",
                "target": { "type": "T", "member": "M" },
                "replacement": { "assembly": "A", "type": "R", "member": "M" }
              }]
            }
            """;

        RewriteRule rule = RuleSetJson.Parse(doc).Rules[0];
        Assert.True(rule.Target.ParameterTypeFullNames.IsDefault);
        Assert.False(rule.Target.HasParameterConstraint);
    }

    [Fact]
    public void RoundTripIsSignatureStable()
    {
        RewriteRuleSet original = RuleSetJson.Parse(ValidDocument);
        string written = RuleSetJson.Write(original);
        RewriteRuleSet reparsed = RuleSetJson.Parse(written);

        Assert.Equal(original.ComputeSignature(), reparsed.ComputeSignature());
        Assert.Equal(written, RuleSetJson.Write(reparsed));
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("""{ "id": "s", "version": "1" }""", "'rules' array")]
    [InlineData("""{ "version": "1", "rules": [] }""", "required property 'id'")]
    [InlineData("""{ "id": "s", "version": "1", "rules": [ { "id": "r", "operation": "Nope", "target": {"type":"T","member":"M"}, "replacement": {"assembly":"A","type":"R","member":"M"} } ] }""", "not one of")]
    public void RejectsMalformedDocuments(string json, string expectedFragment)
    {
        RuleSetFormatException ex = Assert.Throws<RuleSetFormatException>(() => RuleSetJson.Parse(json));
        Assert.Contains(expectedFragment, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateRuleIds()
    {
        const string doc = """
            {
              "id": "s", "version": "1",
              "rules": [
                { "id": "dup", "operation": "RedirectCall", "target": {"type":"T","member":"M"}, "replacement": {"assembly":"A","type":"R","member":"M"} },
                { "id": "dup", "operation": "RedirectCall", "target": {"type":"T","member":"N"}, "replacement": {"assembly":"A","type":"R","member":"N"} }
              ]
            }
            """;

        RuleSetFormatException ex = Assert.Throws<RuleSetFormatException>(() => RuleSetJson.Parse(doc));
        Assert.Contains("dup", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTypeOperationWithMember()
    {
        const string doc = """
            {
              "id": "s", "version": "1",
              "rules": [{ "id": "r", "operation": "SubstituteType", "target": {"type":"T","member":"M"}, "replacement": {"assembly":"A","type":"R"} }]
            }
            """;

        Assert.Throws<RuleSetFormatException>(() => RuleSetJson.Parse(doc));
    }

    [Fact]
    public void RejectsMemberOperationWithoutMember()
    {
        const string doc = """
            {
              "id": "s", "version": "1",
              "rules": [{ "id": "r", "operation": "RedirectCall", "target": {"type":"T"}, "replacement": {"assembly":"A","type":"R","member":"M"} }]
            }
            """;

        Assert.Throws<RuleSetFormatException>(() => RuleSetJson.Parse(doc));
    }

    [Fact]
    public void RejectsTargetWithAssembly()
    {
        const string doc = """
            {
              "id": "s", "version": "1",
              "rules": [{ "id": "r", "operation": "RedirectCall", "target": {"assembly":"X","type":"T","member":"M"}, "replacement": {"assembly":"A","type":"R","member":"M"} }]
            }
            """;

        RuleSetFormatException ex = Assert.Throws<RuleSetFormatException>(() => RuleSetJson.Parse(doc));
        Assert.Contains("must not specify an 'assembly'", ex.Message, StringComparison.Ordinal);
    }
}

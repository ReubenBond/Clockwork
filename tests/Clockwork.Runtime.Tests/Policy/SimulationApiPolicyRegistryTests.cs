using Clockwork.Runtime.Policy;

namespace Clockwork.Runtime.Tests.Policy;

/// <summary>
/// Covers <see cref="SimulationApiPolicyRegistry"/>: the strict default, the ban on
/// <see cref="SimulationApiPolicy.PassThrough"/> as a default, deterministic per-API/per-assembly
/// override precedence, override clearing, and diagnostic reason strings.
/// </summary>
public sealed class SimulationApiPolicyRegistryTests
{
    private static readonly SimulationApiKey SendAsync = new("System.Net.Http", "HttpClient.SendAsync");
    private static readonly SimulationApiKey GetAsync = new("System.Net.Http", "HttpClient.GetAsync");

    [Fact]
    public void DefaultConstructorUsesControlledAsTheStrictSimulationDefault()
    {
        var registry = new SimulationApiPolicyRegistry();
        Assert.Equal(SimulationApiPolicy.Controlled, registry.DefaultPolicy);
    }

    [Fact]
    public void ConstructorRejectsPassThroughAsTheDefaultPolicy()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SimulationApiPolicyRegistry(SimulationApiPolicy.PassThrough));
        Assert.Equal("defaultPolicy", exception.ParamName);
    }

    [Fact]
    public void ConstructorAllowsRejectedAsTheDefaultPolicy()
    {
        var registry = new SimulationApiPolicyRegistry(SimulationApiPolicy.Rejected);
        Assert.Equal(SimulationApiPolicy.Rejected, registry.DefaultPolicy);
    }

    [Fact]
    public void ResolveReturnsTheDefaultPolicyForAnUnclassifiedApi()
    {
        var registry = new SimulationApiPolicyRegistry();
        var decision = registry.Resolve(SendAsync);

        Assert.Equal(SimulationApiPolicy.Controlled, decision.Policy);
        Assert.Contains("default", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveAppliesAPerAssemblyOverride()
    {
        var registry = new SimulationApiPolicyRegistry();
        registry.SetAssemblyPolicy("System.Net.Http", SimulationApiPolicy.PassThrough, "explicitly allowed for this test");

        var decision = registry.Resolve(SendAsync);

        Assert.Equal(SimulationApiPolicy.PassThrough, decision.Policy);
        Assert.Equal("explicitly allowed for this test", decision.Reason);
    }

    [Fact]
    public void ResolveAppliesADefaultReasonWhenNoneIsSuppliedForAnAssemblyOverride()
    {
        var registry = new SimulationApiPolicyRegistry();
        registry.SetAssemblyPolicy("System.Net.Http", SimulationApiPolicy.Rejected);

        var decision = registry.Resolve(SendAsync);
        Assert.Contains("assembly", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvePrefersAPerApiOverrideOverAMatchingPerAssemblyOverride()
    {
        var registry = new SimulationApiPolicyRegistry();
        registry.SetAssemblyPolicy("System.Net.Http", SimulationApiPolicy.Rejected, "assembly-level rejection");
        registry.SetApiPolicy(SendAsync, SimulationApiPolicy.PassThrough, "this one API is fine");

        var sendDecision = registry.Resolve(SendAsync);
        Assert.Equal(SimulationApiPolicy.PassThrough, sendDecision.Policy);
        Assert.Equal("this one API is fine", sendDecision.Reason);

        // A sibling API in the same assembly, with no per-API override of its own, still falls
        // back to the per-assembly override - precedence is per-key, not "once any API in the
        // assembly has an override, the whole assembly follows it".
        var getDecision = registry.Resolve(GetAsync);
        Assert.Equal(SimulationApiPolicy.Rejected, getDecision.Policy);
        Assert.Equal("assembly-level rejection", getDecision.Reason);
    }

    [Fact]
    public void SetApiPolicyCanUsePassThroughEvenThoughItCannotBeTheDefault()
    {
        // Pass-through must always be explicit - an explicit per-API override is exactly that.
        var registry = new SimulationApiPolicyRegistry();
        registry.SetApiPolicy(SendAsync, SimulationApiPolicy.PassThrough);

        Assert.Equal(SimulationApiPolicy.PassThrough, registry.Resolve(SendAsync).Policy);
    }

    [Fact]
    public void ClearAssemblyPolicyRevertsToTheDefault()
    {
        var registry = new SimulationApiPolicyRegistry();
        registry.SetAssemblyPolicy("System.Net.Http", SimulationApiPolicy.Rejected);

        Assert.True(registry.ClearAssemblyPolicy("System.Net.Http"));
        Assert.Equal(SimulationApiPolicy.Controlled, registry.Resolve(SendAsync).Policy);
    }

    [Fact]
    public void ClearAssemblyPolicyReturnsFalseWhenNoOverrideExisted()
    {
        var registry = new SimulationApiPolicyRegistry();
        Assert.False(registry.ClearAssemblyPolicy("Nonexistent.Assembly"));
    }

    [Fact]
    public void ClearApiPolicyRevertsToAnyRemainingAssemblyOverrideThenTheDefault()
    {
        var registry = new SimulationApiPolicyRegistry();
        registry.SetAssemblyPolicy("System.Net.Http", SimulationApiPolicy.Rejected);
        registry.SetApiPolicy(SendAsync, SimulationApiPolicy.PassThrough);

        Assert.True(registry.ClearApiPolicy(SendAsync));
        Assert.Equal(SimulationApiPolicy.Rejected, registry.Resolve(SendAsync).Policy);
    }

    [Fact]
    public void ClearApiPolicyReturnsFalseWhenNoOverrideExisted()
    {
        var registry = new SimulationApiPolicyRegistry();
        Assert.False(registry.ClearApiPolicy(SendAsync));
    }

    [Fact]
    public void SetAssemblyPolicyThrowsForNullOrEmptyAssemblyName()
    {
        var registry = new SimulationApiPolicyRegistry();
        Assert.Throws<ArgumentException>(() => registry.SetAssemblyPolicy(string.Empty, SimulationApiPolicy.Rejected));
    }

    [Fact]
    public void SimulationApiKeyEqualityIsStructuralOnAssemblyAndApiName()
    {
        var a = new SimulationApiKey("Assembly", "Api");
        var b = new SimulationApiKey("Assembly", "Api");
        Assert.Equal(a, b);
    }

    [Fact]
    public void DecisionResolutionIsDeterministicAcrossRepeatedCalls()
    {
        var registry = new SimulationApiPolicyRegistry();
        registry.SetAssemblyPolicy("System.Net.Http", SimulationApiPolicy.Rejected);
        registry.SetApiPolicy(SendAsync, SimulationApiPolicy.PassThrough);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(SimulationApiPolicy.PassThrough, registry.Resolve(SendAsync).Policy);
            Assert.Equal(SimulationApiPolicy.Rejected, registry.Resolve(GetAsync).Policy);
        }
    }
}

using System.Reflection;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Tests.Execution;

/// <summary>
/// Covers the capability-token security model (<see cref="SimulationActivationToken"/> /
/// <see cref="SimulationRuntimeActivation"/>): production code outside this assembly's
/// <c>InternalsVisibleTo</c> grants has no way to mint a token, and therefore no way to activate
/// ambient simulation execution context, through any public surface.
/// </summary>
public sealed class SimulationActivationTokenTests
{
    [Fact]
    public void CreateTokenProducesANonNullToken()
    {
        var token = SimulationRuntimeActivation.CreateToken();
        Assert.NotNull(token);
    }

    [Fact]
    public void CreateTokenProducesADistinctInstanceEachCall()
    {
        var first = SimulationRuntimeActivation.CreateToken();
        var second = SimulationRuntimeActivation.CreateToken();
        Assert.NotSame(first, second);
    }

    [Fact]
    public void SimulationActivationTokenHasNoPublicConstructor()
    {
        // The only public members are inherited from object; there must be no way to `new` one
        // from outside this assembly's InternalsVisibleTo trust boundary.
        var publicConstructors = typeof(SimulationActivationToken).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void SimulationRuntimeActivationIsNotPublic()
    {
        // If this type (or CreateToken) were ever made public, any production code could mint a
        // token and activate simulation context - the whole point of the capability model is that
        // only code inside the InternalsVisibleTo trust boundary can do this.
        Assert.False(typeof(SimulationRuntimeActivation).IsPublic);
    }

    [Fact]
    public void SimulationExecutionContextExposesNoPublicBooleanOrEnvironmentBasedActivationSwitch()
    {
        // Guards against regressing to a "public static bool IsSimulating { get; set; }" or
        // similar accidental global activation switch: the only public way to make
        // SimulationExecutionContext active must require a SimulationActivationToken argument.
        var publicStaticMembers = typeof(SimulationExecutionContext)
            .GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name is not (nameof(Equals) or nameof(ReferenceEquals) or nameof(GetType) or "GetHashCode" or "ToString"))
            .ToArray();

        var settableBooleans = publicStaticMembers
            .OfType<PropertyInfo>()
            .Where(p => p.PropertyType == typeof(bool) && p.CanWrite);

        Assert.Empty(settableBooleans);

        var methodsThatCanActivateWithoutAToken = publicStaticMembers
            .OfType<MethodInfo>()
            .Where(m => m.Name.StartsWith("Enter", StringComparison.Ordinal))
            .Where(m => !m.GetParameters().Any(p => p.ParameterType == typeof(SimulationActivationToken)))
            .Where(m => m.Name != nameof(SimulationExecutionContext.EnterNode) && m.Name != nameof(SimulationExecutionContext.EnterLogicalExecution));

        // EnterNode/EnterLogicalExecution don't take a token directly, but both throw unless an
        // EnterRuntime(token, ...) scope is already active - so they cannot activate anything on
        // their own. Every other "Enter*" method must take a token.
        Assert.Empty(methodsThatCanActivateWithoutAToken);
    }
}

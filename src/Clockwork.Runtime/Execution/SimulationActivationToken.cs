namespace Clockwork.Runtime.Execution;

/// <summary>
/// <para>
/// A capability token proving that the holder is the simulation host that is entitled to activate
/// ambient simulation execution context. There is deliberately no public constructor, no public
/// global boolean (e.g. an "IsSimulating" switch), and no environment variable that flips this
/// on: the only way to obtain a token is <see cref="SimulationRuntimeActivation.CreateToken"/>,
/// which is <see langword="internal"/> to this assembly and explicitly granted to the root
/// Clockwork (Clockwork.Simulation) assembly via <c>InternalsVisibleTo</c> - see
/// <c>AssemblyInfo.cs</c>. Production application code, third-party libraries, and test code
/// outside that trust boundary cannot construct a token and therefore cannot activate ambient
/// simulation context, even by accident.
/// </para>
/// <para>
/// Holding a token does not by itself do anything observable: it is only useful as an argument to
/// <see cref="SimulationExecutionContext.EnterRuntime(SimulationActivationToken, SimulationRuntimeIdentity)"/>.
/// </para>
/// </summary>
public sealed class SimulationActivationToken
{
    internal SimulationActivationToken()
    {
    }
}

/// <summary>
/// The single, narrow entry point for minting <see cref="SimulationActivationToken"/> instances.
/// This type (and the token constructor) are internal - only assemblies granted
/// <c>InternalsVisibleTo</c> by <c>Clockwork.Runtime</c> (currently just the root Clockwork
/// assembly, which is the simulation host) can call <see cref="CreateToken"/>.
/// </summary>
internal static class SimulationRuntimeActivation
{
    /// <summary>
    /// Creates a new activation token. Intended to be called exactly once per simulation host
    /// instance (e.g. once per <c>SimulationCluster&lt;TNode&gt;</c>), not shared across hosts.
    /// </summary>
    internal static SimulationActivationToken CreateToken() => new();
}

using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>Centralizes active-simulation assertion and runtime-service resolution for rewrite targets.</summary>
public static class SimulationRuntimeDispatch
{
    /// <summary>
    /// Requires an active simulation and returns the ambient execution snapshot.
    /// </summary>
    /// <param name="apiName">The fully-qualified name of the controlled API.</param>
    /// <returns>The active simulation execution snapshot.</returns>
    /// <exception cref="SimulationNotActiveException">Thrown when no simulation is active.</exception>
    public static SimulationExecutionSnapshot RequireActiveSimulation(string apiName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        return SimulationExecutionContext.Current ?? throw new SimulationNotActiveException(apiName);
    }

    /// <summary>
    /// Resolves the environment registered for the active simulation.
    /// </summary>
    /// <param name="apiName">The fully-qualified name of the controlled API, for diagnostics.</param>
    /// <returns>The active runtime and the environment registered for it.</returns>
    /// <exception cref="SimulationServiceMissingException">
    /// Thrown when a simulation is active but no environment is registered for its runtime.
    /// </exception>
    public static (SimulationRuntimeIdentity Runtime, ISimulationRuntimeEnvironment Environment, SimulationNodeIdentity? Node) RequireEnvironment(string apiName)
    {
        var snapshot = RequireActiveSimulation(apiName);

        if (!SimulationRuntimeServices.TryGet(snapshot.Runtime, out var resolved) || resolved is null)
        {
            throw new SimulationServiceMissingException(snapshot.Runtime, apiName);
        }

        return (snapshot.Runtime, resolved, snapshot.Node);
    }
}

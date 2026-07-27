using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>
/// <para>
/// The single dispatch primitive every deterministic BCL shim uses to decide what to do. It reads the
/// ambient <see cref="SimulationExecutionContext"/> exactly once and encodes the deterministic
/// contract:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>No simulation active</b> - returns <see langword="false"/>; the shim runs the real BCL API
/// (production pass-through, the overwhelmingly common case, a single cheap
/// <see cref="SimulationExecutionContext.IsActive"/> read).
/// </description></item>
/// <item><description>
/// <b>Simulation active and an environment is registered</b> - returns <see langword="true"/> with
/// the resolved environment and active node; the shim takes the
/// deterministic path.
/// </description></item>
/// <item><description>
/// <b>Simulation active but no environment registered</b> - throws
/// <see cref="SimulationServiceMissingException"/>; the shim must never silently use real time or real
/// randomness inside a simulation.
/// </description></item>
/// </list>
/// </summary>
public static class SimulationRuntimeDispatch
{
    /// <summary>
    /// Resolves the deterministic environment for the ambient simulation runtime, if a simulation is
    /// active. See the type remarks for the exact three-way contract.
    /// </summary>
    /// <param name="apiName">
    /// The fully-qualified name of the controlled API, used only for the diagnostic thrown when a
    /// simulation is active but no environment is registered.
    /// </param>
    /// <param name="environment">The resolved environment when the deterministic path applies.</param>
    /// <param name="node">The active node identity (may be <see langword="null"/> for cluster-level work).</param>
    /// <returns>
    /// <see langword="true"/> if the shim should take the deterministic path; <see langword="false"/>
    /// if it should run the real BCL API.
    /// </returns>
    /// <exception cref="SimulationServiceMissingException">
    /// Thrown when a simulation is active but no environment is registered for its runtime.
    /// </exception>
    public static bool TryGetEnvironment(
        string apiName,
        out ISimulationRuntimeEnvironment environment,
        out SimulationNodeIdentity? node)
    {
        var snapshot = SimulationExecutionContext.Current;
        if (snapshot is null)
        {
            environment = null!;
            node = null;
            return false;
        }

        if (!SimulationRuntimeServices.TryGet(snapshot.Runtime, out var resolved) || resolved is null)
        {
            throw new SimulationServiceMissingException(snapshot.Runtime, apiName);
        }

        environment = resolved;
        node = snapshot.Node;
        return true;
    }

    /// <summary>
    /// Resolves the ambient simulation runtime for diagnostics, throwing if none is active. Used by
    /// the crypto rejection path, which only runs when a simulation is known to be active.
    /// </summary>
    /// <param name="apiName">The fully-qualified name of the controlled API, for diagnostics.</param>
    /// <returns>The active runtime and the environment registered for it.</returns>
    /// <exception cref="SimulationServiceMissingException">
    /// Thrown when a simulation is active but no environment is registered for its runtime.
    /// </exception>
    internal static (SimulationRuntimeIdentity Runtime, ISimulationRuntimeEnvironment Environment, SimulationNodeIdentity? Node) RequireEnvironment(string apiName)
    {
        var snapshot = SimulationExecutionContext.Current
            ?? throw new InvalidOperationException(
                $"RequireEnvironment('{apiName}') was called with no active simulation. This is a shim bug.");

        if (!SimulationRuntimeServices.TryGet(snapshot.Runtime, out var resolved) || resolved is null)
        {
            throw new SimulationServiceMissingException(snapshot.Runtime, apiName);
        }

        return (snapshot.Runtime, resolved, snapshot.Node);
    }
}

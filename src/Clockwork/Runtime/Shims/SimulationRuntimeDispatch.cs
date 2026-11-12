using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>Centralizes active-simulation assertion and runtime-environment access for rewrite targets.</summary>
public static class SimulationRuntimeDispatch
{
    /// <summary>
    /// Requires an active simulation, on its logical thread, and returns the ambient execution snapshot.
    /// </summary>
    /// <param name="apiName">The fully-qualified name of the controlled API.</param>
    /// <returns>The active simulation execution snapshot.</returns>
    /// <exception cref="SimulationNotActiveException">Thrown when no simulation is active.</exception>
    /// <exception cref="SimulationThreadAffinityException">
    /// Thrown when the calling thread carries the simulation's ambient context but is not its logical thread.
    /// </exception>
    public static SimulationExecutionSnapshot RequireActiveSimulation(string apiName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        SimulationExecutionSnapshot snapshot =
            SimulationExecutionContext.Current ?? throw new SimulationNotActiveException(apiName);
        if (!snapshot.Runtime.Scheduler.IsSimulationThread)
        {
            ThrowThreadAffinityViolation(snapshot.Runtime, apiName);
        }

        return snapshot;
    }

    /// <summary>
    /// Resolves the environment carried by the active simulation.
    /// </summary>
    /// <param name="apiName">The fully-qualified name of the controlled API, for diagnostics.</param>
    /// <returns>The active runtime, its environment, and the active node.</returns>
    public static (SimulationRuntimeIdentity Runtime, ISimulationRuntimeEnvironment Environment, SimulationNodeIdentity? Node) RequireEnvironment(string apiName)
    {
        var snapshot = RequireActiveSimulation(apiName);
        return (snapshot.Runtime, snapshot.Runtime.Environment, snapshot.Node);
    }

    /// <summary>
    /// Rejects a controlled API called with <paramref name="runtime"/>'s ambient context off its logical thread. The
    /// scheduler records the violation so the thread driving it surfaces the failure even when the escaped work swallows
    /// the exception thrown here.
    /// </summary>
    /// <param name="runtime">The runtime whose ambient context the calling thread carries.</param>
    /// <param name="apiName">The fully-qualified name of the controlled API.</param>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowThreadAffinityViolation(SimulationRuntimeIdentity runtime, string apiName)
    {
        // The driving thread rethrows its own instance, captured with this thread's stack: one exception object must not
        // be thrown on two threads at once.
        var surfaced = new SimulationThreadAffinityException(apiName, runtime);
        ExceptionDispatchInfo.SetCurrentStackTrace(surfaced);
        runtime.Scheduler.ReportThreadAffinityViolation(surfaced);
        throw new SimulationThreadAffinityException(apiName, runtime);
    }
}

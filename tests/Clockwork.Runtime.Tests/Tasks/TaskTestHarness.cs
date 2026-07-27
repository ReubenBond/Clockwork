using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Tests.Tasks;

/// <summary>
/// Test helpers for driving the controlled task machinery: entering an active simulation with a
/// registered <see cref="ISimulationTaskCoordinator"/> (a real loop-backed one by default) and a node,
/// so <see cref="ControlledTaskRuntime"/> takes its controlled path.
/// </summary>
internal static class TaskTestHarness
{
    public const string DefaultNodeAddress = "10.0.0.1";

    public static SimulationRuntimeIdentity NewRuntime(int seed = 12345, string? description = null) =>
        new(Guid.NewGuid(), seed, description);

    /// <summary>
    /// Runs <paramref name="body"/> inside an active simulation with <paramref name="coordinator"/>
    /// registered and the default node entered.
    /// </summary>
    public static T RunInSimulation<T>(
        ISimulationTaskCoordinator coordinator,
        Func<T> body,
        string? nodeAddress = DefaultNodeAddress,
        SimulationRuntimeIdentity? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        var token = SimulationRuntimeActivation.CreateToken();
        var activeRuntime = runtime ?? NewRuntime();

        using (SimulationTaskCoordination.Register(token, activeRuntime, coordinator))
        using (SimulationExecutionContext.EnterRuntime(token, activeRuntime))
        {
            if (nodeAddress is null)
            {
                return body();
            }

            using (SimulationExecutionContext.EnterNode(new SimulationNodeIdentity(nodeAddress)))
            {
                return body();
            }
        }
    }

    public static void RunInSimulation(
        ISimulationTaskCoordinator coordinator,
        Action body,
        string? nodeAddress = DefaultNodeAddress,
        SimulationRuntimeIdentity? runtime = null) =>
        RunInSimulation<object?>(
            coordinator,
            () =>
            {
                body();
                return null;
            },
            nodeAddress,
            runtime);

    /// <summary>
    /// Runs <paramref name="body"/> inside an active simulation with a node entered but <em>no</em>
    /// coordinator registered, to exercise the missing-service failure path.
    /// </summary>
    public static void RunInSimulationWithoutCoordinator(Action body, string? nodeAddress = DefaultNodeAddress)
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = NewRuntime();

        using (SimulationExecutionContext.EnterRuntime(token, runtime))
        {
            if (nodeAddress is null)
            {
                body();
                return;
            }

            using (SimulationExecutionContext.EnterNode(new SimulationNodeIdentity(nodeAddress)))
            {
                body();
            }
        }
    }
}

namespace Clockwork.Runtime.Execution;

/// <summary>
/// <para>
/// Bundles everything a simulation-kernel component (a task queue, a node context) needs to
/// install/restore ambient <see cref="SimulationExecutionContext"/> scope around the work it
/// executes: the activation token proving the caller is the simulation host, the runtime identity
/// to make ambient, and - for node-scoped queues - the node identity to narrow to.
/// </para>
/// <para>
/// This is deliberately a plain, optional, additive configuration value (see e.g.
/// <c>SimulationTaskQueue</c>'s constructor in the root Clockwork.Simulation package): components
/// that are never given one keep behaving exactly as they did before ambient-context integration
/// existed - no scope is installed, no external-entry check runs, and nothing observable changes.
/// This is what lets pre-existing hand-written <c>SimulationCluster{TNode}</c>/<c>SimulationNode</c>
/// subclasses keep working unmodified while builder-created simulations (which the simulation host
/// controls end-to-end) opt every queue into full ambient integration.
/// </para>
/// </summary>
/// <param name="ActivationToken">The simulation host's activation capability token.</param>
/// <param name="Runtime">The runtime identity to make ambient.</param>
/// <param name="Node">
/// The node identity to make ambient, or <see langword="null"/> for cluster-level (non-node-scoped)
/// components.
/// </param>
public sealed record SimulationAmbientContextConfiguration(
    SimulationActivationToken ActivationToken,
    SimulationRuntimeIdentity Runtime,
    SimulationNodeIdentity? Node);

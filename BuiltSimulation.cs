namespace Clockwork;

/// <summary>
/// <para>
/// The <see cref="SimulationCluster{TNode}"/> produced by <see cref="SimulationBuilder.Build"/>.
/// Sealed and non-generic over any application type - it is a
/// <see cref="SimulationCluster{TNode}"/> of the common <see cref="SimulationNode"/> base type, which
/// is what lets plain <see cref="SimulationNodeHandle{TState}"/> instances and arbitrary custom
/// <see cref="SimulationNode"/> subclasses coexist as nodes of the same cluster (see
/// <see cref="SimulationBuilder"/>'s remarks for the precise scope and limits of this).
/// </para>
/// <para>
/// Includes an auto-created <see cref="Network"/> (a <see cref="SimulationNetwork"/> wired up to this
/// cluster's node list and a derived random generator) so simple simulations don't need to construct
/// one by hand. Disposing the cluster (via <see cref="SimulationCluster{TNode}.DisposeAsync"/>)
/// disposes every registered node that implements <see cref="IAsyncDisposable"/>/<see cref="IDisposable"/>,
/// and - for <see cref="SimulationNodeHandle{TState}"/> nodes - their state payload too, if it
/// implements either interface.
/// </para>
/// </summary>
public sealed class BuiltSimulation : SimulationCluster<SimulationNode>
{
    private readonly List<SimulationNode> _materializedNodes;

    internal BuiltSimulation(
        int seed,
        DateTimeOffset? startDateTime,
        IReadOnlyList<SimulationBuilderPendingNode> pendingNodes,
        CancellationToken cancellationToken,
        TimeZoneInfo? simulationTimeZone = null,
        Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy cryptoRandomnessPolicy =
            Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy.Reject)
        : base(seed, startDateTime, simulationTimeZone, cryptoRandomnessPolicy, cancellationToken)
    {
        _materializedNodes = new List<SimulationNode>(pendingNodes.Count);
        foreach (var pending in pendingNodes)
        {
            var context = new SimulationNodeContext(Clock, Guard, CreateDerivedRandom(), TaskQueue, ambientContext: CreateNodeAmbientContext(pending.Address));
            var node = pending.Materialize(context);
            RegisterNode(node);
            _materializedNodes.Add(node);
        }

        Network = new SimulationNetwork(() => Nodes, CreateDerivedRandom());
    }

    /// <summary>
    /// Gets the simulated network auto-created for this cluster, wired up to route between exactly
    /// the nodes registered on this cluster.
    /// </summary>
    public SimulationNetwork Network { get; }

    /// <summary>
    /// Gets the node registered with the given network address, or <see langword="null"/> if none
    /// exists. This is the primary way to retrieve nodes registered via
    /// <see cref="SimulationBuilder.AddCustomNode{TNode}(string, Func{SimulationNodeContext, TNode})"/>,
    /// whose custom-typed node cannot be handed back synchronously from the builder. Cast (or use
    /// <c>Nodes.OfType&lt;TNode&gt;()</c>) to recover the concrete type.
    /// </summary>
    /// <param name="address">The node's network address.</param>
    /// <returns>The matching node, or <see langword="null"/> if none is registered under that address.</returns>
    public SimulationNode? GetNodeByAddress(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        return Nodes.FirstOrDefault(n => string.Equals(n.NetworkAddress, address, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var node in _materializedNodes)
        {
            if (node is ISimulationNodeStateHolder holder)
            {
                await DisposeIfDisposableAsync(holder.StateObject).ConfigureAwait(false);
            }

            await DisposeIfDisposableAsync(node).ConfigureAwait(false);
        }
    }

    private static async ValueTask DisposeIfDisposableAsync(object? target)
    {
        switch (target)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}

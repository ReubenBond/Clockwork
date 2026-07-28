using Clockwork.Runtime.Random;

namespace Clockwork;

/// <summary>
/// <para>
/// The non-generic <see cref="SimulationCluster{TNode}"/> produced by <see cref="SimulationBuilder.Build"/>.
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
public sealed class SimulationCluster : SimulationCluster<SimulationNode>
{
    private readonly List<SimulationNode> _materializedNodes;
    private readonly HashSet<SimulationNode> _registeredNodes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _materializedAddresses = new(StringComparer.Ordinal);

    internal SimulationCluster(
        int seed,
        DateTimeOffset? startDateTime,
        IReadOnlyList<SimulationBuilderPendingNode> pendingNodes,
        CancellationToken cancellationToken,
        TimeZoneInfo? simulationTimeZone = null,
        CryptoRandomnessPolicy cryptoRandomnessPolicy = CryptoRandomnessPolicy.Reject)
        : base(seed, startDateTime, simulationTimeZone, cryptoRandomnessPolicy, cancellationToken)
    {
        _materializedNodes = new List<SimulationNode>(pendingNodes.Count);
        try
        {
            foreach (var pending in pendingNodes)
            {
                var nodeRandom = new SimulationRandom(
                    SeedAuthority.GetSiteSeed(SimulationSeedDomain.Application, pending.Address));
                var context = new SimulationNodeContext(
                    Clock,
                    Guard,
                    nodeRandom,
                    TaskQueue,
                    ambientContext: CreateNodeAmbientContext(pending.Address));
                var node = pending.Materialize(context);
                if (node is null)
                {
                    throw new InvalidOperationException(
                        $"The factory for node '{pending.Address}' returned null.");
                }

                _materializedNodes.Add(node);
                ValidateMaterializedNode(pending.Address, node);
                RegisterNode(node);
                _registeredNodes.Add(node);
            }

            Network = new SimulationNetwork(
                () => Nodes,
                new SimulationRandom(SeedAuthority.GetDomainSeed(SimulationSeedDomain.Network)));
        }
        catch (Exception materializationException)
        {
            var cleanupFailures = CleanupAfterFailedMaterialization();
            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "Simulation materialization and cleanup both failed.",
                    [materializationException, .. cleanupFailures]);
            }

            throw;
        }
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
    public SimulationNode? FindNode(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        return Nodes.FirstOrDefault(n => string.Equals(n.NetworkAddress, address, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds a node with the given network address and type, or returns <see langword="null"/>.
    /// </summary>
    public TNode? FindNode<TNode>(string address)
        where TNode : SimulationNode =>
        FindNode(address) as TNode;

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        List<Exception>? failures = null;
        foreach (var node in _materializedNodes)
        {
            if (_registeredNodes.Remove(node))
            {
                try
                {
                    UnregisterNode(node);
                }
                catch (Exception exception)
                {
                    AddFailure(ref failures, exception);
                }
            }

            if (node is ISimulationNodeStateHolder holder)
            {
                try
                {
                    await DisposeIfDisposableAsync(holder.StateObject).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AddFailure(ref failures, exception);
                }
            }

            try
            {
                await DisposeIfDisposableAsync(node).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AddFailure(ref failures, exception);
            }

            if (node is ISimulationNodeStateHolder stateHolder)
            {
                stateHolder.Detach();
            }
        }

        _materializedNodes.Clear();
        _materializedAddresses.Clear();

        if (failures is not null)
        {
            throw new AggregateException("One or more simulation nodes failed to dispose.", failures);
        }
    }

    private void ValidateMaterializedNode(string requestedAddress, SimulationNode node)
    {
        var actualAddress = node.NetworkAddress;
        if (string.IsNullOrEmpty(actualAddress))
        {
            throw new InvalidOperationException(
                $"The factory for node '{requestedAddress}' returned a node with a null or empty network address.");
        }

        if (!_materializedAddresses.Add(actualAddress))
        {
            throw new InvalidOperationException(
                $"The factory for node '{requestedAddress}' returned address '{actualAddress}', which collides with an already materialized node.");
        }

        if (!string.Equals(requestedAddress, actualAddress, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The factory for node '{requestedAddress}' returned a node with address '{actualAddress}'. " +
                "Custom node addresses must exactly match the requested address.");
        }
    }

    private List<Exception> CleanupAfterFailedMaterialization()
    {
        try
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
            return [];
        }
        catch (AggregateException exception)
        {
            return [.. exception.Flatten().InnerExceptions];
        }
        catch (Exception exception)
        {
            return [exception];
        }
    }

    private static void AddFailure(ref List<Exception>? failures, Exception exception)
    {
        failures ??= [];
        if (exception is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(exception);
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

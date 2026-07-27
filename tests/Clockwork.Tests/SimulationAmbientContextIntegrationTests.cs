using Clockwork.Runtime.Execution;

namespace Clockwork.Tests;

/// <summary>
/// <para>
/// Covers the Phase 2 ambient-context integration wired into <see cref="SimulationCluster{TNode}"/>,
/// <see cref="SimulationNodeContext"/>, and <see cref="SimulationBuilder"/>/<see cref="BuiltSimulation"/>:
/// callbacks executed through an ambient-integrated <see cref="SimulationTaskQueue"/> observe the
/// correct <see cref="SimulationExecutionContext"/> (runtime + node identity), two nodes never
/// observe each other's identity, and the ambient context is fully torn down again once the
/// driving call returns.
/// </para>
/// <para>
/// It also pins down the deliberate compatibility distinction between old hand-written
/// <see cref="SimulationCluster{TNode}"/> subclasses (which get ambient context on the
/// <em>cluster-level</em> queue only, preserving their exact prior node-queue behavior) and
/// <see cref="SimulationBuilder"/>-created simulations (which get full runtime+node ambient
/// integration on every node) - see the integration commit for the rationale.
/// </para>
/// </summary>
public sealed class SimulationAmbientContextIntegrationTests
{
    [Fact]
    public async Task BuiltSimulationNodeCallbackObservesTheCorrectRuntimeAndNodeIdentity()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var node = builder.AddNode("node-1");
        await using var cluster = builder.Build();

        SimulationExecutionSnapshot? captured = null;
        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => captured = SimulationExecutionContext.Current));

        cluster.RunUntilIdle();

        Assert.NotNull(captured);
        Assert.Equal(cluster.RuntimeIdentity.Id, captured!.Runtime.Id);
        Assert.NotNull(captured.Node);
        Assert.Equal("node-1", captured.Node!.Address);
    }

    [Fact]
    public async Task ClusterLevelCallbackObservesTheRuntimeButNoNodeIdentity()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        _ = builder.AddNode("node-1");
        await using var cluster = builder.Build();

        SimulationExecutionSnapshot? captured = null;
        cluster.TaskQueue.Enqueue(new ScheduledActionItem(() => captured = SimulationExecutionContext.Current));

        cluster.RunUntilIdle();

        Assert.NotNull(captured);
        Assert.Equal(cluster.RuntimeIdentity.Id, captured!.Runtime.Id);
        Assert.Null(captured.Node);
    }

    [Fact]
    public async Task TwoNodesNeverObserveEachOthersAmbientNodeIdentity()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var nodeA = builder.AddNode("node-a");
        var nodeB = builder.AddNode("node-b");
        await using var cluster = builder.Build();

        var observedAddressesInsideA = new List<string?>();
        var observedAddressesInsideB = new List<string?>();

        // Interleave several callbacks on each node so that neither node running "second" could
        // accidentally inherit stale ambient state left over from the other.
        for (var i = 0; i < 5; i++)
        {
            nodeA.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => observedAddressesInsideA.Add(SimulationExecutionContext.Current?.Node?.Address)));
            nodeB.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => observedAddressesInsideB.Add(SimulationExecutionContext.Current?.Node?.Address)));
        }

        cluster.RunUntilIdle();

        Assert.Equal(5, observedAddressesInsideA.Count);
        Assert.Equal(5, observedAddressesInsideB.Count);
        Assert.All(observedAddressesInsideA, address => Assert.Equal("node-a", address));
        Assert.All(observedAddressesInsideB, address => Assert.Equal("node-b", address));
    }

    [Fact]
    public async Task AmbientContextIsFullyClearedOnTheCallingThreadAfterRunUntilIdleReturns()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var node = builder.AddNode("node-1");
        await using var cluster = builder.Build();

        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));

        Assert.False(SimulationExecutionContext.IsActive);
        cluster.RunUntilIdle();
        Assert.False(SimulationExecutionContext.IsActive);
    }

    [Fact]
    public async Task AmbientContextIsFullyClearedOnTheCallingThreadAfterRunUntilReturns()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var node = builder.AddNode("node-1");
        await using var cluster = builder.Build();

        var executed = false;
        node.Context.TaskQueue.EnqueueAfter(() => executed = true, TimeSpan.FromSeconds(1));

        Assert.True(cluster.RunUntil(() => executed));
        Assert.False(SimulationExecutionContext.IsActive);
    }

    [Fact]
    public async Task OldHandWrittenSubclassGetsClusterLevelAmbientContextButNotNodeLevel()
    {
        // TestCluster/TestNode mirror the pre-Phase-2 hand-written subclass pattern (see
        // SimulationClusterTests.TestCluster): they build their own SimulationNodeContext directly
        // and never pass an ambient-context configuration, exactly as existing production
        // subclasses in this repository do today. This must keep behaving exactly as before.
        await using var cluster = new LegacyStyleTestCluster(seed: 7);
        var node = cluster.AddNode("legacy-node-1");

        SimulationExecutionSnapshot? capturedOnClusterQueue = null;
        cluster.TaskQueue.Enqueue(new ScheduledActionItem(() => capturedOnClusterQueue = SimulationExecutionContext.Current));

        var observedActiveInsideNodeCallback = true; // Overwritten below; starts true so a missed callback fails loudly.
        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => observedActiveInsideNodeCallback = SimulationExecutionContext.IsActive));

        cluster.RunUntilIdle();
        node.Context.RunUntilIdle();

        // Cluster-level queue: every SimulationCluster<TNode> (old or new) installs ambient context
        // on its own cluster-level queue unconditionally - this is purely additive and safe because
        // nothing currently reads it to change behavior.
        Assert.NotNull(capturedOnClusterQueue);
        Assert.Equal(cluster.RuntimeIdentity.Id, capturedOnClusterQueue!.Runtime.Id);

        // Node-level queue: a hand-written SimulationNode subclass that builds its own
        // SimulationNodeContext without an ambientContext argument gets no ambient scope at all on
        // that node's queue - preserving prior behavior exactly for existing production subclasses.
        Assert.False(observedActiveInsideNodeCallback);
    }

    [Fact]
    public async Task NewBuilderCreatedNodeGetsFullAmbientIntegrationUnlikeTheLegacySubclassPattern()
    {
        // Direct contrast with the previous test: a SimulationBuilder-created node's TaskQueue *is*
        // ambient-integrated, because BuiltSimulation explicitly passes CreateNodeAmbientContext(...)
        // when constructing each node's SimulationNodeContext.
        var builder = new SimulationBuilder().WithSeed(7);
        var node = builder.AddNode("builder-node-1");
        await using var cluster = builder.Build();

        var observedActiveInsideNodeCallback = false;
        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => observedActiveInsideNodeCallback = SimulationExecutionContext.IsActive));

        cluster.RunUntilIdle();

        Assert.True(observedActiveInsideNodeCallback);
    }

    private sealed class LegacyStyleTestCluster : SimulationCluster<LegacyStyleTestNode>
    {
        public LegacyStyleTestCluster(int seed)
            : base(seed, DateTimeOffset.UnixEpoch)
        {
        }

        public LegacyStyleTestNode AddNode(string address)
        {
            // Deliberately mirrors the pre-Phase-2 pattern: no ambientContext argument at all.
            var context = new SimulationNodeContext(Clock, Guard, CreateDerivedRandom(), TaskQueue);
            var node = new LegacyStyleTestNode(address, context);
            RegisterNode(node);
            return node;
        }

        protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
    }

    private sealed class LegacyStyleTestNode(string address, SimulationNodeContext context) : SimulationNode
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;
    }
}

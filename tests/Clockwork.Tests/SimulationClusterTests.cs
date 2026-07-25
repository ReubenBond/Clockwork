namespace Clockwork.Tests;

public sealed class SimulationClusterTests
{
    [Fact]
    public async Task ClusterRunsScheduledWorkDeterministically()
    {
        await using var cluster = new TestCluster(seed: 12345);
        var node = cluster.AddNode("node-1");
        var executed = false;

        node.Context.TaskQueue.EnqueueAfter(() => executed = true, TimeSpan.FromSeconds(5));

        Assert.True(cluster.RunUntil(() => executed));
        Assert.Equal(cluster.StartDateTime + TimeSpan.FromSeconds(5), cluster.TimeProvider.GetUtcNow());
    }

    [Fact]
    public async Task SuspendedNodeResumesAfterSimulatedDuration()
    {
        await using var cluster = new TestCluster(seed: 12345);
        var node = cluster.AddNode("node-1");

        node.SuspendFor(TimeSpan.FromSeconds(5));

        Assert.True(node.IsSuspended);
        Assert.True(cluster.RunUntil(() => !node.IsSuspended));
    }

    [Fact]
    public async Task NetworkPartitionsAreDirectionalAndHealable()
    {
        await using var cluster = new TestCluster(seed: 12345);
        _ = cluster.AddNode("node-1");
        _ = cluster.AddNode("node-2");

        cluster.Network.CreatePartition("node-1", "node-2");

        Assert.Equal(DeliveryStatus.Partitioned, cluster.Network.CheckDelivery("node-1", "node-2"));
        Assert.Equal(DeliveryStatus.Success, cluster.Network.CheckDelivery("node-2", "node-1"));

        cluster.Network.HealPartition("node-1", "node-2");
        Assert.True(cluster.Network.CanDeliver("node-1", "node-2"));
    }

    private sealed class TestCluster : SimulationCluster<TestNode>
    {
        public TestCluster(int seed)
            : base(seed, DateTimeOffset.UnixEpoch)
        {
            Network = new SimulationNetwork(() => Nodes, Random.Fork());
        }

        public SimulationNetwork Network { get; }

        public TestNode AddNode(string address)
        {
            var context = new SimulationNodeContext(Clock, Guard, CreateDerivedRandom(), TaskQueue);
            var node = new TestNode(address, context);
            RegisterNode(node);
            return node;
        }

        protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
    }

    private sealed class TestNode(string address, SimulationNodeContext context) : SimulationNode
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;
    }
}

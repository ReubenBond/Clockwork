namespace Clockwork.Tests;

public sealed class SimulationNetworkTests
{
    [Fact]
    public void LoopbackDeliveryAlwaysSucceedsRegardlessOfPartitionsOrDropRate()
    {
        var network = CreateNetwork([], seed: 1);
        network.CreatePartition("node-1", "node-1");
        network.MessageDropRate = 1.0;

        Assert.Equal(DeliveryStatus.Success, network.CheckDelivery("node-1", "node-1"));
    }

    [Fact]
    public void PartitionTakesPrecedenceOverRandomDrop()
    {
        var network = CreateNetwork([], seed: 1);
        network.MessageDropRate = 0; // Would never drop on its own.
        network.CreatePartition("node-1", "node-2");

        Assert.Equal(DeliveryStatus.Partitioned, network.CheckDelivery("node-1", "node-2"));
    }

    [Fact]
    public void SameSeedProducesTheSameSequenceOfDropDecisions()
    {
        var firstDecisions = CollectDropDecisions(seed: 7);
        var secondDecisions = CollectDropDecisions(seed: 7);

        Assert.Equal(firstDecisions, secondDecisions);

        // A 50% drop rate over many trials should drop at least once and succeed at least once,
        // otherwise the test would not actually be exercising both branches.
        Assert.Contains(DeliveryStatus.Dropped, firstDecisions);
        Assert.Contains(DeliveryStatus.Success, firstDecisions);
    }

    [Fact]
    public void DifferentSeedsCanProduceDifferentDropDecisionSequences()
    {
        var firstDecisions = CollectDropDecisions(seed: 7);
        var secondDecisions = CollectDropDecisions(seed: 99);

        Assert.NotEqual(firstDecisions, secondDecisions);
    }

    [Fact]
    public void DisabledDelaysAlwaysReturnZero()
    {
        var network = CreateNetwork([], seed: 1);
        network.EnableDelays = false;

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(TimeSpan.Zero, network.GetMessageDelay());
        }
    }

    [Fact]
    public void EnabledDelaysAreBoundedByBaseDelayPlusJitterAndAreReproducible()
    {
        var baseDelay = TimeSpan.FromMilliseconds(10);
        var maxJitter = TimeSpan.FromMilliseconds(5);

        SimulationNetwork Build(int seed)
        {
            var network = CreateNetwork([], seed);
            network.EnableDelays = true;
            network.BaseMessageDelay = baseDelay;
            network.MaxJitter = maxJitter;
            return network;
        }

        var first = Build(seed: 3);
        var second = Build(seed: 3);

        for (var i = 0; i < 20; i++)
        {
            var firstDelay = first.GetMessageDelay();
            var secondDelay = second.GetMessageDelay();

            Assert.Equal(firstDelay, secondDelay);
            Assert.InRange(firstDelay, baseDelay, baseDelay + maxJitter);
        }
    }

    [Fact]
    public void IsolateNodeBlocksDeliveryInBothDirectionsForAllPeersAndReconnectHeals()
    {
        var nodes = new List<SimulationNode> { new TestNode("node-1"), new TestNode("node-2"), new TestNode("node-3") };
        var network = CreateNetwork(nodes, seed: 1);

        network.IsolateNode("node-1");

        Assert.True(network.IsNodeIsolated("node-1"));
        Assert.False(network.CanDeliver("node-1", "node-2"));
        Assert.False(network.CanDeliver("node-2", "node-1"));
        Assert.False(network.CanDeliver("node-1", "node-3"));
        Assert.True(network.CanDeliver("node-2", "node-3"));

        network.ReconnectNode("node-1");

        Assert.False(network.IsNodeIsolated("node-1"));
        Assert.True(network.CanDeliver("node-1", "node-2"));
        Assert.True(network.CanDeliver("node-2", "node-1"));
    }

    [Fact]
    public void HealAllPartitionsRemovesEveryPartition()
    {
        var network = CreateNetwork([], seed: 1);
        network.CreateBidirectionalPartition("node-1", "node-2");
        network.CreatePartition("node-2", "node-3");

        network.HealAllPartitions();

        Assert.True(network.CanDeliver("node-1", "node-2"));
        Assert.True(network.CanDeliver("node-2", "node-1"));
        Assert.True(network.CanDeliver("node-2", "node-3"));
    }

    private static List<DeliveryStatus> CollectDropDecisions(int seed)
    {
        var network = CreateNetwork([], seed);
        network.MessageDropRate = 0.5;

        var decisions = new List<DeliveryStatus>();
        for (var i = 0; i < 50; i++)
        {
            decisions.Add(network.CheckDelivery("node-1", "node-2"));
        }

        return decisions;
    }

    private static SimulationNetwork CreateNetwork(IReadOnlyList<SimulationNode> nodes, int seed) => new(() => nodes, new SimulationRandom(seed));

    private sealed class TestNode(string address) : SimulationNode
    {
        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;

        public override SimulationNodeContext Context { get; } = new(
            new SimulationClock(DateTimeOffset.UnixEpoch),
            new SingleThreadedGuard(),
            new SimulationRandom(0));
    }
}

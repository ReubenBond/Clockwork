using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tasks;

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

        Assert.Equal(SimulationExecutionReason.ConditionMet, cluster.RunUntil(() => executed).Reason);
        Assert.Equal(cluster.StartDateTime + TimeSpan.FromSeconds(5), cluster.TimeProvider.GetUtcNow());
    }

    [Fact]
    public async Task SuspendedNodeResumesAfterSimulatedDuration()
    {
        await using var cluster = new TestCluster(seed: 12345);
        var node = cluster.AddNode("node-1");

        node.SuspendFor(TimeSpan.FromSeconds(5));

        Assert.True(node.IsSuspended);
        Assert.Equal(SimulationExecutionReason.ConditionMet, cluster.RunUntil(() => !node.IsSuspended).Reason);
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

    [Fact]
    public async Task RunUntilReportsIdleWhenThereIsNoPendingWork()
    {
        await using var cluster = new TestCluster(seed: 12345);
        _ = cluster.AddNode("node-1");

        Assert.Equal(SimulationExecutionReason.Idle, cluster.RunUntil(() => false, maxIterations: 100).Reason);
    }

    [Fact]
    public async Task RunUntilReportsWhenTheNextDueTimeExceedsMaxSimulatedTimeAdvance()
    {
        await using var cluster = new TestCluster(seed: 12345)
        {
            MaxSimulatedTimeAdvance = TimeSpan.FromSeconds(1),
        };
        var node = cluster.AddNode("node-1");

        // Schedule work far beyond the stuck-detection threshold; the condition never becomes true.
        node.Context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(10));

        Assert.Equal(
            SimulationExecutionReason.MaxSimulatedTimeAdvanceExceeded,
            cluster.RunUntil(() => false, maxIterations: 100).Reason);
    }

    [Fact]
    public async Task RunUntilIdleReportsTheNumberOfTasksExecutedBeforeGoingIdle()
    {
        await using var cluster = new TestCluster(seed: 12345);
        var node = cluster.AddNode("node-1");
        var executedCount = 0;

        for (var i = 0; i < 3; i++)
        {
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => executedCount++));
        }

        Assert.Equal(3, cluster.RunUntilIdle().StepsExecuted);
        Assert.Equal(3, executedCount);
    }

    [Fact]
    public async Task CreateDerivedRandomIsReproducibleAcrossClustersWithTheSameSeed()
    {
        await using var first = new TestCluster(seed: 42);
        await using var second = new TestCluster(seed: 42);

        var firstDerived = first.CreateDerivedRandom();
        var secondDerived = second.CreateDerivedRandom();

        Assert.Equal(firstDerived.Next(), secondDerived.Next());
        Assert.Equal(firstDerived.NextGuid(), secondDerived.NextGuid());

        // The parent stream must also stay in sync after deriving a child stream.
        Assert.Equal(first.Random.Next(), second.Random.Next());
    }

    [Fact]
    public async Task DisposeFailureStillReleasesInfrastructureAndSecondDisposeIsANoOp()
    {
        var cluster = new FailingDisposeCluster(seed: 42);
        var runtime = cluster.RuntimeIdentity;
        var teardownToken = cluster.TeardownCancellationToken;

        var exception = await Assert.ThrowsAsync<AggregateException>(() => cluster.DisposeAsync().AsTask());

        var failure = Assert.Single(exception.InnerExceptions);
        Assert.Equal("cluster cleanup failed", failure.Message);
        Assert.Equal(1, cluster.DisposeCallCount);
        Assert.True(teardownToken.IsCancellationRequested);
        Assert.False(SimulationRuntimeServices.TryGet(runtime, out _));
        Assert.False(SimulationTaskCoordination.TryGet(runtime, out _));

        await cluster.DisposeAsync();
        Assert.Equal(1, cluster.DisposeCallCount);
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

    private sealed class FailingDisposeCluster(int seed) : SimulationCluster<TestNode>(seed, DateTimeOffset.UnixEpoch)
    {
        public int DisposeCallCount { get; private set; }

        protected override ValueTask DisposeAsyncCore()
        {
            DisposeCallCount++;
            throw new InvalidOperationException("cluster cleanup failed");
        }
    }
}

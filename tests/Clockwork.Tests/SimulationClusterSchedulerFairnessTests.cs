using Clockwork.Runtime.Tasks;

namespace Clockwork.Tests;

public sealed class SimulationClusterSchedulerFairnessTests
{
    [Fact]
    public async Task ClusterAndNodeQueuesCannotStarveEachOther()
    {
        await using var cluster = new FairnessTestCluster();
        var node = cluster.AddNode("node-a");
        var executionOrder = new List<string>();

        EnqueueRepeatedly(cluster.TaskQueue, "cluster", 4);
        EnqueueRepeatedly(node.Context.TaskQueue, "node-a", 4);

        RunSteps(cluster, 8);

        Assert.Equal(
            ["cluster", "node-a", "cluster", "node-a", "cluster", "node-a", "cluster", "node-a"],
            executionOrder);

        void EnqueueRepeatedly(SimulationTaskQueue queue, string source, int remaining)
        {
            queue.Enqueue(new ScheduledActionItem(() =>
            {
                executionOrder.Add(source);
                if (remaining > 1)
                {
                    EnqueueRepeatedly(queue, source, remaining - 1);
                }
            }));
        }
    }

    [Fact]
    public async Task SortedNodeQueuesCannotStarveEachOther()
    {
        await using var cluster = new FairnessTestCluster();
        var nodeB = cluster.AddNode("node-b");
        var nodeA = cluster.AddNode("node-a");
        var executionOrder = new List<string>();

        EnqueueRepeatedly(nodeA, 4);
        EnqueueRepeatedly(nodeB, 4);

        RunSteps(cluster, 8);

        Assert.Equal(
            ["node-a", "node-b", "node-a", "node-b", "node-a", "node-b", "node-a", "node-b"],
            executionOrder);

        void EnqueueRepeatedly(FairnessTestNode node, int remaining)
        {
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() =>
            {
                executionOrder.Add(node.NetworkAddress);
                if (remaining > 1)
                {
                    EnqueueRepeatedly(node, remaining - 1);
                }
            }));
        }
    }

    [Fact]
    public async Task NodeQueueAndControlledLoopCannotStarveEachOther()
    {
        await using var cluster = new FairnessTestCluster();
        var node = cluster.AddNode("node-a");
        var executionOrder = new List<string>();

        EnqueueNode(4);
        EnqueueControlled(4);

        RunSteps(cluster, 8);

        Assert.Equal(
            ["node-a", "controlled", "node-a", "controlled", "node-a", "controlled", "node-a", "controlled"],
            executionOrder);

        void EnqueueNode(int remaining)
        {
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() =>
            {
                executionOrder.Add("node-a");
                if (remaining > 1)
                {
                    EnqueueNode(remaining - 1);
                }
            }));
        }

        void EnqueueControlled(int remaining)
        {
            cluster.ScheduleControlled(() =>
            {
                executionOrder.Add("controlled");
                if (remaining > 1)
                {
                    EnqueueControlled(remaining - 1);
                }
            });
        }
    }

    [Fact]
    public async Task SchedulingOrderIsClusterThenOrdinalNodesThenControlledLoop()
    {
        await using var cluster = new FairnessTestCluster();
        var nodeB = cluster.AddNode("node-b");
        var nodeA = cluster.AddNode("node-a");
        var executionOrder = new List<string>();

        cluster.TaskQueue.Enqueue(new ScheduledActionItem(() => executionOrder.Add("cluster")));
        nodeB.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => executionOrder.Add("node-b")));
        nodeA.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => executionOrder.Add("node-a")));
        cluster.ScheduleControlled(() => executionOrder.Add("controlled"));

        RunSteps(cluster, 4);

        Assert.Equal(["cluster", "node-a", "node-b", "controlled"], executionOrder);
    }

    [Fact]
    public async Task CursorRemainsValidWhenNodesAreSuspendedUnregisteredAndRegistered()
    {
        await using var cluster = new FairnessTestCluster();
        var nodeA = cluster.AddNode("node-a");
        var nodeB = cluster.AddNode("node-b");
        var executionOrder = new List<string>();

        cluster.TaskQueue.Enqueue(new ScheduledActionItem(() => executionOrder.Add("cluster")));
        nodeA.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => executionOrder.Add("node-a")));
        nodeB.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => executionOrder.Add("node-b")));
        cluster.ScheduleControlled(() => executionOrder.Add("controlled"));

        Assert.True(cluster.RunOneStep());
        nodeA.Suspend();
        Assert.True(cluster.RunOneStep());

        cluster.RemoveNode(nodeB);
        var nodeC = cluster.AddNode("node-c");
        nodeC.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => executionOrder.Add("node-c")));
        nodeA.Resume();

        RunSteps(cluster, 3);

        Assert.Equal(["cluster", "node-b", "node-c", "controlled", "node-a"], executionOrder);
    }

    private static void RunSteps(FairnessTestCluster cluster, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Assert.True(cluster.RunOneStep());
        }
    }

    private sealed class FairnessTestCluster : SimulationCluster<FairnessTestNode>
    {
        public FairnessTestCluster()
            : base(seed: 1, DateTimeOffset.UnixEpoch)
        {
        }

        public FairnessTestNode AddNode(string address)
        {
            var context = new SimulationNodeContext(Clock, Guard, ForkRandom(), TaskQueue);
            var node = new FairnessTestNode(address, context);
            RegisterNode(node);
            return node;
        }

        public void RemoveNode(FairnessTestNode node) => UnregisterNode(node);

        public bool RunOneStep() => RunOneTaskRoundRobin();

        public void ScheduleControlled(Action action)
        {
            Assert.True(SimulationTaskCoordination.TryGet(RuntimeIdentity, out var coordinator));
            coordinator!.Schedule(node: null, action);
        }

        protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
    }

    private sealed class FairnessTestNode(string address, SimulationNodeContext context) : SimulationNode
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;
    }
}

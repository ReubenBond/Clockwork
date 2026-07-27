namespace Clockwork.Tests;

/// <summary>
/// Covers the <see cref="SimulationBuilder"/> fluent composition API and the
/// <see cref="BuiltSimulation"/>/<see cref="SimulationNodeHandle{TState}"/> types it produces:
/// building working simulations without a hand-written <see cref="SimulationCluster{TNode}"/>/
/// <see cref="SimulationNode"/> subclass, compatibility with existing hand-written subclasses, the
/// heterogeneous node registration foundation, handle lifetime rules, disposal, and determinism.
/// </summary>
public sealed class SimulationBuilderTests
{
    [Fact]
    public async Task BuildThrowsWhenNoSeedWasSpecified()
    {
        var builder = new SimulationBuilder();
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("WithSeed", exception.Message, StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task PlainHandleNodeWorksWithoutAnySubclass()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var node = builder.AddNode("node-1");
        await using var cluster = builder.Build();

        var executed = false;
        node.Context.TaskQueue.EnqueueAfter(() => executed = true, TimeSpan.FromSeconds(5));

        Assert.True(cluster.RunUntil(() => executed));
        Assert.Equal(cluster.StartDateTime + TimeSpan.FromSeconds(5), cluster.TimeProvider.GetUtcNow());
    }

    [Fact]
    public async Task StateHandleNodeCarriesTheGivenPayload()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var counter = builder.AddNode("counter", state: 0);
        await using var cluster = builder.Build();

        Assert.Equal(0, counter.State);
        counter.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));
        cluster.RunUntilIdle();
        Assert.Equal(0, counter.State);
    }

    [Fact]
    public async Task StateFactoryOverloadReceivesTheNodesOwnContext()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        SimulationNodeContext? capturedContext = null;
        var node = builder.AddNode("node-1", context =>
        {
            capturedContext = context;
            return context.Random.Next();
        });

        await using var cluster = builder.Build();

        Assert.NotNull(capturedContext);
        Assert.Same(capturedContext, node.Context);
    }

    [Fact]
    public async Task HandleContextAndStateThrowBeforeBuildIsCalled()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var node = builder.AddNode("node-1", state: 42);

        var contextException = Assert.Throws<InvalidOperationException>(() => node.Context);
        var stateException = Assert.Throws<InvalidOperationException>(() => node.State);
        Assert.Contains("node-1", contextException.Message, StringComparison.Ordinal);
        Assert.Contains("node-1", stateException.Message, StringComparison.Ordinal);
        Assert.False(node.IsInitialized);

        await using var cluster = builder.Build();
        Assert.True(node.IsInitialized);
        Assert.Equal(42, node.State);
        _ = node.Context; // Does not throw once built.
    }

    [Fact]
    public async Task DuplicateAddressAcrossAnyOverloadThrows()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        _ = builder.AddNode("dup");

        Assert.Throws<ArgumentException>(() => builder.AddNode("dup", state: 1));
        Assert.Throws<ArgumentException>(() => builder.AddCustomNode("dup", context => new CustomNode("dup", context)));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task WithStartDateTimeAndWithCancellationTokenFlowThroughToTheBuiltCluster()
    {
        using var cts = new CancellationTokenSource();
        var startDateTime = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var builder = new SimulationBuilder()
            .WithSeed(1)
            .WithStartDateTime(startDateTime)
            .WithCancellationToken(cts.Token);

        await using var cluster = builder.Build();

        Assert.Equal(startDateTime, cluster.StartDateTime);
        Assert.False(cluster.TeardownCancellationToken.IsCancellationRequested);

        await cts.CancelAsync();
        Assert.True(cluster.TeardownCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task CustomNodeSubclassCanBeRegisteredAlongsidePlainHandles()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var handle = builder.AddNode("handle-node", state: 0);
        builder.AddCustomNode("custom-node", context => new CustomNode("custom-node", context));

        await using var cluster = builder.Build();

        Assert.Equal(2, cluster.Nodes.Count);
        var custom = Assert.IsType<CustomNode>(cluster.GetNodeByAddress("custom-node"));
        Assert.Equal("custom-node", custom.NetworkAddress);
        Assert.Same(custom, cluster.Nodes.OfType<CustomNode>().Single());
        Assert.Same(handle, cluster.GetNodeByAddress("handle-node"));

        custom.RecordGreeting("hello");
        Assert.Equal("hello", custom.LastGreeting);
    }

    [Fact]
    public async Task GetNodeByAddressReturnsNullForAnUnknownAddress()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        _ = builder.AddNode("node-1");
        await using var cluster = builder.Build();

        Assert.Null(cluster.GetNodeByAddress("does-not-exist"));
    }

    [Fact]
    public async Task NetworkIsAutoCreatedAndRoutesBetweenRegisteredNodes()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        _ = builder.AddNode("node-1");
        _ = builder.AddNode("node-2");
        await using var cluster = builder.Build();

        cluster.Network.CreatePartition("node-1", "node-2");

        Assert.Equal(DeliveryStatus.Partitioned, cluster.Network.CheckDelivery("node-1", "node-2"));
        Assert.Equal(DeliveryStatus.Success, cluster.Network.CheckDelivery("node-2", "node-1"));

        cluster.Network.HealPartition("node-1", "node-2");
        Assert.True(cluster.Network.CanDeliver("node-1", "node-2"));
    }

    [Fact]
    public async Task DisposeAsyncDisposesStatePayloadsAndCustomNodesThatAreDisposable()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var handle = builder.AddNode("handle-node", new DisposableState());
        builder.AddCustomNode("custom-node", context => new DisposableCustomNode("custom-node", context));

        var cluster = builder.Build();
        var custom = (DisposableCustomNode)cluster.GetNodeByAddress("custom-node")!;
        var state = handle.State;

        await cluster.DisposeAsync();

        Assert.True(state.Disposed);
        Assert.True(custom.Disposed);
    }

    [Fact]
    public async Task SameSeedProducesReproducibleDerivedRandomValues()
    {
        var firstBuilder = new SimulationBuilder().WithSeed(42);
        _ = firstBuilder.AddNode("node-1", context => context.Random.Next());
        await using var first = firstBuilder.Build();

        var secondBuilder = new SimulationBuilder().WithSeed(42);
        _ = secondBuilder.AddNode("node-1", context => context.Random.Next());
        await using var second = secondBuilder.Build();

        var firstNode = (SimulationNodeHandle<int>)first.Nodes[0];
        var secondNode = (SimulationNodeHandle<int>)second.Nodes[0];
        Assert.Equal(firstNode.State, secondNode.State);
    }

    [Fact]
    public async Task HandWrittenSimulationClusterSubclassesContinueToWorkUnaffectedByTheBuilder()
    {
        // The builder/BuiltSimulation are purely additive; existing hand-written
        // SimulationCluster<TNode> subclasses must keep working exactly as before.
        await using var cluster = new LegacyStyleCluster(seed: 7);
        var node = cluster.AddNode("node-1");
        var executed = false;
        node.Context.TaskQueue.EnqueueAfter(() => executed = true, TimeSpan.FromSeconds(1));

        Assert.True(cluster.RunUntil(() => executed));
    }

    private sealed class CustomNode(string address, SimulationNodeContext context) : SimulationNode
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;

        public string? LastGreeting { get; private set; }

        public void RecordGreeting(string greeting) => LastGreeting = greeting;
    }

    private sealed class DisposableState : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class DisposableCustomNode(string address, SimulationNodeContext context) : SimulationNode, IAsyncDisposable
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LegacyStyleCluster : SimulationCluster<LegacyStyleNode>
    {
        public LegacyStyleCluster(int seed)
            : base(seed, DateTimeOffset.UnixEpoch)
        {
        }

        public LegacyStyleNode AddNode(string address)
        {
            var context = new SimulationNodeContext(Clock, Guard, CreateDerivedRandom(), TaskQueue);
            var node = new LegacyStyleNode(address, context);
            RegisterNode(node);
            return node;
        }

        protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
    }

    private sealed class LegacyStyleNode(string address, SimulationNodeContext context) : SimulationNode
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;
    }
}

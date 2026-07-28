using System.Reflection;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tasks;

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
    public async Task BuildCannotBeCalledTwice()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var handle = builder.AddNode("node-1", state: 42);
        await using var cluster = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("cannot be reused", exception.Message, StringComparison.Ordinal);
        Assert.Single(cluster.Nodes);
        Assert.Same(handle, cluster.Nodes[0]);
        Assert.Equal(42, handle.State);
    }

    [Fact]
    public void ThrowingSecondFactoryCleansCreatedNodesAndRuntimeRegistrationsAndPreventsReuse()
    {
        var events = new List<string>();
        SimulationRuntimeIdentity? runtime = null;
        var factoryFailure = new InvalidOperationException("second factory failed");
        var builder = new SimulationBuilder().WithSeed(1);
        builder.AddCustomNode("first", context =>
        {
            runtime = GetRuntimeIdentity(context);
            return new TrackingNode("first", context, events);
        });
        builder.AddCustomNode<CustomNode>("second", _ => throw factoryFailure);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Same(factoryFailure, exception);
        Assert.Equal(["first"], events);
        Assert.NotNull(runtime);
        Assert.False(SimulationRuntimeServices.TryGet(runtime, out _));
        Assert.False(SimulationTaskCoordination.TryGet(runtime, out _));

        var reuseException = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("cannot be reused", reuseException.Message, StringComparison.Ordinal);
        Assert.Equal(["first"], events);
    }

    [Fact]
    public void FactoryAndCleanupFailuresAreAggregatedAfterRemainingCleanupAndInfrastructureRelease()
    {
        var events = new List<string>();
        SimulationRuntimeIdentity? runtime = null;
        var factoryFailure = new InvalidOperationException("third factory failed");
        var builder = new SimulationBuilder().WithSeed(1);
        builder.AddCustomNode("first", context =>
        {
            runtime = GetRuntimeIdentity(context);
            return new TrackingNode("first", context, events, throwOnDispose: true);
        });
        builder.AddCustomNode("second", context => new TrackingNode("second", context, events));
        builder.AddCustomNode<CustomNode>("third", _ => throw factoryFailure);

        var exception = Assert.Throws<AggregateException>(() => builder.Build());

        Assert.Equal(
            ["third factory failed", "first disposal failed"],
            exception.InnerExceptions.Select(static error => error.Message));
        Assert.Equal(["first", "second"], events);
        Assert.NotNull(runtime);
        Assert.False(SimulationRuntimeServices.TryGet(runtime, out _));
        Assert.False(SimulationTaskCoordination.TryGet(runtime, out _));
    }

    [Fact]
    public void FailedMaterializationDisposesStateAndDetachesHandle()
    {
        var state = new DisposableState();
        var builder = new SimulationBuilder().WithSeed(1);
        var handle = builder.AddNode("first", state);
        builder.AddCustomNode<CustomNode>("second", _ => throw new InvalidOperationException("boom"));

        _ = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.True(state.Disposed);
        Assert.False(handle.IsInitialized);
        Assert.Throws<InvalidOperationException>(() => handle.Context);
        Assert.Throws<InvalidOperationException>(() => handle.State);
    }

    [Fact]
    public void BuildRejectsNullCustomFactoryResult()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        builder.AddCustomNode<CustomNode>("node-1", _ => null!);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("node-1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRejectsEmptyCustomNodeAddressAndDisposesTheNode()
    {
        var events = new List<string>();
        var builder = new SimulationBuilder().WithSeed(1);
        builder.AddCustomNode("node-1", context => new TrackingNode(string.Empty, context, events));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("null or empty", exception.Message, StringComparison.Ordinal);
        Assert.Equal([""], events);
    }

    [Fact]
    public void BuildRejectsNullCustomNodeAddressAndDisposesTheNode()
    {
        DisposableAddressNode? node = null;
        var builder = new SimulationBuilder().WithSeed(1);
        builder.AddCustomNode("node-1", context =>
        {
            node = new DisposableAddressNode(null!, context);
            return node;
        });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("null or empty", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(node);
        Assert.True(node.Disposed);
    }

    [Fact]
    public void BuildRejectsCustomNodeAddressMismatchAndDisposesTheNode()
    {
        var events = new List<string>();
        var builder = new SimulationBuilder().WithSeed(1);
        builder.AddCustomNode("node", context => new TrackingNode("NODE", context, events));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("exactly match", exception.Message, StringComparison.Ordinal);
        Assert.Contains("node", exception.Message, StringComparison.Ordinal);
        Assert.Contains("NODE", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["NODE"], events);
    }

    [Fact]
    public void BuildRejectsMaterializedAddressCollisionAndDisposesEveryCreatedNode()
    {
        var events = new List<string>();
        var builder = new SimulationBuilder().WithSeed(1);
        builder.AddCustomNode("shared", context => new TrackingNode("shared", context, events));
        builder.AddCustomNode("requested", context => new TrackingNode("shared", context, events));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("collides", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["shared", "shared"], events);
    }

    [Fact]
    public async Task PlainHandleNodeWorksWithoutAnySubclass()
    {
        var builder = new SimulationBuilder().WithSeed(1);
        var node = builder.AddNode("node-1");
        await using var cluster = builder.Build();

        var executed = false;
        node.Context.TaskQueue.EnqueueAfter(() => executed = true, TimeSpan.FromSeconds(5));

        Assert.Equal(SimulationExecutionReason.ConditionMet, cluster.RunUntil(() => executed).Reason);
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
    public async Task DisposeAsyncContinuesCleanupAggregatesFailuresAndIsIdempotent()
    {
        var events = new List<string>();
        var firstState = new TrackingDisposable("state-failure", events, throwOnDispose: true);
        var laterState = new TrackingDisposable("state-success", events);
        var builder = new SimulationBuilder().WithSeed(1);
        var firstHandle = builder.AddNode("state-failure", firstState);
        builder.AddCustomNode(
            "node-failure",
            context => new TrackingNode("node-failure", context, events, throwOnDispose: true));
        var laterHandle = builder.AddNode("state-success", laterState);
        builder.AddCustomNode("node-success", context => new TrackingNode("node-success", context, events));
        var cluster = builder.Build();
        var runtime = cluster.RuntimeIdentity;
        var teardownToken = cluster.TeardownCancellationToken;

        var exception = await Assert.ThrowsAsync<AggregateException>(() => cluster.DisposeAsync().AsTask());

        Assert.Equal(
            ["state-failure", "node-failure", "state-success", "node-success"],
            events);
        Assert.Equal(
            ["state-failure disposal failed", "node-failure disposal failed"],
            exception.InnerExceptions.Select(static error => error.Message));
        Assert.True(firstState.Disposed);
        Assert.True(laterState.Disposed);
        Assert.False(firstHandle.IsInitialized);
        Assert.False(laterHandle.IsInitialized);
        Assert.Empty(cluster.Nodes);
        Assert.True(teardownToken.IsCancellationRequested);
        Assert.False(SimulationRuntimeServices.TryGet(runtime, out _));
        Assert.False(SimulationTaskCoordination.TryGet(runtime, out _));

        await cluster.DisposeAsync();
        Assert.Equal(
            ["state-failure", "node-failure", "state-success", "node-success"],
            events);
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
    public async Task NodeRandomUsesStableApplicationDomainSeedAcrossTopologyOrderEdits()
    {
        const int seed = 42;
        var firstBuilder = new SimulationBuilder().WithSeed(seed);
        var firstStable = firstBuilder.AddNode("stable");
        _ = firstBuilder.AddNode("peer");
        await using var first = firstBuilder.Build();

        var editedBuilder = new SimulationBuilder().WithSeed(seed);
        _ = editedBuilder.AddNode("added");
        _ = editedBuilder.AddNode("peer");
        var editedStable = editedBuilder.AddNode("stable");
        await using var edited = editedBuilder.Build();

        var expectedSeed = new SimulationSeedAuthority(seed)
            .GetSiteSeed(SimulationSeedDomain.Application, "stable");
        Assert.Equal(expectedSeed, firstStable.Context.Random.Seed);
        Assert.Equal(expectedSeed, editedStable.Context.Random.Seed);
        Assert.Equal(firstStable.Context.Random.Next(), editedStable.Context.Random.Next());
        Assert.Equal(firstStable.Context.Random.Next(), editedStable.Context.Random.Next());
    }

    [Fact]
    public async Task NetworkRandomUsesFixedNetworkDomainAcrossTopologyOrderEdits()
    {
        const int seed = 42;
        const double dropRate = 0.5;
        var firstBuilder = new SimulationBuilder().WithSeed(seed);
        _ = firstBuilder.AddNode("stable");
        _ = firstBuilder.AddNode("peer");
        await using var first = firstBuilder.Build();

        var editedBuilder = new SimulationBuilder().WithSeed(seed);
        _ = editedBuilder.AddNode("added");
        _ = editedBuilder.AddNode("peer");
        _ = editedBuilder.AddNode("stable");
        await using var edited = editedBuilder.Build();

        var expectedRandom = new SimulationRandom(
            new SimulationSeedAuthority(seed).GetDomainSeed(SimulationSeedDomain.Network));
        var expected = Enumerable.Range(0, 32)
            .Select(_ => expectedRandom.Chance(dropRate) ? DeliveryStatus.Dropped : DeliveryStatus.Success)
            .ToArray();
        var firstOutcomes = GetDeliveryOutcomes(first.Network, dropRate);
        var editedOutcomes = GetDeliveryOutcomes(edited.Network, dropRate);

        Assert.Equal(expected, firstOutcomes);
        Assert.Equal(expected, editedOutcomes);
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

        Assert.Equal(SimulationExecutionReason.ConditionMet, cluster.RunUntil(() => executed).Reason);
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

    private sealed class TrackingDisposable(
        string name,
        List<string> events,
        bool throwOnDispose = false) : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            events.Add(name);
            Disposed = true;
            if (throwOnDispose)
            {
                throw new InvalidOperationException($"{name} disposal failed");
            }
        }
    }

    private sealed class TrackingNode(
        string address,
        SimulationNodeContext context,
        List<string> events,
        bool throwOnDispose = false) : SimulationNode, IAsyncDisposable
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;

        public ValueTask DisposeAsync()
        {
            events.Add(NetworkAddress);
            if (throwOnDispose)
            {
                throw new InvalidOperationException($"{NetworkAddress} disposal failed");
            }

            return ValueTask.CompletedTask;
        }
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

    private sealed class DisposableAddressNode(string address, SimulationNodeContext context) : SimulationNode, IDisposable
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
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

    private static SimulationRuntimeIdentity GetRuntimeIdentity(SimulationNodeContext context)
    {
        var field = typeof(SimulationTaskQueue).GetField(
            "_ambientContext",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var ambientContext = Assert.IsType<SimulationAmbientContextConfiguration>(
            field?.GetValue(context.TaskQueue));
        return ambientContext.Runtime;
    }

    private static DeliveryStatus[] GetDeliveryOutcomes(SimulationNetwork network, double dropRate)
    {
        network.MessageDropRate = dropRate;
        return Enumerable.Range(0, 32)
            .Select(_ => network.CheckDelivery("stable", "peer"))
            .ToArray();
    }
}

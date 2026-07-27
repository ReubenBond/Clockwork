namespace Clockwork.Tests;

/// <summary>
/// Characterizes the cluster's execution as a structured, replayable trace by capturing the
/// existing <c>On*</c> extensibility hooks on <see cref="SimulationCluster{TNode}"/>. This does not
/// introduce any new tracing architecture - it only records the hook invocations that already
/// exist, which is enough to prove that two independent runs with the same seed and the same
/// script of operations produce an identical sequence of orchestration events.
/// </summary>
public sealed class SimulationClusterTraceSnapshotTests
{
    [Fact]
    public async Task IdenticalSeedsAndScriptsProduceIdenticalTraceSnapshots()
    {
        var firstTrace = await RunScriptAndCaptureTraceAsync(seed: 2024);
        var secondTrace = await RunScriptAndCaptureTraceAsync(seed: 2024);

        Assert.Equal(firstTrace, secondTrace);

        // Sanity check that the trace actually captured a meaningful, non-trivial timeline
        // rather than trivially matching because nothing happened.
        Assert.True(firstTrace.Count >= 4);
        Assert.Contains(firstTrace, e => e.Kind == "TimeAdvancing");
        Assert.Contains(firstTrace, e => e.Kind == "ConditionMet");
    }

    private static async Task<IReadOnlyList<TraceEvent>> RunScriptAndCaptureTraceAsync(int seed)
    {
        await using var cluster = new TracingCluster(seed);
        var node = cluster.AddNode("node-1");

        // A small, deterministic script mixing node suspension with delayed work, so the
        // resulting trace has multiple distinguishable event kinds.
        var messagesDelivered = 0;
        node.Context.TaskQueue.EnqueueAfter(() => messagesDelivered++, TimeSpan.FromSeconds(2));
        node.SuspendFor(TimeSpan.FromSeconds(1));

        // RunForDuration advances time (emitting TimeAdvancing) and drains work until idle
        // (emitting ReachedIdleState), covering the suspend/resume + delayed work interaction.
        cluster.RunForDuration(TimeSpan.FromSeconds(3));

        // The condition is already satisfied at this point, so this only exercises the
        // ConditionMet hook without doing any further work.
        cluster.RunUntil(() => messagesDelivered == 1);

        return cluster.Trace;
    }

    public readonly record struct TraceEvent(string Kind, TimeSpan SimulatedTime, string? Detail);

    private sealed class TracingCluster : SimulationCluster<TracingNode>
    {
        private readonly List<TraceEvent> _trace = [];

        public TracingCluster(int seed)
            : base(seed, DateTimeOffset.UnixEpoch)
        {
        }

        public IReadOnlyList<TraceEvent> Trace => _trace;

        public TracingNode AddNode(string address)
        {
            var context = new SimulationNodeContext(Clock, Guard, CreateDerivedRandom(), TaskQueue);
            var node = new TracingNode(address, context);
            RegisterNode(node);
            return node;
        }

        protected override void OnConditionMet(int iterations) => Record("ConditionMet", iterations.ToString(System.Globalization.CultureInfo.InvariantCulture));

        protected override void OnSimulationIdleNoPendingWork(int iterations) => Record("IdleNoPendingWork", iterations.ToString(System.Globalization.CultureInfo.InvariantCulture));

        protected override void OnSimulationStuckMaxTime(TimeSpan timeDelta) => Record("StuckMaxTime", timeDelta.ToString());

        protected override void OnSimulationStuckConsecutiveTimeAdvances(int count) => Record("StuckConsecutiveTimeAdvances", count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        protected override void OnMaxIterationsReached(int maxIterations) => Record("MaxIterationsReached", maxIterations.ToString(System.Globalization.CultureInfo.InvariantCulture));

        protected override void OnSimulationReachedIdleState() => Record("ReachedIdleState", detail: null);

        protected override void OnTimeAdvancing(TimeSpan delta) => Record("TimeAdvancing", delta.ToString());

        protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

        private void Record(string kind, string? detail) => _trace.Add(new TraceEvent(kind, Clock.CurrentTime, detail));
    }

    private sealed class TracingNode(string address, SimulationNodeContext context) : SimulationNode
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;
    }
}

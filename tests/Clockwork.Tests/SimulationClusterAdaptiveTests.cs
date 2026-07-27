namespace Clockwork.Tests;

/// <summary>
/// Covers the adaptive execution-budget entry points (<see cref="SimulationCluster{TNode}.RunUntilConverged"/>
/// and <see cref="SimulationCluster{TNode}.RunUntilIdleConverged"/>): escalation across successive
/// batches, immediate termination without escalation for every non-<see cref="SimulationExecutionReason.MaxIterationsReached"/>
/// reason, the hard <see cref="SimulationAdaptiveBudget.MaxTotalIterations"/> ceiling, and folding of
/// combined counters across batches.
/// </summary>
public sealed class SimulationClusterAdaptiveTests
{
    [Fact]
    public async Task RunUntilConvergedEscalatesAcrossBatchesUntilConditionIsMet()
    {
        await using var cluster = new AdaptiveTestCluster(seed: 1);
        var node = cluster.AddNode("node-1");
        var counter = 0;
        const int stepsNeeded = 20;
        for (var i = 0; i < stepsNeeded; i++)
        {
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => counter++));
        }

        // A single batch of 2 iterations cannot possibly satisfy a condition that needs 20 steps -
        // reaching ConditionMet here is only possible if RunUntilConverged escalated across several
        // batches on its own.
        var budget = new SimulationAdaptiveBudget(initialMaxIterations: 2, growthFactor: 2.0, maxTotalIterations: 1000);
        var result = cluster.RunUntilConverged(() => counter >= stepsNeeded, budget);

        Assert.Equal(SimulationExecutionReason.ConditionMet, result.Reason);
        Assert.True(result.ConditionMet);
        Assert.Equal(stepsNeeded, result.StepsExecuted);
        Assert.Equal(stepsNeeded, counter);
        Assert.True(result.Iterations > budget.InitialMaxIterations, "Escalation must have occurred across more than one batch.");
    }

    [Fact]
    public async Task RunUntilConvergedStopsImmediatelyOnIdleWithoutEscalating()
    {
        await using var cluster = new AdaptiveTestCluster(seed: 1);
        _ = cluster.AddNode("node-1");

        // The initial batch is large enough that, if escalation happened needlessly, the result
        // would report far more than a handful of iterations. An empty, condition-never-met cluster
        // must stop at Idle well before that.
        var budget = new SimulationAdaptiveBudget(initialMaxIterations: 1000, growthFactor: 2.0, maxTotalIterations: 1_000_000);
        var result = cluster.RunUntilConverged(() => false, budget);

        Assert.Equal(SimulationExecutionReason.Idle, result.Reason);
        Assert.False(result.ConditionMet);
        Assert.True(result.Iterations <= 2, "An idle cluster should stop within the first batch's first couple of iterations.");
    }

    [Theory]
    [InlineData(SimulationExecutionReason.MaxSimulatedTimeAdvanceExceeded)]
    [InlineData(SimulationExecutionReason.MaxConsecutiveTimeAdvancesExceeded)]
    public async Task RunUntilConvergedStopsOnGenuinelyStuckReasonsWithoutEscalating(SimulationExecutionReason expectedReason)
    {
        await using var cluster = new AdaptiveTestCluster(seed: 1);
        if (expectedReason == SimulationExecutionReason.MaxSimulatedTimeAdvanceExceeded)
        {
            cluster.MaxSimulatedTimeAdvance = TimeSpan.FromSeconds(1);
            var node = cluster.AddNode("node-1");
            node.Context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(10));
        }
        else
        {
            cluster.MaxConsecutiveTimeAdvances = 2;
            var node = cluster.AddNode("node-1");
            node.Suspend();
            for (var i = 1; i <= 5; i++)
            {
                node.Context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(i));
            }
        }

        // A single batch's worth of iterations (well under the initial budget) is all it should take
        // to detect the stuck condition - escalating further would not help and must not happen.
        var budget = new SimulationAdaptiveBudget(initialMaxIterations: 1000, growthFactor: 2.0, maxTotalIterations: 1_000_000);
        var result = cluster.RunUntilConverged(() => false, budget);

        Assert.Equal(expectedReason, result.Reason);
        Assert.True(result.Iterations < budget.InitialMaxIterations);
    }

    [Fact]
    public async Task RunUntilConvergedHonorsTheHardTotalIterationsCapWhenConditionNeverIsMet()
    {
        await using var cluster = new AdaptiveTestCluster(seed: 1);
        var node = cluster.AddNode("node-1");

        // Self-perpetuating work: every step re-enqueues itself immediately, so the cluster is never
        // idle and every iteration is a real step. Escalation would run forever without a hard cap.
        void RunForever()
        {
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(RunForever));
        }

        RunForever();

        var budget = new SimulationAdaptiveBudget(initialMaxIterations: 3, growthFactor: 2.0, maxTotalIterations: 20);
        var result = cluster.RunUntilConverged(() => false, budget);

        Assert.Equal(SimulationExecutionReason.MaxIterationsReached, result.Reason);
        Assert.Equal(budget.MaxTotalIterations, result.Iterations);
        Assert.Equal(budget.MaxTotalIterations, result.StepsExecuted);
    }

    [Fact]
    public async Task RunUntilConvergedUsesTheDefaultBudgetWhenNoneIsSpecified()
    {
        await using var cluster = new AdaptiveTestCluster(seed: 1);
        _ = cluster.AddNode("node-1");

        var result = cluster.RunUntilConverged(() => true);

        Assert.Equal(SimulationExecutionReason.ConditionMet, result.Reason);
        Assert.Equal(0, result.Iterations);
    }

    [Fact]
    public async Task RunUntilConvergedRejectsNullCondition()
    {
        await using var cluster = new AdaptiveTestCluster(seed: 1);
        Assert.Throws<ArgumentNullException>(() => cluster.RunUntilConverged(null!));
    }

    [Fact]
    public async Task RunUntilIdleConvergedEscalatesAcrossBatchesUntilIdle()
    {
        await using var cluster = new AdaptiveTestCluster(seed: 1);
        var node = cluster.AddNode("node-1");
        const int stepsToDrain = 20;
        for (var i = 0; i < stepsToDrain; i++)
        {
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));
        }

        var budget = new SimulationAdaptiveBudget(initialMaxIterations: 2, growthFactor: 2.0, maxTotalIterations: 1000);
        var result = cluster.RunUntilIdleConverged(budget: budget);

        Assert.Equal(SimulationExecutionReason.Idle, result.Reason);
        Assert.Equal(stepsToDrain, result.StepsExecuted);
        Assert.True(result.Iterations > budget.InitialMaxIterations, "Escalation must have occurred across more than one batch.");
    }

    [Fact]
    public async Task RunUntilIdleConvergedStopsImmediatelyOnTeardownCancellationWithoutEscalating()
    {
        using var cts = new CancellationTokenSource();
        await using var cluster = new AdaptiveTestCluster(seed: 1, cancellationToken: cts.Token);
        var node = cluster.AddNode("node-1");
        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));
        await cts.CancelAsync();

        var budget = new SimulationAdaptiveBudget(initialMaxIterations: 1000, growthFactor: 2.0, maxTotalIterations: 1_000_000);
        var result = cluster.RunUntilIdleConverged(budget: budget);

        Assert.Equal(SimulationExecutionReason.TeardownCancellationRequested, result.Reason);
        Assert.Equal(0, result.Iterations);
    }

    [Fact]
    public async Task RunUntilIdleConvergedFoldsCountersAcrossEscalatedBatches()
    {
        await using var cluster = new AdaptiveTestCluster(seed: 1);
        var node = cluster.AddNode("node-1");
        const int steps = 6;
        for (var i = 0; i < steps; i++)
        {
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));
        }

        // Two timers spaced one second apart force two separate time advances after the immediate
        // steps drain, split across escalating batches.
        node.Context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(1));
        node.Context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(2));

        var budget = new SimulationAdaptiveBudget(initialMaxIterations: 1, growthFactor: 2.0, maxTotalIterations: 1000);
        var result = cluster.RunUntilIdleConverged(budget: budget);

        // Each timer contributes one time advance (to reach its due time) plus one step (to execute
        // its callback once ready), on top of the immediate steps.
        const int timers = 2;
        Assert.Equal(SimulationExecutionReason.Idle, result.Reason);
        Assert.Equal(steps + timers, result.StepsExecuted);
        Assert.Equal(timers, result.TimeAdvanceCount);
        Assert.Equal(cluster.StartDateTime + TimeSpan.FromSeconds(2), result.EndTime);

        // Every iteration across every folded batch is either a step, a time advance, or the final
        // "check and stop" iteration that discovered idleness - so the sum of the two counters can
        // never exceed the total iteration count.
        Assert.True(result.Iterations >= result.StepsExecuted + result.TimeAdvanceCount);
    }

    private sealed class AdaptiveTestCluster : SimulationCluster<AdaptiveTestNode>
    {
        public AdaptiveTestCluster(int seed, DateTimeOffset? startDateTime = null, CancellationToken cancellationToken = default)
            : base(seed, startDateTime ?? DateTimeOffset.UnixEpoch, cancellationToken: cancellationToken)
        {
        }

        public AdaptiveTestNode AddNode(string address)
        {
            var context = new SimulationNodeContext(Clock, Guard, CreateDerivedRandom(), TaskQueue);
            var node = new AdaptiveTestNode(address, context);
            RegisterNode(node);
            return node;
        }

        protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
    }

    private sealed class AdaptiveTestNode(string address, SimulationNodeContext context) : SimulationNode
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;
    }
}

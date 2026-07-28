namespace Clockwork.Tests;

public sealed class SimulationClusterRunToCompletionTests
{
    [Fact]
    public async Task FixedRunToCompletionDrivesCapturedContinuations()
    {
        await using var cluster = new TestCluster(seed: 1);
        var resumed = false;

        cluster.RunToCompletion(async () =>
        {
            await Task.Yield();
            resumed = true;
        });

        Assert.True(resumed);
    }

    [Fact]
    public async Task AdaptiveGenericRunToCompletionReturnsTheTaskResult()
    {
        await using var cluster = new TestCluster(seed: 1);
        var budget = new AdaptiveExecutionBudget(
            initialMaxIterations: 1,
            growthFactor: 2,
            maxTotalIterations: 100);

        int result = cluster.RunToCompletion(
            async () =>
            {
                await Task.Yield();
                return 42;
            },
            budget);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunToCompletionPropagatesTheOriginalTaskFault()
    {
        await using var cluster = new TestCluster(seed: 1);

        var exception = Assert.Throws<InvalidOperationException>(
            () => cluster.RunToCompletion(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("expected failure");
            }));

        Assert.Equal("expected failure", exception.Message);
    }

    [Fact]
    public async Task AdaptiveRunToCompletionExhaustionIncludesTheDetailedResult()
    {
        await using var cluster = new TestCluster(seed: 1);
        var completion = new TaskCompletionSource();
        var budget = new AdaptiveExecutionBudget(
            initialMaxIterations: 1,
            growthFactor: 2,
            maxTotalIterations: 3);

        var exception = Assert.Throws<TimeoutException>(
            () => cluster.RunToCompletion(() => completion.Task, budget));

        Assert.Contains("Reason: Idle", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MaxIterations=3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixedAndAdaptiveOverloadsResolveByArgumentType()
    {
        await using var cluster = new TestCluster(seed: 1);
        var budget = new AdaptiveExecutionBudget(maxTotalIterations: 1_000);

        SimulationExecutionResult fixedUntil = cluster.RunUntil(() => true, 1);
        SimulationExecutionResult adaptiveUntil = cluster.RunUntil(() => true, budget);
        SimulationExecutionResult fixedIdle = cluster.RunUntilIdle(maxIterations: 1);
        SimulationExecutionResult adaptiveIdle = cluster.RunUntilIdle(budget);
        cluster.RunToCompletion(() => Task.CompletedTask, 1);
        cluster.RunToCompletion(() => Task.CompletedTask, budget);
        int fixedValue = cluster.RunToCompletion(() => Task.FromResult(1), 1);
        int adaptiveValue = cluster.RunToCompletion(() => Task.FromResult(2), budget);

        Assert.Equal(SimulationExecutionReason.ConditionMet, fixedUntil.Reason);
        Assert.Equal(SimulationExecutionReason.ConditionMet, adaptiveUntil.Reason);
        Assert.Equal(SimulationExecutionReason.Idle, fixedIdle.Reason);
        Assert.Equal(SimulationExecutionReason.Idle, adaptiveIdle.Reason);
        Assert.Equal(1, fixedValue);
        Assert.Equal(2, adaptiveValue);
    }

    private sealed class TestCluster(int seed) : SimulationCluster<TestNode>(seed, DateTimeOffset.UnixEpoch)
    {
        protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
    }

    private sealed class TestNode : SimulationNode
    {
        public override SimulationNodeContext Context => throw new NotSupportedException();

        public override string NetworkAddress => throw new NotSupportedException();

        public override bool IsInitialized => true;
    }
}

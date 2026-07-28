using System.Globalization;

namespace Clockwork.Tests;

/// <summary>
/// Covers the structured execution outcome API (<see cref="SimulationExecutionResult"/> and
/// friends) introduced alongside the consolidated drive-loop engine: every distinguishable
/// termination reason, hook-firing
/// behavior, pending-work diagnostics, deterministic formatting, and boundary values.
/// </summary>
public sealed class SimulationExecutionResultTests
{
    [Fact]
    public async Task RunUntilReportsConditionMetWithNoPendingWork()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        var node = cluster.AddNode("node-1");
        var executed = false;
        node.Context.TaskQueue.EnqueueAfter(() => executed = true, TimeSpan.FromSeconds(5));

        var result = cluster.RunUntil(() => executed);

        Assert.Equal(SimulationExecutionReason.ConditionMet, result.Reason);
        Assert.True(result.ConditionMet);
        Assert.Equal(cluster.StartDateTime, result.StartTime);
        Assert.Equal(cluster.StartDateTime + TimeSpan.FromSeconds(5), result.EndTime);
        Assert.Equal(TimeSpan.FromSeconds(5), result.ElapsedSimulatedTime);
        Assert.Equal(1, result.StepsExecuted);
        Assert.True(result.TimeAdvanceCount >= 1);
        Assert.Equal(0, result.PendingWork.PendingCount);
        Assert.Equal(1, cluster.HookCounts.GetValueOrDefault("OnConditionMet"));
        Assert.DoesNotContain("OnSimulationIdleNoPendingWork", cluster.HookCounts.Keys);
    }

    [Fact]
    public async Task RunUntilReportsIdleWhenConditionNeverMetAndNoPendingWork()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        _ = cluster.AddNode("node-1");

        var result = cluster.RunUntil(() => false, maxIterations: 100);

        Assert.Equal(SimulationExecutionReason.Idle, result.Reason);
        Assert.False(result.ConditionMet);
        Assert.Equal(0, result.PendingWork.PendingCount);
        Assert.Equal(1, cluster.HookCounts.GetValueOrDefault("OnSimulationIdleNoPendingWork"));
        Assert.DoesNotContain("OnSimulationReachedIdleState", cluster.HookCounts.Keys);
    }

    [Fact]
    public async Task RunUntilIdleReportsIdleOnAnEmptyCluster()
    {
        await using var cluster = new RecordingCluster(seed: 1);

        var result = cluster.RunUntilIdle();

        Assert.Equal(SimulationExecutionReason.Idle, result.Reason);
        Assert.False(result.ConditionMet);
        Assert.Equal(0, result.Iterations);
        Assert.Equal(0, result.PendingWork.PendingCount);
        Assert.Empty(result.PendingWork.Items);
        Assert.Equal(1, cluster.HookCounts.GetValueOrDefault("OnSimulationReachedIdleState"));
    }

    [Fact]
    public async Task IdleWithPendingWorkIsReportedWhenASuspendedNodeHoldsAReadyItem()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        var node = cluster.AddNode("node-1");
        node.Suspend();
        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));

        var result = cluster.RunUntilIdle();

        Assert.Equal(SimulationExecutionReason.IdleWithPendingWork, result.Reason);
        Assert.Equal(0, result.PendingWork.RunnableCount);
        Assert.Equal(0, result.PendingWork.WaitingCount);
        Assert.Equal(1, result.PendingWork.BlockedCount);
        Assert.Equal(1, result.PendingWork.PendingCount);

        var item = Assert.Single(result.PendingWork.Items);
        Assert.Equal("node-1", item.QueueIdentity);
        Assert.Equal(nameof(ScheduledActionItem), item.ItemType);
        Assert.True(item.IsReady);
        Assert.True(item.IsBlocked);

        // Reaching idle-with-pending-work still fires the same hook as a plain idle state -
        // this is purely additive diagnostic information, not a behavioral change.
        Assert.Equal(1, cluster.HookCounts.GetValueOrDefault("OnSimulationReachedIdleState"));
    }

    [Fact]
    public async Task PendingWorkItemsAreOrderedDeterministicallyByDueTimeThenSequenceThenQueue()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        var nodeB = cluster.AddNode("node-2");
        var nodeA = cluster.AddNode("node-1");
        nodeA.Suspend();
        nodeB.Suspend();

        // Both items become due at the same simulated instant with independent per-queue sequence
        // numbers (0 each) - the queue identity is the only remaining, deterministic tiebreaker.
        nodeB.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));
        nodeA.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));

        var result = cluster.RunUntilIdle();

        Assert.Equal(2, result.PendingWork.Items.Count);
        Assert.Equal("node-1", result.PendingWork.Items[0].QueueIdentity);
        Assert.Equal("node-2", result.PendingWork.Items[1].QueueIdentity);
    }

    [Fact]
    public async Task MaxSimulatedTimeAdvanceExceededReportsTheAttemptedAdvance()
    {
        await using var cluster = new RecordingCluster(seed: 1) { MaxSimulatedTimeAdvance = TimeSpan.FromSeconds(1) };
        var node = cluster.AddNode("node-1");
        node.Context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(10));

        var result = cluster.RunUntil(() => false, maxIterations: 100);

        Assert.Equal(SimulationExecutionReason.MaxSimulatedTimeAdvanceExceeded, result.Reason);
        Assert.Equal(TimeSpan.FromSeconds(10), result.AttemptedTimeAdvance);
        Assert.Equal(1, cluster.HookCounts.GetValueOrDefault("OnSimulationStuckMaxTime"));
    }

    [Fact]
    public async Task MaxConsecutiveTimeAdvancesExceededIsReportedWhenManyAdvancesNeverExecuteWork()
    {
        await using var cluster = new RecordingCluster(seed: 1) { MaxConsecutiveTimeAdvances = 2 };
        var node = cluster.AddNode("node-1");
        node.Suspend();

        // None of these will ever execute (the node is suspended), so every step advances time
        // without doing any work. Five distinct due times comfortably exceed the threshold of 2.
        for (var i = 1; i <= 5; i++)
        {
            node.Context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(i));
        }

        var result = cluster.RunUntil(() => false, maxIterations: 100);

        Assert.Equal(SimulationExecutionReason.MaxConsecutiveTimeAdvancesExceeded, result.Reason);
        Assert.Equal(3, result.ConsecutiveTimeAdvanceCount);
        Assert.Equal(3, result.TimeAdvanceCount);
        Assert.Equal(1, cluster.HookCounts.GetValueOrDefault("OnSimulationStuckConsecutiveTimeAdvances"));
    }

    [Fact]
    public async Task MaxIterationsReachedStopsBeforeAnyOtherReason()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        var node = cluster.AddNode("node-1");
        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));

        var result = cluster.RunUntil(() => false, maxIterations: 1);

        Assert.Equal(SimulationExecutionReason.MaxIterationsReached, result.Reason);
        Assert.Equal(1, result.Iterations);
        Assert.Equal(1, result.StepsExecuted);
        Assert.Equal(1, cluster.HookCounts.GetValueOrDefault("OnMaxIterationsReached"));
    }

    [Fact]
    public async Task TeardownCancellationRequestedStopsRunUntilIdleImmediately()
    {
        using var cts = new CancellationTokenSource();
        await using var cluster = new RecordingCluster(seed: 1, cancellationToken: cts.Token);
        var node = cluster.AddNode("node-1");
        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));
        await cts.CancelAsync();

        var result = cluster.RunUntilIdle();

        Assert.Equal(SimulationExecutionReason.TeardownCancellationRequested, result.Reason);
        Assert.Equal(0, result.Iterations);
        Assert.Equal(1, cluster.HookCounts.GetValueOrDefault("OnTeardownCancellationRequested"));
    }

    [Fact]
    public async Task RunUntilDoesNotObserveTeardownCancellation()
    {
        // RunUntil never checked teardown cancellation in the original
        // implementation; only the RunUntilIdle family does. This must stay true.
        using var cts = new CancellationTokenSource();
        await using var cluster = new RecordingCluster(seed: 1, cancellationToken: cts.Token);
        await cts.CancelAsync();

        var result = cluster.RunUntil(() => false, maxIterations: 5);

        Assert.Equal(SimulationExecutionReason.Idle, result.Reason);
        Assert.DoesNotContain("OnTeardownCancellationRequested", cluster.HookCounts.Keys);
    }

    [Fact]
    public async Task RunForReportsIdleAfterProcessingTimerWork()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        var node = cluster.AddNode("node-1");
        var fired = false;
        node.Context.TaskQueue.EnqueueAfter(() => fired = true, TimeSpan.FromSeconds(2));

        var result = cluster.RunFor(TimeSpan.FromSeconds(5));

        Assert.True(fired);
        Assert.Equal(SimulationExecutionReason.Idle, result.Reason);
        Assert.False(result.ConditionMet);
        Assert.Equal(cluster.StartDateTime, result.StartTime);
        Assert.Equal(cluster.StartDateTime + TimeSpan.FromSeconds(5), result.EndTime);
        Assert.Equal(1, result.StepsExecuted);
        Assert.Equal(1, cluster.HookCounts.GetValueOrDefault("OnTimeAdvancing"));
    }

    [Fact]
    public async Task RunForWithZeroDurationIsANoOp()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        _ = cluster.AddNode("node-1");

        var result = cluster.RunFor(TimeSpan.Zero);

        Assert.Equal(SimulationExecutionReason.Idle, result.Reason);
        Assert.Equal(0, result.Iterations);
        Assert.Equal(0, result.TimeAdvanceCount);
        Assert.Equal(result.StartTime, result.EndTime);
        Assert.DoesNotContain("OnTimeAdvancing", cluster.HookCounts.Keys);
    }

    [Fact]
    public async Task RunForRejectsNegativeDuration()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => cluster.RunFor(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public async Task RunUntilRejectsNullCondition()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        Assert.Throws<ArgumentNullException>(() => cluster.RunUntil(null!));
    }

    [Fact]
    public async Task MaxIterationsBoundaryOfZeroNeverEvaluatesTheCondition()
    {
        await using var cluster = new RecordingCluster(seed: 1);
        var conditionEvaluated = false;

        var result = cluster.RunUntil(() => { conditionEvaluated = true; return true; }, maxIterations: 0);

        Assert.Equal(SimulationExecutionReason.MaxIterationsReached, result.Reason);
        Assert.Equal(0, result.Iterations);
        Assert.False(conditionEvaluated);
    }

    [Fact]
    public async Task MaxConsecutiveTimeAdvancesBoundaryOfZeroTripsOnTheFirstAdvance()
    {
        await using var cluster = new RecordingCluster(seed: 1) { MaxConsecutiveTimeAdvances = 0 };
        var node = cluster.AddNode("node-1");
        node.Context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(1));

        var result = cluster.RunUntil(() => false, maxIterations: 100);

        Assert.Equal(SimulationExecutionReason.MaxConsecutiveTimeAdvancesExceeded, result.Reason);
        Assert.Equal(1, result.ConsecutiveTimeAdvanceCount);
    }

    [Fact]
    public async Task ToStringAndToDetailedStringAreDeterministicAcrossIdenticalRuns()
    {
        var first = await RunScriptAsync();
        var second = await RunScriptAsync();

        Assert.Equal(first.ToString(), second.ToString());
        Assert.Equal(first.ToDetailedString(), second.ToDetailedString());
        Assert.Contains("Idle", first.ToString(), StringComparison.Ordinal);
        Assert.Contains("PendingWork:", first.ToDetailedString(), StringComparison.Ordinal);

        static async Task<SimulationExecutionResult> RunScriptAsync()
        {
            await using var cluster = new RecordingCluster(seed: 99);
            var node = cluster.AddNode("node-1");
            node.Suspend();
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));
            return cluster.RunUntilIdle();
        }
    }

    [Fact]
    public async Task FormattingUsesInvariantCultureRegardlessOfCurrentThreadCulture()
    {
        // German formatting uses a comma as the decimal separator and different date ordering;
        // the invariant-culture formatting used by ToString/ToDetailedString must not be affected
        // by the current thread culture, so an identical script run under each culture must
        // produce byte-for-byte identical output.
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariantResult = await RunScriptAsync();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var germanResult = await RunScriptAsync();

            Assert.Equal(invariantResult.ToString(), germanResult.ToString());
            Assert.Equal(invariantResult.ToDetailedString(), germanResult.ToDetailedString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        static async Task<SimulationExecutionResult> RunScriptAsync()
        {
            await using var cluster = new RecordingCluster(seed: 1);
            var node = cluster.AddNode("node-1");
            node.Context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(1.5));
            return cluster.RunUntilIdle();
        }
    }

    private sealed class RecordingCluster : SimulationCluster<TestNode>
    {
        public RecordingCluster(int seed, DateTimeOffset? startDateTime = null, CancellationToken cancellationToken = default)
            : base(seed, startDateTime ?? DateTimeOffset.UnixEpoch, cancellationToken: cancellationToken)
        {
        }

        public Dictionary<string, int> HookCounts { get; } = new(StringComparer.Ordinal);

        public TestNode AddNode(string address)
        {
            var context = new SimulationNodeContext(Clock, Guard, CreateDerivedRandom(), TaskQueue);
            var node = new TestNode(address, context);
            RegisterNode(node);
            return node;
        }

        protected override void OnConditionMet(int iterations) => Record(nameof(OnConditionMet));

        protected override void OnSimulationIdleNoPendingWork(int iterations) => Record(nameof(OnSimulationIdleNoPendingWork));

        protected override void OnSimulationStuckMaxTime(TimeSpan timeDelta) => Record(nameof(OnSimulationStuckMaxTime));

        protected override void OnSimulationStuckConsecutiveTimeAdvances(int count) => Record(nameof(OnSimulationStuckConsecutiveTimeAdvances));

        protected override void OnMaxIterationsReached(int maxIterations) => Record(nameof(OnMaxIterationsReached));

        protected override void OnTeardownCancellationRequested() => Record(nameof(OnTeardownCancellationRequested));

        protected override void OnSimulationReachedIdleState() => Record(nameof(OnSimulationReachedIdleState));

        protected override void OnTimeAdvancing(TimeSpan delta) => Record(nameof(OnTimeAdvancing));

        protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

        private void Record(string hookName) => HookCounts[hookName] = HookCounts.GetValueOrDefault(hookName) + 1;
    }

    private sealed class TestNode(string address, SimulationNodeContext context) : SimulationNode
    {
        public override SimulationNodeContext Context { get; } = context;

        public override string NetworkAddress { get; } = address;

        public override bool IsInitialized => true;
    }
}

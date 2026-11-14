namespace Clockwork.Tests;

public sealed class SimulationClusterRunToCompletionTests
{
    [Fact]
    public async Task FixedRunToCompletionDrivesCapturedContinuations()
    {
        await using var cluster = new SimulationCluster(seed: 1, DateTimeOffset.UnixEpoch);
        var resumed = false;

        cluster.RunToCompletion(async () =>
        {
            await Task.Yield();
            resumed = true;
        }, TestContext.Current.CancellationToken);

        Assert.True(resumed);
    }

    [Fact]
    public async Task AdaptiveGenericRunToCompletionReturnsTheTaskResult()
    {
        await using var cluster = new SimulationCluster(seed: 1, DateTimeOffset.UnixEpoch);
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
            budget,
            TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunToCompletionPropagatesTheOriginalTaskFault()
    {
        await using var cluster = new SimulationCluster(seed: 1, DateTimeOffset.UnixEpoch);

        var exception = Assert.Throws<InvalidOperationException>(
            () => cluster.RunToCompletion(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("expected failure");
            }, TestContext.Current.CancellationToken));

        Assert.Equal("expected failure", exception.Message);
    }

    [Fact]
    public async Task AllRunToCompletionOverloadsHonorCallerCancellationBeforeStartingWork()
    {
        await using var cluster = new SimulationCluster(seed: 1, DateTimeOffset.UnixEpoch);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var budget = new AdaptiveExecutionBudget(maxTotalIterations: 1_000);
        var invocations = 0;

        Task RunAsync()
        {
            invocations++;
            return Task.CompletedTask;
        }

        Task<int> RunWithResultAsync()
        {
            invocations++;
            return Task.FromResult(42);
        }

        Assert.Throws<OperationCanceledException>(
            () => cluster.RunToCompletion(
                RunAsync,
                cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(
            () => cluster.RunToCompletion(
                RunAsync,
                budget,
                cancellation.Token));
        Assert.Throws<OperationCanceledException>(
            () => cluster.RunToCompletion(
                RunWithResultAsync,
                cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(
            () => cluster.RunToCompletion(
                RunWithResultAsync,
                budget,
                cancellation.Token));

        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task RunToCompletionReportsCancellationInsteadOfTimeoutForIncompleteTask()
    {
        await using var cluster = new SimulationCluster(seed: 1, DateTimeOffset.UnixEpoch);
        using var cancellation = new CancellationTokenSource();
        var neverCompletes = new TaskCompletionSource();

        var exception = Assert.Throws<OperationCanceledException>(
            () => cluster.RunToCompletion(
                async () =>
                {
                    cancellation.Cancel();
                    await neverCompletes.Task;
                },
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task AdaptiveRunToCompletionExhaustionIncludesTheDetailedResult()
    {
        await using var cluster = new SimulationCluster(seed: 1, DateTimeOffset.UnixEpoch);
        var completion = new TaskCompletionSource();
        var budget = new AdaptiveExecutionBudget(
            initialMaxIterations: 1,
            growthFactor: 2,
            maxTotalIterations: 3);

        var exception = Assert.Throws<TimeoutException>(
            () => cluster.RunToCompletion(() => completion.Task, budget, TestContext.Current.CancellationToken));

        Assert.Contains("Reason: Idle", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MaxIterations=3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixedAndAdaptiveOverloadsResolveByArgumentType()
    {
        await using var cluster = new SimulationCluster(seed: 1, DateTimeOffset.UnixEpoch);
        var budget = new AdaptiveExecutionBudget(maxTotalIterations: 1_000);

        SimulationExecutionResult fixedUntil = cluster.RunUntil(() => true, TestContext.Current.CancellationToken, 1);
        SimulationExecutionResult adaptiveUntil = cluster.RunUntil(() => true, budget, TestContext.Current.CancellationToken);
        SimulationExecutionResult fixedIdle = cluster.RunUntilIdle(TestContext.Current.CancellationToken, maxIterations: 1);
        SimulationExecutionResult adaptiveIdle = cluster.RunUntilIdle(budget, TestContext.Current.CancellationToken);
        cluster.RunToCompletion(() => Task.CompletedTask, TestContext.Current.CancellationToken, 1);
        cluster.RunToCompletion(() => Task.CompletedTask, budget, TestContext.Current.CancellationToken);
        int fixedValue = cluster.RunToCompletion(() => Task.FromResult(1), TestContext.Current.CancellationToken, 1);
        int adaptiveValue = cluster.RunToCompletion(() => Task.FromResult(2), budget, TestContext.Current.CancellationToken);

        Assert.Equal(SimulationExecutionReason.ConditionMet, fixedUntil.Reason);
        Assert.Equal(SimulationExecutionReason.ConditionMet, adaptiveUntil.Reason);
        Assert.Equal(SimulationExecutionReason.Idle, fixedIdle.Reason);
        Assert.Equal(SimulationExecutionReason.Idle, adaptiveIdle.Reason);
        Assert.Equal(1, fixedValue);
        Assert.Equal(2, adaptiveValue);
    }

    // A completion which Clockwork makes for rewritten code - cancelling a token, or releasing a semaphore which a
    // WaitAsync waits on - before the root's first await or in the cluster lane's work, where the cluster's
    // SimulationSynchronizationContext (and in the root its SimulationTaskScheduler) is current, runs a framework
    // continuation of the task it completes inline on the logical thread, as on a thread-pool thread, as the BCL does on
    // one, rather than on the real thread pool.
    [Theory]
    [InlineData("cancel", true)]
    [InlineData("cancel", false)]
    [InlineData("release", true)]
    [InlineData("release", false)]
    public async Task ControlledCompletionsRunFrameworkContinuationsInline(string completion, bool inRoot)
    {
        await using var cluster = new SimulationCluster(seed: 1, DateTimeOffset.UnixEpoch);
        Func<Task<bool>> complete = completion == "cancel"
            ? () => CancelWhileWaiting(ControlledCancellationTokenSource.Cancel)
            : () => ReleaseWhileWaiting(
                () => ControlledSemaphoreSlim.Create(0),
                ControlledSemaphoreSlim.WaitAsync,
                semaphore => ControlledSemaphoreSlim.Release(semaphore));

        bool inline;
        if (inRoot)
        {
            inline = cluster.RunToCompletion(complete, TestContext.Current.CancellationToken);
        }
        else
        {
            Task<bool>? waiting = null;
            cluster.SchedulerLane.Enqueue(() => waiting = complete());
            cluster.RunUntilIdle(TestContext.Current.CancellationToken);
            inline = await waiting!.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        Assert.True(inline);
        if (completion == "cancel")
        {
            // On a thread-pool thread the BCL runs the continuation inline in Cancel too. (Its SemaphoreSlim runs a
            // WaitAsync's continuations asynchronously, on the thread pool, which a simulation runs on its logical thread.)
            Assert.True(await Task.Run(() => CancelWhileWaiting(source => source.Cancel()), TestContext.Current.CancellationToken));
        }
    }

    // A documented deviation: code which runs inside such a completion, as a callback which Cancel runs does, sees no
    // synchronization context, where the BCL runs it under the canceller's.
    [Fact]
    public async Task CodeInsideAControlledCompletionRunsWithoutTheLanesContext()
    {
        await using var cluster = new SimulationCluster(seed: 1, DateTimeOffset.UnixEpoch);
        SynchronizationContext? canceller = null;
        SynchronizationContext? callback = new();
        cluster.SchedulerLane.Enqueue(() =>
        {
            using var source = new CancellationTokenSource();
            using CancellationTokenRegistration registration = source.Token.Register(() => callback = SynchronizationContext.Current);
            canceller = SynchronizationContext.Current;
            ControlledCancellationTokenSource.Cancel(source);
        });
        cluster.RunUntilIdle(TestContext.Current.CancellationToken);

        Assert.IsType<SimulationSynchronizationContext>(canceller);
        Assert.Null(callback);
    }

    private static Task<bool> CancelWhileWaiting(Action<CancellationTokenSource> cancel)
    {
        using var source = new CancellationTokenSource();
        Task<bool> waiting = ResumedOnTheCompletingThreadAsync(
            async () => await new TaskCompletionSource().Task.WaitAsync(source.Token).ConfigureAwait(false));
        cancel(source);
        return waiting;
    }

    private static Task<bool> ReleaseWhileWaiting(
        Func<SemaphoreSlim> create,
        Func<SemaphoreSlim, Task> wait,
        Action<SemaphoreSlim> release)
    {
        using SemaphoreSlim semaphore = create();
        Task<bool> waiting = ResumedOnTheCompletingThreadAsync(async () => await wait(semaphore).ConfigureAwait(false));
        release(semaphore);
        return waiting;
    }

    // A framework await: ConfigureAwait(false) registers a bare continuation, which the BCL runs inline in the call which
    // completes the task only where that call runs as on a thread-pool thread.
    private static async Task<bool> ResumedOnTheCompletingThreadAsync(Func<Task> wait)
    {
        int thread = Environment.CurrentManagedThreadId;
        try
        {
            await wait().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        return Environment.CurrentManagedThreadId == thread;
    }
}

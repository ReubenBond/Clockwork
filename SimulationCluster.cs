using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Clockwork;

/// <summary>
/// <para>
/// Abstract base class for simulation clusters that orchestrate deterministic testing
/// of distributed systems. Provides generic task scheduling, time management, and
/// node lifecycle management independent of any specific application domain.
/// </para>
/// <para>Derived classes implement application-specific node creation and cluster operations.</para>
/// </summary>
/// <typeparam name="TNode">The concrete simulation node type.</typeparam>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public abstract partial class SimulationCluster<TNode> : IAsyncDisposable
    where TNode : SimulationNode
{
    private readonly SortedDictionary<string, TNode> _nodes = new(StringComparer.Ordinal);
    private readonly SimulationTimeProvider _timeProvider;
    private readonly CancellationTokenSource _teardownCts;
    private readonly SimulationDriveLoop _driveLoop;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationCluster{TNode}"/> class.
    /// Initializes a new simulation cluster with the specified seed.
    /// </summary>
    /// <param name="seed">The seed for deterministic random number generation.</param>
    /// <param name="startDateTime">Optional starting date/time for the simulation. Defaults to UTC now.</param>
    /// <param name="cancellationToken">Optional cancellation token to link with the cluster teardown.</param>
    protected SimulationCluster(int seed, DateTimeOffset? startDateTime = null, CancellationToken cancellationToken = default)
    {
        _teardownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TeardownCancellationToken = _teardownCts.Token;
        Seed = seed;
        StartDateTime = startDateTime ?? DateTimeOffset.UtcNow;

        Random = new SimulationRandom(seed);

        // Create shared clock and cluster-level queue
        Clock = new SimulationClock(StartDateTime);
        TaskQueue = new SimulationTaskQueue(Clock, Guard);
        TaskScheduler = new SimulationTaskScheduler(TaskQueue);

        // Create time provider using cluster queue (for GetUtcNow queries)
        _timeProvider = new SimulationTimeProvider(TaskQueue, Clock);

        // The single engine that drives RunUntil/RunUntilIdle/RunForDuration.
        _driveLoop = new SimulationDriveLoop(
            () => _timeProvider.GetUtcNow(),
            RunOneTaskRoundRobin,
            GetNextWaitingDueTime,
            Clock.Advance,
            CapturePendingWorkSummary,
            TeardownCancellationToken);
    }

    /// <summary>
    /// Gets the seed used for this cluster.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// Gets a cancellation token used to signal when the simulation is being torn down.
    /// </summary>
    public CancellationToken TeardownCancellationToken { get; }

    /// <summary>
    /// Gets or sets maximum simulated time to advance before considering the simulation stuck.
    /// Default is 10 minutes of simulated time.
    /// </summary>
    public TimeSpan MaxSimulatedTimeAdvance { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the maximum number of consecutive time advances allowed without executing any
    /// work in between, before considering the simulation stuck. Default is 10,000.
    /// </summary>
    public int MaxConsecutiveTimeAdvances { get; set; } = 10_000;

    /// <summary>
    /// Gets the starting date/time for the simulation.
    /// </summary>
    public DateTimeOffset StartDateTime { get; }

    /// <summary>
    /// Gets the simulation random instance.
    /// </summary>
    public SimulationRandom Random { get; }

    /// <summary>
    /// Gets the shared simulation clock.
    /// </summary>
    public SimulationClock Clock { get; }

    /// <summary>
    /// Gets the simulation time provider.
    /// </summary>
    public TimeProvider TimeProvider => _timeProvider;

    /// <summary>
    /// Gets all nodes in the simulation, including suspended nodes (snapshot).
    /// Consider using <see cref="ActiveNodes"/> for most operations.
    /// </summary>
    public IReadOnlyList<TNode> Nodes => [.. _nodes.Values];

    /// <summary>
    /// Gets all active (non-suspended) nodes in the simulation (snapshot).
    /// Suspended nodes cannot process messages and are excluded from convergence checks.
    /// </summary>
    public IReadOnlyList<TNode> ActiveNodes => [.. _nodes.Values.Where(n => !n.IsSuspended)];

    /// <summary>
    /// Gets the cluster-level task queue for scheduling general simulation work.
    /// For node-specific work, use the node's context to get the node's queue.
    /// </summary>
    public SimulationTaskQueue TaskQueue { get; }

    /// <summary>
    /// Gets the cluster-level task scheduler for scheduling general simulation work.
    /// For node-specific work, use the node's context to get the node's scheduler.
    /// </summary>
    public SimulationTaskScheduler TaskScheduler { get; }

    /// <summary>
    /// Gets the cluster-level synchronization context.
    /// Install this on the test thread to capture async continuations in the simulation.
    /// </summary>
    public SimulationSynchronizationContext SynchronizationContext => TaskQueue.SynchronizationContext;

    /// <summary>
    /// Gets the single-threaded guard used to detect accidental concurrent access.
    /// This guard should be shared with all simulation components to ensure single-threaded execution.
    /// </summary>
    public SingleThreadedGuard Guard { get; } = new();

    /// <summary>
    /// Gets the simulation context for a specific node.
    /// </summary>
    /// <param name="node">The node to get the context for.</param>
    /// <returns>The node's simulation context.</returns>
    public SimulationNodeContext GetNodeContext(TNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Context;
    }

    /// <summary>
    /// Registers a node with the simulation.
    /// </summary>
    protected void RegisterNode(TNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var key = node.NetworkAddress;
        using var _ = Guard.Enter();
        if (!_nodes.TryAdd(key, node))
        {
            throw new InvalidOperationException($"Node with address {key} already exists");
        }

        OnNodeRegistered(node);
    }

    /// <summary>
    /// Unregisters a node from the simulation.
    /// The node is removed from the routing table so it won't receive new messages.
    /// Note: This does NOT clear the node's task queue - the node may still have
    /// pending work that needs to complete (e.g., during disposal).
    /// </summary>
    protected void UnregisterNode(TNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var key = node.NetworkAddress;
        using var _ = Guard.Enter();
        _nodes.Remove(key);
        OnNodeUnregistered(node);
    }

    /// <summary>
    /// Gets a node by its network address.
    /// </summary>
    protected TNode? GetNode(string address)
    {
        using var _ = Guard.Enter();
        _nodes.TryGetValue(address, out var node);
        return node;
    }

    /// <summary>
    /// Called when a node is registered with the simulation.
    /// Override to perform additional setup.
    /// </summary>
    protected virtual void OnNodeRegistered(TNode node) { }

    /// <summary>
    /// Called when a node is unregistered from the simulation.
    /// Override to perform additional cleanup.
    /// </summary>
    protected virtual void OnNodeUnregistered(TNode node) { }

    /// <summary>
    /// Creates a new deterministic random instance derived from the cluster's random.
    /// </summary>
    public SimulationRandom CreateDerivedRandom()
    {
        using var _ = Guard.Enter();
#pragma warning disable CA5394 // Do not use insecure randomness
        return new SimulationRandom(Random.Next());
#pragma warning restore CA5394 // Do not use insecure randomness
    }

    /// <summary>
    /// Runs the simulation until the specified condition is met.
    /// </summary>
    /// <param name="condition">The condition that ends the run when it becomes true.</param>
    /// <param name="maxIterations">The maximum number of loop iterations to execute.</param>
    /// <returns><see langword="true"/> if the condition was met; <see langword="false"/> for any other stopping reason.</returns>
    public bool RunUntil(Func<bool> condition, int maxIterations = 100000)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return RunUntilCore(condition, maxIterations);
    }

    /// <summary>
    /// Runs the simulation until the specified condition is met, returning a detailed result
    /// describing exactly why the run stopped, how much work it did, and what (if anything) is
    /// still pending. This is the detailed counterpart to <see cref="RunUntil(Func{bool}, int)"/>.
    /// </summary>
    /// <param name="condition">The condition that ends the run when it becomes true.</param>
    /// <param name="maxIterations">The maximum number of loop iterations to execute.</param>
    /// <returns>A detailed result describing the execution.</returns>
    public SimulationExecutionResult RunUntilDetailed(Func<bool> condition, int maxIterations = 100000)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return ExecuteDriveLoop(condition, MaxSimulatedTimeAdvance, maxIterations, observeTeardownCancellation: false);
    }

    /// <summary>
    /// Core implementation of RunUntil without context installation (for internal use).
    /// Uses round-robin execution across all non-suspended node contexts, plus the cluster queue.
    /// </summary>
    protected bool RunUntilCore(Func<bool> condition, int maxIterations)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return ExecuteDriveLoop(condition, MaxSimulatedTimeAdvance, maxIterations, observeTeardownCancellation: false).ConditionMet;
    }

    /// <summary>
    /// Attempts to execute one ready task using round-robin across all node contexts and the cluster queue.
    /// Returns true if a task was executed.
    /// </summary>
    protected bool RunOneTaskRoundRobin()
    {
        using var _ = Guard.Enter();

        // Try the cluster queue (for scheduled operations like auto-resume)
        if (TaskQueue.RunOnce())
        {
            return true;
        }

        // Try to execute from non-suspended node contexts (round-robin)
        foreach (var node in Nodes)
        {
            var context = node.Context;
            if (context.State == SimulationNodeState.Running && context.Step())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the earliest due time across all queues (node contexts + cluster queue).
    /// </summary>
    protected DateTimeOffset? GetNextWaitingDueTime()
    {
        using var _ = Guard.Enter();
        return Nodes.Select(n => n.Context.NextWaitingDueTime).Concat([TaskQueue.NextWaitingDueTime]).Min();
    }

    /// <summary>
    /// Runs the simulation until it becomes idle.
    /// </summary>
    /// <returns>The number of iterations executed. Callers can compare this to maxIterations
    /// and the current time to determine which limit was reached.</returns>
    public int RunUntilIdle(TimeSpan? maxSimulatedTime = null, int maxIterations = 100000) => RunUntilIdleCore(maxSimulatedTime, maxIterations);

    /// <summary>
    /// Runs the simulation until it becomes idle, returning a detailed result describing exactly
    /// why the run stopped, how much work it did, and what (if anything) is still pending. This is
    /// the detailed counterpart to <see cref="RunUntilIdle(TimeSpan?, int)"/>.
    /// </summary>
    /// <param name="maxSimulatedTime">The maximum simulated-time gap to jump in a single advance. Defaults to <see cref="MaxSimulatedTimeAdvance"/>.</param>
    /// <param name="maxIterations">The maximum number of loop iterations to execute.</param>
    /// <returns>A detailed result describing the execution.</returns>
    public SimulationExecutionResult RunUntilIdleDetailed(TimeSpan? maxSimulatedTime = null, int maxIterations = 100000) =>
        ExecuteDriveLoop(condition: null, maxSimulatedTime ?? MaxSimulatedTimeAdvance, maxIterations, observeTeardownCancellation: true);

    /// <summary>
    /// Core implementation of RunUntilIdle without context installation (for internal use).
    /// Uses round-robin execution across all non-suspended node contexts, plus the cluster queue.
    /// </summary>
    /// <returns>The number of iterations executed.</returns>
    protected int RunUntilIdleCore(TimeSpan? maxSimulatedTime, int maxIterations) => RunUntilIdleDetailed(maxSimulatedTime, maxIterations).Iterations;

    /// <summary>
    /// Drives a task to completion by running the simulation.
    /// The task factory is invoked with the cluster's synchronization context installed,
    /// ensuring async continuations are captured on the simulation scheduler.
    /// </summary>
    public void Run(Func<Task> taskFactory, int maxIterations = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(taskFactory);
        using var lockScope = Guard.Enter();

        var task = new Task<Task>(taskFactory);
        task.Start(TaskScheduler);

        if (!RunUntilCore(() => task.IsCompleted && task.Result.IsCompleted, maxIterations))
        {
            if (!task.IsCompleted || !task.GetAwaiter().GetResult().IsCompleted)
            {
                throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"Task did not complete within {maxIterations} iterations"));
            }
        }

        task.GetAwaiter().GetResult().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs the simulation for the specified duration or until the maximum iterations are exceeded.
    /// This is the preferred method for advancing time in tests, as it ensures that any
    /// tasks triggered by timers are processed before returning.
    /// </summary>
    /// <param name="delta">The amount of time to advance.</param>
    /// <param name="maxIterations">Maximum iterations to run while processing tasks.</param>
    /// <returns>True if the simulation reached an idle state; false if max iterations reached.</returns>
    public bool RunForDuration(TimeSpan delta, int maxIterations = 100000)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "Time delta cannot be negative");
        }

        if (delta == TimeSpan.Zero)
        {
            return true;
        }

        // Advance time to trigger timers, then run until idle
        OnTimeAdvancing(delta);

        using var lockScope = Guard.Enter();

        var targetTime = Clock.UtcNow + delta;
        var iterations = RunUntilIdleCore(maxSimulatedTime: delta, maxIterations);
        if (Clock.UtcNow < targetTime)
        {
            Clock.Advance(targetTime - Clock.UtcNow);
        }

        // If first call didn't exhaust max iterations, it reached idle or a time limit
        // Run again to process any remaining work
        var remainingIterations = maxIterations - iterations;
        if (remainingIterations == 0)
        {
            return false;
        }

        return RunUntilIdleCore(maxSimulatedTime: null, remainingIterations) < remainingIterations;
    }

    /// <summary>
    /// Runs the simulation for the specified duration or until the maximum iterations are
    /// exceeded, returning a detailed result describing exactly why the run stopped, how much work
    /// it did, and what (if anything) is still pending. This is the detailed counterpart to
    /// <see cref="RunForDuration(TimeSpan, int)"/>.
    /// </summary>
    /// <param name="delta">The amount of time to advance.</param>
    /// <param name="maxIterations">Maximum iterations to run while processing tasks.</param>
    /// <returns>A detailed result describing the execution.</returns>
    public SimulationExecutionResult RunForDurationDetailed(TimeSpan delta, int maxIterations = 100000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

        using var lockScope = Guard.Enter();
        var startTime = TimeProvider.GetUtcNow();

        if (delta == TimeSpan.Zero)
        {
            return new SimulationExecutionResult(
                SimulationExecutionReason.Idle,
                startTime,
                startTime,
                iterations: 0,
                stepsExecuted: 0,
                timeAdvanceCount: 0,
                consecutiveTimeAdvanceCount: 0,
                CapturePendingWorkSummary(),
                new SimulationExecutionLimits(maxIterations, MaxSimulatedTimeAdvance, MaxConsecutiveTimeAdvances),
                attemptedTimeAdvance: null);
        }

        OnTimeAdvancing(delta);

        var targetTime = Clock.UtcNow + delta;
        var first = RunUntilIdleDetailed(maxSimulatedTime: delta, maxIterations);

        var forcedTimeAdvance = 0;
        if (Clock.UtcNow < targetTime)
        {
            Clock.Advance(targetTime - Clock.UtcNow);
            forcedTimeAdvance = 1;
        }

        var remainingIterations = maxIterations - first.Iterations;
        if (remainingIterations <= 0)
        {
            return CombineExecutionResults(startTime, first, second: null, forcedTimeAdvance);
        }

        var second = RunUntilIdleDetailed(maxSimulatedTime: null, remainingIterations);
        return CombineExecutionResults(startTime, first, second, forcedTimeAdvance);
    }

    /// <summary>Called when a RunUntil condition is met.</summary>
    protected virtual void OnConditionMet(int iterations) { }

    /// <summary>Called when the simulation is idle with no pending work.</summary>
    protected virtual void OnSimulationIdleNoPendingWork(int iterations) { }

    /// <summary>Called when the simulation exceeds the max simulated time advance.</summary>
    protected virtual void OnSimulationStuckMaxTime(TimeSpan timeDelta) { }

    /// <summary>Called when the simulation has too many consecutive time advances.</summary>
    protected virtual void OnSimulationStuckConsecutiveTimeAdvances(int count) { }

    /// <summary>Called when max iterations is reached.</summary>
    protected virtual void OnMaxIterationsReached(int maxIterations) { }

    /// <summary>Called when teardown cancellation is requested.</summary>
    protected virtual void OnTeardownCancellationRequested() { }

    /// <summary>Called when the simulation reaches an idle state.</summary>
    protected virtual void OnSimulationReachedIdleState() { }

    /// <summary>Called when time is about to be advanced.</summary>
    protected virtual void OnTimeAdvancing(TimeSpan delta) { }

    /// <summary>
    /// Runs the consolidated drive-loop engine for one logical RunUntil/RunUntilIdle operation and
    /// dispatches the appropriate <c>On*</c> hook(s) based on the outcome, preserving the exact
    /// hook-firing behavior of the original, separate implementations.
    /// </summary>
    private SimulationExecutionResult ExecuteDriveLoop(Func<bool>? condition, TimeSpan maxSimulatedTimeAdvance, int maxIterations, bool observeTeardownCancellation)
    {
        using var _ = Guard.Enter();
        var options = new SimulationDriveLoopOptions(condition, maxSimulatedTimeAdvance, maxIterations, MaxConsecutiveTimeAdvances, observeTeardownCancellation);
        var result = _driveLoop.Execute(options);
        DispatchExecutionHooks(result, isConditionBased: condition is not null);
        return result;
    }

    /// <summary>
    /// Fires the legacy <c>On*</c> extensibility hooks that correspond to <paramref name="result"/>,
    /// with the same arguments and under the same circumstances as the original, un-consolidated
    /// RunUntilCore/RunUntilIdleCore implementations.
    /// </summary>
    private void DispatchExecutionHooks(SimulationExecutionResult result, bool isConditionBased)
    {
        switch (result.Reason)
        {
            case SimulationExecutionReason.ConditionMet:
                OnConditionMet(result.Iterations);
                break;

            case SimulationExecutionReason.Idle:
            case SimulationExecutionReason.IdleWithPendingWork:
                if (isConditionBased)
                {
                    OnSimulationIdleNoPendingWork(result.Iterations);
                }
                else
                {
                    OnSimulationReachedIdleState();
                }

                break;

            case SimulationExecutionReason.MaxSimulatedTimeAdvanceExceeded:
                OnSimulationStuckMaxTime(result.AttemptedTimeAdvance ?? TimeSpan.Zero);
                break;

            case SimulationExecutionReason.MaxConsecutiveTimeAdvancesExceeded:
                OnSimulationStuckConsecutiveTimeAdvances(result.ConsecutiveTimeAdvanceCount);
                break;

            case SimulationExecutionReason.MaxIterationsReached:
                OnMaxIterationsReached(result.Limits.MaxIterations);
                break;

            case SimulationExecutionReason.TeardownCancellationRequested:
                OnTeardownCancellationRequested();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result), result.Reason, "Unrecognized execution reason.");
        }
    }

    /// <summary>
    /// Combines the two RunUntilIdleDetailed phases of <see cref="RunForDurationDetailed"/> (plus the
    /// forced advance to the target time, if any) into a single result describing the whole operation.
    /// </summary>
    private static SimulationExecutionResult CombineExecutionResults(
        DateTimeOffset startTime,
        SimulationExecutionResult first,
        SimulationExecutionResult? second,
        int forcedTimeAdvanceCount)
    {
        var final = second ?? first;
        return new SimulationExecutionResult(
            final.Reason,
            startTime,
            final.EndTime,
            first.Iterations + (second?.Iterations ?? 0),
            first.StepsExecuted + (second?.StepsExecuted ?? 0),
            first.TimeAdvanceCount + (second?.TimeAdvanceCount ?? 0) + forcedTimeAdvanceCount,
            final.ConsecutiveTimeAdvanceCount,
            final.PendingWork,
            final.Limits,
            final.AttemptedTimeAdvance);
    }

    /// <summary>
    /// Captures a snapshot of runnable, waiting, and blocked work across the cluster queue and
    /// every node queue (including suspended nodes), with per-item diagnostics in stable order.
    /// </summary>
    private SimulationPendingWorkSummary CapturePendingWorkSummary()
    {
        using var _ = Guard.Enter();
        var now = Clock.UtcNow;
        var diagnostics = new List<SimulationScheduledItemDiagnostic>();
        var runnableCount = 0;
        var waitingCount = 0;
        var blockedCount = 0;

        CollectQueueDiagnostics("cluster", TaskQueue, isSuspended: false);
        foreach (var node in Nodes)
        {
            CollectQueueDiagnostics(node.NetworkAddress, node.Context.TaskQueue, node.Context.State == SimulationNodeState.Suspended);
        }

        var orderedDiagnostics = diagnostics
            .OrderBy(static d => d.DueTime)
            .ThenBy(static d => d.SequenceNumber)
            .ThenBy(static d => d.QueueIdentity, StringComparer.Ordinal)
            .ToArray();

        return new SimulationPendingWorkSummary(runnableCount, waitingCount, blockedCount, orderedDiagnostics);

        void CollectQueueDiagnostics(string queueIdentity, SimulationTaskQueue queue, bool isSuspended)
        {
            foreach (var item in queue.ScheduledItems)
            {
                var isReady = item.DueTime <= now;
                var isBlocked = isReady && isSuspended;
                diagnostics.Add(new SimulationScheduledItemDiagnostic(queueIdentity, item.GetType().Name, item.DueTime, item.SequenceNumber, isReady, isBlocked));

                if (isBlocked)
                {
                    blockedCount++;
                }
                else if (isReady)
                {
                    runnableCount++;
                }
                else
                {
                    waitingCount++;
                }
            }
        }
    }

    /// <summary>
    /// Safely cancels a CancellationTokenSource, catching and optionally logging any exceptions.
    /// </summary>
    protected static void SafeCancel(CancellationTokenSource cts, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(cts);
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // CTS was already disposed - this is fine
        }
        catch (AggregateException ex)
        {
            // Log but don't throw - we're in cleanup
#pragma warning disable CA1848 // Use the LoggerMessage delegates - this is rarely called cleanup code
            logger?.LogWarning(ex, "Exception during cancellation");
#pragma warning restore CA1848
        }
    }

    /// <summary>
    /// Performs application-specific async disposal.
    /// Override in derived classes to dispose nodes and other resources.
    /// </summary>
    protected abstract ValueTask DisposeAsyncCore();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        Run(async () =>
        {
            SafeCancel(_teardownCts);
            await DisposeAsyncCore();
        });

        _teardownCts.Dispose();
        GC.SuppressFinalize(this);
    }

    private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"SimulationCluster(Seed={Seed}, Nodes={Nodes.Count}, Time={Clock.CurrentTime:hh\\:mm\\:ss\\.fff})");
}

using System.Diagnostics;
using System.Globalization;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Shims;
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
    private readonly SimulationActivationToken _activationToken;
    private readonly SimulationRuntimeEnvironment _runtimeEnvironment;
    private readonly IDisposable _runtimeRegistration;
    private readonly Clockwork.Runtime.Tasks.ControlledTaskLoopCoordinator _taskCoordinator;
    private readonly IDisposable _taskCoordinatorRegistration;
    private RoundRobinCursorKind _roundRobinCursor;
    private string? _roundRobinNodeAddressExclusive;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationCluster{TNode}"/> class.
    /// Initializes a new simulation cluster with the specified seed.
    /// </summary>
    /// <param name="seed">The seed for deterministic random number generation.</param>
    /// <param name="startDateTime">Optional starting date/time for the simulation. Defaults to UTC now.</param>
    /// <param name="simulationTimeZone">
    /// Optional local time zone the deterministic <c>DateTime.Now</c>/<c>Today</c> shims observe.
    /// Defaults to <see cref="TimeZoneInfo.Utc"/> so local and UTC time coincide deterministically.
    /// </param>
    /// <param name="cryptoRandomnessPolicy">
    /// Optional policy for cryptographic-randomness calls during simulation. Defaults to
    /// <see cref="CryptoRandomnessPolicy.Reject"/>, which fails such calls with a precise
    /// diagnostic rather than ever substituting insecure bytes.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token to link with the cluster teardown.</param>
    protected SimulationCluster(
        int seed,
        DateTimeOffset? startDateTime = null,
        TimeZoneInfo? simulationTimeZone = null,
        CryptoRandomnessPolicy cryptoRandomnessPolicy = CryptoRandomnessPolicy.Reject,
        CancellationToken cancellationToken = default)
    {
        _teardownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TeardownCancellationToken = _teardownCts.Token;
        Seed = seed;
        StartDateTime = startDateTime ?? DateTimeOffset.UtcNow;
        SimulationTimeZone = simulationTimeZone ?? TimeZoneInfo.Utc;
        CryptoRandomnessPolicy = cryptoRandomnessPolicy;

        Random = new SimulationRandom(seed);

        // Runtime plumbing: an activation token minted once per cluster instance (this is the
        // simulation host, so it is entitled to one - see SimulationActivationToken) plus the
        // runtime identity and seed authority that flow through every ambient scope this cluster
        // installs.
        _activationToken = SimulationRuntimeActivation.CreateToken();
        RuntimeIdentity = new SimulationRuntimeIdentity(Guid.NewGuid(), seed, GetType().Name);
        SeedAuthority = new SimulationSeedAuthority(seed);

        // Create shared clock and cluster-level queue
        Clock = new SimulationClock(StartDateTime);
        TaskQueue = new SimulationTaskQueue(Clock, Guard, CreateClusterAmbientContext());
        TaskScheduler = new SimulationTaskScheduler(TaskQueue);

        // Create time provider using cluster queue (for GetUtcNow queries)
        _timeProvider = new SimulationTimeProvider(TaskQueue, Clock);

        // The single engine that drives RunUntil/RunUntilIdle/RunFor.
        _driveLoop = new SimulationDriveLoop(
            () => _timeProvider.GetUtcNow(),
            RunOneTaskRoundRobin,
            GetNextWaitingDueTime,
            AdvanceClock,
            CapturePendingWorkSummary,
            TeardownCancellationToken);

        // Deterministic BCL shim wiring: back the process-wide runtime environment with this
        // cluster's virtual clock and seed authority, drawing only from the Application/Identity
        // seed domains so it never perturbs the scheduler, network, or Buggify streams. The
        // registration is capability-gated by this cluster's activation token and keyed by the
        // runtime identity, so parallel clusters never collide; it is torn down in DisposeAsync.
        // The default crypto policy rejects OS-entropy calls during simulation (no silent
        // substitution); a UTC local zone keeps DateTime.Now/Today deterministic by default.
        _runtimeEnvironment = new SimulationRuntimeEnvironment(
            SeedAuthority,
            () => _timeProvider.GetUtcNow(),
            SimulationTimeZone,
            StartDateTime,
            CryptoRandomnessPolicy.ToRuntimePolicy());
        _runtimeRegistration = SimulationRuntimeServices.Register(
            _activationToken,
            RuntimeIdentity,
            _runtimeEnvironment);

        // Controlled async/task wiring: register a deterministic task coordinator for this
        // runtime so the controlled compiler machinery (async builders, awaiters, Task/ValueTask shims)
        // resolves a real coordinator instead of failing with "missing runtime service" whenever
        // rewritten code runs inside this cluster. A single ControlledTaskLoop per runtime is the whole
        // async scheduler: continuations from every node share it (see ControlledTaskLoopCoordinator).
        // The cluster's drive loop pumps this loop in RunOneTaskRoundRobin, so fire-and-forget controlled
        // continuations advance alongside queued work; synchronous controlled waits pump it directly.
        // Keyed by the same runtime identity and torn down in DisposeAsync, exactly like the environment.
        _taskCoordinator = new Clockwork.Runtime.Tasks.ControlledTaskLoopCoordinator();
        _taskCoordinatorRegistration = Clockwork.Runtime.Tasks.SimulationTaskCoordination.Register(
            _activationToken,
            RuntimeIdentity,
            _taskCoordinator);
    }

    /// <summary>
    /// Gets the seed used for this cluster.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// <para>
    /// Gets this cluster's runtime identity - a process-unique id (plus the seed and this
    /// cluster's concrete type name, for diagnostics) that becomes ambient (see
    /// <see cref="Clockwork.Runtime.Execution.SimulationExecutionContext"/>) around every piece of
    /// work this cluster or its nodes execute through an ambient-integrated queue.
    /// </para>
    /// <para>
    /// Runtime services and diagnostics use this identity as the stable "which simulation is this?"
    /// answer wherever ambient context is installed.
    /// </para>
    /// </summary>
    public SimulationRuntimeIdentity RuntimeIdentity { get; }

    /// <summary>
    /// Gets the root deterministic seed/decision authority for this cluster, exposing independent
    /// named seed domains (scheduler, network, application, identity, Buggify, model - see
    /// <see cref="Clockwork.Runtime.Random.SimulationSeedDomain"/>) plus stable per-node/per-site
    /// child derivation, all as pure functions of <see cref="Seed"/> - never of registration or
    /// fork order.
    /// </summary>
    public SimulationSeedAuthority SeedAuthority { get; }

    /// <summary>
    /// Gets the local time zone the deterministic <c>DateTime.Now</c>/<c>DateTime.Today</c> shims
    /// observe for nodes in this cluster. Defaults to <see cref="TimeZoneInfo.Utc"/> so local and UTC
    /// time coincide deterministically regardless of the host machine's zone.
    /// </summary>
    public TimeZoneInfo SimulationTimeZone { get; }

    /// <summary>
    /// Gets the cryptographic-randomness policy the deterministic crypto shims enforce during this
    /// simulation. Defaults to <see cref="CryptoRandomnessPolicy.Reject"/>: OS-entropy calls
    /// fail with a precise diagnostic rather than ever silently substituting insecure bytes.
    /// </summary>
    public CryptoRandomnessPolicy CryptoRandomnessPolicy { get; }

    /// <summary>
    /// Gets the deterministic runtime environment the BCL shims dispatch to while this cluster's
    /// ambient runtime is active. Backed by this cluster's virtual clock and seed authority.
    /// </summary>
    public ISimulationRuntimeEnvironment RuntimeEnvironment => _runtimeEnvironment;

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
    public SimulationRandom ForkRandom()
    {
        using var _ = Guard.Enter();
#pragma warning disable CA5394 // Do not use insecure randomness
        return new SimulationRandom(Random.Next());
#pragma warning restore CA5394 // Do not use insecure randomness
    }

    /// <summary>
    /// <para>
    /// Creates the ambient-context configuration for a node-scoped queue: this cluster's
    /// activation token and runtime identity, narrowed to the given node's identity. Pass the
    /// result to a per-node <see cref="SimulationTaskQueue"/>/<see cref="SimulationNodeContext"/>
    /// so that work executed on that queue ambiently reports both the cluster's runtime and this
    /// specific node - see <see cref="Clockwork.Runtime.Execution.SimulationExecutionContext"/>.
    /// </para>
    /// <para>
    /// Purely additive plumbing: nodes that never call this (e.g. hand-written
    /// <see cref="SimulationNodeContext"/> construction outside a builder-created simulation)
    /// simply get no ambient integration, exactly as before this existed.
    /// </para>
    /// </summary>
    /// <param name="nodeAddress">The node's stable network address.</param>
    /// <returns>An ambient-context configuration scoped to this cluster and the given node.</returns>
    protected SimulationAmbientContextConfiguration CreateNodeAmbientContext(string nodeAddress)
    {
        ArgumentException.ThrowIfNullOrEmpty(nodeAddress);
        return new SimulationAmbientContextConfiguration(_activationToken, RuntimeIdentity, new SimulationNodeIdentity(nodeAddress));
    }

    /// <summary>
    /// Creates the ambient-context configuration for the cluster-level (non-node-scoped) queue:
    /// this cluster's activation token and runtime identity, with no node narrowing.
    /// </summary>
    private SimulationAmbientContextConfiguration CreateClusterAmbientContext() =>
        new(_activationToken, RuntimeIdentity, Node: null);

    /// <summary>
    /// Runs the simulation until the specified condition is met.
    /// </summary>
    /// <param name="condition">The condition that ends the run when it becomes true.</param>
    /// <param name="maxIterations">The maximum number of loop iterations to execute.</param>
    /// <returns>A detailed result describing the execution.</returns>
    public SimulationExecutionResult RunUntil(Func<bool> condition, int maxIterations = 100_000)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return ExecuteDriveLoop(condition, MaxSimulatedTimeAdvance, maxIterations, observeTeardownCancellation: false);
    }

    /// <summary>
    /// Attempts to execute one ready task using persistent round-robin order across the cluster queue,
    /// ordinally sorted node queues, and the controlled task loop.
    /// Returns true if a task was executed.
    /// </summary>
    protected bool RunOneTaskRoundRobin()
    {
        using var _ = Guard.Enter();
        var nodes = Nodes;
        var sourceCount = nodes.Count + 2;
        var startIndex = GetRoundRobinStartIndex(nodes);

        for (var offset = 0; offset < sourceCount; offset++)
        {
            var sourceIndex = (startIndex + offset) % sourceCount;
            if (sourceIndex == 0)
            {
                if (TaskQueue.RunOnce())
                {
                    _roundRobinCursor = RoundRobinCursorKind.Node;
                    _roundRobinNodeAddressExclusive = null;
                    return true;
                }

                continue;
            }

            if (sourceIndex <= nodes.Count)
            {
                var node = nodes[sourceIndex - 1];
                var context = node.Context;
                if (context.State == SimulationNodeState.Running && context.Step())
                {
                    _roundRobinCursor = RoundRobinCursorKind.Node;
                    _roundRobinNodeAddressExclusive = node.NetworkAddress;
                    return true;
                }

                continue;
            }

            if (_taskCoordinator.Loop.IsIdle)
            {
                continue;
            }

            using (SimulationExecutionContext.EnterRuntime(_activationToken, RuntimeIdentity))
            {
                if (_taskCoordinator.Loop.RunOnce())
                {
                    _roundRobinCursor = RoundRobinCursorKind.Cluster;
                    _roundRobinNodeAddressExclusive = null;
                    return true;
                }
            }
        }

        return false;
    }

    private int GetRoundRobinStartIndex(IReadOnlyList<TNode> nodes)
    {
        if (_roundRobinCursor == RoundRobinCursorKind.Cluster)
        {
            return 0;
        }

        if (_roundRobinNodeAddressExclusive is null)
        {
            return 1;
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            if (StringComparer.Ordinal.Compare(nodes[index].NetworkAddress, _roundRobinNodeAddressExclusive) > 0)
            {
                return index + 1;
            }
        }

        return nodes.Count + 1;
    }

    /// <summary>
    /// Gets the earliest due time across all queues (node contexts + cluster queue) and the controlled
    /// loop's virtual-time deadlines (finite <c>Monitor</c>/<c>SemaphoreSlim</c> waits), so a finite wait
    /// is driven to its simulated deadline by the same advance-to-next-due machinery as every timer.
    /// </summary>
    protected DateTimeOffset? GetNextWaitingDueTime()
    {
        using var _ = Guard.Enter();
        var earliest = Nodes.Select(n => n.Context.NextWaitingDueTime).Concat([TaskQueue.NextWaitingDueTime]).Min();

        // Fold in the controlled loop's next virtual-time deadline (measured from StartDateTime). Null-safe:
        // with no pending deadline this is a no-op, so existing advance behaviour is completely unchanged.
        var loopDue = _taskCoordinator.Loop.NextDeadlineDue();
        if (loopDue is not null)
        {
            var loopAbsolute = StartDateTime + loopDue.Value;
            if (earliest is null || loopAbsolute < earliest.Value)
            {
                earliest = loopAbsolute;
            }
        }

        return earliest;
    }

    /// <summary>
    /// Advances the shared simulation clock, then steps the controlled loop's modelled time to match and
    /// fires any virtual-time deadlines that are now due (finite <c>Monitor</c>/<c>SemaphoreSlim</c> waits).
    /// Forward-only and null-safe: with no pending deadlines the loop step is a cheap no-op.
    /// </summary>
    /// <param name="delta">The non-negative amount to advance.</param>
    private void AdvanceClock(TimeSpan delta)
    {
        Clock.Advance(delta);
        _taskCoordinator.Loop.AdvanceTimeTo(_timeProvider.GetUtcNow() - StartDateTime);
    }

    /// <summary>
    /// Runs the simulation until it becomes idle.
    /// </summary>
    /// <param name="maxTimeAdvance">The maximum simulated-time gap to jump in a single advance. Defaults to <see cref="MaxSimulatedTimeAdvance"/>.</param>
    /// <param name="maxIterations">The maximum number of loop iterations to execute.</param>
    /// <returns>A detailed result describing the execution.</returns>
    public SimulationExecutionResult RunUntilIdle(TimeSpan? maxTimeAdvance = null, int maxIterations = 100_000) =>
        ExecuteDriveLoop(condition: null, maxTimeAdvance ?? MaxSimulatedTimeAdvance, maxIterations, observeTeardownCancellation: true);

    /// <summary>
    /// Drives a task to completion by running the simulation.
    /// The task factory is invoked with the cluster's synchronization context installed,
    /// ensuring async continuations are captured on the simulation scheduler.
    /// </summary>
    public void RunToCompletion(Func<Task> taskFactory, int maxIterations = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(taskFactory);
        using var lockScope = Guard.Enter();

        Task task = StartTask(taskFactory);
        SimulationExecutionResult result = RunUntil(() => task.IsCompleted, maxIterations);
        EnsureTaskCompleted(task, result);
        task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Drives a task to completion using an adaptive execution budget.
    /// </summary>
    public void RunToCompletion(Func<Task> taskFactory, AdaptiveExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(taskFactory);
        ArgumentNullException.ThrowIfNull(budget);
        using var lockScope = Guard.Enter();

        Task task = StartTask(taskFactory);
        SimulationExecutionResult result = RunUntil(() => task.IsCompleted, budget);
        EnsureTaskCompleted(task, result);
        task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Drives a task to completion and returns its result.
    /// </summary>
    public T RunToCompletion<T>(Func<Task<T>> taskFactory, int maxIterations = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(taskFactory);
        using var lockScope = Guard.Enter();

        Task<T> task = StartTask(taskFactory);
        SimulationExecutionResult result = RunUntil(() => task.IsCompleted, maxIterations);
        EnsureTaskCompleted(task, result);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Drives a task to completion using an adaptive execution budget and returns its result.
    /// </summary>
    public T RunToCompletion<T>(Func<Task<T>> taskFactory, AdaptiveExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(taskFactory);
        ArgumentNullException.ThrowIfNull(budget);
        using var lockScope = Guard.Enter();

        Task<T> task = StartTask(taskFactory);
        SimulationExecutionResult result = RunUntil(() => task.IsCompleted, budget);
        EnsureTaskCompleted(task, result);
        return task.GetAwaiter().GetResult();
    }

    private Task StartTask(Func<Task> taskFactory)
    {
        Task<Task> outer;
        using (ExecutionContext.SuppressFlow())
        {
            outer = new Task<Task>(taskFactory);
        }

        outer.Start(TaskScheduler);
        return outer.Unwrap();
    }

    private Task<T> StartTask<T>(Func<Task<T>> taskFactory)
    {
        Task<Task<T>> outer;
        using (ExecutionContext.SuppressFlow())
        {
            outer = new Task<Task<T>>(taskFactory);
        }

        outer.Start(TaskScheduler);
        return outer.Unwrap();
    }

    private static void EnsureTaskCompleted(Task task, SimulationExecutionResult result)
    {
        if (!task.IsCompleted)
        {
            throw new TimeoutException(string.Create(
                CultureInfo.InvariantCulture,
                $"Task did not complete.{Environment.NewLine}{result.ToDetailedString()}"));
        }
    }

    /// <summary>
    /// Runs the simulation for the specified duration or until the maximum iterations are
    /// exceeded, returning a detailed result describing exactly why the run stopped, how much work
    /// it did, and what (if anything) is still pending.
    /// </summary>
    /// <param name="duration">The amount of time to advance.</param>
    /// <param name="maxIterations">Maximum iterations to run while processing tasks.</param>
    /// <returns>A detailed result describing the execution.</returns>
    public SimulationExecutionResult RunFor(TimeSpan duration, int maxIterations = 100_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        using var lockScope = Guard.Enter();
        var startTime = TimeProvider.GetUtcNow();

        if (duration == TimeSpan.Zero)
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

        OnTimeAdvancing(duration);

        var targetTime = Clock.UtcNow + duration;
        var first = RunUntilIdle(maxTimeAdvance: duration, maxIterations);

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

        var second = RunUntilIdle(maxTimeAdvance: null, remainingIterations);
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
    private SimulationExecutionResult ExecuteDriveLoop(
        Func<bool>? condition,
        TimeSpan maxSimulatedTimeAdvance,
        int maxIterations,
        bool observeTeardownCancellation,
        int initialConsecutiveTimeAdvances = 0)
    {
        using var _ = Guard.Enter();
        var options = new SimulationDriveLoopOptions(
            condition,
            maxSimulatedTimeAdvance,
            maxIterations,
            MaxConsecutiveTimeAdvances,
            observeTeardownCancellation,
            initialConsecutiveTimeAdvances);
        var result = _driveLoop.Execute(options);
        DispatchExecutionHooks(result, isConditionBased: condition is not null);
        return result;
    }

    /// <summary>
    /// Fires the <c>On*</c> extensibility hooks that correspond to <paramref name="result"/>.
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
    /// Combines the two RunUntilIdle passes of <see cref="RunFor"/> (plus the
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

    private enum RoundRobinCursorKind
    {
        Cluster,
        Node,
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

        foreach (Clockwork.Runtime.Tasks.ControlledTaskDeadlineInfo deadline in
            _taskCoordinator.Loop.CapturePendingDeadlines())
        {
            DateTimeOffset dueTime = StartDateTime + deadline.DueTime;
            bool isReady = dueTime <= now;
            diagnostics.Add(new SimulationScheduledItemDiagnostic(
                "controlled-task-loop",
                "PausedUntilTime",
                dueTime,
                deadline.Sequence,
                isReady,
                IsBlocked: false));
            if (isReady)
            {
                runnableCount++;
            }
            else
            {
                waitingCount++;
            }
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Exception>? failures = null;

        try
        {
            RunToCompletion(async () =>
            {
                SafeCancel(_teardownCts);
                _taskCoordinator.Loop.CancelPendingDeadlines();
                await DisposeAsyncCore();
            });
        }
        catch (Exception exception)
        {
            AddDisposalFailure(ref failures, exception);
        }

        // Tear down the deterministic runtime environment registration so a later simulation with a
        // fresh runtime identity starts clean and this cluster's environment stops serving shims.
        try
        {
            _runtimeRegistration.Dispose();
        }
        catch (Exception exception)
        {
            AddDisposalFailure(ref failures, exception);
        }

        // Unregister the controlled task coordinator so a later runtime starts with no coordinator and
        // the missing-service path is exercised correctly until it registers its own.
        try
        {
            _taskCoordinator.Dispose();
            _taskCoordinatorRegistration.Dispose();
        }
        catch (Exception exception)
        {
            AddDisposalFailure(ref failures, exception);
        }

        try
        {
            _teardownCts.Dispose();
        }
        catch (Exception exception)
        {
            AddDisposalFailure(ref failures, exception);
        }

        GC.SuppressFinalize(this);

        if (failures is not null)
        {
            throw new AggregateException("One or more errors occurred while disposing the simulation cluster.", failures);
        }
    }

    private static void AddDisposalFailure(ref List<Exception>? failures, Exception exception)
    {
        failures ??= [];
        if (exception is AggregateException aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else
        {
            failures.Add(exception);
        }
    }

    private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"SimulationCluster(Seed={Seed}, Nodes={Nodes.Count}, Time={Clock.CurrentTime:hh\\:mm\\:ss\\.fff})");
}

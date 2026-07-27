# Clockwork

Clockwork is a deterministic simulation testing framework for distributed systems in .NET. It provides controlled time, cooperative task scheduling, seeded randomness, simulated networks, node lifecycle controls, and chaos injection so failures can be reproduced from a seed.

## Features

- A shared `SimulationClock` and `SimulationTimeProvider`
- Deterministic `TaskScheduler` and `SynchronizationContext` implementations
- Per-node task queues with suspend, resume, and single-step controls
- Seeded random streams for reproducible scenarios
- In-memory network partitions, isolation, loss, delay, and jitter
- Extensible cluster and chaos-injection base classes
- A fluent `SimulationBuilder` for common simulations that don't need a hand-written subclass
- Adaptive `RunUntilConverged`/`RunUntilIdleConverged` execution budgets
- Stable, cross-process-safe seed derivation from strings (`SimulationSeed`)
- Reusable rendezvous primitives (`SimulationGate`, `SimulationLatch`, `SimulationBarrier`)
- In-memory logging for simulation diagnostics

Clockwork targets .NET 10.

## Build and test

```powershell
dotnet build Clockwork.slnx
dotnet run --project tests\Clockwork.Tests\Clockwork.Tests.csproj -- --timeout 60s
dotnet pack Clockwork.csproj --configuration Release
```

The NuGet package ID is `Clockwork.Simulation`. Until packages are published, clone the repository or add it as a Git submodule and reference `Clockwork.csproj`.

## Define a simulation

Derive your application-specific node and cluster types from `SimulationNode` and `SimulationCluster<TNode>`:

```csharp
using Clockwork;

public sealed class TestNode(string address, SimulationNodeContext context) : SimulationNode
{
    public override string NetworkAddress { get; } = address;
    public override SimulationNodeContext Context { get; } = context;
    public override bool IsInitialized => true;
}

public sealed class TestCluster : SimulationCluster<TestNode>
{
    public TestCluster(int seed)
        : base(seed)
    {
        Network = new SimulationNetwork(() => Nodes, Random.Fork());
    }

    public SimulationNetwork Network { get; }

    public TestNode AddNode(string address)
    {
        var context = new SimulationNodeContext(Clock, Guard, CreateDerivedRandom(), TaskQueue);
        var node = new TestNode(address, context);
        RegisterNode(node);
        return node;
    }

    protected override ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
}
```

## Build a simulation without a subclass

For simulations that don't need application-specific cluster behavior, `SimulationBuilder`
produces a working `SimulationCluster<TNode>` without a hand-written subclass:

```csharp
using Clockwork;

var builder = new SimulationBuilder().WithSeed(12345);
var node1 = builder.AddNode("node-1", state: 0);   // SimulationNodeHandle<int>
var node2 = builder.AddNode("node-2");             // SimulationNodeHandle<object?>

await using var cluster = builder.Build();

var handled = false;
node1.Context.TaskQueue.EnqueueAfter(() => handled = true, TimeSpan.FromSeconds(30));
cluster.RunUntilConverged(() => handled);

cluster.Network.CreateBidirectionalPartition(node1.NetworkAddress, node2.NetworkAddress);
```

`WithSeed` is required - `Build()` throws `InvalidOperationException` if it was never called, so a
built simulation can never be accidentally non-deterministic by defaulting to a wall-clock-derived
seed. `AddNode`/`AddNode<TState>` return a `SimulationNodeHandle<TState>` immediately so you can
capture it in a local variable, but its `Context` and `State` are only usable **after** `Build()`
returns - accessing them earlier throws `InvalidOperationException`, since the node's queue, clock,
and random generator do not exist until the cluster does.

`SimulationBuilder` and hand-written `SimulationCluster<TNode>` subclasses are not mutually
exclusive: everything under "Define a simulation" above continues to work unmodified, and nothing
about the builder changes `SimulationCluster<TNode>`'s existing API or behavior.

### Registering existing node subclasses alongside plain handles

`AddCustomNode<TNode>` registers an existing `SimulationNode` subclass in the same
`BuiltSimulation`, side-by-side with plain handles - a foundation for heterogeneous node
composition:

```csharp
// MyWorkerNode is any existing SimulationNode subclass - e.g. TestNode from "Define a
// simulation" above: public sealed class MyWorkerNode(string address, SimulationNodeContext context) : SimulationNode { ... }
var builder = new SimulationBuilder().WithSeed(1);
var counter = builder.AddNode("counter", state: 0);
builder.AddCustomNode("worker", context => new MyWorkerNode("worker", context));

await using var cluster = builder.Build();
var worker = (MyWorkerNode)cluster.GetNodeByAddress("worker")!;
```

Unlike the handle overloads, `AddCustomNode` cannot hand the constructed node back synchronously -
`factory` needs a real `SimulationNodeContext`, which doesn't exist until `Build()` runs. Retrieve
it afterwards via `BuiltSimulation.GetNodeByAddress(...)` or `cluster.Nodes.OfType<MyWorkerNode>()`.

**What this foundation does not include yet:** there is no dependency-injection-style construction
or startup ordering between nodes, and no per-node-type discovery beyond address lookup and
`OfType<T>()`. Full heterogeneous lifecycle support (typed groups, startup ordering, DI-style
construction) is deferred to a future phase - this PR ships the clean, tested foundation (shared
clock/guard/drive-loop across mixed node types) rather than a partial lifecycle API that would be
misleading about what it actually guarantees.

`BuiltSimulation` disposes every registered node - and, for handles, their state payload - that
implements `IAsyncDisposable`/`IDisposable` when the cluster itself is disposed.

`SimulationBuilder`/`BuiltSimulation` are deliberately small and composable (plain classes, no
required base class for node state) so that a future Generic Host integration can wrap them without
a redesign; this PR does not implement `IHost` integration itself.

## Drive simulated execution

```csharp
await using var cluster = new TestCluster(seed: 12345);
var node1 = cluster.AddNode("node-1");
var node2 = cluster.AddNode("node-2");

var completed = false;
node1.Context.TaskQueue.EnqueueAfter(
    () => completed = true,
    TimeSpan.FromSeconds(30));

cluster.RunUntil(() => completed);

cluster.Network.CreateBidirectionalPartition(node1.NetworkAddress, node2.NetworkAddress);
cluster.RunForDuration(TimeSpan.FromMinutes(5));
cluster.Network.HealBidirectionalPartition(node1.NetworkAddress, node2.NetworkAddress);
```

`RunUntil`, `RunUntilIdle`, and `RunForDuration` execute one queued operation at a time and advance the shared clock only when no work is ready. Internally, all three are thin wrappers around a single consolidated drive-loop engine, so their hook-firing order, time-advancement behavior, and stuck/limit detection are guaranteed to stay in sync with each other.

## Detailed execution results and diagnostics

Each `bool`/`int`-returning method above has a `*Detailed` counterpart that returns a
`SimulationExecutionResult` describing exactly why the run stopped, instead of collapsing that
information into a single boolean or count:

```csharp
var result = cluster.RunUntilDetailed(() => completed, maxIterations: 10_000);

if (result.Reason != SimulationExecutionReason.ConditionMet)
{
    // ToDetailedString() includes the reason, simulated start/end/elapsed time, iteration and
    // time-advance counts, and a stable, invariant-culture-formatted snapshot of any runnable,
    // waiting, or blocked work left in the cluster and node queues.
    throw new InvalidOperationException(result.ToDetailedString());
}
```

- `RunUntilDetailed(Func<bool>, int)` - detailed counterpart to `RunUntil`.
- `RunUntilIdleDetailed(TimeSpan?, int)` - detailed counterpart to `RunUntilIdle`.
- `RunForDurationDetailed(TimeSpan, int)` - detailed counterpart to `RunForDuration`.

`SimulationExecutionResult.Reason` (a `SimulationExecutionReason`) distinguishes every way a run
can stop: `ConditionMet`, `Idle`, `IdleWithPendingWork` (idle, but a suspended node has ready work
it cannot execute), `MaxSimulatedTimeAdvanceExceeded`, `MaxConsecutiveTimeAdvancesExceeded`,
`MaxIterationsReached`, and `TeardownCancellationRequested`. `SimulationExecutionResult.PendingWork`
(a `SimulationPendingWorkSummary`) reports runnable/waiting/blocked counts plus a stably-ordered
list of per-item diagnostics (queue identity, scheduled item type, due time, sequence number, and
readiness) - useful for diagnosing a stuck or failed-to-progress simulation without adding any
production-code instrumentation. `SimulationCluster<TNode>.MaxConsecutiveTimeAdvances` (default
10,000) makes the previously hardcoded stuck-detection threshold configurable and inspectable,
alongside the existing `MaxSimulatedTimeAdvance` property.

The existing `RunUntil`/`RunUntilIdle`/`RunForDuration` methods, their `protected`
`RunUntilCore`/`RunUntilIdleCore` helpers, and every `On*` extensibility hook keep their exact
signatures and behavior - the detailed APIs are purely additive. Prefer the detailed APIs in new
code and in test-failure diagnostics; keep using the existing APIs anywhere you only need a
boolean/count result.

## Adaptive execution budgets

`RunUntil`/`RunUntilIdle` (and their `*Detailed` counterparts) require a `maxIterations` sized to
the scenario. `RunUntilConverged`/`RunUntilIdleConverged` remove that guesswork by escalating the
iteration budget automatically:

```csharp
// No maxIterations to guess - starts small and escalates only as needed.
var result = cluster.RunUntilConverged(() => allNodesConverged);

// Or tune the escalation curve and hard cap explicitly:
var budget = new SimulationAdaptiveBudget(initialMaxIterations: 500, growthFactor: 8.0, maxTotalIterations: 5_000_000);
var idleResult = cluster.RunUntilIdleConverged(budget: budget);
```

Both return a `SimulationExecutionResult`, combined across every batch actually run (summed
`Iterations`/`StepsExecuted`/`TimeAdvanceCount`; `Reason`/`PendingWork`/etc. from the final batch -
the same folding convention `RunForDurationDetailed` already uses to merge sub-calls).

**Progress heuristic, precisely:** each batch runs via `RunUntilDetailed`/`RunUntilIdleDetailed`. If
a batch's `Reason` is anything other than `SimulationExecutionReason.MaxIterationsReached`,
execution stops immediately without escalating - either the goal was reached (`ConditionMet`), or
the simulation is genuinely stuck in a way a bigger budget cannot fix (`Idle`,
`IdleWithPendingWork`, `MaxSimulatedTimeAdvanceExceeded`, `MaxConsecutiveTimeAdvancesExceeded`,
`TeardownCancellationRequested`). Escalation happens only after `MaxIterationsReached`, because
reaching that reason means every iteration in the batch executed a real step or a clock advance
(`StepsExecuted + TimeAdvanceCount == Iterations` for that batch) - there is always more forward
motion for a bigger budget to reach. A scenario that is truly spinning without making progress is
instead caught by the separate, always-enforced `MaxConsecutiveTimeAdvances` safety net, which
surfaces as `MaxConsecutiveTimeAdvancesExceeded` rather than `MaxIterationsReached`.

`SimulationAdaptiveBudget.MaxTotalIterations` (default 10,000,000) is a hard ceiling on the sum of
iterations across every batch - escalation never removes the safety cap that explicit
`maxIterations` limits already provide; it just removes the need to pick a value up front.
`SimulationAdaptiveBudget.Default` (1,000 initial iterations, 4x growth, 10,000,000 total) is used
when no budget is supplied.

## Stable deterministic seeds

Hard-coding an arbitrary integer seed per test is brittle once you have more than a handful of
tests. `SimulationSeed` derives a stable seed from strings instead:

```csharp
var seed = SimulationSeed.FromString(nameof(MyTest));
// or combine multiple components (e.g. class + method name):
var seed2 = SimulationSeed.FromStrings(GetType().FullName!, nameof(MyTest));

await using var cluster = new TestCluster(seed);
```

This deliberately never uses `string.GetHashCode()`/`object.GetHashCode()`: the runtime documents
that hash code as unstable across processes, .NET versions, and even repeated runs of the same
process (string hashing is randomized per-process by default) - using it as a seed would silently
break reproducibility, the entire point of a seed, the moment a suite runs on a different machine
or a second time. Instead, `SimulationSeed` SHA-256-hashes the UTF-8 bytes of the input and
interprets the first four bytes of the digest as a big-endian signed `int`. SHA-256 and UTF-8 are
both fixed, versioned, platform-independent, so the same string always produces the same seed on
any machine, any .NET version, and any process - including across separate machines.

## Rendezvous primitives

`SimulationGate`, `SimulationLatch`, and `SimulationBarrier` replace hand-rolled
`TaskCompletionSource` (or lists of them) for letting simulated work wait for a signal. All three
dispatch waiter completions through a `SimulationTaskQueue` - never inline, never via a real-time
wait or thread-pool callback - so release order is deterministic:

```csharp
// Gate: level-triggered, reopenable. Waiters block while closed, pass through while open.
var gate = new SimulationGate(cluster.TaskQueue);
var waitTask = gate.WaitAsync(cancellationToken);
gate.Open(); // releases every current waiter; can be Close()d and Open()ed again

// Latch: one-shot countdown, modeled on CountdownEvent. Cannot be reset once signaled.
var latch = new SimulationLatch(cluster.TaskQueue, initialCount: 3);
latch.Signal(); // decrement; releases all waiters when the count reaches zero

// Barrier: cyclic rendezvous, modeled on System.Threading.Barrier. Resets automatically each round.
var barrier = new SimulationBarrier(cluster.TaskQueue, participantCount: 3);
await barrier.ArriveAndWaitAsync(cancellationToken); // released only once every participant has arrived
```

All three observe cancellation synchronously (per Clockwork's determinism requirements) and accept
an optional `name` for debugger diagnostics. `SimulationBarrier` additionally retracts a canceled
participant's arrival, so a canceled wait never silently counts toward releasing the others.

## Determinism requirements

Clockwork can only control dependencies routed through the simulation:

- Inject `TimeProvider`; do not use wall-clock APIs or `Task.Delay` directly.
- Keep continuations on the simulation context; avoid `ConfigureAwait(false)`.
- Do not use `Task.Run`, thread-pool APIs, real network I/O, or real file I/O.
- Use `SimulationRandom` or a derived random stream instead of `Random.Shared`.
- Forward cancellation tokens and use synchronous cancellation callbacks.

`SimulationSynchronizationContext.Send` supports exactly two safe cases, and rejects everything
else with a precise diagnostic rather than papering over it: if the calling thread is already on
the context's owning simulated operation (`SynchronizationContext.Current` or
`TaskScheduler.Current` backed by the same queue), the callback runs inline immediately, since
nothing else can be running concurrently with it. Otherwise, the callback is scheduled onto the
queue (preserving deterministic FIFO order) and `Send` synchronously pumps that same queue on the
calling thread until the callback executes - it never performs a real-time cross-thread wait,
which nothing in the simulation autonomously satisfies. If a third thread is genuinely,
concurrently inside the queue at the same time, `Send` throws `InvalidOperationException`
identifying the conflicting callback and state instead of deadlocking or silently reordering work:
simulation code must not touch a queue from more than one thread at once.

## Roadmap and compatibility

See [docs/compatibility.md](docs/compatibility.md) for the intended deterministic
instrumentation modes (cooperative, controlled, race exploration, optional deep
instrumentation) and the platform/deployment contract (.NET 10, Windows/Linux/macOS,
JIT and ReadyToRun today; deferred limitations for single-file, trimming,
NativeAOT, signed assemblies, and profiler conflicts).

This phase (composition ergonomics, adaptive budgets, stable seeds, rendezvous primitives, and the
`Send` improvement above) is still entirely cooperative-mode: it does not add ambient runtime
context, controlled-operation physical-thread gating, IL rewriting (Cecil), fault injection
("Buggify"), API compatibility shims, Generic Host integration, or HTTP support. Those remain
tracked by the modes in `docs/compatibility.md`.

## License

Clockwork is licensed under the [MIT License](LICENSE). See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the policy on adapting
third-party material.

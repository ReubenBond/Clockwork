# Clockwork

Clockwork is a deterministic simulation testing framework for distributed systems in .NET. It provides controlled time, cooperative task scheduling, seeded randomness, simulated networks, node lifecycle controls, and chaos injection so failures can be reproduced from a seed.

## Features

- A shared `SimulationClock` and `SimulationTimeProvider`
- Deterministic `TaskScheduler` and `SynchronizationContext` implementations
- Per-node task queues with suspend, resume, and single-step controls
- Seeded random streams for reproducible scenarios
- In-memory network partitions, isolation, loss, delay, and jitter
- Extensible cluster and chaos-injection base classes
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

## Determinism requirements

Clockwork can only control dependencies routed through the simulation:

- Inject `TimeProvider`; do not use wall-clock APIs or `Task.Delay` directly.
- Keep continuations on the simulation context; avoid `ConfigureAwait(false)`.
- Do not use `Task.Run`, thread-pool APIs, real network I/O, or real file I/O.
- Use `SimulationRandom` or a derived random stream instead of `Random.Shared`.
- Forward cancellation tokens and use synchronous cancellation callbacks.

## Roadmap and compatibility

See [docs/compatibility.md](docs/compatibility.md) for the intended deterministic
instrumentation modes (cooperative, controlled, race exploration, optional deep
instrumentation) and the platform/deployment contract (.NET 10, Windows/Linux/macOS,
JIT and ReadyToRun today; deferred limitations for single-file, trimming,
NativeAOT, signed assemblies, and profiler conflicts).

## License

Clockwork is licensed under the [MIT License](LICENSE). See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the policy on adapting
third-party material.

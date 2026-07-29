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
- Versioned canonical replay artifacts with exact compatibility and divergence checks
- Bounded seeded schedule exploration and deterministic failure-trace minimization
- Stable operation/resource wait graphs, deadlock cycles, race pairs, and timer diagnostics
- `clockwork record`, `replay`, `explore`, `minimize`, and `trace show` commands
- Fixed and adaptive `RunUntil`/`RunUntilIdle` execution budgets
- Stable, cross-process-safe seed derivation from strings (`SimulationSeed`)
- Reusable rendezvous primitives (`SimulationGate`, `SimulationLatch`, `SimulationBarrier`)
- In-memory logging for simulation diagnostics

Clockwork targets .NET 10.

## Current capability contract

This matrix is the authoritative current-state summary. Detailed signatures are generated in
[`docs/rule-inventory.md`](docs/rule-inventory.md); compatibility rationale and deployment constraints
are in [`docs/compatibility.md`](docs/compatibility.md).

| Area | Current support | Durable limitations |
|---|---|---|
| Simulation kernel | Virtual clock, seeded randomness, simulated network, cooperative task queues, node lifecycle, diagnostics, rendezvous primitives, and controlled scheduling. | Application hosting and transport models are consumer-owned; Clockwork ships no dedicated hosting or HTTP package. |
| Build and CLI instrumentation | Opt-in, out-of-place Cecil rewriting through `Clockwork.Instrumentation.Build` and `Clockwork.Tool`; direct-call analyzers; deterministic manifests and incremental inputs. | Instrument before R2R, single-file bundling, trimming, or NativeAOT. Rewritten strong names and closure references are stripped automatically; Authenticode is not re-applied. |
| Deterministic BCL rules | `clockwork.bcl.deterministic` controls the exact time, identity, and random signatures in the generated inventory. | APIs outside the inventory retain no determinism claim. Cryptographic entropy is rejected unless the explicit deterministic-insecure test policy is enabled. |
| Controlled concurrency | `clockwork.tasks.controlled` controls async builders/awaiters, task combinators and waits, `Task.Run`, all .NET 10 `TaskFactory`/`TaskFactory<T>.StartNew` overloads, `Thread`, `ThreadPool`, `Parallel`, `Monitor`, `System.Threading.Lock`, and `SemaphoreSlim`. Debug and Release lowering are conformance-tested. | Synchronous `ValueTask` blocking, custom task schedulers, unsupported task-creation options, native-overlapped thread-pool work, and OS-specific thread controls are rejected. |
| Synchronization | Full .NET 10 `Interlocked` and `Volatile`; `SpinWait`; events and wait handles; registered waits; `ReaderWriterLockSlim`; `ManualResetEventSlim`; unnamed kernel `Mutex`/`Semaphore`; `SpinLock`; `ExecutionContext`; `SynchronizationContext`; `Barrier`; and `CountdownEvent`. | Named/cross-process primitives, open-existing APIs, raw handles, raw `SynchronizationContext.Wait`, and `WaitAll` arrays containing a `Mutex` are rejected. |
| Virtual timers | `System.Threading.Timer`, `System.Timers.Timer`, `PeriodicTimer`, all .NET 10 `Task.Delay` and `Task.WaitAsync` overloads, timer-driven cancellation, and `TimeProvider.System`/`CreateTimer`. | Custom providers and non-null `System.Timers.Timer.SynchronizingObject`/designer integration are rejected. |
| Replay and race exploration | Canonical replay schema version 1, exact compatibility/divergence checks, bounded serial schedule exploration, failure minimization, and opt-in `RaceExploration` instrumentation with structured first-race reports. | Controlled mode adds no fine-grained memory/control-flow calls. Race tracking excludes unmanaged memory, multidimensional arrays, reflection/dynamic invocation, and interface-only collection calls as detailed in compatibility docs. |
| Activation | Instrumented controlled entry points require an active simulation and registered runtime service; uninstrumented binaries retain ordinary BCL behavior. | There is no inactive pass-through from rewritten controlled calls to real BCL operations. |
## Build and test

```powershell
dotnet build Clockwork.slnx
dotnet run --project tests\Clockwork.Tests\Clockwork.Tests.csproj -- --timeout 60s
dotnet pack src/Clockwork/Clockwork.csproj --configuration Release
```

The NuGet package ID is `Clockwork.Simulation`. Until packages are published, clone the repository or add it as a Git submodule and reference `src/Clockwork/Clockwork.csproj`.

## Instrumented simulation test projects

Keep ordinary and simulation tests in separate projects. Only simulation test projects reference
`Clockwork.Instrumentation.Build` and opt into staged execution:

```xml
<PropertyGroup>
  <ClockworkInstrumentedTestProject>true</ClockworkInstrumentedTestProject>
  <ClockworkUseBuiltInRules>true</ClockworkUseBuiltInRules>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Clockwork.Instrumentation.Build" PrivateAssets="all" />
</ItemGroup>
```

Clockwork snapshots ordinary test output under `obj`, rewrites the complete eligible managed closure
out of place, validates it, and deploys it to the simulation test project's normal `bin` directory.
Strong-name identities, matching intra-closure reference tokens, and friend-assembly key qualifiers
are stripped automatically. Test-host implementation assemblies are copied unchanged, and generated
test-host bootstrap types are excluded from rewriting, because they execute before a simulation
exists. `dotnet build` followed by `dotnet test --no-build` therefore runs the rewritten test copy
naturally. The next build restores the pristine snapshot before compilation. Production outputs and
projects without the opt-in remain ordinary IL.

Repositories using the targets from a source project reference can set
`ClockworkInstrumentationTaskAssembly` to the built task assembly path.

## Optional race exploration instrumentation

Race exploration is a build-time opt-in separate from ordinary controlled rewriting:

```xml
<ClockworkInstrumentationEnabled>true</ClockworkInstrumentationEnabled>
<ClockworkInstrumentationMode>RaceExploration</ClockworkInstrumentationMode>
```

The CLI equivalent is `clockwork instrument ... --mode RaceExploration`; JSON configuration uses
`"mode": "RaceExploration"`. The selected mode is recorded in per-assembly and closure manifests and
participates in rewrite signatures and incremental cache keys. `Controlled` remains the default and
does not add memory, branch, array, indirect, or collection scheduling calls.

Injected points yield only while running as a `ControlledOperation`. They use the scheduler's selected
strategy and decision/replay log, preserving the exactly-one-running baton invariant. A run exposes
`ControlledOperationScheduler.RaceExplorationResult`, `FirstRace`, and
`CaptureRaceSchedulingPoints()`. A race is a distinct `RaceDetected` outcome with both operations,
access kinds, logical location, source/IL sites, synchronization context, and the schedule trace.

## Record, replay, explore, and minimize

The core API accepts an explicit controlled-scheduler scenario:

```csharp
var recorded = ReplayRunner.Record(
    new ReplayRecordingOptions
    {
        RootSeed = 12345,
        SchedulingPolicy = ReplaySchedulingPolicy.SeededRandom,
        ScheduleSeed = 17,
    },
    scheduler =>
    {
        scheduler.Schedule("worker-a", scheduler.Yield);
        scheduler.Schedule("worker-b", () => { /* controlled work */ });
    });

ReplayArtifactSerializer.Write("failure.cwr.json", recorded.Artifact);

var replayed = ReplayRunner.Replay(
    recorded.Artifact,
    ReplayCompatibilityRequirements.Current(),
    scheduler =>
    {
        scheduler.Schedule("worker-a", scheduler.Yield);
        scheduler.Schedule("worker-b", () => { /* same controlled scenario */ });
    });
```

`ScheduleExplorer.Explore` runs a bounded serial seed corpus while keeping `RootSeed` unchanged.
`ReplayTraceMinimizer.Minimize` delta-debugs scheduling/resource choices against an exact-replay
failure predicate. See [`docs/replay.md`](docs/replay.md) for the schema, CLI, test fixture, version
policy, compatibility rules, and limitations.

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
        var context = new SimulationNodeContext(Clock, Guard, ForkRandom(), TaskQueue);
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
cluster.RunUntil(() => handled, AdaptiveExecutionBudget.Default);

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
non-generic `SimulationCluster`, side-by-side with plain handles - a foundation for heterogeneous node
composition:

```csharp
// MyWorkerNode is any existing SimulationNode subclass - e.g. TestNode from "Define a
// simulation" above: public sealed class MyWorkerNode(string address, SimulationNodeContext context) : SimulationNode { ... }
var builder = new SimulationBuilder().WithSeed(1);
var counter = builder.AddNode("counter", state: 0);
builder.AddCustomNode("worker", context => new MyWorkerNode("worker", context));

await using var cluster = builder.Build();
var worker = cluster.FindNode<MyWorkerNode>("worker")!;
```

Unlike the handle overloads, `AddCustomNode` cannot hand the constructed node back synchronously -
`factory` needs a real `SimulationNodeContext`, which doesn't exist until `Build()` runs. Retrieve
it afterwards via `cluster.FindNode<MyWorkerNode>(...)`.

**What this foundation does not include yet:** there is no dependency-injection-style construction
or startup ordering between nodes, and no per-node-type discovery beyond address lookup and
`OfType<T>()`. Full heterogeneous lifecycle support (typed groups, startup ordering, DI-style
construction) is not provided. The supported foundation is the shared
clock/guard/drive-loop across mixed node types, rather than a partial lifecycle API that would be
misleading about what it actually guarantees.

The non-generic `SimulationCluster` disposes every registered node - and, for handles, their state payload - that
implements `IAsyncDisposable`/`IDisposable` when the cluster itself is disposed.

`SimulationBuilder` and the non-generic `SimulationCluster` are deliberately small and composable plain classes with no
required base class for node state. Clockwork does not provide `IHost` integration.

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
cluster.RunFor(TimeSpan.FromMinutes(5));
cluster.Network.HealBidirectionalPartition(node1.NetworkAddress, node2.NetworkAddress);
```

`RunUntil`, `RunUntilIdle`, and `RunFor` execute one queued operation at a time and advance the shared clock only when no work is ready. All three return `SimulationExecutionResult`, including the stop reason, counters, limits, and pending-work snapshot. They share one drive-loop engine, so hook firing, time advancement, and stuck detection remain consistent.

## Execution results and diagnostics

Every drive method returns a `SimulationExecutionResult` describing exactly why the run stopped:

```csharp
var result = cluster.RunUntil(() => completed, maxIterations: 10_000);

if (result.Reason != SimulationExecutionReason.ConditionMet)
{
    // ToDetailedString() includes the reason, simulated start/end/elapsed time, iteration and
    // time-advance counts, and a stable, invariant-culture-formatted snapshot of any runnable,
    // waiting, or blocked work left in the cluster and node queues.
    throw new InvalidOperationException(result.ToDetailedString());
}
```

- `RunUntil(Func<bool>, int)` - drives until the condition or another stop reason.
- `RunUntilIdle(TimeSpan?, int)` - drains until idle or a configured bound.
- `RunFor(TimeSpan, int)` - reaches the exact target time, then drains work due at that instant.

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

`RunToCompletion(Func<Task>, ...)` drives async work under the cluster synchronization context and
controlled scheduler. Generic overloads return `Task<T>` results directly. Fixed and adaptive
budgets are selected by argument type; an incomplete task throws `TimeoutException` containing the
detailed execution result, while task faults propagate unchanged.

## Adaptive execution budgets

The `AdaptiveExecutionBudget` overloads escalate the iteration budget automatically:

```csharp
var budget = new AdaptiveExecutionBudget(
    initialMaxIterations: 500,
    growthFactor: 8.0,
    maxTotalIterations: 5_000_000);
var result = cluster.RunUntil(() => allNodesConverged, budget);
var idleResult = cluster.RunUntilIdle(budget);
```

Both return a `SimulationExecutionResult`, combined across every batch actually run (summed
`Iterations`/`StepsExecuted`/`TimeAdvanceCount`; `Reason`/`PendingWork`/etc. from the final batch -
the same folding convention `RunFor` uses to merge sub-calls).

**Progress heuristic, precisely:** each batch uses the same drive loop as fixed-budget execution. If
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

`AdaptiveExecutionBudget.MaxTotalIterations` (default 10,000,000) is a hard ceiling on the sum of
iterations across every batch - escalation never removes the safety cap that explicit
`maxIterations` limits already provide; it just removes the need to pick a value up front.
`AdaptiveExecutionBudget.Default` supplies 1,000 initial iterations, 4x growth, and a 10,000,000
total cap.

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

- In cooperative mode, inject `TimeProvider` and route delays through it. In controlled mode, the
  listed wall-clock, timer, and delay APIs are rewritten automatically.
- Keep continuations on the simulation context; avoid `ConfigureAwait(false)`.
- Do not use `Task.Run`, thread-pool APIs, real network I/O, or real file I/O.
- Use `SimulationRandom` or a derived random stream instead of `Random.Shared`.
- Forward cancellation tokens and use synchronous cancellation callbacks.

> With the built-in `clockwork.bcl.deterministic` rule set enabled (see
> [Deterministic BCL rule set](#deterministic-bcl-rule-set)), the direct
> **static** calls in this list - wall-clock APIs and `Random.Shared`/`new Random()` among them -
> are rewritten to deterministic shims automatically, so unmodified source becomes deterministic
> under simulation without manual `TimeProvider`/`SimulationRandom` threading. The guidance above
> still applies to APIs outside that inventory and to instance-based nondeterminism.

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

## Compatibility

See [docs/compatibility.md](docs/compatibility.md) for the supported deterministic
instrumentation modes (cooperative, controlled, race exploration, optional deep
instrumentation) and the platform/deployment contract (.NET 10, Windows/Linux/macOS,
JIT and ReadyToRun, plus limitations for single-file, trimming,
NativeAOT, signed assemblies, and profiler conflicts).


## Deterministic BCL rule set

The built-in simulation rule set **`clockwork.bcl.deterministic`** (version `2.0.0`),
makes ordinary source that calls the direct **static** time / identity / random BCL surface
deterministic - with no dependency injection, no `TimeProvider` threading, and no manual shim
wiring. Enabling it rewrites those call sites to Cecil-free runtime shims in `Clockwork.Runtime`.
The complete, exhaustive list of controlled and rejected signatures is generated into
[`docs/rule-inventory.md`](docs/rule-inventory.md) and verified against the shipped rules by a
test, so the documentation cannot drift from the code.

**Enabling it (no JSON required).**

```xml
<!-- MSBuild: strict by default -->
<PropertyGroup>
  <ClockworkUseBuiltInRules>true</ClockworkUseBuiltInRules>
</PropertyGroup>
```

```
# CLI
clockwork instrument --source <directory> --output <dir> --builtin clockwork.bcl.deterministic
#   --builtin all                 enable every shipped rule set
#   --builtin-include Clock Random restrict to specific families
#   --builtin-exclude Crypto      drop a family
#   --builtin-strict false        relax strict validation (strict is the default)
```

**Simulation-only contract (never a silent fallback).** Instrumented closure binaries are
simulation/test artifacts, not production replacements. Every Controlled entry point requires an
active Clockwork simulation; invoking one without an active simulation throws
`SimulationNotActiveException` before any real BCL operation runs. With an active simulation:

- **With a registered runtime environment** - dispatches to the current
  node's simulated clock and the correct independent seed domain (Application/Identity only;
  the scheduler, network, and Buggify seed streams are never perturbed).
- **With no registered environment** - throws
  `SimulationServiceMissingException` rather than read real wall-clock time or OS entropy.

Production binaries remain uninstrumented and therefore retain ordinary BCL behavior. The renamed
runtime inventory uses `ControlledDateTime`, `ControlledDateTimeOffset`, `ControlledStopwatch`,
`ControlledEnvironment`, `ControlledGuid`, `ControlledRandom`,
`ControlledRandomNumberGenerator`, `ControlledInsecureRandomNumberGenerator`, and
`SimulationStableHash`.

**Semantics.** Local-time clocks (`DateTime.Now`/`Today`, `DateTimeOffset.Now`) honour the
configured simulation time zone, while UTC clocks return the node's virtual UTC time.
`Environment.TickCount`/`TickCount64` wrap with correct `int`/`long` behaviour, and
`Stopwatch.GetTimestamp`/`GetElapsedTime(long)` are machine-independent. `Guid.NewGuid`
draws deterministic bytes while preserving the RFC 4122 variant and version 4;
`Guid.CreateVersion7` encodes the simulated UTC millisecond timestamp in the first 48 bits with
version 7 (no monotonicity guarantee beyond the BCL contract). `Random.Shared` and unseeded
`new Random()` become **per-node** deterministic streams that replay under a fixed seed and
schedule and never share mutable state across nodes; explicitly seeded `new Random(int)`
preserves the caller's seed exactly. Cryptographic randomness (`RandomNumberGenerator` static
entropy APIs) is **rejected by default** with a precise diagnostic naming the assembly, method,
and location; a strictly test-only opt-in,
`SimulationBuilder.WithCryptoRandomnessPolicy(CryptoRandomnessPolicy.DeterministicInsecureForTesting)`,
can substitute deterministic-insecure bytes - production security semantics are never changed and
insecure bytes are never silently substituted.

**Compile-time guidance.** The `Clockwork.Analyzers` project ships two diagnostics that mirror
the rule set: `CW1001` flags controlled time/identity/random members that require instrumentation,
and `CW1002` flags cryptographic randomness that is rejected under simulation.

Verified end to end by a conformance test project that compiles unmodified BCL-calling source,
rewrites it with the built-in rule set, and observes deterministic behaviour under a live
simulation. Separate uninstrumented binaries retain normal BCL behaviour. Determinism is claimed
**only** for the exact rules tabulated in the [rule inventory](docs/rule-inventory.md); see
[compatibility](docs/compatibility.md) for the documented holes.


## Controlled task, timer, and async rule set

The second built-in simulation rule set, **`clockwork.tasks.controlled`** (version `3.0.0`),
makes ordinary `async`/`await` code and the direct `Task` surface run on the simulation's single
logical thread instead of the physical thread pool — again with no dependency injection or manual
wiring. It is selected independently of the BCL rule set:

```
clockwork instrument --source <directory> --output <dir> --builtin clockwork.tasks.controlled
#   --builtin all   enable every shipped rule set (BCL + controlled tasks)
```

**How it works.** A member-aware substitution pass retargets the compiler-generated builder and
awaiter types of an `async` state machine onto controlled value-type equivalents
(`AsyncTaskMethodBuilder`(`<T>`), `TaskAwaiter`(`<T>`), `ConfiguredTaskAwaitable`(`<T>`)`/…Awaiter`,
`YieldAwaitable`/`YieldAwaiter`, and the `async ValueTask`/`ValueTask<T>` machinery
`AsyncValueTaskMethodBuilder`(`<T>`), `ValueTaskAwaiter`(`<T>`),
`ConfiguredValueTaskAwaitable`(`<T>`)`/…Awaiter` → their `Controlled…` counterparts), rewriting
every field, local, member reference, and closed-generic type operand so both Debug and Release
state machines are controlled. The controlled awaiter hands each continuation to the simulation
coordinator, so **`ConfigureAwait(false)` stays controlled** inside a simulation (for both `Task`
and `ValueTask`). Uninstrumented production binaries retain the normal BCL async machinery.
Alongside it, call-site redirects route the `Task.WhenAll`/`Task.WhenAny` combinators — non-generic **and their generic
`Task<T>` overloads** — the synchronous `Task.Wait()`/`WaitAll`/`WaitAny(Task[])` waits, the
blocking generic `Task<T>.Result` accessor, the `TaskExtensions.Unwrap` extension methods, and
`Task.ContinueWith(Action<Task>)` to
`Clockwork.Runtime.Tasks.ControlledTask`. Synchronous waits and blocking `Task<T>.Result` reads
**pump the coordinator loop until completion instead of blocking a physical thread**, so they never
deadlock the scheduler, then delegate to the real API for its exact `AggregateException` semantics.

**Simulation-only contract.** As with the BCL rule set, instrumented Controlled entry points require
an active Clockwork simulation. Continuations and waits route through the coordinator; when an active
simulation has no registered task coordinator, the shim throws
`ControlledTaskServiceMissingException` rather than escaping to the thread pool. There is no
inactive pass-through. `Task.Run`, every .NET 10 `TaskFactory.StartNew` state/options/scheduler form,
`Thread`, `ThreadPool`, and `Parallel` are controlled.

**Modern synchronization.** The same opt-in rule set now controls
`ReaderWriterLockSlim` (logical-strand read, upgradeable-read, and write ownership/recursion),
`ManualResetEventSlim` (set/reset/waits and a controlled `WaitHandle` bridge), unnamed kernel
`Mutex`/`Semaphore` (through the controlled wait-handle kernel), `SpinLock` (whole-type
substitution), `ExecutionContext`, `SynchronizationContext`, `Barrier`, and `CountdownEvent`.
Contended operations pump controlled work and finite waits use virtual deadlines: Clockwork neither
busy-spins nor blocks an OS thread. `WaitHandle.WaitAll` is supported for controlled handles except
when its array contains a `Mutex`, which is rejected because atomic multi-mutex acquisition is not
modelled. Named/cross-process mutexes, semaphores, and events, their open-existing APIs, raw
`Handle`/`SafeWaitHandle` accessors, and raw `SynchronizationContext.Wait` are rejected precisely.
Activation is unchanged: enable the built-ins through the MSBuild package/property, the CLI
`--builtin clockwork.tasks.controlled` (or `--builtin all`), or the corresponding instrumentation
packages; the exact selected signatures and policies are generated in the
[rule inventory](docs/rule-inventory.md).

The controlled rule set also substitutes the three public timer types, redirects every .NET 10
`Task.Delay`/`Task.WaitAsync` overload, controls timer-driven cancellation, and bridges
`TimeProvider.System`/`CreateTimer`. Periodic ticks coalesce, timer callbacks flow the BCL user
`ExecutionContext` where applicable, and all finite deadlines appear as pending virtual-time work.
Control parity is claimed **only** for the exact signatures in the
[rule inventory](docs/rule-inventory.md). This work adapts the *design* of Microsoft Coyote's
controlled-task model (MIT); see [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md) for the attribution.


## License

Clockwork is licensed under the [MIT License](LICENSE). See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the policy on adapting
third-party material.

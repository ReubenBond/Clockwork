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

## Current capability contract

This section is the authoritative current-state summary; phase-labelled sections later in this file
are historical implementation notes.

- The cooperative simulation kernel, builder, virtual clock, seeded randomness, network, diagnostics,
  rendezvous primitives, and controlled scheduling runtime are implemented.
- `Clockwork.Instrumentation.Build` and `Clockwork.Tool` perform opt-in, out-of-place Cecil rewriting of
  application/dependency closures. The shipped Roslyn analyzer reports controlled and rejected direct
  BCL usage; all of these components are implemented and exercised in CI.
- `clockwork.bcl.deterministic` controls the exact time, identity, and random signatures in
  [`docs/rule-inventory.md`](docs/rule-inventory.md).
- `clockwork.tasks.controlled` controls async builders/awaiters, task combinators and waits,
  `Task.Run`, all 24 .NET 10 `TaskFactory`/`TaskFactory<T>.StartNew` overloads, `Thread`,
  `ThreadPool`, `Parallel`, `Monitor`, `System.Threading.Lock`, and `SemaphoreSlim`. Work executes on
  controlled logical strands; Debug and Release compiler lowering are both conformance-tested.
- All six .NET 10 `Task.Delay` overloads are rejected during simulation until virtual delays are
  implemented, and pass through unchanged outside simulation. Custom `TaskScheduler` instances and
  unsupported `TaskCreationOptions` are likewise rejected rather than ignored.
- Exact limitations: general wait handles/events, `ReaderWriterLockSlim`, `Mutex`, kernel `Semaphore`,
  struct `SpinLock`, timers/cancellation timers, and synchronous `ValueTask` blocking are not rewritten.
  `SemaphoreSlim.AvailableWaitHandle`, registered thread-pool waits, and unmodellable OS APIs are
  explicitly rejected. Execution is cooperative and non-preemptive between yield points.

## Build and test

```powershell
dotnet build Clockwork.slnx
dotnet run --project tests\Clockwork.Tests\Clockwork.Tests.csproj -- --timeout 60s
dotnet pack src/Clockwork/Clockwork.csproj --configuration Release
```

The NuGet package ID is `Clockwork.Simulation`. Until packages are published, clone the repository or add it as a Git submodule and reference `src/Clockwork/Clockwork.csproj`.

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

> With the built-in `clockwork.bcl.deterministic` rule set enabled (see
> [First deterministic BCL rule set](#first-deterministic-bcl-rule-set-phase-5)), the direct
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

## Roadmap and compatibility

See [docs/compatibility.md](docs/compatibility.md) for the intended deterministic
instrumentation modes (cooperative, controlled, race exploration, optional deep
instrumentation) and the platform/deployment contract (.NET 10, Windows/Linux/macOS,
JIT and ReadyToRun today; deferred limitations for single-file, trimming,
NativeAOT, signed assemblies, and profiler conflicts).

## Historical implementation notes

The phase-labelled sections below record how the current implementation was delivered. Statements
about what a phase did not yet include describe that historical milestone, not current capability.

### Deterministic instrumentation runtime plumbing (historical Phase 2)

`Clockwork.Runtime` (referenced by the `Clockwork`/`Clockwork.Simulation` package at
`src/Clockwork/Clockwork.csproj`) adds
the ambient-context, activation-security, seed-domain, decision-log, and API-policy plumbing that
future controlled/race-exploration instrumentation will build on. **This is runtime plumbing only:
nothing here intercepts application code yet, and no existing behavior changed** - every type below
is either newly ambient/observable data or an explicit, additive constructor parameter.

- **Ambient execution context.** `Clockwork.Runtime.Execution.SimulationExecutionContext` exposes
  `IsActive`, `Current` (a `SimulationExecutionSnapshot` with the active runtime, node, and logical
  execution identity), and `TryGetCurrentRuntime`. It is `AsyncLocal<T>`-backed, so it flows across
  `await` automatically, is isolated between parallel `Task`s (e.g. two simulations on the same test
  process), and every `Enter*` method returns an `IDisposable` scope that restores the exact
  enclosing frame on `Dispose()` - including when the guarded code throws. `SuppressFlow(reason)`
  wraps `ExecutionContext.SuppressFlow()` for the rare case where ambient flow into new unflowed work
  (e.g. `Task.Run` used deliberately for something outside the simulation) must be prevented, and
  records a bounded diagnostic trail (`SimulationFlowSuppressionDiagnostics`) so an unexpected loss
  of ambient context can be explained rather than guessed at.
- **Secure activation.** There is no public global switch, environment variable, or default that
  activates simulation context. `EnterRuntime` requires a `SimulationActivationToken`, which only
  `Clockwork.Runtime.Execution.SimulationRuntimeActivation` (`internal`, granted via
  `InternalsVisibleTo` to the simulation host packages and test assemblies) can mint. Outside an
  active simulation, `IsActive`/`TryGetCurrentRuntime` are cheap `AsyncLocal` reads that report
  `false`/no-runtime - deliberately shaped so a future inlineable "Buggify"-style shim can check
  activity with negligible overhead in production.
- **Independent named seed domains.** `Clockwork.Runtime.Random.SimulationSeedAuthority` derives a
  seed per `SimulationSeedDomain` (`Scheduler`, `Network`, `Application`, `Identity`, `Buggify`,
  `Model`) as a pure function of the authority's root seed and the domain name - consuming
  randomness in one domain never perturbs another. `GetSiteSeed`/`CreateChildAuthority` derive
  per-node/per-site child seeds from a caller-supplied *stable identity* (e.g. a node's network
  address) rather than construction/fork order, so reordering or adding unrelated nodes never
  reseeds an existing one. `SimulationCluster<TNode>.SeedAuthority` exposes this per-cluster; the
  root `SimulationSeed.FromString(s)`/`FromStrings(...)` now delegate to the same underlying
  `DeterministicHash` algorithm (byte-identical output - this is a pure refactor).
- **Typed deterministic decision log.** `Clockwork.Runtime.Decisions.SimulationDecisionLog` records
  `SimulationDecisionRecord`s (domain, kind, optional stable source/site id, input metadata,
  selected result, plus the runtime/node/logical-execution identity active at the time) under a
  monotonically increasing `SimulationDecisionId`, in exact call order across every domain. This is
  a data model and recording contract only - nothing calls `Record` automatically yet; a future
  controlled-operation scheduler is expected to.
- **Replay contract (validation only, not a scheduler).**
  `Clockwork.Runtime.Decisions.SimulationDecisionReplayValidator` compares a live decision against
  an `ISimulationDecisionReplayReader` (with an in-memory implementation for tests), by *content*
  (domain/kind/source/input/result) - deliberately ignoring the run-identifying fields (id, runtime,
  node, logical execution) that necessarily differ between the original recording and a later
  replay. It throws `SimulationDecisionReplayMismatchException` at the first divergence and does not
  throw again for decisions after that point, since a scheduler replay engine (not implemented in
  this phase) is what would actually resume from a divergent point.
- **API interception policy classification.**
  `Clockwork.Runtime.Policy.SimulationApiPolicyRegistry` resolves `Controlled`/`Rejected`/
  `PassThrough` for a `SimulationApiKey` (assembly + API name), with deterministic precedence
  (per-API override > per-assembly override > registry default) and a diagnostic `Reason` per
  decision. The registry's default can never be `PassThrough` - skipping determinism for an API must
  always be an explicit, targeted override while a simulation is active, never a silent fallback.
  This is a policy data model only; nothing intercepts calls based on it yet.
- **External-entry guard.** `Clockwork.Runtime.Execution.SimulationExternalEntryGuard.ValidateEntry`
  is called from `SimulationTaskQueue`'s item-dispatch path (see below) to detect a callback
  executing while the calling thread's ambient context belongs to a *different* simulation runtime
  than the one about to run - the "external entry" case (two simulations sharing a thread without
  properly restoring their scopes, or a callback that escaped one simulation onto another's thread).
  It deliberately does **not** flag the common, expected case of no ambient context at all, and
  throws `SimulationExternalEntryException` with an actionable message (including any recent,
  matching flow-suppression event) instead of silently repairing or broadly catching.
- **Ambient integration into the existing kernel.** `SimulationCluster<TNode>` mints a
  `SimulationActivationToken`/`RuntimeIdentity`/`SeedAuthority` and installs ambient context on its
  own cluster-level `TaskQueue` (every cluster, old subclass or new). `SimulationBuilder`/
  `BuiltSimulation` additionally installs a *node-scoped* ambient context on every node it creates,
  so builder-created node callbacks observe both the runtime and their own node identity. Because
  timers and synchronization-context callbacks are both dispatched through the same
  `SimulationTaskQueue.RunOnce()` path, they get this integration automatically with no separate
  wiring. **Hand-written `SimulationCluster<TNode>`/`SimulationNode` subclasses that construct their
  own `SimulationNodeContext` directly (the pattern in "Define a simulation" above) are unaffected**:
  without an explicit `ambientContext` argument, their node-level queues get no ambient scope,
  preserving their exact prior behavior. This distinction is deliberate and tested.

### Controlled-operation kernel (Phase 3A)

`Clockwork.Runtime.Scheduling` adds the *controlled-operation kernel* - the foundational
scheduling layer for controlled mode. `ControlledOperationScheduler` guarantees that **at most
one logical operation executes system-under-test code at a time, even across multiple physical
threads**, using a single permission baton handed off through wait handles (no busy-spin, no
`Thread.Abort`). The scheduler owns every state transition
(`Created → Runnable → Running → {Paused, Completed, Faulted, Canceled}`); illegal transitions
throw with diagnostics. Each operation carries a `SimulationLogicalExecutionId` that is distinct
from `Environment.CurrentManagedThreadId` (a logical operation may hop physical threads) and is
installed into `SimulationExecutionContext` automatically, so decision records pick it up with no
Phase 2 API change. Generic pause/resume primitives let an operation yield the baton
deterministically and later resume without physical concurrency.

The kernel is available to the existing `SimulationTaskQueue` as an **opt-in** compatibility
bridge (one controlled operation per user callback); it is off by default, so every existing
simulation and trace snapshot is byte-identical. The actual `Monitor`/`Semaphore`/wait-handle
shims, resource ownership and wait queues, virtual timeouts, and deadlock detection that build on
this kernel are deferred to Phase 3B - see `docs/compatibility.md`.

### Reusable resource/wait scheduler (Phase 3B)

`Clockwork.Runtime.Scheduling` (and `Scheduling/Resources`, `Scheduling/Strategies`) adds the
*reusable resource and wait layer* the later synchronization shims will sit on - still with **no
public BCL shims**. It introduces controlled resources with stable identity, optional owner,
capacity, and a deterministic waiter queue (`ControlledResource`); atomic
`WaitOnResource`/`SignalOne`/`SignalAll` that transition the running operation to
*paused-on-resource*, yield the baton, and later wake it with no lost/duplicate/stale wakeups;
virtual-time timeouts (zero/finite/infinite) modeled by an internal `ControlledVirtualClock` that
mirrors `SimulationClock` semantics without a package dependency, resolving release-vs-timeout
races deterministically; synchronous `CancellationToken` integration (via `Register`, never
`CancelAsync` or a thread-pool hop) that resolves release/timeout/cancel to exactly one terminal
reason and never leaks a registration; a wait-for graph with deterministic deadlock detection and
liveness classification (`DetectDeadlock`, `DescribeLiveness`) that distinguishes a true resource
cycle from *paused-until-time*, *externally completable*, and *quiescent* states; and pluggable
scheduling strategies (`IControlledSchedulingStrategy`) - FIFO, round-robin (the default,
identical to Phase 3A), seeded-random from the Phase 2 `Scheduler` seed domain, priority, and
exact replay - where every real choice is recorded and replay fails at the first divergence.

Fairness is defined narrowly: **no BCL fairness is promised**; waiter order is only guaranteed
deterministic under the selected policy and replayable. The public `Monitor`/`Semaphore`/
`WaitHandle`/`Task` shims and the Cecil/call-site rewriting that would redirect real BCL calls
onto this layer remain **Phase 6/7** work - see `docs/compatibility.md`.

None of this is wired into any interception or IL-rewriting layer - see
`docs/compatibility.md` for what remains deferred to later phases (the Cecil-based deep
instrumentation mode, public BCL shims, a public Buggify API, Generic Host integration, and HTTP
support).

### Rewrite-engine core (Phase 4A)

`Clockwork.Instrumentation` (namespaces `Rules`, `Rewriting`, `Manifest`, `Diagnostics`) adds the
**generic IL rewrite-engine core** on top of `Mono.Cecil` 0.11.6. It is **internal and
experimental**: a deterministic, rule-driven Cecil transformation pipeline plus an extensive golden
test corpus, and nothing else. `RewriteEngine.Rewrite` applies a caller-supplied, versioned
`RewriteRuleSet` (integrating the Phase 2 `Controlled`/`Rejected`/`PassThrough` policy classification)
against an input assembly using caller-supplied replacement ("shim") assemblies, then validates the
output by reading it back and emits a deterministic `InstrumentationManifest`.

Verified transformations: static/instance `call`/`callvirt` redirection, `newobj` redirection to a
static factory, generic-instance methods, type-reference substitution, post-call wrapping,
deterministic rejection injection, and correct rewriting inside by-ref/array/constrained/delegate/
async/iterator/nested shapes and `try`/`catch`/filter/`finally` regions (with handler and branch
boundaries repaired). Portable/embedded PDBs and per-site source mapping are preserved; absent
symbols are reported, not dropped. Assembly/rule-set-level idempotence makes a re-run with the same
rules a verified no-op and fails clearly on an incompatible rule-set version; a targeted call whose
replacement cannot be resolved is a hard failure.

Explicitly **not** in Phase 4A (deferred to Phase 4B or later): MSBuild target/task activation and
CLI commands, recursive publish-output rewriting, strong-name re-signing and Authenticode, load-time
`AssemblyLoadContext` hooks, any concrete BCL deterministic shim, the Phase 6/7 synchronization shims
and Coyote-style task/lock substitutions, `Buggify`, Generic Host, HTTP, and profiler/native detours.
The engine performs the IL mechanics only and is wired to no build or deployment step yet. The
Cecil-based passes adapt parts of Microsoft Coyote's rewriting engine under the MIT license - see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### Build and tool integration (Phase 4B)

Phase 4B makes the Phase 4A engine usable from an ordinary build and from the command line. It adds
no BCL shim rules - it is generic, opt-in plumbing that fails explicitly rather than silently
degrading. Two packages ship:

- **`Clockwork.Instrumentation.Build`** - an MSBuild task plus `build/` props and targets. It is a
  development dependency and is **strictly opt-in**: an ordinary build does nothing.
- **`Clockwork.Tool`** - a .NET global/local tool exposing the `clockwork` command.

**Opt-in build usage.** Reference the build package and switch instrumentation on explicitly, then
supply one or more rule-set documents:

```xml
<ItemGroup>
  <PackageReference Include="Clockwork.Instrumentation.Build" Version="..." PrivateAssets="all" />
</ItemGroup>
<PropertyGroup>
  <ClockworkInstrumentationEnabled>true</ClockworkInstrumentationEnabled>
</PropertyGroup>
<ItemGroup>
  <ClockworkRuleSet Include="clockwork.rules.json" />
  <!-- Optional include/exclude globs over the discovered closure. -->
  <ClockworkInclude Include="MyApp.*" />
  <ClockworkExclude Include="ThirdParty.Untouched" />
</ItemGroup>
```

With `ClockworkInstrumentationEnabled=true`, an `AfterTargets="Build"` step discovers the resolved
output closure (respecting `.deps.json`, runtimeconfig, satellite/native assets and framework/
reference-assembly exclusion), rewrites **only managed IL** out-of-place under
`obj/<Config>/<Tfm>/clockwork/instrumented/`, copies the assets needed to run the staged app, and
writes a manifest to `obj/<Config>/<Tfm>/clockwork/clockwork.manifest.json`. Source and `bin`
outputs are never mutated. The step is incremental, keyed by input assembly/symbol hashes, the
rule-set signature, engine version, configuration and reference set. Optional overridable
properties include `ClockworkConfigurationPath` (a JSON [configuration](#instrumentation-configuration)),
`ClockworkStagingDirectory`, `ClockworkManifestPath`, `ClockworkReadyToRunPolicy`,
`ClockworkStrongNamePolicy`, and `ClockworkStrongNameKeyPath`. A project-adjacent
`clockwork.config.json` is auto-discovered.

The task package targets `net10.0` and requires the .NET 10 SDK: use `dotnet build` / `dotnet
msbuild`. It cannot be loaded by .NET Framework MSBuild (classic `msbuild.exe` in Visual Studio).

**CLI usage.** Install the tool and rewrite or inspect assemblies:

```
dotnet tool install --global Clockwork.Tool
clockwork rewrite --input <dir-or-assembly> --output <dir> --rules clockwork.rules.json [--config clockwork.config.json] [--dry-run]
clockwork inspect <assembly> [--json]
```

`rewrite` stages a rewritten closure; `--dry-run` reports the planned transformations without
writing. `inspect` reports managed/ReadyToRun status, strong-name state, symbol form, and prior
Clockwork instrumentation (idempotence) markers, as deterministic text or JSON. Exit codes are
nonzero and classified by failure kind. The `run`/`replay`/`minimize` commands are intentionally
deferred to later replay work.

**Instrumentation configuration.** Configuration and rule sets are plain JSON documents with strict
schema, type, and signature validation - **no arbitrary code is executed from configuration**.
Multiple rule sets merge deterministically with a defined precedence, which is the mechanism future
built-in Clockwork rules, application rules, and third-party rules will share.

**Strong naming.** Signed, public-signed, and delay-signed inputs are detected. Re-signing is
performed only when a key is supplied via `ClockworkStrongNamePolicy=Resign` +
`ClockworkStrongNameKeyPath`; when re-signing is required but no key is available the build fails
clearly. Public-key-token consistency across a rewritten dependency closure is verified. Authenticode
signatures are detected and reported as unsupported - they are never re-applied and a rewritten
assembly's Authenticode signature does not survive; re-sign such outputs with your own toolchain
after instrumentation.

**ReadyToRun.** R2R/native code sections are detected. The default `Reject` policy fails rather than
emit stale native code; the opt-in `StripToIL` policy round-trips through Cecil to produce IL-only
staged output. Because instrumentation rewrites managed IL, it must run **before** crossgen/R2R
publish, single-file bundling, and Native AOT - instrument first, then publish.

Verified end-to-end by process-execution tests (an enabled staged executable dispatches to a test
shim while a normal one does not, across Debug/Release, symbols present/absent, config-loaded rules,
rejected calls, incremental rebuilds, exclusions, a signed closure, and the R2R policy) and by
package smoke tests that pack, install, and run the real targets and tool.

## First deterministic BCL rule set (Phase 5)

The first production built-in rule set, **`clockwork.bcl.deterministic`** (version `1.0.0`),
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
clockwork rewrite --input <dir-or-assembly> --output <dir> --builtin clockwork.bcl.deterministic
#   --builtin all                 enable every shipped rule set
#   --builtin-include Clock Random restrict to specific families
#   --builtin-exclude Crypto      drop a family
#   --builtin-strict false        relax strict validation (strict is the default)
```

**Three-state contract (never a silent fallback).** Each shim checks whether a simulation is
active:

- **Outside a simulation** - runs the real BCL API unchanged (production pass-through).
- **Inside a simulation with a registered runtime environment** - dispatches to the current
  node's simulated clock and the correct independent seed domain (Application/Identity only;
  the scheduler, network, and Buggify seed streams are never perturbed).
- **Inside a simulation with no registered environment** - throws
  `SimulationServiceMissingException` rather than read real wall-clock time or OS entropy.

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
`SimulationBuilder.WithCryptoRandomnessPolicy(SimulationCryptoRandomnessPolicy.DeterministicInsecureForTesting)`,
can substitute deterministic-insecure bytes - production security semantics are never changed and
insecure bytes are never silently substituted.

**Compile-time guidance.** The `Clockwork.Analyzers` project ships two diagnostics that mirror
the rule set: `CW1001` flags controlled time/identity/random members that require instrumentation,
and `CW1002` flags cryptographic randomness that is rejected under simulation.

Verified end to end by a conformance test project that compiles unmodified BCL-calling source,
rewrites it with the built-in rule set, and observes deterministic behaviour under a live
simulation (and normal BCL behaviour outside one). Determinism is claimed **only** for the exact
rules tabulated in the [rule inventory](docs/rule-inventory.md); see
[compatibility](docs/compatibility.md) for the documented holes.


## Controlled task and async rule set (Phase 6A/6B)

The second production built-in rule set, **`clockwork.tasks.controlled`** (version `1.0.0`),
makes ordinary `async`/`await` code and the direct `Task` surface run on the simulation's single
logical thread instead of the physical thread pool — again with no dependency injection or manual
wiring. It is selected independently of the BCL rule set:

```
clockwork rewrite --input <dir-or-assembly> --output <dir> --builtin clockwork.tasks.controlled
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
and `ValueTask`) while still delegating to normal BCL semantics outside one. Alongside it, call-site
redirects route the `Task.WhenAll`/`Task.WhenAny` combinators — non-generic **and their generic
`Task<T>` overloads** — the synchronous `Task.Wait()`/`WaitAll`/`WaitAny(Task[])` waits, the
blocking generic `Task<T>.Result` accessor, the `TaskExtensions.Unwrap` extension methods, and
`Task.ContinueWith(Action<Task>)` to
`Clockwork.Runtime.Tasks.ControlledTask`. Synchronous waits and blocking `Task<T>.Result` reads
**pump the coordinator loop until completion instead of blocking a physical thread**, so they never
deadlock the scheduler, then delegate to the real API for its exact `AggregateException` semantics.

**Three-state contract.** As with the BCL rule set: outside a simulation everything is a
transparent pass-through to the real BCL; inside a simulation continuations and waits route through
the coordinator; inside a simulation with no registered task coordinator the shim throws
`ControlledTaskServiceMissingException` rather than escaping to the thread pool. `Task.Run`, every
.NET 10 `TaskFactory.StartNew` state/options/scheduler form, `Thread`, `ThreadPool`, and `Parallel`
are controlled. Every .NET 10 `Task.Delay` overload is **rejected** under simulation until virtual
delays are implemented. `Monitor`, `System.Threading.Lock`, and `SemaphoreSlim` are controlled;
general wait handles and timers remain outside the inventory. Control parity is claimed **only** for
the exact signatures in the [rule inventory](docs/rule-inventory.md). This work adapts
the *design* of Microsoft Coyote's controlled-task model (MIT); see
[THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md) for the attribution.


## License

Clockwork is licensed under the [MIT License](LICENSE). See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the policy on adapting
third-party material.

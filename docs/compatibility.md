# Compatibility and capability contract

This document describes Clockwork's supported deterministic execution modes and
platform/deployment contract. It is a durable product document and should be updated,
not duplicated, as supported scope changes.

> **Authoritative summary:** The current capability matrix is
> [README — Current capability contract](../README.md#current-capability-contract). The kernel,
> build/tool rewriting pipeline, analyzers, and built-in BCL/task/synchronization
> rules described there are implemented. This document describes only current behavior and
> durable limitations.

## Intended execution modes

Clockwork supports four modes of running application code against
the deterministic kernel. They trade off fidelity, overhead, and how much of the
application's own concurrency they can observe or control.

### Cooperative mode

The application opts in explicitly: it uses `TimeProvider`, the simulation
`TaskScheduler`/`SynchronizationContext`, `SimulationRandom`, and the simulated
network surfaces directly, as described in the README's "Determinism requirements"
section. This remains available without instrumentation. The application author explicitly routes
nondeterminism through Clockwork APIs.

### Controlled mode

Clockwork verifies usage with shipped Roslyn diagnostics and redirects the exact built-in rule
inventory through opt-in IL rewriting. Controlled async/task/thread/thread-pool/Parallel and modern
synchronization surfaces run on logical strands. Controlled code never falls through to an inactive
or unregistered runtime service: it fails explicitly instead. Timers, delays, asynchronous timeouts,
and timer-driven cancellation use the shared virtual-time deadline scheduler.

#### Scheduling and resource model

Controlled mode and cooperative mode share one `SimulationScheduler`. It owns operation selection,
continuations, node lanes, virtual time, and timers, ensuring that at most one logical operation executes
system-under-test code at a time. Logical execution ids are independent of physical thread ids and flow
through `SimulationExecutionContext` into decisions and diagnostics. `SimulationSchedulerLane` is only a
node-scoped scheduling and diagnostic facade; it does not own a separate queue or execution pump.

The reusable resource model provides stable resource identities, ownership and capacity metadata,
deterministic waiter queues, atomic pause/wakeup, virtual-time deadlines, synchronous cancellation,
wait-for graphs, deadlock/liveness diagnostics, and FIFO, round-robin, seeded-random, priority, and exact
replay scheduling strategies. Release, timeout, and cancellation races resolve exactly once; equal virtual
deadlines fire by registration order. Fairness is deliberately limited to deterministic, replayable waiter
selection and does not claim BCL fairness.
Built-in strategies are created through `SimulationSchedulingStrategies`; custom
`ISimulationSchedulingStrategy` implementations remain supported.

The built-in task and synchronization shims use this model for monitors, semaphores, wait handles,
synchronous task waits, timers, and modern synchronization primitives. Controlled instrumentation remains
opt-in; uninstrumented applications can continue to use cooperative mode directly.

### Race exploration mode

`RaceExploration` is an explicit instrumentation mode (`--mode RaceExploration`,
`<ClockworkInstrumentationMode>RaceExploration</ClockworkInstrumentationMode>`, or JSON
`"mode": "RaceExploration"`). `Controlled` remains the default and runs none of the fine-grained
passes, so normal controlled binaries incur no memory/control-flow instrumentation calls.

The Cecil pass injects:

- tracked reads/writes for reference-type instance fields (`ldfld`/`stfld`), static fields
  (`ldsfld`/`stsfld`), and one-dimensional vector array elements (`ldelem.*`/`stelem.*`);
- schedule-only points for volatile fields, field-address and indirect/object accesses
  (`ldflda`/`ldsflda`, `ldind.*`/`stind.*`, `ldobj`/`stobj`, `cpobj`, `initobj`, and `ldelema`) where
  the evaluation stack does not retain enough information for a stable object-plus-offset identity;
- control-flow points at `brtrue`/`brfalse`, matching Coyote's memory-access rewriting coverage;
- direct concrete calls on `List<T>`, `Dictionary<TKey,TValue>`, and `HashSet<T>` as tracked
  collection accesses, plus scheduling points for `ConcurrentBag<T>`,
  `ConcurrentDictionary<TKey,TValue>`, `ConcurrentQueue<T>`, and `ConcurrentStack<T>`.

Injected calls carry the original containing method, IL offset, portable-PDB document, and source
line. Branch targets, volatile prefixes, exception-handler boundaries, and return values remain
valid. Constructors and property accessors follow Coyote's documented exclusions. Compiler-generated
`MoveNext` methods are intentionally visited: generated value-type state fields get schedule-only
points, while accesses reached through a hoisted `this` or closure object retain object-field identity.

The runtime assigns weak, monotonically ordered identities through `ConditionalWeakTable`; reports and
location tables retain only numeric identities. A per-operation vector clock detects unordered
read/write and write/write conflicts. Common controlled locksets and release/acquire clocks suppress
protected accesses across Monitor/C# `lock`, `System.Threading.Lock`, `ReaderWriterLockSlim`,
`SemaphoreSlim`, controlled task completion/waits, and scheduler resources/events. The first race is
selected deterministically and returned as a distinct `RaceDetected` structured outcome rather than
thrown through user code.

**Exact limits:** multidimensional/non-vector arrays lowered to helper calls, interface-typed collection
calls, reflection/dynamic invocation, `Span<T>`/native pointers, unmanaged memory, and offsets derived
from arbitrary managed pointers are not assigned tracked locations. They remain schedule-only where an
IL opcode is identifiable. Concurrent collections are interleaving points but are not themselves
reported as data races. Collection coverage is direct-member call coverage, not whole-type substitution;
members reached only through `ICollection<T>`/`IDictionary<TKey,TValue>` are outside the inventory.
Tail-prefixed collection calls are left intact because the CLI requires `tail.` adjacency and a direct
call-to-return flow; injecting after such a call would invalidate the method.
Replay artifacts, bounded schedule exploration, and trace minimization operate over these scheduling
points. This capability does not add profiler/native detours or runtime hosting/transport interception.

### Replay and schedule exploration

Replay artifacts use canonical UTF-8 JSON with format identity `clockwork.replay` and schema version 2.
They record the root seed, explicit schedule seed, strategy/options, Clockwork/runtime compatibility,
optional closure-manifest and assembly hashes, ordered scheduler/resource/timer decisions, race
scheduling points, terminal outcome, and structured operation/resource/timer/race/deadlock diagnostics.
Readers ignore unknown optional properties within a supported schema version. A schema version,
runtime version, rule-set, manifest, or assembly hash mismatch fails before scenario code executes.

Recording and replay are exposed through `ReplayRunner`. Replay validates every decision at the first
divergence, including expected/actual source, candidate metadata, and selected result, then verifies
that the complete decision stream was consumed at the reproduced terminal boundary. An `Aborted`
outcome identifies an interrupted decision prefix and is rejected for exact replay.

`ScheduleExplorer` varies only the schedule seed while keeping the model/application root seed fixed.
Iterations run serially; iteration, failure, step, cancellation, and optional wall-clock safety bounds
are explicit. Iteration ids are deterministic, aggregate outcomes are stable, and the smallest artifact
per failure identity is retained. Parallel exploration is rejected until runtime/instrumentation
isolation can be guaranteed.

`ReplayTraceMinimizer` uses bounded delta debugging plus discrete scheduling/resource alternative
selection. A candidate is accepted only if exact replay passes compatibility and first-divergence
validation and reproduces the original failure category and identity.

Artifacts exclude process arguments, environment variables, arbitrary user metadata, stack traces,
caller work descriptions, and source paths by default. Caller descriptions/source paths can be
explicitly retained through the API. Per-field, decision, race-point, assembly, and total-byte limits
are enforced before use.

### Build-time instrumentation boundary

Clockwork's deepest supported instrumentation is opt-in, out-of-place caller rewriting performed
by `Clockwork.Instrumentation.Build` or `Clockwork.Tool` before execution. It can rewrite
application and selected dependency assemblies, including third-party managed callers, but does
not rewrite framework assemblies, mixed-mode/native code, or calls originating inside excluded
vendor binaries. Those boundaries require an explicit Clockwork model, rejection, or consumer
adapter when they affect simulation behavior.

Clockwork does not provide a CLR profiler/ReJIT component, startup-hook or
`AssemblyLoadContext` load-time rewriting, or native detours. Runtime interception is explicitly
out of scope: it would compete with coverage and APM profilers, add platform-specific native
deployment and security burden, and still not cover NativeAOT or unmanaged behavior. The current
consumer evidence does not show a determinism blocker that would justify those costs.

The build package and CLI use Mono.Cecil; see
[Third-party notices](../THIRD-PARTY-NOTICES.md).

#### Rewrite engine

The **generic IL rewrite engine** in `Clockwork.Instrumentation` uses Mono.Cecil 0.11.6. It is a deterministic,
rule-driven Cecil transformation pipeline plus an extensive golden test corpus, and
does not perform runtime interception. The engine (`RewriteEngine.Rewrite`) takes a caller-supplied, versioned
`RewriteRuleSet` and a set of replacement ("shim") assemblies, applies an ordered set
of passes to the input assembly, validates the result by reading it back, and emits a
deterministic `InstrumentationManifest`. The rule model integrates the simulation API
policy classification (`Controlled`/`Rejected`) without the engine referencing any
concrete shim by identity. APIs remain unchanged only when callers omit their rule or
exclude the containing assembly from instrumentation.

**Supported IL transformations (verified by the golden corpus):**

- static and instance `call`/`callvirt` redirection to a static replacement method
  (the instance receiver becomes the replacement's first argument);
- `newobj` redirection to a static factory method;
- generic-instance methods (type arguments carried onto the replacement) and calls
  embedded in generic types;
- type-reference substitution in method bodies (`newarr`, `castclass`, `isinst`,
  `box`, `unbox(.any)`, `ldtoken`, `initobj`, `sizeof`, `constrained.`);
- optional post-call wrapping (interception after a matched call);
- deterministic rejection injection before an unsupported/forbidden invocation;
- correct behavior when the redirected site sits inside by-ref/array/constrained-
  generic/delegate/async-state-machine/iterator/nested-type shapes and inside
  `try`/`catch`/filter/`finally` regions, with exception-handler and branch
  boundaries repaired when instructions are inserted or replaced;
- portable and embedded PDB preservation with per-site source mapping; one-for-one
  replacements retain Cecil's offset-based sequence points. Member substitutions record
  the exact operand instruction offset and nearest source point when a call site exists;
  structural type/member edits without a call site use offset `-1` rather than a fabricated
  `IL_0000`. Absent or unsupported symbols are reported (`CWR0004`/`CWR0005`), never silently dropped;
- assembly/rule-set/options-level idempotence markers: re-running with the same rule set and
  semantic rewrite options is a verified no-op, and an incompatible rule set or options
  fingerprint fails clearly (`CWR0008`) rather than double-rewriting;
- strict resolution — a targeted member whose replacement cannot be resolved is a hard
  failure (`CWR0001`); a targeted call is never silently skipped.

**Engine boundary:** load-time `AssemblyLoadContext` hooks, profiler/native detours, mixed-mode
rewriting, Authenticode re-signing, application hosting, and transport interception are not
provided. Mixed-mode assemblies are rejected (`CWR0011`). The engine supplies IL transformation
mechanics; build and CLI orchestration remain separate opt-in entry points.

#### Build and tool integration

The build and command-line entry points are generic, strictly opt-in plumbing that fails explicitly rather than
silently degrading. It ships two packages: `Clockwork.Instrumentation.Build` (an MSBuild
task with `build/` props and targets, a development dependency) and `Clockwork.Tool`
(the `clockwork` CLI).

**Opt-in only.** An ordinary build never instruments. The `ClockworkInstrument` target
runs `AfterTargets="Build"` only when the consumer sets
`ClockworkInstrumentationEnabled=true` and supplies at least one `@(ClockworkRuleSet)`
document. It discovers the full resolved managed closure (honoring `.deps.json`, runtimeconfig,
satellite/resource/native assets, and include/exclude globs). Framework and reference assemblies
are always copied rather than rewritten, and an explicit include cannot override that safety
boundary. It rewrites **only managed IL** out-of-place under
`obj/<Config>/<Tfm>/clockwork/instrumented/`, copies the non-managed assets needed to run
the staged app unchanged, and emits a manifest under
`obj/<Config>/<Tfm>/clockwork/clockwork.manifest.json`. Source and `bin` outputs are never
mutated. The work is incremental, keyed by every input asset's hash, the rule-set signature,
engine version, manifest schema, configuration, and reference set. Each copied native library,
dependency/runtime configuration file, symbol file, replacement assembly, and race-runtime asset is
recorded as a typed manifest entry containing its closure-relative path and lower-case SHA-256.
Incremental hits verify the staged hash of every rewritten assembly and copied asset; a missing or
modified output invalidates the hit and rebuilds the full staged closure. Closure manifests use strict
schema version 3; `copiedAssets` entries are objects containing `relativePath` and `sha256`. Version 2
is rejected rather than interpreted through a compatibility shape. Manifests are limited to 16 MiB,
4,096 assembly entries, 65,536 copied assets, and 8,192 UTF-16 characters per string.

**Instrumented test projects.** Executable test projects can set
`ClockworkInstrumentedTestProject=true`. After an ordinary project build, the package snapshots the
complete test output under `obj` and automatically selects the test assembly plus every
eligible managed assembly in its resolved closure. It rewrites that complete simulation closure out
of place and validates every manifest assembly and rewrite signature before deploying the result to
that simulation test project's `bin` directory. Strong-name identities are stripped automatically
from rewritten assemblies, together with their intra-closure reference tokens and
`InternalsVisibleTo` public-key qualifiers. Test-host implementation assemblies are automatically
excluded because discovery and runner startup execute before a simulation exists. The test entry
assembly is also copied unchanged so async test methods can create the simulation before controlled
code runs; the complete application/dependency closure remains eligible for rewriting. The next build restores the pristine snapshot first, so
incremental compilation and instrumentation-mode changes never consume a previously rewritten input.
Because the rewritten test copy occupies the project's normal module path, `dotnet build` followed
by `dotnet test --no-build` uses it without runner-specific dispatch hooks. Production project
outputs and non-opted-in test projects remain ordinary IL. Instrumentation must be selected per
project, never using a solution-wide `ClockworkInstrumentationEnabled` global property.

**Task package requires the .NET 10 SDK.** The task and its Cecil-based engine target
`net10.0` and load only under `dotnet build` / `dotnet msbuild`; .NET Framework MSBuild
(classic `msbuild.exe`) cannot host them. The `Clockwork.Tool` CLI exposes `rewrite`
(with `--dry-run`) and `inspect` (text or `--json`), with nonzero exit codes classified
by failure kind. The tool also exposes explicit `IReplayScenario` harness commands for
`run`/`replay`/`explore`/`minimize`/`trace show`; it never discovers a process or scenario type
implicitly.

**Configuration is data, not code.** Configuration and rule sets are JSON documents,
validated strictly for schema, types, and signatures; **no arbitrary code is executed
from configuration**. Multiple rule sets merge deterministically by a defined precedence shared
by built-in, application, and third-party rules. Instrumentation configuration files must declare
`"schemaVersion": 2`. Its exact optional fields are `ruleSets`, `mode`, `builtInRuleSets`,
`builtInIncludeFamilies`, `builtInExcludeFamilies`, `include`, `exclude`, and `targetRuntime`;
unknown fields are rejected. Version 1 is rejected, and there are no migration
or compatibility aliases.

**Strong naming (build/tool scope).** Signed, public-signed, and delay-signed inputs are
detected. Clockwork automatically strips strong-name identities from every rewritten assembly and
removes matching public-key tokens from references within the rewritten closure. Friend-assembly
public-key qualifiers are removed at the same time, so the transformed closure remains internally
consistent without signing keys. This is safe for isolated simulation/test artifacts; instrumented
assemblies are not production replacements. **Authenticode** signatures are detected
and reported as unsupported - they are never re-applied, and a rewritten assembly does not
retain its Authenticode signature; re-sign such outputs with your own toolchain after
instrumentation.

**ReadyToRun (build/tool scope).** R2R/native sections are detected and always stripped by
round-tripping the usable managed IL through Cecil before rewriting, producing IL-only staged
output with no stale native code. Mixed-mode images and images without usable managed IL are
rejected. Instrument before single-file bundling and Native AOT; those published forms cannot be
recovered to a rewriteable managed closure.

#### Deterministic BCL rule set

The built-in simulation rule set `clockwork.bcl.deterministic` (version `3.0.0`),
redirects the direct **static** time / identity / random BCL surface to Cecil-free runtime
shims in the `Clockwork` assembly, under namespaces matching
`Clockwork.Shims.<framework namespace>`. The complete, exhaustive
list of controlled and rejected signatures is generated into
[`rule-inventory.md`](rule-inventory.md) and verified against the shipped rules by a test, so
the published inventory can never drift from the code.

Instrumented closure binaries are simulation/test artifacts, not production replacements. Every
Controlled entry point requires an active Clockwork simulation; without one it throws
`SimulationNotActiveException` before any real BCL operation runs. With an active simulation:

- **With a registered runtime environment** it dispatches to the node's
  simulated clock and the correct independent seed domain (Application/Identity only - never
  the scheduler, network, or Buggify domains).
- **Runtime completeness** installs the deterministic environment and task coordinator atomically
  before a runtime can become ambient. There is no active-but-unconfigured state and no process-wide
  service registry.

Uninstrumented production binaries retain ordinary BCL behavior. The runtime inventory names are
`ControlledDateTime`, `ControlledDateTimeOffset`, `ControlledStopwatch`, `ControlledEnvironment`,
`ControlledGuid`, `ControlledRandom`, `ControlledRandomNumberGenerator`,
`SimulationRandomNumberGenerator`, and `SimulationStableHash`.

Semantics: local-time clocks (`DateTime.Now`/`Today`, `DateTimeOffset.Now`) honour the
configured simulation time zone; `Environment.TickCount`/`TickCount64` wrap with correct
`int`/`long` behaviour; `Stopwatch.GetTimestamp`/`GetElapsedTime(long)` are machine-independent.
`Guid.NewGuid` draws deterministic bytes while preserving RFC 4122 variant and version 4;
`Guid.CreateVersion7` encodes the simulated UTC millisecond timestamp in the first 48 bits with
version 7 (no monotonicity guarantee beyond the BCL contract). `Random.Shared` and unseeded
`new Random()` become per-node deterministic streams; explicitly seeded `new Random(int)`
preserves the caller's seed exactly. Cryptographic randomness (`RandomNumberGenerator` static
APIs and controlled factory instances) draws deterministic, per-node non-cryptographic bytes from a
stream isolated from application randomness, identity generation, scheduling, networking, and fault
injection.

**Opt-in.** No JSON authoring is required. MSBuild consumers set
`<ClockworkUseBuiltInRules>true</ClockworkUseBuiltInRules>`; CLI consumers pass
`--builtin clockwork.bcl.deterministic` (or `--builtin all`) with optional
`--builtin-include`/`--builtin-exclude` family filters. The selected families are versioned and folded into the rule-set signature,
so incremental rebuilds stay correct.

#### Controlled task and async rule set

The second built-in simulation rule set, `clockwork.tasks.controlled` (version `2.0.0`),
controls the compiler-generated async machinery and the direct `Task` surface that ordinary
application code uses, so `async`/`await` runs on the simulation's single logical thread instead
of the physical thread pool. It is selected independently of the BCL rule set (CLI
`--builtin clockwork.tasks.controlled`, or `--builtin all` for both). The exhaustive controlled
and rejected signature list is generated into [`rule-inventory.md`](rule-inventory.md) and
verified against the shipped rules by a test.

The rule set has two halves. A **member-aware type substitution** pass retargets the
compiler-generated builder and awaiter types of an `async` state machine onto controlled
value-type equivalents in `Clockwork.Shims.System.Runtime.CompilerServices`
(`AsyncTaskMethodBuilder`(`<T>`) → `ControlledAsyncTaskMethodBuilder`(`<T>`); `TaskAwaiter`(`<T>`),
`ConfiguredTaskAwaitable`(`<T>`)`/ConfiguredTaskAwaiter`, and `YieldAwaitable`/`YieldAwaiter` →
their `Controlled…` counterparts), rewriting field, local, method- and field-reference, and
type-operand metadata (including closed generic instances such as `TaskAwaiter<int>`) so a Debug
or Release state machine is fully controlled. The same pass also retargets the
`async ValueTask`/`async ValueTask<T>` machinery — `AsyncValueTaskMethodBuilder`(`<T>`),
`ValueTaskAwaiter`(`<T>`), and `ConfiguredValueTaskAwaitable`(`<T>`)`/ConfiguredValueTaskAwaiter` →
their `Controlled…` counterparts (the `ValueTaskMachinery` family) — so value-task `async`/`await`
and `ValueTask.ConfigureAwait(false)` are controlled identically. The controlled awaiter hands
every continuation to the simulation coordinator rather than the awaited task's completion
callback, which is exactly why **`ConfigureAwait(false)` stays controlled**. Uninstrumented
production binaries retain normal BCL async semantics. A
**call-site redirect** half routes both the
non-generic `Task.WhenAll` / `Task.WhenAny` (array, span, pair, enumerable) combinators **and their
generic `Task<T>` overloads** (array, span, enumerable, and the `WhenAny<T>` pair), the synchronous
`Task.Wait()` / `Task.WaitAll` / `Task.WaitAny(Task[])` waits, the blocking generic
`Task<T>.Result` accessor, the `TaskExtensions.Unwrap` extension methods, and
`Task.ContinueWith(Action<Task>)` to
`Clockwork.Shims.System.Threading.Tasks.ControlledTask`. Combinators delegate to the real BCL (their completion
is driven by antecedents that complete on the logical thread); synchronous waits **pump the
coordinator loop until completion instead of blocking a physical thread**, then delegate to the
real API to reproduce its exact `AggregateException` semantics, so a synchronous wait or a blocking
`Task<T>.Result` read on incomplete controlled work never deadlocks the scheduler.
`Task.Run` and `TaskFactory.StartNew` queue controlled operations instead of escaping, while
`Task.Delay` and `Task.WaitAsync` register virtual deadlines.

The redirect obeys the same simulation-only invariant as the BCL rule set: instrumented Controlled
builders, awaiters, and shims require an active Clockwork simulation. Continuations and waits route
through the coordinator carried by the complete ambient runtime and cannot silently escape to the
thread pool.

Synchronous blocking on `ValueTask`/`ValueTask<T>` remains unsupported: a value task may be
consumed only once, so a blocking drain is unsafe and `await` is the supported controlled path.
Other APIs absent from the generated rule inventory are outside the support claim.

#### Threads, thread pool, Parallel, and task parity

`clockwork.tasks.controlled` ensures that every unit of concurrent work an application
spawns — a `Thread`, a `Task.Run`/`TaskFactory.StartNew` body, a `ThreadPool.QueueUserWorkItem`
callback, or a `Parallel` branch — is modelled as a **controlled operation scheduled on the same
single logical thread** the async machinery already uses, instead of escaping onto a physical OS
thread or the real thread pool. The exhaustive controlled/rejected signature list is regenerated
into [`rule-inventory.md`](rule-inventory.md); the Coyote parity matrix is
[`coyote-parity.md`](coyote-parity.md).

Controlled surfaces include the full `Task.Run` and `TaskFactory`/
`TaskFactory<T>.StartNew` families; the generic `Task<T>.ContinueWith(Action<Task<T>>)`
and result-producing `Task<T>.ContinueWith<TNewResult>(Func<Task<T>,TNewResult>)` continuations;
`Thread` construction/`Start`/`Join`/`Sleep`/`Yield`/`SpinWait`; `ThreadPool.QueueUserWorkItem` and
`UnsafeQueueUserWorkItem` (including the generic `Action<TState>` and `IThreadPoolWorkItem` forms);
and `Parallel.Invoke`/`For`/`ForEach`.

**Deliberate deviations from real BCL semantics:**

- **Cooperative, non-preemptive execution.** A controlled thread/threadpool/Parallel body runs as a
  single scheduling unit; it interleaves with other controlled work only at explicit yield points
  (`await`, `Task.Yield`, `Thread.Yield`, `Thread.Sleep`, `Join`, a blocking `Task` wait). This is
  faithful for the async-first concurrency Clockwork targets, but a purely synchronous CPU loop with
  no yield point does **not** interleave the way real preemptive threads would. `SimulationScheduler`
  is the live backend for both task continuations and controlled operations, but fully preemptive
  synchronous interleaving is not supported.
- **`Thread.Sleep` / `Thread.Join(timeout)` are virtual waits.** They yield the logical thread
  through the deterministic loop rather than consuming real wall-clock time. The same virtual-time
  scheduler now backs timers, delays, asynchronous timeouts, and timer-driven cancellation.
- **Safe vs. unsafe `ExecutionContext` flow is modelled.** `QueueUserWorkItem` captures and flows
  the caller's `ExecutionContext`; `UnsafeQueueUserWorkItem` does not — matching the BCL contract —
  so `AsyncLocal` values observed by the callback differ between the two exactly as they do on the
  real thread pool.
- **OS-specific and un-modellable surfaces are rejected precisely, not silently ignored.** Thread
  `Priority`/apartment-state/`Interrupt`, `Parallel` `ParallelLoopState` (break/stop) and
  thread-local overloads, and `ThreadPool.UnsafeQueueNativeOverlapped` all fail at the rewritten call
  site with a diagnostic that names the exact API. The registered-wait APIs
  (`RegisterWaitForSingleObject`/`UnsafeRegisterWaitForSingleObject`) are **controlled**
  (see below) — they depend on controlled wait handles, which now exist.
- **Uncontrolled process/termination APIs are rejected *unconditionally*.** `Process.Start`/`Kill`/
  `WaitForExit`/`WaitForExitAsync` and `Environment.Exit`/`FailFast` throw whether or not a
  simulation is active (a rewritten assembly must never launch, kill, or tear down a real OS
  process). This unconditional guard is distinct from the simulation-only Controlled invariant,
  because there is no faithful in-simulation model of spawning or killing a process.
- **Cross-assembly uncontrolled-task detection is diagnosis, not wrapping.** With
  `DetectUncontrolledTasks` enabled, a call into an uncontrolled dependency assembly that returns a
  `Task`/`Task<T>`/`ValueTask`/`ValueTask<T>` or a custom awaitable is flagged with the `CWR0200`
  warning at the exact call site, so a task whose continuation could escape the coordinator is never
  silently accepted. Clockwork diagnoses rather than runtime-wraps the foreign task (honest about
  what it can prove); framework-hosted I/O remains outside the instrumentation boundary.
- **Exception-handler hardening is defence-in-depth.** With `HardenExceptionHandlers` enabled, a
  `dup; call SimulationExceptionGuard.ThrowIfControlSignal` is injected at the start of every broad
  `catch (Exception)`/`catch`/filter handler so an internal scheduler control signal cannot be
  swallowed by application `catch` blocks. Finally blocks, rethrow-only handlers, and async
  state-machine `SetException` handlers are skipped, and **normal application exception handling is
  unchanged** — the guard is a no-op for every object that is not the internal control signal. This
  layers on top of the runtime's preference for explicit gate/state transitions over control
  exceptions.

Every instrumented Controlled shim requires an active simulation: work routes through the
coordinator, and an active simulation with no registered coordinator throws rather than silently
escaping. The synchronization and virtual-timer capabilities delivered since then are recorded below.

Each mode is intended to be strictly additive: an application written for
cooperative mode should continue to work unmodified under controlled, race
exploration, or deep instrumentation mode.

#### Monitors, locks, and semaphores

The controlled rule set puts the highest-value synchronization primitives on the same cooperative logical-thread
kernel: `System.Threading.Monitor` (and therefore every C# `lock (object)` statement),
`System.Threading.Lock` (and the C# `lock (Lock)` statement), and `System.Threading.SemaphoreSlim`.
The exhaustive controlled/rejected signature list is regenerated into
[`rule-inventory.md`](rule-inventory.md); the per-member Coyote parity ledger is
[`coyote-parity.md`](coyote-parity.md).

What is now **controlled**: the entire `Monitor` static surface — `Enter`/`Exit`/`IsEntered`, all six
`TryEnter` overloads, all five `Wait` overloads, `Pulse`/`PulseAll` — modelling per-object ownership, a
reentrancy count, and a condition-variable wait set on the logical thread. Because the C# compiler lowers
`lock (object)` to `Monitor.Enter(obj, ref bool)` + `finally Monitor.Exit(obj)`, redirecting `Monitor`
controls every `lock` automatically (verified against both Debug and Release lowering, nested/reentrant
locks, third-party assemblies, and lock release through exceptions/finally). `System.Threading.Lock` and
its nested `Scope` ref struct are controlled by **type substitution**, which covers the C# `lock (Lock)`
scope lowering (`EnterScope`/`Scope.Dispose`) as well as the explicit
`Enter`/`Exit`/`TryEnter`/`IsHeldByCurrentThread` members. `SemaphoreSlim` is controlled across its
constructors, `CurrentCount`, the synchronous `Wait` overloads, the asynchronous `WaitAsync` overloads,
`Release`, and `Dispose`, enforcing the maximum count and serving waiters in deterministic FIFO order.

**Finite positive timeouts are honoured exactly in virtual time.** `Monitor.TryEnter(obj, 250)`,
`Monitor.Wait(obj, 250)`, `Lock.TryEnter(250)`, `SemaphoreSlim.Wait(250)`, and `SemaphoreSlim.WaitAsync(250)`
wait until acquisition/signal or a **simulated** deadline, then return/set `false` on timeout — never
consuming wall-clock time. Zero timeouts stay faithful non-blocking tries and infinite timeouts stay
indefinite. The deadline is a `PausedUntilTime` state driven by the cluster clock, so it is *not* a
deadlock cycle edge; advancing the cluster clock fires it. Because modelled time only advances when
nothing else is runnable, any release, pulse, or cancellation possible at the current instant beats a
same-instant timeout (the scheduler's deterministic first-winner policy), and ties between two deadlines at
the same instant resolve by registration order. Cancellation is honoured faithfully — a
`CancellationToken` fires synchronously on the logical thread and throws `OperationCanceledException`.

**Deliberate deviations from real BCL semantics** (documented here, tested, and revisited when the
physical-gate backend lands):

- **A never-satisfiable acquire or *indefinite* wait surfaces as a deadlock diagnostic, not a hang.**
  A finite wait times out (above); an infinite one with no possible progress is reported. Instead of
  blocking a physical thread forever, an unsatisfiable contended `Enter`, `Monitor.Wait`, or
  `SemaphoreSlim.Wait` throws the loop-model `SimulationSynchronousWaitDeadlockException`. One
  consequence: a `Monitor.Wait` that deadlocks has already released the monitor to wait, so a
  compiler-generated `lock` `finally` that then runs `Monitor.Exit` would observe no ownership — the
  deadlock is a terminal diagnostic for the run, not a recoverable exception to catch inside a `lock`.
- **Ownership is by logical strand, not physical thread.** All controlled work shares one cooperative
  thread, so ownership/reentrancy is tracked by the ambient logical-strand id assigned at the single
  new-strand choke point. This is faithful for the async-first concurrency Clockwork targets; a purely
  synchronous CPU loop that never yields does not interleave the way real preemptive threads would.
- **Waiter selection is deterministic and replayable, but does not promise BCL fairness.** `Pulse`,
  `PulseAll`, and `SemaphoreSlim.Release` serve waiters in arrival (FIFO) order for reproducibility; the
  real BCL makes no such guarantee, so code that depends on a specific non-FIFO wakeup order is not a
  target.
- **`SemaphoreSlim.AvailableWaitHandle` is controlled.** It exposes a `WaitHandle`; the
  rewritten getter hands back a bridged controlled manual-reset handle whose signalled state tracks
  `CurrentCount > 0` and follows disposal, so it composes with the controlled `WaitOne`/`WaitAny`/`WaitAll`
  surface instead of leaking an uncontrolled OS handle.
- **Lock objects are never kept alive by the model.** Monitor/semaphore association state lives in a
  `ConditionalWeakTable` keyed weakly by the lock/semaphore object, so a controlled association never
  roots an otherwise-collectible object.

Every instrumented Controlled shim requires an active simulation. The operation routes
through the coordinator; an active simulation with no registered coordinator throws rather than
silently escaping. Uninstrumented production binaries continue to call the ordinary BCL primitives.
The controlled inventory includes wait handles / events (`WaitHandle`/`EventWaitHandle`/`AutoResetEvent`/`ManualResetEvent`,
including `WaitAny`/`WaitAll`/`SignalAndWait`), `Interlocked`, `Volatile`, `SpinWait`, the
`SemaphoreSlim.AvailableWaitHandle` bridge, and the `ThreadPool` registered-wait APIs under control.

#### Modern synchronization

The modern synchronization rules expand the exact, opt-in `clockwork.tasks.controlled` inventory; the generated
[`rule-inventory.md`](rule-inventory.md) is the complete controlled/rejected signature list. The
following behavior applies only to instrumented closure binaries under an active simulation.
Uninstrumented production binaries keep ordinary BCL behavior; an inactive simulation or a missing
runtime service is an explicit failure, never a pass-through to OS synchronization.

- **`ReaderWriterLockSlim`:** every public constructor, state/recursion/waiter property,
  enter/try-enter/exit overload, and `Dispose` is redirected. The real object is only an identity key;
  read, upgradeable-read, and write ownership/recursion are per logical strand. Contention pumps the
  scheduler and finite timeouts use virtual time; writer waiters hold back new readers/upgradeable
  readers to prevent writer starvation.
- **`ManualResetEventSlim`:** constructors, `IsSet`/`SpinCount`, `Set`/`Reset`, every wait overload,
  `WaitHandle`, and `Dispose` are redirected. Its configured spin count is observable metadata only:
  waits do not busy-spin. The bridge is a controlled manual-reset handle, and signal, timeout,
  cancellation, and disposal are modelled state.
- **Kernel `Mutex` and `Semaphore`:** unnamed constructors and release members are controlled through
  the wait-handle kernel; the BCL object is identity only. Mutex ownership and recursion are logical
  strand state. Owner exit without `ReleaseMutex` deliberately leaves it owned, so a later indefinite
  wait reports the controlled deadlock diagnostic instead of fabricating
  `AbandonedMutexException`. Non-null named constructors and all `OpenExisting`/`TryOpenExisting`
  forms are rejected because they represent cross-process kernel state; null-name constructor forms
  are treated as unnamed.
- **`SpinLock`:** whole-type substitution preserves value-type/copy semantics and optional
  owner tracking, but acquisition pumps controlled work rather than spinning CPUs; finite waits are
  virtual-time waits. **`Barrier` and `CountdownEvent`** are likewise whole-type substitutions:
  their participant/count state, callbacks, waits, cancellation, timeouts, disposal, and
  `CountdownEvent.WaitHandle` bridge remain in the simulation.
- **`ExecutionContext` and `SynchronizationContext`:** capture/run, flow suppression/restoration,
  copy/disposal, ambient context, and callback dispatch are controlled. `SynchronizationContext.Post`
  queues through the coordinator and `Send` executes on the current logical strand rather than calling
  custom dispatch. Legacy `ExecutionContext.GetObjectData` serialization and raw
  `SynchronizationContext.Wait(IntPtr[], ...)` are rejected before they can use uncontrolled behavior.
- **Wait-handle composition:** `WaitOne`, `WaitAny`, `WaitAll`, and `SignalAndWait` operate on
  controlled handles without OS blocking; bridges and registered waits compose with that state.
  `WaitAll` validates arrays and atomically consumes eligible auto-reset handles, but rejects an
  array containing a `Mutex` because atomic multi-mutex acquisition is not modelled. Raw
  `Handle`/`SafeWaitHandle` accessors and handles not created by Clockwork are rejected.

All synchronous waits use deterministic loop pumping: zero timeouts poll, finite timeouts use virtual
deadlines, and an unsatisfiable indefinite wait reports the controlled deadlock diagnostic. Disposal
marks controlled state disposed and faults/blocks subsequent use according to the controlled surface;
ownership is logical-strand rather than physical-thread ownership.

### Virtual timer and deadline contract

The controlled rule set classifies the exact .NET 10 timer surface:

- `System.Threading.Timer`: constructors `(TimerCallback)`, and
  `(TimerCallback, object?, int|long|uint|TimeSpan, int|long|uint|TimeSpan)`; all four matching
  `Change` overloads; `ActiveCount`; `Dispose()`, `Dispose(WaitHandle)`, and `DisposeAsync()`.
- `System.Timers.Timer`: constructors `()`, `(double)`, and `(TimeSpan)`; `AutoReset`, `Enabled`,
  `Interval`, `Elapsed`, `Start`, `Stop`, `Close`, and disposal. Non-null `SynchronizingObject` and
  designer `Site` integration are rejected because they can marshal work outside the scheduler.
- `PeriodicTimer`: constructors `(TimeSpan)` and `(TimeSpan, TimeProvider)`, mutable `Period`,
  `WaitForNextTickAsync(CancellationToken)`, and `Dispose()`.
- `Task.Delay`: all six `int`/`TimeSpan`, cancellation, and `TimeProvider` overloads.
- `Task.WaitAsync` and `Task<T>.WaitAsync`: all five overloads on each type (cancellation,
  `TimeSpan`, and `TimeProvider` combinations).
- `CancellationTokenSource`: timed constructors `(int)`, `(TimeSpan)`, and
  `(TimeSpan, TimeProvider)`; both `CancelAfter` overloads; cancellation, reset, and disposal paths
  which must invalidate a pending timer generation.
- `TimeProvider.System` and `TimeProvider.CreateTimer(TimerCallback, object?, TimeSpan, TimeSpan)`.
  The returned object implements the standard `ITimer`; interface `Change` and disposal dispatch to
  the controlled timer. Unrecognized custom providers reject instead of allocating an OS timer.

Finite deadlines advance only when no work is runnable. Work already queued at the current virtual
instant therefore wins before time advances. Equal deadlines fire in registration order; timer
callbacks are then appended to the controlled ready queue. Periodic schedules are based on successive
virtual due instants, callbacks never overlap physically, and `PeriodicTimer` coalesces unconsumed
ticks. Generation checks suppress stale firings after reentrant `Change`, stop, reset, or disposal.
Applicable timer callbacks restore the construction-time user `ExecutionContext` while executing on a
fresh logical strand. Pending deadlines appear in diagnostics as `PausedUntilTime`, and teardown
cancels all remaining registrations without invoking user callbacks.

## Platform and deployment contract

### Supported today

- **.NET 10** is the only targeted runtime (`net10.0`, see `Directory.Build.props`).
- **Windows, Linux, and macOS** are all supported for the existing kernel; it has no
  platform-specific dependencies (no P/Invoke, no OS-specific I/O).
- **JIT execution** (the default `dotnet run`/`dotnet test` path) is fully supported.
- **Deterministic BCL rule set** (`clockwork.bcl.deterministic`) covers the direct static
  time/identity/random surface enumerated in [`rule-inventory.md`](rule-inventory.md).
  Determinism is claimed **only** for those exact signatures.
- **Controlled task rule set** (`clockwork.tasks.controlled`) controls the compiler-generated
  `async Task`/`async ValueTask` machinery and the direct `Task`/`Task<T>` combinator (non-generic
  and generic `WhenAll`/`WhenAny`), synchronous-wait, blocking `Task<T>.Result`, and continuation
  surface (including the generic `Task<T>.ContinueWith` and result-producing
  `ContinueWith<TNewResult>`) enumerated in [`rule-inventory.md`](rule-inventory.md),
  routing `async`/`await` and synchronous waits through the simulation coordinator. The inventory
  also controls `Task.Run`, all 24 .NET 10 `TaskFactory`/`TaskFactory<T>.StartNew` signatures, `Thread`, `ThreadPool`
  (`QueueUserWorkItem`/`UnsafeQueueUserWorkItem`), and `Parallel` under control (see the Coyote
  parity matrix, [`coyote-parity.md`](coyote-parity.md)). Control is claimed **only** for those
  exact signatures, including virtual timers, all `Task.Delay`/`Task.WaitAsync` overloads, and
  timer-driven cancellation; synchronous `ValueTask` blocking remains a documented hole.
  The inventory additionally includes `Monitor` (and the C# `lock (object)`
  statement), `System.Threading.Lock` (and the C# `lock (Lock)` statement), and `SemaphoreSlim` under
  control. The synchronization surface also includes `Interlocked`, `Volatile`, `SpinWait`,
  the wait-handle / event family (`WaitHandle`/`EventWaitHandle`/`AutoResetEvent`/`ManualResetEvent`
  with `WaitOne`/`WaitAny`/`WaitAll`/`SignalAndWait`), the `SemaphoreSlim.AvailableWaitHandle` bridge,
  and the `ThreadPool` registered-wait APIs
  (`RegisterWaitForSingleObject`/`UnsafeRegisterWaitForSingleObject`) are all now controlled; named /
  cross-process event APIs and raw handle accessors are rejected with tested diagnostics. Modern
  synchronization adds `ReaderWriterLockSlim`, `ManualResetEventSlim`, unnamed kernel
  `Mutex`/`Semaphore`, `SpinLock`,
  `ExecutionContext`, `SynchronizationContext`, `Barrier`, and `CountdownEvent`; their exact surfaces
  are described above and in the generated inventory. Their waits use virtual time and controlled loop
  pumping, never a busy spin or OS block.
- **ReadyToRun inputs** are detected by the build/tool path: the default policy rejects them, while
  `StripToIL` produces an IL-only staged output. Instrument before publishing R2R.

### Limitations

- **Single-file, trimming, and NativeAOT ordering.** Clockwork rewrites managed assemblies, not
  completed bundles or native images. Instrument the resolved IL closure before single-file
  bundling, trimming, crossgen/ReadyToRun, or NativeAOT. Rewriting an already bundled, trimmed,
  ReadyToRun, or NativeAOT output is unsupported.
- **Signed assemblies.** Rewritten strong-name identities and matching closure references are
  stripped automatically. Authenticode is detected but not re-applied; consumers must apply it
  after instrumentation if an instrumented artifact must be redistributed.
- **Nondeterministic BCL surface beyond the rule inventory.** Only the exact signatures in
  [`rule-inventory.md`](rule-inventory.md) are rewritten. Documented holes include `Stopwatch`
  instance APIs and `GetElapsedTime(long, long)`; unlisted `RandomNumberGenerator` overloads; and
  `DateTime`/`DateTimeOffset`
  parse/format/convert helpers. Timer limitations are unrecognized custom `TimeProvider`
  implementations, non-null `System.Timers.Timer.SynchronizingObject`/designer integration, and
  `Timer.Dispose(WaitHandle)` with a handle outside Clockwork's controlled event surface.
- **No runtime interception.** Clockwork intentionally provides no CLR profiler/ReJIT component,
  startup hook, `AssemblyLoadContext` rewriting, or native detours. Instrumentation is build-time
  and out-of-place, avoiding profiler conflicts with coverage and APM tools.

These limitations apply only to controlled-mode build-time redirection. Cooperative mode requires
no rewriting or hooking and has no additional deployment constraints.

## Project layout

The implemented package boundaries under `src/` map to the modes above:

| Project | Depends on | Current purpose |
|---|---|---|
| `Clockwork` | *(none)* | Simulation kernel, ambient context, policy, controlled task/thread/synchronization shims, logical strands, and unified scheduling/resource infrastructure. |
| `Clockwork.Instrumentation` | `Clockwork` | Cecil rewrite engine, manifests, built-in rules, closure orchestration, and diagnostics. |
| `Clockwork.Instrumentation.Build` | `Clockwork.Instrumentation` | Opt-in MSBuild task + targets that instrument the resolved output closure out-of-place. |
| `Clockwork.Tool` | `Clockwork.Instrumentation` | `dotnet clockwork instrument` / `inspect` CLI over the shared orchestrator. |
| `Clockwork.Analyzers` | *(none)* | Roslyn diagnostics aligned with controlled/rejected direct BCL usage. |
| `Clockwork.Testing` | `Clockwork` | Reusable test helpers, scenario builders, and in-memory log capture for consumers. |

`Clockwork.Testing` remains a separate helper project and namespace boundary. Application hosting and transport models are
consumer-owned and outside the Clockwork core; consumers compose them over `SimulationNetwork` and
the generic application-composition APIs, and no dedicated hosting or HTTP packages ship. Exact
shipped interception behavior is defined by [`rule-inventory.md`](rule-inventory.md).

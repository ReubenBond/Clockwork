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
or unregistered runtime service: it fails explicitly instead. (Completing a `TaskCompletionSource`
is the one exception: it passes through to the BCL outside a simulation.) Controlled code also runs only
on the simulation's logical thread (see [Thread affinity](#thread-affinity)). Timers, delays, asynchronous timeouts,
and timer-driven cancellation use the shared virtual-time deadline scheduler.

#### Thread affinity

A simulation's logical thread is, at any moment, the thread running one of its scheduler's operations
or the one thread holding `SimulationScheduler.EnterControlScope()`
(`SimulationScheduler.IsSimulationThread`). The cluster's drive methods (`RunUntil`, `RunUntilIdle`,
`RunFor`, `RunToCompletion`), its disposal, and `ReplayRunner` hold that scope on the calling thread. A
second thread entering it while it is held throws `SimulationSchedulerException`. Every controlled entry
point called with a simulation's ambient context requires the logical thread, and on any other thread
throws `SimulationThreadAffinityException` naming the API, the thread, and the runtime and seed, before
it does anything else. The exception-hardening guard injected into `catch` blocks and filters reads no
simulation state and runs on any thread, so escaped work's own handlers run as written.

The ambient context flows with the `ExecutionContext`, so work which uninstrumented code moves onto a
real thread carries it there: the real `Task.Run` with which `BackgroundService.StartAsync` starts a
rewritten `ExecuteAsync` (see the [.NET 11 note](#supported-today)), a timer, thread-pool item, or
continuation queued by an uninstrumented library, or a `CancellationToken.Register` callback which a
thread outside the simulation runs by cancelling the token. That work races the coordinator, so it
fails at its first controlled call. Start it from instrumented code instead, so it runs as a controlled
operation; for a hosted service, implement `IHostedService` in the system under test rather than
deriving from the shared framework's `BackgroundService`.

The scheduler records the first violation, with the escaped thread's stack, and rethrows it from its
next step on the driving thread whether or not an operation runs, so the run fails even when the escaped
work swallows the exception, as a faulted `ExecuteTask` does. Cluster disposal collects a violation not
yet rethrown among its failures, and `ReplayRunner` ends the run as `Aborted`, since the escaped work ran
outside the recorded schedule. Detection follows the escaped work's own timing: a violation which
happens after disposal reaches only the escaped thread, and an exception which escapes a real timer or
thread-pool callback ends the process, as in .NET.

Two kinds of call legitimately run off an operation thread. A `TaskCompletionSource` completion with
no ambient context (from a finalizer, say) passes through to the BCL. Host code which runs simulation
callbacks inline while no step is in progress - cancelling a token which the system under test
registered callbacks on, between drives - runs them on the logical thread inside `EnterControlScope()`.
The cluster cancels `TeardownCancellationToken` that way, at disposal or, once the token passed to its
constructor is cancelled wherever that happens, from the next step of a cluster drive method. Pass the system under
test a token which the scenario owns and cancels from simulation work, rather than a token another
thread cancels, such as a test framework's timeout token.

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
They record the simulation seed, explicit schedule seed, strategy/options, Clockwork/runtime compatibility,
optional closure-manifest and assembly hashes, ordered scheduler/resource/timer decisions, race
scheduling points, terminal outcome, and structured operation/resource/timer/race/deadlock diagnostics.
Readers ignore unknown optional properties within a supported schema version. A schema version,
runtime version, rule-set, manifest, or assembly hash mismatch fails before scenario code executes.

Recording and replay are exposed through `ReplayRunner`. Replay validates every decision at the first
divergence, including expected/actual source, candidate metadata, and selected result, then verifies
that the complete decision stream was consumed at the reproduced terminal boundary. An `Aborted`
outcome identifies an interrupted decision prefix and is rejected for exact replay.

The terminal outcome is `Aborted`, `RaceDetected`, `Faulted` for an exception that escapes an operation
body, `Deadlocked` for a wait-for cycle, or `BoundExceeded` when one applies, and otherwise the entry
task's own terminal state. For an asynchronous entry (`RecordClusterEntry`, `ReplayClusterEntry`,
`ExploreClusterEntry`), the entry task is the task the scenario returns, and the runner stops when it
completes, as a process ends when `Main` returns: work left at that point, including a wait-for cycle,
is reported in the diagnostics and does not change the outcome. A synchronous scenario callback
(`Record`, `RecordCluster`, `ExploreCluster`) is an entry which completed when it returned; its run
continues until the work it registered quiesces or reaches a bound, and a wait-for cycle at that
quiescence is `Deadlocked`. Artifacts record the entry form, and replaying one in the other form is
rejected. An exception, including an `OperationCanceledException`, that escapes a `Thread` body,
thread-pool item, timer callback, scheduler-lane or synchronization-context callback, or raw `Schedule`
body is unhandled, as in .NET, and faults the run with its concrete type; task-backed operations such as
`Task.Run` capture it in their task instead. Canceled operations, including the continuation a
`Task.WaitAsync` withdraws when its timeout or token wins, are diagnostics and never make a run
`Canceled`. See [`replay.md`](replay.md#outcome-classification) for the exact rules and failure
identities.

`ScheduleExplorer` varies only the schedule seed while keeping the simulation seed fixed.
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
  `box`, `unbox(.any)`, `ldtoken`, `initobj`, `sizeof`, `constrained.`), including operands composed
  from a substituted type such as the `constrained. List<Barrier>.Enumerator` prefix of a `foreach`;
- substitution in signatures composed from a substituted type: fields, locals, parameters and return
  types, base types and interfaces (`List<Barrier>`, `IEnumerable<CountdownEvent>`), explicit interface
  implementations, and generic parameter constraints (`where T : IEnumerable<Barrier>`). A type which
  derives directly from a substituted type fails the rewrite (`CWR0001`) rather than invoking the
  replacement's members on an object of a different type;
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
- type exclusion — a type marked `[DoNotRewrite]` or named in `ExcludedTypeFullNames` is not rewritten,
  and neither are the compiler-generated types nested in it (marked `[CompilerGenerated]`, or nested in
  such a type: async and iterator state machines, lambda and local-function closures, and an async
  lambda's state machine in its closure), transitively, so the type and its helpers stay consistent and
  its code runs with BCL behaviour, uncontrolled. An explicitly declared nested type keeps its own
  annotation. Because its lambdas and local functions are excluded with it, a callback an excluded type hands
  to simulated code runs uncontrolled too: its body uses real time, real timers, and the thread pool, so
  excluding a type removes its callbacks from the simulation. Each excluded type is recorded in the manifest
  (`CWR0006`). Rewritten code cannot cross the boundary with a substituted type: a reference into an excluded
  type whose signature mentions one, or an implementation or override across it whose signature does, fails
  (`CWR0001`), as below; nor can excluded code use a rewritten type whose base type, interfaces, or
  constraints the rewrite substitutes, nor rewritten code an excluded type that keeps such a part;
- strict resolution — a targeted member whose replacement cannot be resolved is a hard
  failure (`CWR0001`); a targeted call is never silently skipped. A member reference whose signature a
  type substitution rewrites, but whose declaring type it keeps, binds only where that type is rewritten
  too, so the rewrite fails (`CWR0001`) for a member of an assembly the closure does not rewrite (the
  framework, a replacement assembly, or an excluded assembly) or of a type excluded from rewriting, and for
  a field of an excluded type whose type is substituted. Code excluded from rewriting keeps the original
  signatures, so an excluded type's reference to a member of its module whose signature the rewrite changed
  fails too. An interface implementation or override is matched to its member by signature when the type
  loads, so one whose signature the rewrite changes on one side only (between a rewritten type and an
  excluded type or a type of an assembly the closure does not rewrite, including where one of them names the
  implemented interface or base type with a substituted generic argument, which the rewrite substitutes in a
  rewritten type only) fails (`CWR0001`) rather than throwing `TypeLoadException`. An explicit
  implementation's declaration is retargeted with the member it implements, and fails (`CWR0001`) where that
  member does not bind after rewriting: the substitute lacks it, or its declaring type keeps the original
  signature. The rewrite also substitutes a rewritten type's base type, interfaces, and generic parameter
  constraints, and a method's constraints, while code excluded from rewriting keeps the original ones, so an
  excluded type that mentions a type or method of its module so reshaped, or a type deriving from one, fails
  (`CWR0001`): it could use the type through a base type or interface it no longer has (adding an element of
  the original type to what is now a list of the substitute throws `ArrayTypeMismatchException`), or
  instantiate it with a type that satisfies only the original constraint. Conversely, rewritten code sees
  such a part as composed from the substitute, so rewritten code that mentions a type which keeps a base type,
  interface, or constraint composed from a substituted type (an excluded type, a type of an assembly the
  closure does not rewrite, or a type deriving from one, such as an excluded `Old : List<Barrier>` that
  rewritten code would use as a `List<ControlledBarrier>`), or a generic method of such a type that keeps
  such a constraint, fails (`CWR0001`) too. Any mention counts, because IL does not spell out a conversion
  to a base type or interface. The closure runner tells the engine which assemblies it
  rewrites; a direct engine caller that does not is taken to rewrite none, so every such reference into
  another assembly fails closed. The set is part of the options fingerprint, so an already-rewritten input
  reused in a closure that rewrites different assemblies fails (`CWR0008`). Once the closure is staged, the
  runner resolves every reference into a rewritten assembly that mentions a substituted or substitute type,
  from a copied assembly or an excluded type, checks every implementation and override across assemblies the
  same way, and fails the run (`CWR0001`) if one no longer binds, if a copied assembly or an excluded type
  mentions a reshaped type or method of a rewritten assembly, including a type of its own assembly deriving
  from one, or if rewritten code mentions a type that another rewritten assembly excludes, or that derives
  from one, which keeps a part composed from a substituted type. It resolves the staged closure as the
  runtime binds it: a framework assembly is the runtime's own copy, and any other assembly is the one in the
  staging directory, whose subdirectories (another target framework's build, say) are not searched. A cycle
  of type forwarders met while resolving fails the rewrite or the run (`CWR0003`) rather than overflowing the
  stack. Read-back validation resolves every such reference whose declaring assembly the closure does not
  change (this assembly, the framework, a replacement assembly, or an excluded one) and reports one that
  does not bind (`CWR0009`).

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
assembly and Clockwork kernel are also copied unchanged so async test methods can create the
simulation before controlled code runs; the complete application/dependency closure remains eligible
for rewriting. The next build restores the pristine snapshot first, so
incremental compilation and instrumentation-mode changes never consume a previously rewritten input.
Because the rewritten test copy occupies the project's normal module path, `dotnet build` followed
by `dotnet test --no-build` uses it without runner-specific dispatch hooks. Production project
outputs and non-opted-in test projects remain ordinary IL. Instrumentation must be selected per
project, never using a solution-wide `ClockworkInstrumentationEnabled` global property.

**Task package requires the .NET 11 SDK.** The task and its Cecil-based engine target
`net11.0` and load only under `dotnet build` / `dotnet msbuild`; .NET Framework MSBuild
(classic `msbuild.exe`) cannot host them. Rules are evaluated against a target runtime: the configuration
file's `targetRuntime`, or else `ClockworkTargetRuntime`, which defaults to the project's target framework
version (`11.0` for `net11.0`). A project targeting a runtime outside a rule's `supportedRuntimes` fails the
rewrite (`CWR0010`) at each call site the rule matches. The built-in rules support target runtime `11.0`.
The `Clockwork.Tool` CLI exposes `rewrite`
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

**Surface classification.** Every public and protected member of each type the built-in rules
target is controlled or rejected by a rule, or is listed in the inventory's *Members left as BCL
calls* section with the reason a call to it stays a BCL call: `Value` for `DateTime.AddDays`,
`HostState` for `Environment.MachineName`, `Blocks` for `Task.Wait(int)`, and so on. A type is
targeted when a rule names or substitutes it, or when a member's signature mentions a class,
struct, or interface of `System.Diagnostics.Process.dll`, so a process API is classified wherever
the framework adds it.
Tests classify each targeted type against the .NET 11 reference assemblies with the rewriter's own
rule matcher and substitution mapper, and fail on an unclassified member or on a listed member that
the reference assemblies lack or a rule matches. A constructor counts as rewritten only if rules
rewrite both its `newobj` and a `call` to it, which a derived type's constructor or a value type
constructed in place makes. Snapshots of the reference pack's
assembly list and of the public types in the namespaces of the targeted types fail when a runtime
adds or removes one, prompting a review of it. A member that starts, signals, waits on, reschedules,
or terminates a process is rejected; every other member no rule rewrites is listed with its
behavior, and the reasons the inventory marks as documented holes (`CultureOrTimeZone`,
`HostState`, `Process`, `RealClock`, `ProcessWideId`, `CurrentThread`, `IdentityHandle`,
`ConstructorCall`, `Blocks`, and `Schedules`) are real BCL calls under simulation.

Instrumented closure binaries are simulation/test artifacts, not production replacements. Every
Controlled entry point requires an active Clockwork simulation; without one it throws
`SimulationNotActiveException` before any real BCL operation runs. It also requires the simulation's
logical thread, and elsewhere throws `SimulationThreadAffinityException` (see
[Thread affinity](#thread-affinity)). With an active simulation:

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
and `ValueTask.ConfigureAwait(false)` are controlled identically. The same family substitutes the
configured async disposable and enumerable that `await using` and `await foreach` consume
(`ConfiguredAsyncDisposable`, `ConfiguredCancelableAsyncEnumerable<T>` and its `Enumerator` →
`ControlledConfiguredAsyncDisposable`, `ControlledConfiguredCancelableAsyncEnumerable<T>`), whose BCL
members return configured value-task awaitables, and redirects the `TaskAsyncEnumerableExtensions.ConfigureAwait`
and `WithCancellation` extensions that produce them. As in the BCL, the configured enumerable passes its token to
`GetAsyncEnumerator(CancellationToken)`, where an async iterator combines it with its `[EnumeratorCancellation]`
parameter, and each `MoveNextAsync`/`DisposeAsync` value task is configured with the requested
`continueOnCapturedContext`, which the controlled awaitable treats as `ValueTask.ConfigureAwait(bool)` does. The controlled awaiter hands
every continuation to the simulation coordinator rather than the awaited task's completion
callback, which is exactly why **`ConfigureAwait(false)` stays controlled**. A method compiled with runtime
async (`MethodImplOptions.Async`) has no state machine whose builder and awaiters could be substituted, and
the runtime resumes it through its own continuation machinery, so rewriting one with a rule set that
substitutes the async method builders fails with `CWR0013` naming the method and its assembly; compile it
without runtime async (remove the `runtime-async=on` compiler feature), or exclude an assembly that cannot be
recompiled from rewriting, which leaves it uncontrolled. The runtime-async
`AsyncHelpers.Await` intrinsics that take a configured task or value-task awaitable resume their caller
through the runtime's own continuation machinery instead, so they are rejected by name
(`SimulationApiException`); compile such methods without runtime async. `AsyncHelpers.HandleAsyncEntryPoint`,
through which an async `Main` returns (`task.GetAwaiter().GetResult()`), waits through the controlled awaiter.
`Task.ConfigureAwait(ConfigureAwaitOptions)` and `Task<T>.ConfigureAwait(ConfigureAwaitOptions)` build
the same controlled awaitables: `SuppressThrowing` completes the await normally for a faulted or
canceled `Task`, `ForceYielding` always yields through the coordinator, and invalid options (including
`SuppressThrowing` on `Task<T>`) throw the BCL's `ArgumentOutOfRangeException`. As in the BCL,
`GetAwaiter()` or `ConfigureAwait` on a null task throws `NullReferenceException` and the controlled
awaiters keep their frames out of a rethrown exception's stack trace. A synchronous
`GetAwaiter().GetResult()` on an incomplete task or value task pumps the coordinator until it completes
instead of blocking. Uninstrumented
production binaries retain normal BCL async semantics. A
**call-site redirect** half routes both the
non-generic `Task.WhenAll` / `Task.WhenAny` (array, span, pair, enumerable) combinators **and their
generic `Task<T>` overloads** (array, span, enumerable, and the `WhenAny<T>` pair), the synchronous
`Task.Wait()` / `Task.WaitAll` / `Task.WaitAny(Task[])` waits, the blocking generic
`Task<T>.Result` accessor, the `TaskExtensions.Unwrap` extension methods, all 6 `Task.WhenEach` overloads, all 40
`Task.ContinueWith`/`Task<T>.ContinueWith` overloads, all 48 `TaskFactory`/`TaskFactory<T>`
`ContinueWhenAll`/`ContinueWhenAny` overloads, the `ValueTask`/`ValueTask<T>` `AsTask` and `Preserve`
conversions, and the completion members of `TaskCompletionSource`/`TaskCompletionSource<T>` to shims in
`Clockwork.Shims.System.Threading.Tasks`, and rejects every `TaskFactory`/`TaskFactory<T>` `FromAsync` overload at
the call site. Synchronous waits **pump the
coordinator loop until completion instead of blocking a physical thread**, then delegate to the
real API to reproduce its exact `AggregateException` semantics, so a synchronous wait or a blocking
`Task<T>.Result` read on incomplete controlled work never deadlocks the scheduler.
`TaskAsyncEnumerableExtensions.ToBlockingEnumerable` redirects to
`ControlledTaskAsyncEnumerableExtensions`, whose enumerable pumps the coordinator until each
`MoveNextAsync` and the final `DisposeAsync` complete; as in the BCL, enumeration is deferred, the
token reaches `GetAsyncEnumerator`, the source enumerator is disposed on completion, early exit, and
fault, and exceptions propagate unwrapped.
`Task.Run` and `TaskFactory.StartNew` queue controlled operations instead of escaping, while
`Task.Delay` and `Task.WaitAsync` register virtual deadlines.

**Clockwork registers no BCL continuation on a task.** A continuation the BCL registers inherits the
task's dispatch: completing a `TaskCompletionSource` created with `RunContinuationsAsynchronously`, a
channel's `Completion`, a controlled timer's `DisposeAsync`, or a value-task source which runs continuations
asynchronously (a channel's pending `ReadAsync`/`WaitToReadAsync`, a `ManualResetValueTaskSourceCore<T>` with
`RunContinuationsAsynchronously`) would run it on the real thread pool, so the result it completes would become
ready at a nondeterministic scheduling step. Instead:

- **Combinators.** `WhenAll`, `WhenAny`, and `Unwrap` return a result the BCL computes synchronously when their
  antecedents have already completed (the BCL's own result, including `Task.CompletedTask` for no tasks, the
  task itself for one task to the non-generic `WhenAll`, and the inner task for an outer task which has already
  completed successfully). Otherwise the coordinator polls the antecedents' `IsCompleted` through a readiness wait,
  as the controlled awaiters do, and a controlled operation completes a promise with the BCL's outcome, including
  its exception dispatch information, the cancellation token and exception it picks, and the generic results in
  argument order. Argument validation throws the BCL's exceptions, parameter names, and messages. `WhenAny` returns
  the first antecedent, in argument order, among those the coordinator observes complete. The non-generic `WhenAll`
  aggregates faults, and picks the canceled task, in the order the coordinator observes its antecedents complete
  (as the BCL uses completion order); antecedents observed in the same scheduling step are ordered by argument
  position. The generic `WhenAll` uses argument order, as the BCL does.
- **`ContinueWith`.** The returned task is a genuine BCL continuation of a private gate, so its status, creation
  options, state, cancellation (including `LazyCancellation`), `NotOn*`/`OnlyOn*` filtering, and exception and
  `OperationCanceledException` handling are the BCL's own. The coordinator observes the antecedent complete and a
  controlled operation completes the gate with the antecedent's status (never its exceptions, so a fault the
  continuation ignores stays unobserved and raises `TaskScheduler.UnobservedTaskException`, as in the BCL), which
  runs the continuation inline on that operation, on a fresh logical strand in the execution context captured by
  `ContinueWith`. `ExecuteSynchronously` is a hint, as in
  the BCL: the continuation never runs inside the call which completes the antecedent or inside `ContinueWith`.
  `LongRunning` (a dedicated thread) and `AttachedToParent` (controlled tasks have no parent) cannot be preserved
  and throw `SimulationApiException`. The overloads without a scheduler use `TaskScheduler.Default` rather than
  `TaskScheduler.Current`, as the controlled `TaskFactory.StartNew` treats a factory without one: inside a simulation
  `TaskScheduler.Current` is `TaskScheduler.Default` or the simulation's own `SimulationTaskScheduler` (in the root
  task of `SimulationCluster.RunToCompletion` before its first await, for example), and the coordinator runs the
  continuation either way. An explicit `TaskScheduler.Default` or `SimulationTaskScheduler` is accepted on the same
  terms, so `TaskScheduler.Current` inside the continuation is `TaskScheduler.Default`; any other explicit scheduler
  throws `SimulationApiException`.
- **`Task.WhenEach`.** The BCL registers a completion action on each task when `WhenEach` is called, and yields
  the tasks in the order those actions run through an async iterator which resumes on the thread pool. The
  controlled sequence has the coordinator observe the tasks complete from the call on (tasks complete at the call
  in argument order) until each has completed or the enumeration which claimed them has ended, and records the
  order it observes. `MoveNextAsync` returns a value task which a controlled operation completes once a task is
  available, and which a controlled await observes through the coordinator. Argument validation, the single
  enumeration (the first enumerator to start yields every task; any other yields none), cancellation through the
  enumerator's token (`OperationCanceledException` when a task is available, `TaskCanceledException` while waiting
  for one), `Current`, and disposal are the BCL's. The enumerator acquires each yielded task's race-detection edge.
- **`TaskFactory.ContinueWhenAll`/`ContinueWhenAny`.** As for `ContinueWith`, the returned task is a genuine BCL
  continuation of a private gate, which the coordinator completes once it observes every antecedent complete, or
  one, so its status, creation options, state (the continuation delegate, as in the BCL), and cancellation are the
  BCL's. Argument validation follows the BCL's order and messages, and a token already canceled without
  `LazyCancellation` yields a task canceled at creation, with no state, as in the BCL. `ContinueWhenAll` passes the
  continuation a defensive copy of the antecedents; `ContinueWhenAny` passes the first antecedent, in argument order,
  among those observed complete. The overloads without a scheduler use the factory's, or `TaskScheduler.Default`
  rather than `TaskScheduler.Current`; `LongRunning`, `AttachedToParent`, and schedulers other than
  `TaskScheduler.Default` and `SimulationTaskScheduler` throw `SimulationApiException`, as for `ContinueWith`.
- **`TaskFactory.FromAsync`.** Every overload of both factories is rejected at the call site with
  `SimulationApiException`: the task completes when an `IAsyncResult` completes, on a thread of its
  implementation's choosing, outside Clockwork's control. Await a task-returning API instead.
- **`ValueTask.AsTask`/`Preserve`.** A value task backed by a pending `IValueTaskSource` is polled through the
  source's `GetStatus` by the coordinator, with no completion callback registered on the source, and a controlled
  operation consumes the completed source once through the BCL's own conversion (one `GetStatus`, one `GetResult`).
  Any other value task is converted by the BCL synchronously, so `AsTask` of a value task wrapping a task returns
  that task.
- **`TaskCompletionSource`.** Rewritten code keeps the real `TaskCompletionSource` and `TaskCompletionSource<T>`
  types, with their real `CreationOptions`, so their identity is unchanged at the boundary with uninstrumented
  code. Their completion members (`SetResult`, `SetException`, `SetCanceled`, `SetFromTask`, and the `TrySet*`
  forms) redirect to shims which complete the source through the same BCL member and publish the race-detection
  release edge on its task; an await, synchronous wait, combinator, or continuation which observes the task
  acquires it, and the combinators carry their antecedents' edges to their results.

The redirect obeys the same simulation-only invariant as the BCL rule set: instrumented Controlled
builders, awaiters, and shims require an active Clockwork simulation. Continuations and waits route
through the coordinator carried by the complete ambient runtime and cannot silently escape to the
thread pool. The `TaskCompletionSource` completion members are the one exception: a finalizer, or a
callback from uninstrumented code, may complete a source outside any simulation, so outside one their
shims pass through to the BCL member, and they record the race-detection edge only inside a controlled
operation.

Blocking `ValueTask<T>.Result` reads remain unsupported: the accessor is not rewritten, so `await`, or
`GetAwaiter().GetResult()` through the controlled awaiter, is the supported controlled path.
Other APIs absent from the generated rule inventory are outside the support claim. The task surfaces which can
still run a continuation outside the coordinator are:

- **Continuations registered by uninstrumented code** (an assembly excluded from rewriting, or the framework itself)
  on a task or value-task source follow the BCL's dispatch. A `RunContinuationsAsynchronously` completion source
  completed in rewritten code still runs them on the thread pool; Clockwork's own continuations of the same task
  stay controlled. Instrument the assemblies which await simulation work.
- **Race-detection release edges** are published by rewritten completions (completion sources, async methods,
  `Task.Run`, `TaskFactory.StartNew`, a controlled timer's `DisposeAsync`, and the controlled combinators and
  continuations). A task completed by
  uninstrumented code, including a channel's internal operations, publishes none, so the detector does not order
  accesses through it. Nor does `CancellationTokenSource.Cancel` publish one: a `ContinueWith`, `ContinueWhenAll`, or
  `ContinueWhenAny` canceled through its token before its antecedents complete is completed by the canceller, so a
  write before `Cancel` and a read after awaiting the canceled continuation may be reported as a race that is not
  one, until `Cancel` publishes a release edge.

**Remaining deviations from the BCL.** The coordinator observes completions between scheduling steps, so
`WhenAny`, the non-generic `WhenAll`, `WhenEach`, and `ContinueWhenAny` order antecedents completed within one step
by argument position rather than by their exact completion order; a combinator, continuation, value-task conversion,
or `WhenEach` enumerator whose antecedent is still pending completes one controlled operation after the antecedent
rather than inside the completing call; and a combinator or continuation whose antecedent never completes remains
pending simulation work, as an await of that antecedent does (for `WhenEach`, until its enumeration ends, or until
every task has completed if nothing enumerates it). `ControlledTimer.DisposeAsync` completes its task without
`RunContinuationsAsynchronously`, so its `CreationOptions` report `None`. Because no continuation runs inside the
completing call, Clockwork cannot surface bugs where a continuation runs inline under the completer's lock (a
deadlock or reentrancy the BCL's synchronous continuations can cause). `DocumentedDeviationConformanceTests` pins each
of these deviations, with the BCL's value beside it.

#### Threads, thread pool, Parallel, and task parity

`clockwork.tasks.controlled` ensures that every unit of concurrent work an application
spawns — a `Thread`, a `Task.Run`/`TaskFactory.StartNew` body, a `ThreadPool.QueueUserWorkItem`
callback, or a `Parallel` branch — is modelled as a **controlled operation scheduled on the same
single logical thread** the async machinery already uses, instead of escaping onto a physical OS
thread or the real thread pool. The exhaustive controlled/rejected signature list is regenerated
into [`rule-inventory.md`](rule-inventory.md); the Coyote parity matrix is
[`coyote-parity.md`](coyote-parity.md).

Controlled surfaces include the full `Task.Run` and `TaskFactory`/
`TaskFactory<T>.StartNew` families; every `Task.ContinueWith` and `Task<T>.ContinueWith` overload;
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
- **An unhandled exception faults the run instead of terminating the process.** An exception,
  including an `OperationCanceledException`, that escapes a `Thread` body, a thread-pool work item, or
  a timer callback crashes a real process. Here it faults that controlled operation, which a replayed
  run reports as `Faulted`. A `Join` on the thread returns, observing the terminated thread, because
  `Join` never surfaces a thread's exception.
- **Safe vs. unsafe `ExecutionContext` flow is modelled.** `QueueUserWorkItem` captures and flows
  the caller's `ExecutionContext`; `UnsafeQueueUserWorkItem` does not — matching the BCL contract —
  so `AsyncLocal` values observed by the callback differ between the two exactly as they do on the
  real thread pool.
- **OS-specific and un-modellable surfaces are rejected precisely, not silently ignored.** Thread
  `Priority`/apartment-state (including the obsolete `ApartmentState` setter)/`Interrupt`, `Parallel`
  `ParallelLoopState` (break/stop), thread-local, and `Partitioner` overloads, and
  `ThreadPool.UnsafeQueueNativeOverlapped` all fail at the rewritten call
  site with a diagnostic that names the exact API. The registered-wait APIs
  (`RegisterWaitForSingleObject`/`UnsafeRegisterWaitForSingleObject`) are **controlled**
  (see below) — they depend on controlled wait handles, which now exist.
- **Uncontrolled process/termination APIs are rejected *unconditionally*.** `Process.Start`/`Run`/
  `RunAsync`/`RunAndCaptureText`/`RunAndCaptureTextAsync`/`StartAndForget`/`Kill`/`Signal`, the
  `WaitForExit`/`WaitForExitAsync`/`WaitForExitStatus`/`TryWaitForExitStatus`/`WaitForExitStatusAsync` waits,
  the `ReadAllBytes`/`ReadAllText`/`ReadAllLines` reads and their async forms, the `WaitForInputIdle`
  waits, the `Responding` getter, `CloseMainWindow`, the `EnableRaisingEvents` setter and `BeginOutputReadLine`/`BeginErrorReadLine`
  (whose events the BCL raises on threads of its choosing), the `Process` priority, processor-affinity,
  and working-set setters and the `ProcessThread` scheduling setters, the `SafeProcessHandle`
  `Start`/`Signal`/`Resume`/`Kill` and wait members, the `WindowsProcessStartArguments`/
  `UnixProcessStartArguments` `Start` launchers, and
  `Environment.Exit`/`FailFast` throw whether or not a simulation is active (a rewritten assembly must
  never launch, signal, wait on, reschedule, or tear down a real OS process). This unconditional guard is distinct
  from the simulation-only Controlled invariant, because there is no faithful in-simulation model of
  spawning or killing a process.
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

The controlled rule set classifies the exact .NET 11 timer surface:

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

- **.NET 11** is the only targeted runtime (`net11.0`, see `Directory.Build.props`), built with the
  .NET 11 SDK's 11.0.1xx feature band. `global.json` rolls forward to the newest installed patch of that band
  (`latestPatch`), and CI's `setup-dotnet` reads the same file, so servicing fixes arrive without a commit.
  A new feature band (11.0.2xx) is a deliberate `global.json` bump: a band can ship a new C# compiler, and the
  conformance suite, which compiles its fixtures with the SDK's own compiler under Debug and Release, proves
  Clockwork still controls that compiler's async lowering. Consumers that compile code Clockwork rewrites
  should build with a band Clockwork has proven. The built-in rule sets are classified against the .NET 11
  reference assemblies and declare `supportedRuntimes` 11.0. .NET 11's shared framework includes the
  `Microsoft.Extensions` abstractions (`Logging.Abstractions`, `DependencyInjection.Abstractions`,
  `Options`, `Primitives`, and the other `*.Abstractions` assemblies), and NuGet prunes their packages
  from a `net11.0` project's dependencies, so they load from the shared framework and are not rewritten:
  `BackgroundService.StartAsync`, for example, starts `ExecuteAsync` through the real `Task.Run`. A
  rewritten `ExecuteAsync` started that way fails at its first controlled call with
  `SimulationThreadAffinityException`, and so does the run (see [Thread affinity](#thread-affinity)).
- **Windows, Linux, and macOS** are all supported for the existing kernel; it has no
  platform-specific dependencies (no P/Invoke, no OS-specific I/O).
- **JIT execution** (the default `dotnet run`/`dotnet test` path) is fully supported.
- **Deterministic BCL rule set** (`clockwork.bcl.deterministic`) covers the direct static
  time/identity/random surface enumerated in [`rule-inventory.md`](rule-inventory.md).
  Determinism is claimed **only** for those exact signatures.
- **Controlled task rule set** (`clockwork.tasks.controlled`) controls the compiler-generated
  `async Task`/`async ValueTask` machinery and the direct `Task`/`Task<T>` combinator (non-generic
  and generic `WhenAll`/`WhenAny`, `Unwrap`), synchronous-wait, blocking `Task<T>.Result`, continuation
  (every `Task.ContinueWith` and `Task<T>.ContinueWith` overload), `ValueTask` conversion (`AsTask`, `Preserve`),
  and `TaskCompletionSource` completion surface enumerated in [`rule-inventory.md`](rule-inventory.md),
  routing `async`/`await` and synchronous waits through the simulation coordinator. No controlled surface
  registers a BCL continuation on a task. The inventory
  also controls `Task.Run`, all 24 .NET 11 `TaskFactory`/`TaskFactory<T>.StartNew` signatures, `Thread`, `ThreadPool`
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
  [`rule-inventory.md`](rule-inventory.md) are rewritten. Its *Members left as BCL calls* section
  lists every other member of a targeted type with its behavior; the documented holes among them
  include `Stopwatch` instance APIs (`RealClock`), `DateTime`/`DateTimeOffset` parse/format/convert
  helpers (`CultureOrTimeZone`), host and process reads (`HostState`, `Process`), a derived type's
  constructor chaining to a controlled one (`ConstructorCall`), and the blocking and scheduling
  `Task`, `Thread`, and `Parallel` overloads the controlled surface does not cover (`Blocks`,
  `Schedules`). A delegate created from a member (`ldftn`) calls the BCL member, since rules rewrite
  `call`, `callvirt`, and `newobj` sites. Timer limitations are unrecognized custom `TimeProvider`
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

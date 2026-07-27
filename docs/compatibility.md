# Compatibility and capability contract

This document describes the intended execution modes for Clockwork's deterministic
instrumentation work and the platform/deployment contract those modes are designed
against. It is a durable product document, not a task plan: it should stay accurate
as the corresponding capabilities are implemented, and it should be updated (not
duplicated) as scope firms up in later phases.

> **Status:** Phase 2. The deterministic simulation kernel described in the root
> [README](../README.md) exists today (clock, task scheduler, synchronization
> context, seeded random, simulated network, chaos injection), its
> `RunUntil`/`RunUntilIdle`/`RunForDuration` drive loops share one internal
> execution engine with a structured, diagnosable outcome type
> (`SimulationExecutionResult` and the `*Detailed` methods), and adaptive
> `RunUntilConverged`/`RunUntilIdleConverged` entry points escalate the iteration
> budget automatically instead of requiring a hand-picked `maxIterations` (see the
> README's "Adaptive execution budgets" section). Phase 1B added
> pre-instrumentation ergonomics that sit entirely inside cooperative mode: a
> `SimulationBuilder` fluent composition API (`BuiltSimulation`) so common
> simulations don't need a hand-written `SimulationCluster<TNode>`/`SimulationNode`
> subclass, a foundation for heterogeneous node registration (`AddCustomNode`,
> deliberately without startup ordering or DI-style construction - see the
> README), stable cross-process-safe seed derivation from strings
> (`SimulationSeed`), reusable named rendezvous primitives (`SimulationGate`,
> `SimulationLatch`, `SimulationBarrier`), and a reworked
> `SimulationSynchronizationContext.Send` that supports inline-reentrant and
> schedule-and-pump cases without a real-time wait, and rejects genuine
> cross-thread contention with a precise diagnostic instead of deadlocking.
>
> Phase 2 adds the **runtime plumbing** that controlled/race-exploration
> instrumentation will build on, hosted in `Clockwork.Runtime` (see
> [Project layout](#project-layout)): an ambient, `AsyncLocal`-backed
> `SimulationExecutionContext` (nested scopes, exception-safe disposal, async
> flow, parallel isolation, explicit flow-suppression diagnostics); secure,
> capability-token-gated activation (no public global switch, environment
> variable, or accidental default can activate simulation context); a root
> deterministic seed/decision authority with independent named domains
> (scheduler, network, application, identity, Buggify, model) and stable
> per-node/per-site child derivation that does not depend on registration order;
> a typed deterministic decision-log model plus a replay *validation* contract
> (content-only comparison, first-divergence detection - not a scheduler); an API
> interception policy classification model (`Controlled`/`Rejected`/`PassThrough`
> with deterministic per-API/per-assembly precedence, and pass-through always
> explicit); and an external-entry guard that flags a callback executing under a
> *different* simulation's ambient context without falsely flagging the normal
> no-ambient-context case. `SimulationCluster<TNode>`, `SimulationNodeContext`,
> `SimulationTaskQueue`, and `SimulationBuilder`/`BuiltSimulation` install and
> restore this ambient context automatically; hand-written cluster/node subclasses
> that predate it are unaffected (see the README's "Deterministic instrumentation
> runtime plumbing" section for the exact compatibility rule). None of this
> changes any existing public API's signature or behavior - it is purely
> additive, and none of it intercepts, schedules, or rewrites application code
> yet. Phase 2 explicitly does **not** implement: controlled-operation
> physical-thread gating, resource pause/resume, deadlock detection, IL rewriting
> (Cecil), a public Buggify API, BCL compatibility shims, Generic Host
> integration, or HTTP support - this document exists to pin down the contract
> those will be designed against, so the package scaffolding under `src/` (see
> [Project layout](#project-layout)) has a stable target.

## Intended execution modes

Clockwork's roadmap distinguishes four modes of running application code against
the deterministic kernel. They trade off fidelity, overhead, and how much of the
application's own concurrency they can observe or control.

### Cooperative mode

The application opts in explicitly: it uses `TimeProvider`, the simulation
`TaskScheduler`/`SynchronizationContext`, `SimulationRandom`, and the simulated
network surfaces directly, as described in the README's "Determinism requirements"
section. This is what the current kernel supports today. No IL rewriting, no
profiler, no analyzer - the application author is responsible for routing every
source of nondeterminism through Clockwork's APIs.

### Controlled mode

Clockwork additionally *verifies* cooperative usage and, where feasible, redirects
common nondeterministic entry points (thread pool scheduling, `Task.Delay`, `Random`
construction) automatically, so that accidental escapes from the simulation are
caught early instead of silently reintroducing flakiness. This is expected to be the
first mode built on top of the `Clockwork.Instrumentation` boundary and the
Roslyn analyzers in `Clockwork.Analyzers` (diagnostics for direct wall-clock/thread
pool/`Random.Shared` usage), rather than requiring IL rewriting. Phase 2's runtime
plumbing (ambient `SimulationExecutionContext`, the `SimulationApiPolicyRegistry`
classification model, and the external-entry guard) is the substrate this mode is
expected to build on; it does not itself intercept or redirect any API yet - see the
README's "Deterministic instrumentation runtime plumbing" section.

#### Controlled-operation kernel (Phase 3A)

Controlled mode's *scheduling substrate* is the controlled-operation kernel in
`Clockwork.Runtime/Scheduling/` (`ControlledOperation`, `ControlledOperationScheduler`).
It establishes the one invariant every future controlled primitive depends on: **at
most one logical operation executes system-under-test code at a time, even when
operations run on multiple physical threads.** Each operation runs on its own
dedicated background thread, but a single permission "baton" (handed off via wait
handles - no busy-spin, no `Thread.Abort`) guarantees exactly-one-running. The
scheduler - not arbitrary callers - owns every state transition
(`Created → Runnable → Running → {Paused, Completed, Faulted, Canceled}`); illegal
edges throw `InvalidControlledOperationTransitionException` with diagnostics rather
than silently misbehaving. Teardown unparks paused/running operations through an
explicit control signal and joins their threads with a timeout, so disposal cannot
strand a thread.

Each operation carries a logical execution identity
(`SimulationLogicalExecutionId`) that is **distinct from
`Environment.CurrentManagedThreadId`** - because a single logical operation may hop
physical threads. The scheduler installs it into `SimulationExecutionContext` on the
operation thread, so Phase 2 decision records pick it up automatically with no Phase 2
API change.

The kernel is wired into the existing `SimulationTaskQueue` as an **opt-in
compatibility bridge**: when a scheduler is supplied, each *ready item* (one user
callback) runs as a single controlled operation; internal bookkeeping callbacks are
deliberately **not** wrapped, to avoid needless behavioral churn. The bridge is off by
default - a queue built without a scheduler behaves exactly as before, so every
existing Phase 0/1 trace snapshot stays byte-identical. The one intended difference on
the controlled path is that item bodies observe a non-null logical execution id
(inline items observe `None`). Always-on migration of the whole kernel to controlled
operations is deferred until the resource model exists.

**Deferred to Phase 3B (explicitly not in the kernel):** `Monitor`/`Semaphore`/
wait-handle/synchronous-`Task`-wait shims, the resource ownership + wait-queue model,
virtual timeout/cancellation races, deadlock detection, and fairness/priority
selection strategies beyond the kernel's deterministic round-robin. The kernel only
provides *generic* pause/resume primitives and pause-reason metadata sufficient for
those primitives to be built on top; it implements none of them itself. A paused
operation yields the baton deterministically and later becomes runnable again without
ever introducing physical concurrency.

#### Reusable resource/wait scheduler (Phase 3B)

Phase 3B builds the *reusable resource and wait layer* every future controlled
synchronization primitive needs, all inside `Clockwork.Runtime/Scheduling/` - still
with **no public `Monitor`/`Semaphore`/`WaitHandle`/`Task` shims** (those remain Phase
6/7; see below). It adds:

- **Controlled resources** (`ControlledResource`, `ControlledResourceId`,
  `ControlledResourceKind`): a general model with stable identity, an optional owner,
  capacity/count support, a deterministic waiter queue ordered by enqueue sequence, and
  rich debug metadata. The `kind` distinguishes `Monitor`, `Semaphore`, events, wait
  handles, synchronous `Task` waits, and timers without pretending they share identical
  semantics - specialized behavior is layered on top rather than baked in.
- **Atomic pause/wakeup** (`WaitOnResource`/`SignalOne`/`SignalAll`): registering a wait
  atomically transitions the running operation to *paused-on-resource*, yields the
  permission baton, and later makes it runnable - with no lost wakeups, duplicate queue
  entries, or stale wakeups (a waiter resolves exactly once).
- **Virtual-time timeouts** (`ControlledVirtualClock`): zero, finite, and infinite
  timeouts modeled entirely in virtual time. Because `Clockwork.Runtime` does not depend
  on the root `Clockwork` package, the clock mirrors `SimulationClock` semantics
  internally instead of referencing it. Timeouts fire only during an explicit virtual-time
  advance that happens *only when nothing is runnable*, so a pending signal deterministically
  precedes a same-instant timeout. **No real-time delays are ever used as modeled
  behavior** - wall-clock time only guards test/process teardown.
- **Synchronous cancellation** integrated via `CancellationToken.Register` (never
  `CancelAsync`, never a thread-pool hop): cancellation is observed on the cancelling
  operation's own thread under the scheduler lock. Release/timeout/cancel races resolve to
  exactly one terminal reason, and registrations are always disposed on the way out.
- **Wait-for graph + deadlock detection** (`DetectDeadlock`, `DescribeLiveness`): reports
  deterministic ownership cycles with operation ids/names, resource ids/names, owners,
  waiter order, and originating metadata, and classifies liveness so a genuine resource
  deadlock is distinguished from *paused-until-time*, *externally completable*, and
  *quiescent* states. Only indefinite waits contribute deadlock edges - a timed wait is
  always breakable by advancing modeled time. The liveness summary folds in the existing
  operation-status snapshot, integrating with execution diagnostics.
- **Pluggable scheduling strategies** (`IControlledSchedulingStrategy`): FIFO/legacy,
  round-robin (the **default**, byte-for-byte the Phase 3A behavior), seeded-random (from
  the Phase 2 `Scheduler` seed domain), priority (a crisp `ControlledOperation.Priority`
  integer, *not* BCL thread priority), and exact replay. Every real choice among two or
  more runnable operations is recorded as a `SchedulingOrder` decision when a decision log
  is attached, and replay validation fails at the first divergent choice.

**Fairness is defined narrowly and deliberately.** The layer makes *no* promise of BCL
fairness. Resource waiter order is deterministic under the selected policy and replayable;
that is the only guarantee. The strategy interface is public because choosing a scheduling
policy is a legitimate consumer concern, but it grants no BCL-compatible fairness semantics.

**Still deferred to Phase 6/7 (explicitly not in Phase 3B):** the public
`Monitor`/`Semaphore(Slim)`/`WaitHandle`/synchronous-`Task`-wait shims themselves, and the
Cecil/call-site rewriting that would redirect real BCL calls onto this layer. Phase 3B
provides only the *internal* resource/wait scheduler those shims will sit on; where a
specific future primitive needs specialized semantics, it plugs into an extensible internal
hook (resource `kind`, owner metadata, custom strategy) rather than forcing incorrect
one-size-fits-all behavior into the shared model.

### Race exploration mode

Beyond redirecting nondeterminism, this mode actively perturbs scheduling decisions
(interleavings, delays, delivery order) across repeated seeded runs to search for
concurrency bugs that a single deterministic replay would not surface. This is
expected to build on `SimulationRandom` forking and `SimulationNetwork`'s existing
seeded delay/drop/partition behavior, extended with systematic exploration
strategies rather than single fixed scripts.

### Optional deep instrumentation mode

The most invasive mode: build-time IL rewriting (`Clockwork.Instrumentation.Build`)
or runtime profiling hooks intercept nondeterministic operations that cannot be
caught cooperatively or via analyzers - for example, calls made by third-party
libraries that do not route through `TimeProvider` or accept an injected scheduler.
This mode is expected to depend on IL manipulation tooling (see
[Third-party notices](../THIRD-PARTY-NOTICES.md) for the Mono.Cecil-based approach
under consideration) and is deliberately deferred: it carries the highest
implementation cost and the most platform constraints (see below), so it should
only be built once the lower-cost modes have proven insufficient for a concrete
scenario.

#### Rewrite-engine core (Phase 4A)

Phase 4A adds the **generic IL rewrite-engine core** to `Clockwork.Instrumentation`
(`Mono.Cecil` 0.11.6). It is **internal and experimental**: a deterministic,
rule-driven Cecil transformation pipeline plus an extensive golden test corpus, and
nothing else. The engine (`RewriteEngine.Rewrite`) takes a caller-supplied, versioned
`RewriteRuleSet` and a set of replacement ("shim") assemblies, applies an ordered set
of passes to the input assembly, validates the result by reading it back, and emits a
deterministic `InstrumentationManifest`. The rule model integrates the Phase 2 API
policy classification (`Controlled`/`Rejected`/`PassThrough`) without the engine
referencing any concrete shim by identity.

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
- portable and embedded PDB preservation with per-site source mapping; absent or
  unsupported symbol forms are reported (`CWR0004`/`CWR0005`), never silently dropped;
- assembly/rule-set-level idempotence markers: re-running with the same rule set is a
  verified no-op, and an incompatible rule-set version fails clearly (`CWR0008`)
  rather than double-rewriting;
- strict resolution — a targeted member whose replacement cannot be resolved is a hard
  failure (`CWR0001`); a targeted call is never silently skipped.

**Explicitly not in Phase 4A (deferred to Phase 4B or later):** MSBuild target/task
activation and CLI rewrite commands; recursive publish-output rewriting; strong-name
re-signing and Authenticode; load-time `AssemblyLoadContext` hooks (the in-process
execution *tests* load rewritten fixtures only as a test mechanism); any concrete BCL
deterministic shim; the Phase 6/7 `Monitor`/`Semaphore`/`WaitHandle`/`Task` shims and
Coyote-style task/lock type substitutions; `Buggify`; Generic Host / HTTP integration;
and profiler/native detours. Mixed-mode (native) assemblies are rejected (`CWR0011`).
The engine performs the IL transformation mechanics only; it is wired to no build or
deployment step yet.

#### Build and tool integration (Phase 4B)

Phase 4B wires the Phase 4A engine to a build and a command line. It adds **no BCL shim
rules** - it is generic, strictly opt-in plumbing that fails explicitly rather than
silently degrading. It ships two packages: `Clockwork.Instrumentation.Build` (an MSBuild
task with `build/` props and targets, a development dependency) and `Clockwork.Tool`
(the `clockwork` CLI).

**Opt-in only.** An ordinary build never instruments. The `ClockworkInstrument` target
runs `AfterTargets="Build"` only when the consumer sets
`ClockworkInstrumentationEnabled=true` and supplies at least one `@(ClockworkRuleSet)`
document. It discovers the resolved output closure (honoring `.deps.json`, runtimeconfig,
satellite/resource/native assets, include/exclude globs, and framework/reference-assembly
exclusion), rewrites **only managed IL** out-of-place under
`obj/<Config>/<Tfm>/clockwork/instrumented/`, copies the non-managed assets needed to run
the staged app unchanged, and emits a manifest under
`obj/<Config>/<Tfm>/clockwork/clockwork.manifest.json`. Source and `bin` outputs are never
mutated. The work is incremental, keyed by input assembly/symbol hashes, the rule-set
signature, engine version, configuration, and reference set.

**Task package requires the .NET 10 SDK.** The task and its Cecil-based engine target
`net10.0` and load only under `dotnet build` / `dotnet msbuild`; .NET Framework MSBuild
(classic `msbuild.exe`) cannot host them. The `Clockwork.Tool` CLI exposes `rewrite`
(with `--dry-run`) and `inspect` (text or `--json`), with nonzero exit codes classified
by failure kind. `run`/`replay`/`minimize` are deferred to later replay work.

**Configuration is data, not code.** Configuration and rule sets are JSON documents,
validated strictly for schema, types, and signatures; **no arbitrary code is executed
from configuration**. Multiple rule sets merge deterministically by a defined precedence -
the mechanism future built-in, application, and third-party rules will share.

**Strong naming (build/tool scope).** Signed, public-signed, and delay-signed inputs are
detected. Re-signing happens only when a key is supplied (`ClockworkStrongNamePolicy=Resign`
+ `ClockworkStrongNameKeyPath`); when re-signing is required but no key is available the
build fails clearly rather than emitting a broken signature. Public-key-token consistency
across a rewritten dependency closure is verified. **Authenticode** signatures are detected
and reported as unsupported - they are never re-applied, and a rewritten assembly does not
retain its Authenticode signature; re-sign such outputs with your own toolchain after
instrumentation.

**ReadyToRun (build/tool scope).** R2R/native sections are detected. The default `Reject`
policy fails rather than emit stale native code; the opt-in `StripToIL` policy round-trips
through Cecil to produce IL-only staged output. Because instrumentation rewrites managed
IL, it must run **before** crossgen/R2R publish, single-file bundling, and Native AOT -
instrument first, then publish. Runtime/product-mode hooking of an already-published R2R or
single-file binary remains deferred (see below).

#### First deterministic BCL rule set (Phase 5)

The first production built-in rule set, `clockwork.bcl.deterministic` (version `1.0.0`),
redirects the direct **static** time / identity / random BCL surface to Cecil-free runtime
shims in `Clockwork.Runtime` (namespace `Clockwork.Runtime.Shims`). The complete, exhaustive
list of controlled and rejected signatures is generated into
[`rule-inventory.md`](rule-inventory.md) and verified against the shipped rules by a test, so
the published inventory can never drift from the code.

The redirect is a three-state contract enforced by the shim, never a silent fallback:

- **Outside a simulation** the shim runs the real BCL API unchanged (production pass-through).
- **Inside a simulation with a registered runtime environment** it dispatches to the node's
  simulated clock and the correct independent seed domain (Application/Identity only - never
  the scheduler, network, or Buggify domains).
- **Inside a simulation with no registered environment** it throws
  `SimulationServiceMissingException` rather than read real wall-clock time or OS entropy.

Semantics: local-time clocks (`DateTime.Now`/`Today`, `DateTimeOffset.Now`) honour the
configured simulation time zone; `Environment.TickCount`/`TickCount64` wrap with correct
`int`/`long` behaviour; `Stopwatch.GetTimestamp`/`GetElapsedTime(long)` are machine-independent.
`Guid.NewGuid` draws deterministic bytes while preserving RFC 4122 variant and version 4;
`Guid.CreateVersion7` encodes the simulated UTC millisecond timestamp in the first 48 bits with
version 7 (no monotonicity guarantee beyond the BCL contract). `Random.Shared` and unseeded
`new Random()` become per-node deterministic streams; explicitly seeded `new Random(int)`
preserves the caller's seed exactly. Cryptographic randomness (`RandomNumberGenerator` static
entropy APIs) is **rejected by default** under simulation with a precise diagnostic; a strictly
test-only opt-in (`SimulationBuilder.WithCryptoRandomnessPolicy(DeterministicInsecureForTesting)`)
can substitute deterministic-insecure bytes - production security semantics are never changed.

**Opt-in.** No JSON authoring is required. MSBuild consumers set
`<ClockworkUseBuiltInRules>true</ClockworkUseBuiltInRules>` (strict by default via
`ClockworkStrictBuiltIns`); CLI consumers pass `--builtin clockwork.bcl.deterministic`
(or `--builtin all`) with optional `--builtin-include`/`--builtin-exclude` family filters and
`--builtin-strict`. The selected families are versioned and folded into the rule-set signature,
so incremental rebuilds stay correct.

#### Controlled task and async rule set (Phase 6A)

The second production built-in rule set, `clockwork.tasks.controlled` (version `1.0.0`),
controls the compiler-generated async machinery and the direct `Task` surface that ordinary
application code uses, so `async`/`await` runs on the simulation's single logical thread instead
of the physical thread pool. It is selected independently of the BCL rule set (CLI
`--builtin clockwork.tasks.controlled`, or `--builtin all` for both). The exhaustive controlled
and rejected signature list is generated into [`rule-inventory.md`](rule-inventory.md) and
verified against the shipped rules by a test.

The rule set has two halves. A **member-aware type substitution** pass retargets the
compiler-generated builder and awaiter types of an `async` state machine onto controlled
value-type equivalents in `Clockwork.Runtime.Tasks.CompilerServices`
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
callback, which is exactly why **`ConfigureAwait(false)` stays controlled** while still delegating
to normal BCL semantics outside a simulation. A **call-site redirect** half routes both the
non-generic `Task.WhenAll` / `Task.WhenAny` (array, span, pair, enumerable) combinators **and their
generic `Task<T>` overloads** (array, span, enumerable, and the `WhenAny<T>` pair), the synchronous
`Task.Wait()` / `Task.WaitAll` / `Task.WaitAny(Task[])` waits, the blocking generic
`Task<T>.Result` accessor, the `TaskExtensions.Unwrap` extension methods, and
`Task.ContinueWith(Action<Task>)` to
`Clockwork.Runtime.Tasks.ControlledTask`. Combinators delegate to the real BCL (their completion
is driven by antecedents that complete on the logical thread); synchronous waits **pump the
coordinator loop until completion instead of blocking a physical thread**, then delegate to the
real API to reproduce its exact `AggregateException` semantics, so a synchronous wait or a blocking
`Task<T>.Result` read on incomplete controlled work never deadlocks the scheduler.
`TaskFactory.StartNew` / `TaskFactory<T>.StartNew` are **rejected** at the rewritten call site (they
offload onto a task scheduler / the thread pool) rather than escaping onto a physical thread.

The redirect obeys the same three-state contract as the BCL rule set: outside a simulation every
controlled builder/awaiter/shim is a transparent pass-through to the real BCL; inside a simulation
continuations and waits route through the coordinator; inside a simulation with no registered task
coordinator the shim throws `ControlledTaskServiceMissingException` rather than silently escaping
to the thread pool. `Task.Delay` (virtual timers) and `Task.Run` (thread-pool scheduling) are
**rejected** under simulation with a precise diagnostic rather than modelled with wall-clock time
or an uncontrolled thread — they are owned by later phases.

**Deferred to Phase 6B** (deliberately *not* in this rule set): `Thread`/`ThreadPool`/`Parallel`,
`Monitor`/semaphore/wait-handle public shims, timers and the `Task.Delay` implementation,
cancellation timers, synchronous blocking on `ValueTask`/`ValueTask<T>` (a value task may be
consumed only once, so a blocking drain is unsafe — `await` is the supported controlled path),
generic `Task<T>.ContinueWith<TNewResult>` overloads, `TaskCompletionSource`/`TaskFactory` surfaces
beyond the rejected `StartNew` sites, cross-assembly enforcement, and hardening of exception
filters/handlers against swallowing scheduler-control flow. Phase 6A already prefers explicit
gate/state transitions over control exceptions, so a user `catch` cannot swallow the scheduler; the
remaining filter-level hardening is reported to Phase 6B as a boundary.

Each mode is intended to be strictly additive: an application written for
cooperative mode should continue to work unmodified under controlled, race
exploration, or deep instrumentation mode.

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
  surface enumerated in [`rule-inventory.md`](rule-inventory.md), routing `async`/`await` and
  synchronous waits through the simulation coordinator. Control is claimed **only** for those exact
  signatures; `Task.Delay`/`Task.Run` and `TaskFactory.StartNew` are rejected, and
  `TaskCompletionSource`, generic `Task<T>.ContinueWith`, synchronous `ValueTask` blocking, and
  threading primitives are deferred to Phase 6B.
- **ReadyToRun (R2R) published assemblies** are expected to work for the existing
  kernel and for cooperative/controlled/race-exploration modes, since none of those
  modes require rewriting already-compiled method bodies at load time. This is a
  design intent for Phase 0 scaffolding, not yet independently verified by a
  dedicated R2R test lane.

### Deferred / not yet supported

The following are explicitly **not** supported yet, and are called out here so that
future work sets expectations correctly rather than discovering the limitation as a
surprise:

- **Single-file deployment.** Deep instrumentation that rewrites assemblies at build
  time or hooks module loading at runtime is expected to need adaptation for
  single-file bundles, where assemblies are embedded rather than present as
  discrete files on disk. The Phase 4B build/tool path addresses this only by ordering:
  instrument the IL closure *before* single-file bundling, never after.
- **Trimming.** IL trimming can remove members that instrumentation depends on
  reflecting over or rewriting; deep instrumentation and any reflection-based
  redirection in controlled mode will need explicit trimming annotations or to be
  incompatible with trimming until those annotations exist.
- **NativeAOT.** Build-time IL rewriting after NativeAOT's own compilation step, or
  runtime hooking of a NativeAOT binary (no JIT, no standard profiling APIs in the
  same form), is out of scope until deep instrumentation's design is settled. As with
  R2R and single-file, Phase 4B's build path must run before AOT compilation.
- **Signed (strong-named) assemblies.** Build-time IL rewriting invalidates existing
  assembly signatures. The Phase 4B build/tool path implements an explicit strong-name
  policy (fail, or re-sign with a supplied key, verifying public-key-token consistency
  across the rewritten closure) and detects but does not re-apply Authenticode. What
  remains deferred is *product-mode* (runtime/load-time) handling of signed assemblies.
- **Nondeterministic BCL surface beyond the rule inventory.** Only the exact signatures in
  [`rule-inventory.md`](rule-inventory.md) are rewritten. Documented holes include `Stopwatch`
  instance APIs and `GetElapsedTime(long, long)`; generic crypto helpers `GetItems<T>`/`Shuffle<T>`
  and unlisted `GetString`/`GetHexString` overloads; and `DateTime`/`DateTimeOffset`
  parse/format/convert helpers. Everything outside time/identity/random - task/thread/
  synchronization primitives, timers, collections, Buggify, hosting, and network/HTTP - is out
  of scope for Phase 5.
- **Profiler conflicts.** Deep instrumentation that uses the .NET profiling APIs
  (ICorProfilerCallback) cannot coexist with other profilers (coverage tools, APM
  agents, debuggers attaching a profiler) without explicit multi-profiler
  coordination, which most profiling APIs do not support natively.

These limitations apply only to controlled-mode auto-redirection and deep
instrumentation. Cooperative mode has no such constraints beyond what the .NET
runtime itself imposes, because it requires no rewriting or hooking at all.

## Project layout

The package boundaries scaffolded under `src/` map to the modes above:

| Project | Depends on | Future purpose |
|---|---|---|
| `Clockwork.Runtime` | *(none)* | **Phase 2 (current):** ambient `SimulationExecutionContext`, secure activation, named seed domains, the decision-log/replay contract, and the API policy classification model - see the README's "Deterministic instrumentation runtime plumbing" section. Eventual home of the deterministic kernel itself (currently the root `Clockwork.csproj` / `Clockwork.Simulation` package), which the root package now references. |
| `Clockwork.Instrumentation` | `Clockwork.Runtime` | Contracts and hooks shared by controlled, race exploration, and deep instrumentation modes. |
| `Clockwork.Instrumentation.Build` | `Clockwork.Instrumentation` | **Phase 4B (current):** opt-in MSBuild task + `build/` targets that instrument the resolved output closure out-of-place during `dotnet build`. |
| `Clockwork.Tool` | `Clockwork.Instrumentation` | **Phase 4B (current):** the `clockwork` CLI (`rewrite`, `inspect`) over the shared orchestrator. |
| `Clockwork.Analyzers` | *(none)* | Roslyn diagnostics for cooperative/controlled-mode misuse (direct wall-clock, thread pool, `Random.Shared` usage). |
| `Clockwork.Hosting` | `Clockwork.Runtime` | Integration with `Microsoft.Extensions.Hosting`. |
| `Clockwork.Http` | `Clockwork.Runtime` | `HttpMessageHandler` routed through the simulated network. |
| `Clockwork.Testing` | `Clockwork.Runtime` | Reusable test helpers and scenario builders for consumers. |

As of Phase 2, `Clockwork.Runtime` hosts the runtime plumbing described above (and is
referenced by the root `Clockwork.csproj`). As of Phase 4A/4B, `Clockwork.Instrumentation`
carries the generic IL rewrite engine and `Clockwork.Instrumentation.Build` /
`Clockwork.Tool` expose it through an opt-in build task and the `clockwork` CLI;
`Clockwork.Analyzers`, `Clockwork.Hosting`, `Clockwork.Http`, and `Clockwork.Testing`
remain empty, minimal placeholder projects with no behavior. See the root
`Clockwork.csproj` for the deterministic kernel's current, real implementation. No public
behavior of the existing kernel changed as a result of this or prior scaffolding phases.

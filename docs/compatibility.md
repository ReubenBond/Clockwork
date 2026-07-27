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

Each mode is intended to be strictly additive: an application written for
cooperative mode should continue to work unmodified under controlled, race
exploration, or deep instrumentation mode.

## Platform and deployment contract

### Supported today

- **.NET 10** is the only targeted runtime (`net10.0`, see `Directory.Build.props`).
- **Windows, Linux, and macOS** are all supported for the existing kernel; it has no
  platform-specific dependencies (no P/Invoke, no OS-specific I/O).
- **JIT execution** (the default `dotnet run`/`dotnet test` path) is fully supported.
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
  discrete files on disk.
- **Trimming.** IL trimming can remove members that instrumentation depends on
  reflecting over or rewriting; deep instrumentation and any reflection-based
  redirection in controlled mode will need explicit trimming annotations or to be
  incompatible with trimming until those annotations exist.
- **NativeAOT.** Build-time IL rewriting after NativeAOT's own compilation step, or
  runtime hooking of a NativeAOT binary (no JIT, no standard profiling APIs in the
  same form), is out of scope until deep instrumentation's design is settled.
- **Signed (strong-named) assemblies.** Build-time IL rewriting invalidates existing
  assembly signatures; re-signing requires access to the signing key, which may not
  be available in the build environment doing the rewriting. This needs an explicit
  policy (skip, re-sign, or delay-sign) before deep instrumentation ships.
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
| `Clockwork.Instrumentation.Build` | `Clockwork.Instrumentation` | Build-time IL rewriting for deep instrumentation mode. |
| `Clockwork.Tool` | `Clockwork.Instrumentation` | CLI for running/inspecting instrumented simulations. |
| `Clockwork.Analyzers` | *(none)* | Roslyn diagnostics for cooperative/controlled-mode misuse (direct wall-clock, thread pool, `Random.Shared` usage). |
| `Clockwork.Hosting` | `Clockwork.Runtime` | Integration with `Microsoft.Extensions.Hosting`. |
| `Clockwork.Http` | `Clockwork.Runtime` | `HttpMessageHandler` routed through the simulated network. |
| `Clockwork.Testing` | `Clockwork.Runtime` | Reusable test helpers and scenario builders for consumers. |

As of Phase 2, `Clockwork.Runtime` hosts the runtime plumbing described above (and is
referenced by the root `Clockwork.csproj`); `Clockwork.Instrumentation`,
`Clockwork.Instrumentation.Build`, `Clockwork.Tool`, `Clockwork.Analyzers`,
`Clockwork.Hosting`, `Clockwork.Http`, and `Clockwork.Testing` remain empty, minimal
placeholder projects with no behavior. See the root `Clockwork.csproj` for the
deterministic kernel's current, real implementation. No public behavior of the
existing kernel changed as a result of this or prior scaffolding phases.

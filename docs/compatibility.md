# Compatibility and capability contract

This document describes the intended execution modes for Clockwork's deterministic
instrumentation work and the platform/deployment contract those modes are designed
against. It is a durable product document, not a task plan: it should stay accurate
as the corresponding capabilities are implemented, and it should be updated (not
duplicated) as scope firms up in later phases.

> **Status:** Phase 1A. The deterministic simulation kernel described in the root
> [README](../README.md) exists today (clock, task scheduler, synchronization
> context, seeded random, simulated network, chaos injection), and its
> `RunUntil`/`RunUntilIdle`/`RunForDuration` drive loops now share one internal
> execution engine with a structured, diagnosable outcome type
> (`SimulationExecutionResult` and the `*Detailed` methods - see the README's
> "Detailed execution results and diagnostics" section). This is purely an
> in-process observability improvement to the existing cooperative-mode kernel; it
> does not implement any new execution mode. None of the modes or instrumentation
> capabilities below (controlled, race exploration, deep instrumentation) are
> implemented yet; this document exists to pin down the contract they will be
> designed against, so the package scaffolding under `src/` (see
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
pool/`Random.Shared` usage), rather than requiring IL rewriting.

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
| `Clockwork.Runtime` | *(none)* | Eventual home of the deterministic kernel (currently the root `Clockwork.csproj` / `Clockwork.Simulation` package). |
| `Clockwork.Instrumentation` | `Clockwork.Runtime` | Contracts and hooks shared by controlled, race exploration, and deep instrumentation modes. |
| `Clockwork.Instrumentation.Build` | `Clockwork.Instrumentation` | Build-time IL rewriting for deep instrumentation mode. |
| `Clockwork.Tool` | `Clockwork.Instrumentation` | CLI for running/inspecting instrumented simulations. |
| `Clockwork.Analyzers` | *(none)* | Roslyn diagnostics for cooperative/controlled-mode misuse (direct wall-clock, thread pool, `Random.Shared` usage). |
| `Clockwork.Hosting` | `Clockwork.Runtime` | Integration with `Microsoft.Extensions.Hosting`. |
| `Clockwork.Http` | `Clockwork.Runtime` | `HttpMessageHandler` routed through the simulated network. |
| `Clockwork.Testing` | `Clockwork.Runtime` | Reusable test helpers and scenario builders for consumers. |

As of Phase 0 these are empty, minimal placeholder projects with no behavior; see
the root `Clockwork.csproj` for the current, real implementation. No public behavior
of the existing kernel changes as a result of this scaffolding.

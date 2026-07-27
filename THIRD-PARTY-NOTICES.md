# Third-party notices

This file documents third-party material referenced by, or under evaluation for
adaptation into, Clockwork, and the licensing policy for each. As of this writing,
**no third-party source code has been copied into this repository.** This document
exists ahead of that work so the policy is settled before any adaptation happens,
per the roadmap described in [docs/compatibility.md](docs/compatibility.md).

If and when code or tests are adapted from any of the projects below, this file
must be updated in the same change with:

- The specific files/functions adapted and their origin (repository, path, commit).
- The original license text (or a pointer to it) reproduced per that license's
  attribution requirements.
- Any modifications made, clearly distinguished from the original.

## Projects under evaluation

### Microsoft Coyote

- **License:** MIT
- **Repository:** https://github.com/microsoft/coyote
- **Why it's relevant:** Coyote implements systematic concurrency testing
  (controlled scheduling, race exploration) for .NET, overlapping with Clockwork's
  planned "controlled mode" and "race exploration mode" (see
  [docs/compatibility.md](docs/compatibility.md)). Its test suite is also a useful
  reference for characterizing scheduler edge cases.
- **Adaptation policy:** MIT permits reuse with attribution and retention of the
  copyright/license notice. Any Coyote-derived source or test adapted into this
  repository must retain Coyote's copyright notice in the adapted file (or in this
  document, referencing the file) and note the adaptation here. Wholesale copying
  without attribution is not permitted; substantial adaptation should credit Coyote
  in code comments at the adaptation site as well.
- **Adapted material (Phase 4A):** The following files in
  `src/Clockwork.Instrumentation/Rewriting/` adapt portions of Coyote's Mono.Cecil
  rewriting engine and carry Coyote's copyright/license header at the top of the
  file, per the policy above:
  - `RewritePass.cs` — adapts the IL visitor traversal
    (`VisitAssembly`/`Module`/`Type`/`Method`/`Instruction`), the `Replace` helper
    that fixes up branch targets and exception-handler boundaries, and the
    `SimplifyMacros`/`OptimizeMacros` offset-fix pattern from Coyote's
    `Source/Test/Rewriting/Passes/Pass.cs` and
    `Source/Test/Rewriting/Passes/Rewriting/RewritingPass.cs`.
  - `AssemblyRewriteContext.cs` — adapts Coyote's assembly load/write, symbol
    detection, and rewrite-signature-attribute handling (from Coyote's
    `AssemblyInfo`/rewriting-engine assembly handling).

  Clockwork-specific changes are noted in each file header and are developed in
  commits separate from the mechanical adaptation.

- **Adapted material (Phase 6A):** Phase 6A's controlled task/async machinery
  (`src/Clockwork.Runtime/Tasks/` and `.../Tasks/CompilerServices/`, and the
  member-aware substitution pass in `src/Clockwork.Instrumentation/Rewriting/`) is a
  **design-level adaptation** of Coyote's controlled-task model — its
  `Microsoft.Coyote.Runtime.CompilerServices` builder/awaiter types
  (`AsyncTaskMethodBuilder`, `TaskAwaiter`, `ConfiguredTaskAwaitable`, `YieldAwaitable`,
  the `AsyncValueTaskMethodBuilder`, `ValueTaskAwaiter`, and `ConfiguredValueTaskAwaitable`
  value-task equivalents, and their awaiters), its `Microsoft.Coyote.Runtime.CompilerServices`
  rewriting pass
  that retargets compiler-generated state machines, and the shape of its controlled
  task/awaiter tests. **No Coyote source was copied verbatim into these files:** unlike
  Coyote's from-scratch task reimplementation, Clockwork's controlled builders/awaiters
  are thin value-type wrappers that forward to the real BCL builder/awaiter and only
  redirect *where the continuation is scheduled* (to Clockwork's
  `ISimulationTaskCoordinator` rather than Coyote's `CoyoteRuntime`). The conformance
  tests are original (they compile and rewrite real state machines with Roslyn) rather
  than ports of Coyote's test source. Because the approach — not literal source — was
  adapted, these files do not carry a Coyote copyright header; this entry records the
  design lineage per the attribution policy above. Control parity is claimed **only**
  for the exact signatures enumerated in
  [docs/rule-inventory.md](docs/rule-inventory.md), not for Coyote's full surface.

- **Adapted material (Phase 6B):** Phase 6B's exception-handler hardening pass,
  `src/Clockwork.Instrumentation/Rewriting/ExceptionHardeningRewritingPass.cs`, is a
  **source-level adaptation** of Coyote's
  `Source/Test/Rewriting/Passes/Rewriting/ExceptionFilterRewritingPass.cs` and carries a
  Coyote copyright/license attribution header at the top of the file, per the policy
  above. It reuses Coyote's algorithm for deciding which handlers to instrument (only
  broad `catch (object)`/`catch (Exception)` blocks and exception `filter`s; skipping
  finally/fault blocks, rethrow-only handlers, and compiler-generated
  async-state-machine `SetException` handlers) and Coyote's guard-injection shape
  (`dup; call guard` at the handler start with the adjacent-handler boundary fix-up).
  Clockwork-specific changes are noted in the file header: the guard is resolved through
  Clockwork's shared replacement resolver rather than Coyote's `typeof`-based import; the
  injected guard rethrows Clockwork's internal `ControlledOperationAbortSignal` rather
  than Coyote's `ExecutionCanceledException`/`ThreadInterruptedException`; the
  async-state-machine detection also recognises Clockwork's substituted controlled
  builder types; and every hardened handler is recorded as a manifest transformation.
  The parity comparison of Clockwork's Phase 6B thread/thread-pool/task/Parallel surface
  against Coyote is enumerated in [docs/coyote-parity.md](docs/coyote-parity.md). The
  cross-assembly uncontrolled-task detection pass
  (`CrossAssemblyTaskDetectionPass.cs`) and the controlled `Thread`, `Task.Run`,
  `TaskFactory`, `ThreadPool`, and `Parallel` runtime surfaces are original Clockwork
  code informed by Coyote's model but not copied from its source.

### Mono.Cecil

- **License:** MIT
- **Repository:** https://github.com/jbevain/cecil
- **Why it's relevant:** Cecil is the leading .NET IL manipulation library and the
  most likely dependency for "deep instrumentation mode" build-time IL rewriting
  (`Clockwork.Instrumentation.Build`, see [docs/compatibility.md](docs/compatibility.md)).
  As of Phase 4A it is a **package dependency** of `Clockwork.Instrumentation`
  (`Mono.Cecil` 0.11.6), used by the rewrite engine. It is not referenced by the
  runtime or simulation projects.
- **Adaptation policy:** Cecil is added as a NuGet **package** reference, not source
  adaptation, so it only requires standard NuGet license acknowledgment (MIT, covered
  by the package's `PackageLicenseExpression` and consumer tooling) plus this entry
  noting the dependency and its MIT license. No Cecil source has been copied or
  adapted into this repository; the Coyote-adapted files above use Cecil purely
  through its public API. If any Cecil source is ever copied or adapted directly, the
  same attribution requirements as Coyote above apply.

### FoundationDB

- **License:** Apache License 2.0
- **Repository:** https://github.com/apple/foundationdb
- **Why it's relevant:** FoundationDB's deterministic simulation testing approach
  (a simulated network/clock driving the same binary used in production) is a
  direct architectural precedent for Clockwork's kernel and its planned
  instrumentation modes.
- **Adaptation policy:** Apache-2.0 requires: retaining copyright/license/NOTICE
  text, stating any changes made to adapted files, and including a copy of the
  Apache-2.0 license for any adapted files. Because Apache-2.0 and MIT are not
  compatible in the same direction for combined works in all cases (Apache-2.0 code
  can be included with attribution, but relicensing Apache-2.0 code under this
  repository's MIT license is not permitted), any FoundationDB-derived source or
  test must be kept identifiably separate (clearly marked file header and license
  pointer) rather than silently merged into MIT-licensed files in this repository.
  Where practical, prefer reimplementing the *approach* (which is not copyrightable)
  over adapting FoundationDB's literal source.

## General policy

- Phase 4A adds the `Mono.Cecil` (0.11.6, MIT) package dependency to
  `Clockwork.Instrumentation`, and the `Microsoft.CodeAnalysis.CSharp` (Roslyn, MIT)
  package to `Clockwork.Instrumentation.Tests` (used only to compile fixture
  assemblies at test time). It also adapts two Coyote source files as recorded above.
  The runtime and simulation projects remain free of any IL-rewriting dependency.
- Before adapting source from any of the projects above, or from any other
  third-party project, update this document in the same pull request as the
  adaptation.
- Clockwork itself is licensed under the [MIT License](LICENSE); nothing in this
  document changes that for Clockwork's own original code.

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

### Mono.Cecil

- **License:** MIT
- **Repository:** https://github.com/jbevain/cecil
- **Why it's relevant:** Cecil is the leading .NET IL manipulation library and the
  most likely dependency for "deep instrumentation mode" build-time IL rewriting
  (`Clockwork.Instrumentation.Build`, see [docs/compatibility.md](docs/compatibility.md)).
  It is **not** added as a dependency in this phase (Phase 0 explicitly defers deep
  instrumentation and the Cecil dependency it requires).
- **Adaptation policy:** When Cecil is added as a package dependency, that is a
  binary/package reference, not source adaptation, and only requires standard NuGet
  license acknowledgment (already covered by `PackageLicenseExpression`/consumer
  tooling) plus an entry here noting the dependency and its MIT license. If any
  Cecil source is ever copied or adapted directly (rather than referenced as a
  package), the same attribution requirements as Coyote above apply.

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

- This phase (Phase 0) adds no third-party source code, and no new third-party
  package dependencies beyond what already existed in `Directory.Packages.props`
  (`Microsoft.Extensions.Logging.Abstractions`, xunit.v3.mtp-v2 and its transitive
  test-platform packages).
- Before adapting source from any of the projects above, or from any other
  third-party project, update this document in the same pull request as the
  adaptation.
- Clockwork itself is licensed under the [MIT License](LICENSE); nothing in this
  document changes that for Clockwork's own original code.

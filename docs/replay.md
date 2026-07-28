# Replay and schedule exploration

Clockwork persists controlled executions as canonical UTF-8 JSON. The format name is
`clockwork.replay`; the current schema version is `1`.

## Artifact contract

An artifact records:

- root and schedule seeds, scheduling strategy, strategy options, and hard execution bounds;
- Clockwork/runtime compatibility plus optional instrumentation manifest, rule-set, and assembly hashes;
- ordered scheduler choices, resource-winner and wait-resolution decisions, virtual deadlines, and race scheduling points;
- completion, fault, cancellation, race, deadlock, bound, or aborted outcome with a stable failure identity;
- terminal operation/resource wait graph, pending timer queue, race pair, and deadlock cycles.

JSON property order and number/string formatting are stable. Decision sequences are contiguous and
ordered. Unknown optional properties are ignored within schema version 1. Incompatible format/schema
versions require a reader update and fail before execution.

Artifacts are limited to 16 MiB, 250,000 decisions, 250,000 race points, 4,096 assembly identities,
64 scheduler options, and 8,192 UTF-16 characters per string. Corrupt, oversized, truncated, and
incompatible artifacts are rejected.

Process arguments, environment variables, stack traces, arbitrary object values, caller work
descriptions, and source paths are not retained by default. API callers can explicitly retain bounded
diagnostic messages and source paths.

## Scenario harness

The CLI executes only a public `IReplayScenario` type in an explicitly supplied assembly:

```csharp
public sealed class TransferScenario : IReplayScenario
{
    public void Configure(ControlledOperationScheduler scheduler)
    {
        scheduler.Schedule("sender", scheduler.Yield);
        scheduler.Schedule("receiver", () => { });
    }
}
```

Loading this harness executes application code. Clockwork does not scan for scenarios, infer a command,
or launch an arbitrary child process.

## Commands

```powershell
clockwork record `
  --assembly .\tests.dll `
  --scenario-type Tests.TransferScenario `
  --artifact .\artifacts\transfer.cwr.json `
  --seed 123 --schedule-seed 7 --strategy seeded-random

clockwork replay .\artifacts\transfer.cwr.json `
  --assembly .\tests.dll `
  --scenario-type Tests.TransferScenario

clockwork explore `
  --assembly .\tests.dll `
  --scenario-type Tests.TransferScenario `
  --output .\artifacts `
  --seed 123 --schedule-seed 1 --count 100 --max-failures 1

clockwork minimize .\artifacts\transfer.cwr.json `
  --assembly .\tests.dll `
  --scenario-type Tests.TransferScenario `
  --output .\artifacts\transfer.min.cwr.json

clockwork trace show .\artifacts\transfer.min.cwr.json
clockwork trace show .\artifacts\transfer.min.cwr.json --json
```

Supply `--manifest <closure-manifest>` when the scenario uses an instrumented closure. Replay compares
the manifest, rule-set, mode, assembly hashes, Clockwork runtime version, and .NET compatibility before
executing the scenario.

Exit code `6` reports a reproduced scenario failure, `7` reports artifact/compatibility/divergence
failure, and `8` reports minimization failure. Existing usage, configuration, closure,
instrumentation, and I/O codes remain unchanged.

## Exploration and minimization

Exploration is serial. The root seed remains fixed while schedule seeds increase deterministically from
`FirstScheduleSeed`. Stop controls include iteration count, failure count, per-iteration
step bound, cancellation, and a between-iteration wall-clock safety bound. The result contains stable
iteration ids and outcome counts and retains the smallest artifact for each failure identity.

The minimizer removes decision subsequences and tries discrete scheduling/resource alternatives. Each
candidate runs through exact replay. Compatibility rejection, first decision divergence, surplus or
truncated streams, and a changed failure identity reject the candidate. Progress is ordered and bounded
by attempt count and optional wall-clock time.

## Test integration

`Clockwork.Testing.ReplayTestFixture` derives its default root seed with
`SimulationSeed.FromStrings(testClass, testMethod)`. Failed runs write an artifact and expose it through
`ReplayTestResult.Attachments`, `ToFailureMessage()`, and `GetReplayCommand(...)`.

Environment variables:

| Variable | Meaning |
|---|---|
| `CLOCKWORK_REPLAY_ARTIFACT` | Replay this complete artifact instead of recording. |
| `CLOCKWORK_ROOT_SEED` | Override the test root seed. |
| `CLOCKWORK_SCHEDULE_SEED` | Override the schedule seed. |
| `CLOCKWORK_ARTIFACT_DIRECTORY` | Directory for failed-test artifacts. |

The helper is framework-neutral and works with xUnit/Microsoft.Testing.Platform without coupling the
runtime to either framework.

## Limits

- CLI execution requires an explicit `IReplayScenario`; generic production-process launch is not provided.
- Random and identity values derived solely from stable seed domains do not need individual records.
  A custom nondeterministic choice must be routed through the decision log to become replayable.
- The minimizer preserves failures that exact replay can validate; it does not mutate application state
  or synthesize unrecorded inputs.
- Hosting, transport models, profiler/ReJIT interception, and fault-injection expansion are outside this capability.

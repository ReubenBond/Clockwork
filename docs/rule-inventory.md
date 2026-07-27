# Built-in rewrite rule inventory

<!-- Generated from Clockwork.Instrumentation.Rules.BuiltIn.RuleInventoryDocument.Render().
     Do not edit by hand; a test verifies this file matches the shipped rule set. -->

This is the exact, exhaustive surface the built-in rule sets redirect. Every other API is **not** rewritten. Outside an active simulation each shim runs the real BCL API unchanged; under an active simulation with no registered runtime environment the shim fails explicitly rather than fall back to real time, randomness, or an uncontrolled task.

# Deterministic BCL rule set

Rule set id: `clockwork.bcl.deterministic`  
Version: `1.0.0`  
Shim assembly: `Clockwork.Runtime`

## Clock family

Policy: **Controlled**. Wall-clock, offset-clock, monotonic timestamp, and tick-counter reads dispatch to the node's simulated clock. Local-time APIs honour the configured simulation time zone; tick counters wrap with correct `int`/`long` semantics.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.datetime.now` | `System.DateTime::get_Now()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetNow()` | Controlled |
| `clockwork.bcl.datetime.utcnow` | `System.DateTime::get_UtcNow()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetUtcNow()` | Controlled |
| `clockwork.bcl.datetime.today` | `System.DateTime::get_Today()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetToday()` | Controlled |
| `clockwork.bcl.datetimeoffset.now` | `System.DateTimeOffset::get_Now()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetOffsetNow()` | Controlled |
| `clockwork.bcl.datetimeoffset.utcnow` | `System.DateTimeOffset::get_UtcNow()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetOffsetUtcNow()` | Controlled |
| `clockwork.bcl.stopwatch.gettimestamp` | `System.Diagnostics.Stopwatch::GetTimestamp()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetTimestamp()` | Controlled |
| `clockwork.bcl.stopwatch.getelapsedtime` | `System.Diagnostics.Stopwatch::GetElapsedTime(System.Int64)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetElapsedTime(System.Int64)` | Controlled |
| `clockwork.bcl.environment.tickcount` | `System.Environment::get_TickCount()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetTickCount()` | Controlled |
| `clockwork.bcl.environment.tickcount64` | `System.Environment::get_TickCount64()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicClock::GetTickCount64()` | Controlled |

## Identity family

Policy: **Controlled**. GUIDs draw deterministic bytes while preserving RFC 4122 variant and version. `CreateVersion7` encodes the simulated UTC millisecond timestamp in the first 48 bits; repeated calls at the same simulated instant share that timestamp (no monotonicity guarantee beyond the BCL contract).

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.guid.newguid` | `System.Guid::NewGuid()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicGuid::NewGuid()` | Controlled |
| `clockwork.bcl.guid.createversion7` | `System.Guid::CreateVersion7()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicGuid::CreateVersion7()` | Controlled |
| `clockwork.bcl.guid.createversion7.timestamp` | `System.Guid::CreateVersion7(System.DateTimeOffset)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicGuid::CreateVersion7(System.DateTimeOffset)` | Controlled |

## Random family

Policy: **Controlled**. `Random.Shared` and unseeded `new Random()` become per-node deterministic streams isolated from the scheduler/network/Buggify seed domains; explicitly seeded `new Random(int)` preserves the caller's seed exactly, matching normal BCL behaviour.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.random.shared` | `System.Random::get_Shared()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicRandom::GetShared()` | Controlled |
| `clockwork.bcl.random.ctor.unseeded` | `new System.Random()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicRandom::CreateUnseeded()` | Controlled |
| `clockwork.bcl.random.ctor.seeded` | `new System.Random(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicRandom::CreateSeeded(System.Int32)` | Controlled |

## Crypto family

Policy: **Rejected**. Static entropy APIs are redirected to a policy shim. The default under simulation is a precise rejected-call diagnostic; a test-only opt-in can substitute deterministic-insecure bytes. Production security semantics are never changed.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.rng.create` | `System.Security.Cryptography.RandomNumberGenerator::Create()` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::Create()` | Rejected |
| `clockwork.bcl.rng.create.named` | `System.Security.Cryptography.RandomNumberGenerator::Create(System.String)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::Create(System.String)` | Rejected |
| `clockwork.bcl.rng.fill` | `System.Security.Cryptography.RandomNumberGenerator::Fill(System.Span`1<System.Byte>)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::Fill(System.Span`1<System.Byte>)` | Rejected |
| `clockwork.bcl.rng.getbytes.count` | `System.Security.Cryptography.RandomNumberGenerator::GetBytes(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetBytes(System.Int32)` | Rejected |
| `clockwork.bcl.rng.getint32.exclusive` | `System.Security.Cryptography.RandomNumberGenerator::GetInt32(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetInt32(System.Int32)` | Rejected |
| `clockwork.bcl.rng.getint32.range` | `System.Security.Cryptography.RandomNumberGenerator::GetInt32(System.Int32,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetInt32(System.Int32,System.Int32)` | Rejected |
| `clockwork.bcl.rng.gethexstring.span` | `System.Security.Cryptography.RandomNumberGenerator::GetHexString(System.Span`1<System.Char>,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetHexString(System.Span`1<System.Char>,System.Boolean)` | Rejected |
| `clockwork.bcl.rng.gethexstring.length` | `System.Security.Cryptography.RandomNumberGenerator::GetHexString(System.Int32,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetHexString(System.Int32,System.Boolean)` | Rejected |
| `clockwork.bcl.rng.getstring` | `System.Security.Cryptography.RandomNumberGenerator::GetString(System.ReadOnlySpan`1<System.Char>,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Shims.DeterministicCryptoRandom::GetString(System.ReadOnlySpan`1<System.Char>,System.Int32)` | Rejected |

# Controlled task rule set

Rule set id: `clockwork.tasks.controlled`  
Version: `1.0.0`  
Shim assembly: `Clockwork.Runtime`

## TaskCombinators family

Policy: **Controlled**. `Task.WhenAll`/`WhenAny` (the non-generic `Task[]`, `IEnumerable<Task>`, .NET 9+ params `ReadOnlySpan<Task>`, and two-argument overloads) redirect to controlled combinators. Completion and the returned winner become a deterministic function of when the antecedents complete on the logical thread instead of a physical thread-pool race.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.whenall.array` | `System.Threading.Tasks.Task::WhenAll(System.Threading.Tasks.Task[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.whenall.span` | `System.Threading.Tasks.Task::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenall.enumerable` | `System.Threading.Tasks.Task::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenany.array` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.whenany.span` | `System.Threading.Tasks.Task::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenany.pair` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task,System.Threading.Tasks.Task)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task,System.Threading.Tasks.Task)` | Controlled |
| `clockwork.tasks.whenany.enumerable` | `System.Threading.Tasks.Task::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | Controlled |

## TaskSynchronization family

Policy: **Controlled**. Blocking `Task.Wait()`, `Task.WaitAll`, and `Task.WaitAny` redirect to controlled waits that pump the deterministic loop rather than blocking a physical thread; a never-satisfiable wait surfaces as a precise deadlock diagnostic instead of hanging the scheduler.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.wait.instance` | `System.Threading.Tasks.Task::Wait()` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Wait(System.Threading.Tasks.Task)` | Controlled |
| `clockwork.tasks.waitall.array` | `System.Threading.Tasks.Task::WaitAll(System.Threading.Tasks.Task[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WaitAll(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.waitany.array` | `System.Threading.Tasks.Task::WaitAny(System.Threading.Tasks.Task[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WaitAny(System.Threading.Tasks.Task[])` | Controlled |

## TaskContinuations family

Policy: **Controlled**. `Task.ContinueWith(Action<Task>)` redirects so the continuation is scheduled on the controlled coordinator and runs on the logical thread after the antecedent completes.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.continuewith.action` | `System.Threading.Tasks.Task::ContinueWith(System.Action`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::ContinueWith(System.Threading.Tasks.Task,System.Action`1<System.Threading.Tasks.Task>)` | Controlled |

## TaskDeferred family

Policy: **Rejected**. `Task.Delay` (virtual timers, Phase 8) and `Task.Run` (thread-pool offload, Phase 6B) are rejected under simulation with a precise diagnostic rather than silently using wall time or a real thread-pool thread. Outside simulation they run the real BCL API unchanged.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.delay.milliseconds` | `System.Threading.Tasks.Task::Delay(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Delay(System.Int32)` | Rejected |
| `clockwork.tasks.run.action` | `System.Threading.Tasks.Task::Run(System.Action)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Run(System.Action)` | Rejected |

## AsyncMachinery family

Policy: **Controlled**. The compiler-generated builder and awaiter types of an `async` state machine (`AsyncTaskMethodBuilder`, `TaskAwaiter`, `ConfiguredTaskAwaitable`/`YieldAwaitable` and their awaiters, generic and non-generic) are substituted onto Clockwork's controlled equivalents by the member-aware pass, and `Task.Yield()` redirects to the controlled yield. Every awaited continuation is scheduled through the simulation coordinator instead of the thread pool, and `ConfigureAwait(false)` stays controlled while preserving normal semantics outside simulation.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.builder.task` | `System.Runtime.CompilerServices.AsyncTaskMethodBuilder` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledAsyncTaskMethodBuilder` | Controlled |
| `clockwork.tasks.builder.task.generic` | `System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledAsyncTaskMethodBuilder`1` | Controlled |
| `clockwork.tasks.awaiter.task` | `System.Runtime.CompilerServices.TaskAwaiter` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledTaskAwaiter` | Controlled |
| `clockwork.tasks.awaiter.task.generic` | `System.Runtime.CompilerServices.TaskAwaiter`1` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledTaskAwaiter`1` | Controlled |
| `clockwork.tasks.configured.awaitable` | `System.Runtime.CompilerServices.ConfiguredTaskAwaitable` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledConfiguredTaskAwaitable` | Controlled |
| `clockwork.tasks.configured.awaitable.generic` | `System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledConfiguredTaskAwaitable`1` | Controlled |
| `clockwork.tasks.configured.awaiter` | `System.Runtime.CompilerServices.ConfiguredTaskAwaitable/ConfiguredTaskAwaiter` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledConfiguredTaskAwaiter` | Controlled |
| `clockwork.tasks.configured.awaiter.generic` | `System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1/ConfiguredTaskAwaiter` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledConfiguredTaskAwaiter`1` | Controlled |
| `clockwork.tasks.yield.awaitable` | `System.Runtime.CompilerServices.YieldAwaitable` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledYieldAwaitable` | Controlled |
| `clockwork.tasks.yield.awaiter` | `System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledYieldAwaiter` | Controlled |
| `clockwork.tasks.yield.call` | `System.Threading.Tasks.Task::Yield()` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Yield()` | Controlled |

## Documented holes (not rewritten in these rule sets)

These nondeterministic or entropy-drawing surfaces are intentionally **not** covered and
remain real BCL calls even under simulation:

- `Stopwatch` instance APIs (`Start`/`Stop`/`Restart`/`Elapsed`/`ElapsedMilliseconds`/`ElapsedTicks`) and the `GetElapsedTime(long, long)` overload.
- Generic cryptographic helpers `RandomNumberGenerator.GetItems<T>` and `Shuffle<T>`, and any `GetString`/`GetHexString` overloads beyond those listed above.
- `DateTime`/`DateTimeOffset` parsing/formatting and any culture-, timezone-, or kind-conversion helpers other than the `Now`/`UtcNow`/`Today` clocks above.
- Generic `Task<TResult>` combinator overloads (`WhenAll<T>`/`WhenAny<T>`), the `Task<T>.Result` accessor, `ValueTask`, `TaskCompletionSource`, `TaskFactory`, and the compiler-generated builder/awaiter types. These are served by the `Clockwork.Runtime` controlled-task engine but are **not** in the shipped rule set: matching them requires the member-aware / generic-arity substitution pass deferred to Phase 6B.
- Thread/`ThreadPool`/`Parallel`, `Monitor`/semaphores/wait handles, timers and the `Task.Delay` implementation, and cancellation timers. These are Phase 6B / Phase 8 scope.

Determinism is claimed **only** for the exact rules tabulated above.

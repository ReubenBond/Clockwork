# Built-in rewrite rule inventory

<!-- Generated from Clockwork.Instrumentation.Rules.BuiltIn.RuleInventoryDocument.Render().
     Do not edit by hand; a test verifies this file matches the shipped rule set. -->

This is the exact, exhaustive surface the built-in rule sets redirect. Every other API is **not** rewritten. Instrumented closure binaries are simulation/test artifacts: every Controlled entry point requires an active Clockwork simulation, and an active simulation with no registered runtime service fails explicitly rather than use real time, randomness, or an uncontrolled task. Uninstrumented production binaries retain ordinary BCL behavior.

# Deterministic BCL rule set

Rule set id: `clockwork.bcl.deterministic`
Version: `3.0.0`
Shim assembly: `Clockwork`

## Clock family

Policy: **Controlled**. Wall-clock, offset-clock, monotonic timestamp, and tick-counter reads dispatch to the node's simulated clock. Local-time APIs honour the configured simulation time zone; tick counters wrap with correct `int`/`long` semantics.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.datetime.now` | `System.DateTime::get_Now()` | `Clockwork!Clockwork.Shims.System.ControlledDateTime::GetNow()` | Controlled |
| `clockwork.bcl.datetime.utcnow` | `System.DateTime::get_UtcNow()` | `Clockwork!Clockwork.Shims.System.ControlledDateTime::GetUtcNow()` | Controlled |
| `clockwork.bcl.datetime.today` | `System.DateTime::get_Today()` | `Clockwork!Clockwork.Shims.System.ControlledDateTime::GetToday()` | Controlled |
| `clockwork.bcl.datetimeoffset.now` | `System.DateTimeOffset::get_Now()` | `Clockwork!Clockwork.Shims.System.ControlledDateTimeOffset::GetNow()` | Controlled |
| `clockwork.bcl.datetimeoffset.utcnow` | `System.DateTimeOffset::get_UtcNow()` | `Clockwork!Clockwork.Shims.System.ControlledDateTimeOffset::GetUtcNow()` | Controlled |
| `clockwork.bcl.stopwatch.gettimestamp` | `System.Diagnostics.Stopwatch::GetTimestamp()` | `Clockwork!Clockwork.Shims.System.Diagnostics.ControlledStopwatch::GetTimestamp()` | Controlled |
| `clockwork.bcl.stopwatch.getelapsedtime` | `System.Diagnostics.Stopwatch::GetElapsedTime(System.Int64)` | `Clockwork!Clockwork.Shims.System.Diagnostics.ControlledStopwatch::GetElapsedTime(System.Int64)` | Controlled |
| `clockwork.bcl.environment.tickcount` | `System.Environment::get_TickCount()` | `Clockwork!Clockwork.Shims.System.ControlledEnvironment::GetTickCount()` | Controlled |
| `clockwork.bcl.environment.tickcount64` | `System.Environment::get_TickCount64()` | `Clockwork!Clockwork.Shims.System.ControlledEnvironment::GetTickCount64()` | Controlled |

## Identity family

Policy: **Controlled**. GUIDs draw deterministic bytes while preserving RFC 4122 variant and version. `CreateVersion7` encodes the simulated UTC millisecond timestamp in the first 48 bits; repeated calls at the same simulated instant share that timestamp (no monotonicity guarantee beyond the BCL contract).

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.guid.newguid` | `System.Guid::NewGuid()` | `Clockwork!Clockwork.Shims.System.ControlledGuid::NewGuid()` | Controlled |
| `clockwork.bcl.guid.createversion7` | `System.Guid::CreateVersion7()` | `Clockwork!Clockwork.Shims.System.ControlledGuid::CreateVersion7()` | Controlled |
| `clockwork.bcl.guid.createversion7.timestamp` | `System.Guid::CreateVersion7(System.DateTimeOffset)` | `Clockwork!Clockwork.Shims.System.ControlledGuid::CreateVersion7(System.DateTimeOffset)` | Controlled |

## Random family

Policy: **Controlled**. `Random.Shared` and unseeded `new Random()` become per-node deterministic streams isolated from the scheduler/network/Buggify seed domains; explicitly seeded `new Random(int)` preserves the caller's seed exactly. Stable seed derivation uses `SimulationStableHash`.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.random.shared` | `System.Random::get_Shared()` | `Clockwork!Clockwork.Shims.System.ControlledRandom::GetShared()` | Controlled |
| `clockwork.bcl.random.ctor.unseeded` | `new System.Random()` | `Clockwork!Clockwork.Shims.System.ControlledRandom::CreateUnseeded()` | Controlled |
| `clockwork.bcl.random.ctor.seeded` | `new System.Random(System.Int32)` | `Clockwork!Clockwork.Shims.System.ControlledRandom::CreateSeeded(System.Int32)` | Controlled |

## Crypto family

Policy: **Controlled**. Static random APIs are redirected to `ControlledRandomNumberGenerator` and draw from a deterministic, per-node non-cryptographic stream. Uninstrumented production binaries retain ordinary BCL behavior.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.bcl.rng.create` | `System.Security.Cryptography.RandomNumberGenerator::Create()` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::Create()` | Controlled |
| `clockwork.bcl.rng.create.named` | `System.Security.Cryptography.RandomNumberGenerator::Create(System.String)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::Create(System.String)` | Controlled |
| `clockwork.bcl.rng.fill` | `System.Security.Cryptography.RandomNumberGenerator::Fill(System.Span`1<System.Byte>)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::Fill(System.Span`1<System.Byte>)` | Controlled |
| `clockwork.bcl.rng.getbytes.count` | `System.Security.Cryptography.RandomNumberGenerator::GetBytes(System.Int32)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::GetBytes(System.Int32)` | Controlled |
| `clockwork.bcl.rng.getint32.exclusive` | `System.Security.Cryptography.RandomNumberGenerator::GetInt32(System.Int32)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::GetInt32(System.Int32)` | Controlled |
| `clockwork.bcl.rng.getint32.range` | `System.Security.Cryptography.RandomNumberGenerator::GetInt32(System.Int32,System.Int32)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::GetInt32(System.Int32,System.Int32)` | Controlled |
| `clockwork.bcl.rng.getitems.span` | `System.Security.Cryptography.RandomNumberGenerator::GetItems(System.ReadOnlySpan`1<!!0>,System.Span`1<!!0>)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::GetItems(System.ReadOnlySpan`1<T>,System.Span`1<T>)` | Controlled |
| `clockwork.bcl.rng.getitems.length` | `System.Security.Cryptography.RandomNumberGenerator::GetItems(System.ReadOnlySpan`1<!!0>,System.Int32)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::GetItems(System.ReadOnlySpan`1<T>,System.Int32)` | Controlled |
| `clockwork.bcl.rng.gethexstring.span` | `System.Security.Cryptography.RandomNumberGenerator::GetHexString(System.Span`1<System.Char>,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::GetHexString(System.Span`1<System.Char>,System.Boolean)` | Controlled |
| `clockwork.bcl.rng.gethexstring.length` | `System.Security.Cryptography.RandomNumberGenerator::GetHexString(System.Int32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::GetHexString(System.Int32,System.Boolean)` | Controlled |
| `clockwork.bcl.rng.getstring` | `System.Security.Cryptography.RandomNumberGenerator::GetString(System.ReadOnlySpan`1<System.Char>,System.Int32)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::GetString(System.ReadOnlySpan`1<System.Char>,System.Int32)` | Controlled |
| `clockwork.bcl.rng.shuffle` | `System.Security.Cryptography.RandomNumberGenerator::Shuffle(System.Span`1<!!0>)` | `Clockwork!Clockwork.Shims.System.Security.Cryptography.ControlledRandomNumberGenerator::Shuffle(System.Span`1<T>)` | Controlled |

# Controlled task rule set

Rule set id: `clockwork.tasks.controlled`
Version: `3.0.0`
Shim assembly: `Clockwork`

## TaskCombinators family

Policy: **Controlled**. `Task.WhenAll`/`WhenAny` (the non-generic `Task[]`, `IEnumerable<Task>`, .NET 9+ params `ReadOnlySpan<Task>`, and two-argument overloads, plus their generic `Task<TResult>` counterparts) and the `TaskExtensions.Unwrap` extension methods redirect to controlled combinators. Completion and the returned winner become a deterministic function of when the antecedents complete on the logical thread instead of a physical thread-pool race.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.whenall.array` | `System.Threading.Tasks.Task::WhenAll(System.Threading.Tasks.Task[])` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAll(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.whenall.span` | `System.Threading.Tasks.Task::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenall.enumerable` | `System.Threading.Tasks.Task::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenany.array` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task[])` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.whenany.span` | `System.Threading.Tasks.Task::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenany.pair` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task,System.Threading.Tasks.Task)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task,System.Threading.Tasks.Task)` | Controlled |
| `clockwork.tasks.whenany.enumerable` | `System.Threading.Tasks.Task::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenall.generic.array` | `System.Threading.Tasks.Task::WhenAll(System.Threading.Tasks.Task`1<!!0>[])` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAll(System.Threading.Tasks.Task`1<TResult>[])` | Controlled |
| `clockwork.tasks.whenall.generic.span` | `System.Threading.Tasks.Task::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.whenall.generic.enumerable` | `System.Threading.Tasks.Task::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.whenany.generic.array` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task`1<!!0>[])` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task`1<TResult>[])` | Controlled |
| `clockwork.tasks.whenany.generic.span` | `System.Threading.Tasks.Task::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.whenany.generic.pair` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task`1<!!0>,System.Threading.Tasks.Task`1<!!0>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task`1<TResult>,System.Threading.Tasks.Task`1<TResult>)` | Controlled |
| `clockwork.tasks.whenany.generic.enumerable` | `System.Threading.Tasks.Task::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.unwrap` | `System.Threading.Tasks.TaskExtensions::Unwrap(System.Threading.Tasks.Task`1<System.Threading.Tasks.Task>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Unwrap(System.Threading.Tasks.Task`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.unwrap.generic` | `System.Threading.Tasks.TaskExtensions::Unwrap(System.Threading.Tasks.Task`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Unwrap(System.Threading.Tasks.Task`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |

## TaskSynchronization family

Policy: **Controlled**. Blocking `Task.Wait()`, `Task.WaitAll`, and `Task.WaitAny` redirect to controlled waits that pump the deterministic loop rather than blocking a physical thread; a never-satisfiable wait surfaces as a precise deadlock diagnostic instead of hanging the scheduler.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.wait.instance` | `System.Threading.Tasks.Task::Wait()` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Wait(System.Threading.Tasks.Task)` | Controlled |
| `clockwork.tasks.waitall.array` | `System.Threading.Tasks.Task::WaitAll(System.Threading.Tasks.Task[])` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAll(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.waitany.array` | `System.Threading.Tasks.Task::WaitAny(System.Threading.Tasks.Task[])` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAny(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.result.generic` | `System.Threading.Tasks.Task`1::get_Result()` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Result(System.Threading.Tasks.Task`1<TResult>)` | Controlled |

## TaskContinuations family

Policy: **Controlled**. `Task.ContinueWith(Action<Task>)`, `Task<T>.ContinueWith(Action<Task<T>>)`, and the result-producing `Task<T>.ContinueWith<TNewResult>(Func<Task<T>,TNewResult>)` redirect so the continuation is scheduled on the controlled coordinator and runs on the logical thread after the antecedent completes.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.continuewith.action` | `System.Threading.Tasks.Task::ContinueWith(System.Action`1<System.Threading.Tasks.Task>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::ContinueWith(System.Threading.Tasks.Task,System.Action`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.continuewith.generic.action` | `System.Threading.Tasks.Task`1::ContinueWith(System.Action`1<System.Threading.Tasks.Task`1<!0>>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::ContinueWith(System.Threading.Tasks.Task`1<TResult>,System.Action`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.continuewith.generic.func` | `System.Threading.Tasks.Task`1::ContinueWith(System.Func`2<System.Threading.Tasks.Task`1<!0>,!!0>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::ContinueWith(System.Threading.Tasks.Task`1<TResult>,System.Func`2<System.Threading.Tasks.Task`1<TResult>,TNewResult>)` | Controlled |

## TaskTime family

Policy: **Controlled**. `Task.Delay` and `Task.WaitAsync` use controlled virtual deadlines, preserve cancellation and terminal task state, and never consume wall-clock time.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.delay.milliseconds` | `System.Threading.Tasks.Task::Delay(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Delay(System.Int32)` | Controlled |
| `clockwork.tasks.delay.timespan` | `System.Threading.Tasks.Task::Delay(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Delay(System.TimeSpan)` | Controlled |
| `clockwork.tasks.delay.milliseconds.cancellationtoken` | `System.Threading.Tasks.Task::Delay(System.Int32,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Delay(System.Int32,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.delay.timespan.cancellationtoken` | `System.Threading.Tasks.Task::Delay(System.TimeSpan,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Delay(System.TimeSpan,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.delay.timespan.timeprovider` | `System.Threading.Tasks.Task::Delay(System.TimeSpan,System.TimeProvider)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Delay(System.TimeSpan,System.TimeProvider)` | Controlled |
| `clockwork.tasks.delay.timespan.timeprovider.cancellationtoken` | `System.Threading.Tasks.Task::Delay(System.TimeSpan,System.TimeProvider,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Delay(System.TimeSpan,System.TimeProvider,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.waitasync.cancellationtoken` | `System.Threading.Tasks.Task::WaitAsync(System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.waitasync.timespan` | `System.Threading.Tasks.Task::WaitAsync(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task,System.TimeSpan)` | Controlled |
| `clockwork.tasks.waitasync.timespan.timeprovider` | `System.Threading.Tasks.Task::WaitAsync(System.TimeSpan,System.TimeProvider)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task,System.TimeSpan,System.TimeProvider)` | Controlled |
| `clockwork.tasks.waitasync.timespan.cancellationtoken` | `System.Threading.Tasks.Task::WaitAsync(System.TimeSpan,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task,System.TimeSpan,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.waitasync.timespan.timeprovider.cancellationtoken` | `System.Threading.Tasks.Task::WaitAsync(System.TimeSpan,System.TimeProvider,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task,System.TimeSpan,System.TimeProvider,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.generic.waitasync.cancellationtoken` | `System.Threading.Tasks.Task`1::WaitAsync(System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task`1<TResult>,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.generic.waitasync.timespan` | `System.Threading.Tasks.Task`1::WaitAsync(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task`1<TResult>,System.TimeSpan)` | Controlled |
| `clockwork.tasks.generic.waitasync.timespan.timeprovider` | `System.Threading.Tasks.Task`1::WaitAsync(System.TimeSpan,System.TimeProvider)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task`1<TResult>,System.TimeSpan,System.TimeProvider)` | Controlled |
| `clockwork.tasks.generic.waitasync.timespan.cancellationtoken` | `System.Threading.Tasks.Task`1::WaitAsync(System.TimeSpan,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task`1<TResult>,System.TimeSpan,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.generic.waitasync.timespan.timeprovider.cancellationtoken` | `System.Threading.Tasks.Task`1::WaitAsync(System.TimeSpan,System.TimeProvider,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::WaitAsync(System.Threading.Tasks.Task`1<TResult>,System.TimeSpan,System.TimeProvider,System.Threading.CancellationToken)` | Controlled |

## TaskScheduling family

Policy: **Controlled**. `Task.Run` (all `Action`/`Func<TResult>`/`Func<Task>`/`Func<Task<TResult>>` overloads, with and without a `CancellationToken`) redirects thread-pool scheduling into controlled operations. Each overload redirects to a controlled equivalent that schedules the delegate as a controlled operation on the simulation coordinator, preserving cancellation and unwrap semantics.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.run.action` | `System.Threading.Tasks.Task::Run(System.Action)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Run(System.Action)` | Controlled |
| `clockwork.tasks.run.action.cancellationtoken` | `System.Threading.Tasks.Task::Run(System.Action,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Run(System.Action,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.run.func` | `System.Threading.Tasks.Task::Run(System.Func`1<!!0>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Run(System.Func`1<TResult>)` | Controlled |
| `clockwork.tasks.run.func.cancellationtoken` | `System.Threading.Tasks.Task::Run(System.Func`1<!!0>,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Run(System.Func`1<TResult>,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.run.func.task` | `System.Threading.Tasks.Task::Run(System.Func`1<System.Threading.Tasks.Task>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Run(System.Func`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.run.func.task.cancellationtoken` | `System.Threading.Tasks.Task::Run(System.Func`1<System.Threading.Tasks.Task>,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Run(System.Func`1<System.Threading.Tasks.Task>,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.run.func.task.generic` | `System.Threading.Tasks.Task::Run(System.Func`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Run(System.Func`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.run.func.task.generic.cancellationtoken` | `System.Threading.Tasks.Task::Run(System.Func`1<System.Threading.Tasks.Task`1<!!0>>,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Run(System.Func`1<System.Threading.Tasks.Task`1<TResult>>,System.Threading.CancellationToken)` | Controlled |

## AsyncMachinery family

Policy: **Controlled**. The compiler-generated builder and awaiter types of an `async` state machine (`AsyncTaskMethodBuilder`, `TaskAwaiter`, `ConfiguredTaskAwaitable`/`YieldAwaitable` and their awaiters, generic and non-generic) are substituted onto Clockwork's controlled equivalents by the member-aware pass, and `Task.Yield()` redirects to the controlled yield. Every awaited continuation is scheduled through the simulation coordinator instead of the thread pool, and `ConfigureAwait(false)` stays controlled.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.builder.task` | `System.Runtime.CompilerServices.AsyncTaskMethodBuilder` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledAsyncTaskMethodBuilder` | Controlled |
| `clockwork.tasks.builder.task.generic` | `System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledAsyncTaskMethodBuilder`1` | Controlled |
| `clockwork.tasks.awaiter.task` | `System.Runtime.CompilerServices.TaskAwaiter` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledTaskAwaiter` | Controlled |
| `clockwork.tasks.awaiter.task.generic` | `System.Runtime.CompilerServices.TaskAwaiter`1` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledTaskAwaiter`1` | Controlled |
| `clockwork.tasks.configured.awaitable` | `System.Runtime.CompilerServices.ConfiguredTaskAwaitable` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledConfiguredTaskAwaitable` | Controlled |
| `clockwork.tasks.configured.awaitable.generic` | `System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledConfiguredTaskAwaitable`1` | Controlled |
| `clockwork.tasks.configured.awaiter` | `System.Runtime.CompilerServices.ConfiguredTaskAwaitable/ConfiguredTaskAwaiter` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledConfiguredTaskAwaiter` | Controlled |
| `clockwork.tasks.configured.awaiter.generic` | `System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1/ConfiguredTaskAwaiter` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledConfiguredTaskAwaiter`1` | Controlled |
| `clockwork.tasks.yield.awaitable` | `System.Runtime.CompilerServices.YieldAwaitable` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledYieldAwaitable` | Controlled |
| `clockwork.tasks.yield.awaiter` | `System.Runtime.CompilerServices.YieldAwaitable/YieldAwaiter` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledYieldAwaiter` | Controlled |
| `clockwork.tasks.yield.call` | `System.Threading.Tasks.Task::Yield()` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTask::Yield()` | Controlled |

## ValueTaskMachinery family

Policy: **Controlled**. The compiler-generated builder and awaiter types of an `async ValueTask`/`async ValueTask<T>` state machine (`AsyncValueTaskMethodBuilder`, `ValueTaskAwaiter`, `ConfiguredValueTaskAwaitable` and their awaiters, generic and non-generic) are substituted onto Clockwork's controlled equivalents by the member-aware pass, so every awaited value-task continuation is scheduled through the simulation coordinator. `ConfigureAwait(false)` stays controlled. Synchronous blocking on a value task is not rewritten (a value task may be consumed only once); `await` is the supported controlled path.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.builder.valuetask` | `System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledAsyncValueTaskMethodBuilder` | Controlled |
| `clockwork.tasks.builder.valuetask.generic` | `System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder`1` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledAsyncValueTaskMethodBuilder`1` | Controlled |
| `clockwork.tasks.awaiter.valuetask` | `System.Runtime.CompilerServices.ValueTaskAwaiter` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledValueTaskAwaiter` | Controlled |
| `clockwork.tasks.awaiter.valuetask.generic` | `System.Runtime.CompilerServices.ValueTaskAwaiter`1` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledValueTaskAwaiter`1` | Controlled |
| `clockwork.tasks.configured.valuetask.awaitable` | `System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledConfiguredValueTaskAwaitable` | Controlled |
| `clockwork.tasks.configured.valuetask.awaitable.generic` | `System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledConfiguredValueTaskAwaitable`1` | Controlled |
| `clockwork.tasks.configured.valuetask.awaiter` | `System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable/ConfiguredValueTaskAwaiter` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledConfiguredValueTaskAwaiter` | Controlled |
| `clockwork.tasks.configured.valuetask.awaiter.generic` | `System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1/ConfiguredValueTaskAwaiter` | `Clockwork!Clockwork.Shims.System.Runtime.CompilerServices.ControlledConfiguredValueTaskAwaiter`1` | Controlled |

## TaskFactory family

Policy: **Controlled**. All 24 .NET 10 `TaskFactory.StartNew` and `TaskFactory<T>.StartNew` overloads are classified, including state-carrying delegates and the full cancellation/options/scheduler forms. Each redirects to a controlled equivalent that schedules the delegate as a fresh logical strand while preserving state, cancellation, and results. Non-default schedulers and creation options whose semantics cannot be preserved are rejected precisely.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.factory.startnew.action` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action)` | Controlled |
| `clockwork.tasks.factory.startnew.action.cancellationtoken` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.factory.startnew.action.options` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action,System.Threading.Tasks.TaskCreationOptions)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action,System.Threading.Tasks.TaskCreationOptions)` | Controlled |
| `clockwork.tasks.factory.startnew.action.scheduler` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | Controlled |
| `clockwork.tasks.factory.startnew.action.state` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action`1<System.Object>,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action`1<System.Object>,System.Object)` | Controlled |
| `clockwork.tasks.factory.startnew.action.state.cancellationtoken` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action`1<System.Object>,System.Object,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action`1<System.Object>,System.Object,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.factory.startnew.action.state.options` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action`1<System.Object>,System.Object,System.Threading.Tasks.TaskCreationOptions)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action`1<System.Object>,System.Object,System.Threading.Tasks.TaskCreationOptions)` | Controlled |
| `clockwork.tasks.factory.startnew.action.state.scheduler` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action`1<System.Object>,System.Object,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action`1<System.Object>,System.Object,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | Controlled |
| `clockwork.tasks.factory.startnew.func` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`1<!!0>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`1<TResult>)` | Controlled |
| `clockwork.tasks.factory.startnew.func.cancellationtoken` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`1<!!0>,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`1<TResult>,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.factory.startnew.func.options` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`1<!!0>,System.Threading.Tasks.TaskCreationOptions)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`1<TResult>,System.Threading.Tasks.TaskCreationOptions)` | Controlled |
| `clockwork.tasks.factory.startnew.func.scheduler` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`1<!!0>,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`1<TResult>,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | Controlled |
| `clockwork.tasks.factory.startnew.func.state` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`2<System.Object,!!0>,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`2<System.Object,TResult>,System.Object)` | Controlled |
| `clockwork.tasks.factory.startnew.func.state.cancellationtoken` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`2<System.Object,!!0>,System.Object,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`2<System.Object,TResult>,System.Object,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.factory.startnew.func.state.options` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`2<System.Object,!!0>,System.Object,System.Threading.Tasks.TaskCreationOptions)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`2<System.Object,TResult>,System.Object,System.Threading.Tasks.TaskCreationOptions)` | Controlled |
| `clockwork.tasks.factory.startnew.func.state.scheduler` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`2<System.Object,!!0>,System.Object,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`2<System.Object,TResult>,System.Object,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`1<!0>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`1<TResult>)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func.cancellationtoken` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`1<!0>,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`1<TResult>,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func.options` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`1<!0>,System.Threading.Tasks.TaskCreationOptions)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`1<TResult>,System.Threading.Tasks.TaskCreationOptions)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func.scheduler` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`1<!0>,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`1<TResult>,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func.state` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`2<System.Object,!0>,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`2<System.Object,TResult>,System.Object)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func.state.cancellationtoken` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`2<System.Object,!0>,System.Object,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`2<System.Object,TResult>,System.Object,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func.state.options` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`2<System.Object,!0>,System.Object,System.Threading.Tasks.TaskCreationOptions)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`2<System.Object,TResult>,System.Object,System.Threading.Tasks.TaskCreationOptions)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func.state.scheduler` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`2<System.Object,!0>,System.Object,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`2<System.Object,TResult>,System.Object,System.Threading.CancellationToken,System.Threading.Tasks.TaskCreationOptions,System.Threading.Tasks.TaskScheduler)` | Controlled |

## Thread family

Policy: **Controlled**. `Thread` construction (`ThreadStart`/`ParameterizedThreadStart`, with and without a stack size), `Start`, `Join` (all overloads), `Sleep`, `Yield`, and `SpinWait` redirect to a controlled thread that maps each thread to a controlled operation on the simulation coordinator; `Join`/`Sleep` yield the logical thread via the deterministic loop rather than blocking a physical thread or consuming real time. OS-specific priority, apartment-state, and `Interrupt` operations cannot be modelled faithfully and are rejected with a precise diagnostic.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.environment.currentmanagedthreadid` | `System.Environment::get_CurrentManagedThreadId()` | `Clockwork!Clockwork.Shims.System.ControlledEnvironment::GetCurrentManagedThreadId()` | Controlled |
| `clockwork.thread.ctor.threadstart` | `new System.Threading.Thread(System.Threading.ThreadStart)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Create(System.Threading.ThreadStart)` | Controlled |
| `clockwork.thread.ctor.threadstart.stacksize` | `new System.Threading.Thread(System.Threading.ThreadStart,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Create(System.Threading.ThreadStart,System.Int32)` | Controlled |
| `clockwork.thread.ctor.parameterized` | `new System.Threading.Thread(System.Threading.ParameterizedThreadStart)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Create(System.Threading.ParameterizedThreadStart)` | Controlled |
| `clockwork.thread.ctor.parameterized.stacksize` | `new System.Threading.Thread(System.Threading.ParameterizedThreadStart,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Create(System.Threading.ParameterizedThreadStart,System.Int32)` | Controlled |
| `clockwork.thread.start` | `System.Threading.Thread::Start()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Start(System.Threading.Thread)` | Controlled |
| `clockwork.thread.start.parameter` | `System.Threading.Thread::Start(System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Start(System.Threading.Thread,System.Object)` | Controlled |
| `clockwork.thread.join` | `System.Threading.Thread::Join()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Join(System.Threading.Thread)` | Controlled |
| `clockwork.thread.join.milliseconds` | `System.Threading.Thread::Join(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Join(System.Threading.Thread,System.Int32)` | Controlled |
| `clockwork.thread.join.timespan` | `System.Threading.Thread::Join(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Join(System.Threading.Thread,System.TimeSpan)` | Controlled |
| `clockwork.thread.sleep.milliseconds` | `System.Threading.Thread::Sleep(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Sleep(System.Int32)` | Controlled |
| `clockwork.thread.sleep.timespan` | `System.Threading.Thread::Sleep(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Sleep(System.TimeSpan)` | Controlled |
| `clockwork.thread.spinwait` | `System.Threading.Thread::SpinWait(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::SpinWait(System.Int32)` | Controlled |
| `clockwork.thread.yield` | `System.Threading.Thread::Yield()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Yield()` | Controlled |
| `clockwork.thread.set_priority` | `System.Threading.Thread::set_Priority(System.Threading.ThreadPriority)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::SetPriority(System.Threading.Thread,System.Threading.ThreadPriority)` | Rejected |
| `clockwork.thread.interrupt` | `System.Threading.Thread::Interrupt()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::Interrupt(System.Threading.Thread)` | Rejected |
| `clockwork.thread.setapartmentstate` | `System.Threading.Thread::SetApartmentState(System.Threading.ApartmentState)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::SetApartmentState(System.Threading.Thread,System.Threading.ApartmentState)` | Rejected |
| `clockwork.thread.trysetapartmentstate` | `System.Threading.Thread::TrySetApartmentState(System.Threading.ApartmentState)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThread::TrySetApartmentState(System.Threading.Thread,System.Threading.ApartmentState)` | Rejected |

## ThreadPool family

Policy: **Controlled**. `ThreadPool.QueueUserWorkItem` (the `WaitCallback`, `WaitCallback`+state, and generic `Action<TState>`+state+preferLocal forms) and `UnsafeQueueUserWorkItem` (the `WaitCallback`+state, `IThreadPoolWorkItem`, and generic forms) queue the callback as a controlled operation on the simulation coordinator; the safe variants flow `ExecutionContext` while the unsafe variants do not, matching the BCL. The registered-wait APIs (`RegisterWaitForSingleObject`/`UnsafeRegisterWaitForSingleObject`, across the `uint`/`int`/`long`/`TimeSpan` timeout overloads) run as passive, event-driven controlled waits on the target handle's modelled signalled state: the callback fires with `timedOut: false` on a signal (an auto-reset handle consumes exactly one) or `timedOut: true` on the virtual-time deadline, honouring `executeOnlyOnce`, re-arming otherwise, and flowing `ExecutionContext` for the safe family only; the returned `RegisteredWaitHandle` is substituted with the controlled handle so `Unregister` stops the wait and signals its completion event. `UnsafeQueueNativeOverlapped` depends on native I/O and is rejected with a precise diagnostic.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.threadpool.queue.waitcallback` | `System.Threading.ThreadPool::QueueUserWorkItem(System.Threading.WaitCallback)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::QueueUserWorkItem(System.Threading.WaitCallback)` | Controlled |
| `clockwork.threadpool.queue.waitcallback.state` | `System.Threading.ThreadPool::QueueUserWorkItem(System.Threading.WaitCallback,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::QueueUserWorkItem(System.Threading.WaitCallback,System.Object)` | Controlled |
| `clockwork.threadpool.queue.generic` | `System.Threading.ThreadPool::QueueUserWorkItem(System.Action`1<!!0>,!!0,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::QueueUserWorkItem(System.Action`1<TState>,TState,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsafequeue.waitcallback.state` | `System.Threading.ThreadPool::UnsafeQueueUserWorkItem(System.Threading.WaitCallback,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::UnsafeQueueUserWorkItem(System.Threading.WaitCallback,System.Object)` | Controlled |
| `clockwork.threadpool.unsafequeue.workitem` | `System.Threading.ThreadPool::UnsafeQueueUserWorkItem(System.Threading.IThreadPoolWorkItem,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::UnsafeQueueUserWorkItem(System.Threading.IThreadPoolWorkItem,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsafequeue.generic` | `System.Threading.ThreadPool::UnsafeQueueUserWorkItem(System.Action`1<!!0>,!!0,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::UnsafeQueueUserWorkItem(System.Action`1<TState>,TState,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsafequeuenativeoverlapped` | `System.Threading.ThreadPool::UnsafeQueueNativeOverlapped(System.Threading.NativeOverlapped*)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::RejectNativeOverlapped(System.String)` | Rejected |
| `clockwork.threadpool.registeredwaithandle.type` | `System.Threading.RegisteredWaitHandle` | `Clockwork!Clockwork.Shims.System.Threading.ControlledRegisteredWaitHandle` | Controlled |
| `clockwork.threadpool.registerwait.uint32` | `System.Threading.ThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.UInt32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.UInt32,System.Boolean)` | Controlled |
| `clockwork.threadpool.registerwait.int32` | `System.Threading.ThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int32,System.Boolean)` | Controlled |
| `clockwork.threadpool.registerwait.int64` | `System.Threading.ThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int64,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int64,System.Boolean)` | Controlled |
| `clockwork.threadpool.registerwait.timespan` | `System.Threading.ThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.TimeSpan,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.TimeSpan,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsaferegisterwait.uint32` | `System.Threading.ThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.UInt32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.UInt32,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsaferegisterwait.int32` | `System.Threading.ThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int32,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsaferegisterwait.int64` | `System.Threading.ThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int64,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int64,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsaferegisterwait.timespan` | `System.Threading.ThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.TimeSpan,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.TimeSpan,System.Boolean)` | Controlled |

## Timers family

Policy: **Controlled**. `System.Threading.Timer`, `System.Timers.Timer`, and `PeriodicTimer` are substituted with controlled virtual-time implementations. `TimeProvider.System` and `CreateTimer` bridge to the same scheduler; unsupported provider and designer marshaling paths reject precisely.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.timer.threading.type` | `System.Threading.Timer` | `Clockwork!Clockwork.Shims.System.Threading.ControlledTimer` | Controlled |
| `clockwork.timer.component.type` | `System.Timers.Timer` | `Clockwork!Clockwork.Shims.System.Timers.ControlledTimer` | Controlled |
| `clockwork.timer.periodic.type` | `System.Threading.PeriodicTimer` | `Clockwork!Clockwork.Shims.System.Threading.ControlledPeriodicTimer` | Controlled |
| `clockwork.timeprovider.system` | `System.TimeProvider::get_System()` | `Clockwork!Clockwork.Shims.System.ControlledTimeProvider::get_System()` | Controlled |
| `clockwork.timeprovider.createtimer` | `System.TimeProvider::CreateTimer(System.Threading.TimerCallback,System.Object,System.TimeSpan,System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.ControlledTimeProvider::CreateTimer(System.TimeProvider,System.Threading.TimerCallback,System.Object,System.TimeSpan,System.TimeSpan)` | Controlled |

## CancellationTimers family

Policy: **Controlled**. `CancellationTokenSource` timed constructors and `CancelAfter` use resettable virtual deadlines. Manual cancellation, reset, and disposal remove stale registrations before they can fire.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.cancellationtokensource.ctor.milliseconds` | `new System.Threading.CancellationTokenSource(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::Create(System.Int32)` | Controlled |
| `clockwork.cancellationtokensource.ctor.timespan` | `new System.Threading.CancellationTokenSource(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::Create(System.TimeSpan)` | Controlled |
| `clockwork.cancellationtokensource.ctor.timespan.timeprovider` | `new System.Threading.CancellationTokenSource(System.TimeSpan,System.TimeProvider)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::Create(System.TimeSpan,System.TimeProvider)` | Controlled |
| `clockwork.cancellationtokensource.cancelafter.milliseconds` | `System.Threading.CancellationTokenSource::CancelAfter(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::CancelAfter(System.Threading.CancellationTokenSource,System.Int32)` | Controlled |
| `clockwork.cancellationtokensource.cancelafter.timespan` | `System.Threading.CancellationTokenSource::CancelAfter(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::CancelAfter(System.Threading.CancellationTokenSource,System.TimeSpan)` | Controlled |
| `clockwork.cancellationtokensource.cancel` | `System.Threading.CancellationTokenSource::Cancel()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::Cancel(System.Threading.CancellationTokenSource)` | Controlled |
| `clockwork.cancellationtokensource.cancel.throw` | `System.Threading.CancellationTokenSource::Cancel(System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::Cancel(System.Threading.CancellationTokenSource,System.Boolean)` | Controlled |
| `clockwork.cancellationtokensource.cancelasync` | `System.Threading.CancellationTokenSource::CancelAsync()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::CancelAsync(System.Threading.CancellationTokenSource)` | Controlled |
| `clockwork.cancellationtokensource.tryreset` | `System.Threading.CancellationTokenSource::TryReset()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::TryReset(System.Threading.CancellationTokenSource)` | Controlled |
| `clockwork.cancellationtokensource.dispose` | `System.Threading.CancellationTokenSource::Dispose()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCancellationTokenSource::Dispose(System.Threading.CancellationTokenSource)` | Controlled |

## Parallel family

Policy: **Controlled**. `Parallel.Invoke`, `Parallel.For` (`int`/`long`, with and without `ParallelOptions`), and `Parallel.ForEach(IEnumerable<T>)` run their bodies as controlled operations on the simulation coordinator, preserving results, cancellation, and exception aggregation. The `ParallelLoopState` break/stop overloads cannot be modelled deterministically yet and are rejected with a precise diagnostic.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.parallel.invoke` | `System.Threading.Tasks.Parallel::Invoke(System.Action[])` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::Invoke(System.Action[])` | Controlled |
| `clockwork.parallel.invoke.options` | `System.Threading.Tasks.Parallel::Invoke(System.Threading.Tasks.ParallelOptions,System.Action[])` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::Invoke(System.Threading.Tasks.ParallelOptions,System.Action[])` | Controlled |
| `clockwork.parallel.for.int32` | `System.Threading.Tasks.Parallel::For(System.Int32,System.Int32,System.Action`1<System.Int32>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::For(System.Int32,System.Int32,System.Action`1<System.Int32>)` | Controlled |
| `clockwork.parallel.for.int32.options` | `System.Threading.Tasks.Parallel::For(System.Int32,System.Int32,System.Threading.Tasks.ParallelOptions,System.Action`1<System.Int32>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::For(System.Int32,System.Int32,System.Threading.Tasks.ParallelOptions,System.Action`1<System.Int32>)` | Controlled |
| `clockwork.parallel.for.int64` | `System.Threading.Tasks.Parallel::For(System.Int64,System.Int64,System.Action`1<System.Int64>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::For(System.Int64,System.Int64,System.Action`1<System.Int64>)` | Controlled |
| `clockwork.parallel.for.int64.options` | `System.Threading.Tasks.Parallel::For(System.Int64,System.Int64,System.Threading.Tasks.ParallelOptions,System.Action`1<System.Int64>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::For(System.Int64,System.Int64,System.Threading.Tasks.ParallelOptions,System.Action`1<System.Int64>)` | Controlled |
| `clockwork.parallel.foreach` | `System.Threading.Tasks.Parallel::ForEach(System.Collections.Generic.IEnumerable`1<!!0>,System.Action`1<!!0>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::ForEach(System.Collections.Generic.IEnumerable`1<TSource>,System.Action`1<TSource>)` | Controlled |
| `clockwork.parallel.foreach.options` | `System.Threading.Tasks.Parallel::ForEach(System.Collections.Generic.IEnumerable`1<!!0>,System.Threading.Tasks.ParallelOptions,System.Action`1<!!0>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::ForEach(System.Collections.Generic.IEnumerable`1<TSource>,System.Threading.Tasks.ParallelOptions,System.Action`1<TSource>)` | Controlled |
| `clockwork.parallel.for.int32.loopstate` | `System.Threading.Tasks.Parallel::For(System.Int32,System.Int32,System.Action`2<System.Int32,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.for.int32.loopstate.options` | `System.Threading.Tasks.Parallel::For(System.Int32,System.Int32,System.Threading.Tasks.ParallelOptions,System.Action`2<System.Int32,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.for.int64.loopstate` | `System.Threading.Tasks.Parallel::For(System.Int64,System.Int64,System.Action`2<System.Int64,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.for.int64.loopstate.options` | `System.Threading.Tasks.Parallel::For(System.Int64,System.Int64,System.Threading.Tasks.ParallelOptions,System.Action`2<System.Int64,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.foreach.loopstate` | `System.Threading.Tasks.Parallel::ForEach(System.Collections.Generic.IEnumerable`1<!!0>,System.Action`2<!!0,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.foreach.loopstate.index` | `System.Threading.Tasks.Parallel::ForEach(System.Collections.Generic.IEnumerable`1<!!0>,System.Action`3<!!0,System.Threading.Tasks.ParallelLoopState,System.Int64>)` | `Clockwork!Clockwork.Shims.System.Threading.Tasks.ControlledParallel::RejectUnsupported(System.String)` | Rejected |

## Monitor family

Policy: **Controlled**. The complete .NET 10 `Monitor` surface is classified: synchronization and C# `lock (object)` lowering are controlled with deterministic virtual-time deadlines, while the process-wide `LockContentionCount` metric is rejected because it has no per-simulation meaning.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.monitor.enter` | `System.Threading.Monitor::Enter(System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::Enter(System.Object)` | Controlled |
| `clockwork.monitor.enter.locktaken` | `System.Threading.Monitor::Enter(System.Object,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::Enter(System.Object,System.Boolean&)` | Controlled |
| `clockwork.monitor.exit` | `System.Threading.Monitor::Exit(System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::Exit(System.Object)` | Controlled |
| `clockwork.monitor.isentered` | `System.Threading.Monitor::IsEntered(System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::IsEntered(System.Object)` | Controlled |
| `clockwork.monitor.get_lockcontentioncount` | `System.Threading.Monitor::get_LockContentionCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::LockContentionCount()` | Rejected |
| `clockwork.monitor.tryenter` | `System.Threading.Monitor::TryEnter(System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::TryEnter(System.Object)` | Controlled |
| `clockwork.monitor.tryenter.locktaken` | `System.Threading.Monitor::TryEnter(System.Object,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::TryEnter(System.Object,System.Boolean&)` | Controlled |
| `clockwork.monitor.tryenter.milliseconds` | `System.Threading.Monitor::TryEnter(System.Object,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::TryEnter(System.Object,System.Int32)` | Controlled |
| `clockwork.monitor.tryenter.milliseconds.locktaken` | `System.Threading.Monitor::TryEnter(System.Object,System.Int32,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::TryEnter(System.Object,System.Int32,System.Boolean&)` | Controlled |
| `clockwork.monitor.tryenter.timespan` | `System.Threading.Monitor::TryEnter(System.Object,System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::TryEnter(System.Object,System.TimeSpan)` | Controlled |
| `clockwork.monitor.tryenter.timespan.locktaken` | `System.Threading.Monitor::TryEnter(System.Object,System.TimeSpan,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::TryEnter(System.Object,System.TimeSpan,System.Boolean&)` | Controlled |
| `clockwork.monitor.wait` | `System.Threading.Monitor::Wait(System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::Wait(System.Object)` | Controlled |
| `clockwork.monitor.wait.milliseconds` | `System.Threading.Monitor::Wait(System.Object,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::Wait(System.Object,System.Int32)` | Controlled |
| `clockwork.monitor.wait.milliseconds.exitcontext` | `System.Threading.Monitor::Wait(System.Object,System.Int32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::Wait(System.Object,System.Int32,System.Boolean)` | Controlled |
| `clockwork.monitor.wait.timespan` | `System.Threading.Monitor::Wait(System.Object,System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::Wait(System.Object,System.TimeSpan)` | Controlled |
| `clockwork.monitor.wait.timespan.exitcontext` | `System.Threading.Monitor::Wait(System.Object,System.TimeSpan,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::Wait(System.Object,System.TimeSpan,System.Boolean)` | Controlled |
| `clockwork.monitor.pulse` | `System.Threading.Monitor::Pulse(System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::Pulse(System.Object)` | Controlled |
| `clockwork.monitor.pulseall` | `System.Threading.Monitor::PulseAll(System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMonitor::PulseAll(System.Object)` | Controlled |

## Lock family

Policy: **Controlled**. The .NET 9+ `System.Threading.Lock` type and nested `Scope` are substituted onto controlled equivalents, covering the dedicated C# lock lowering in Debug and Release builds.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.lock.type` | `System.Threading.Lock` | `Clockwork!Clockwork.Shims.System.Threading.ControlledLock` | Controlled |
| `clockwork.lock.scope.type` | `System.Threading.Lock/Scope` | `Clockwork!Clockwork.Shims.System.Threading.ControlledLock/Scope` | Controlled |

## Semaphore family

Policy: **Controlled**. The .NET 10 `SemaphoreSlim` constructors, counts, waits, releases, and disposal are controlled; `AvailableWaitHandle` returns a controlled manual-reset bridge whose signal tracks whether the permit count is positive and which composes with the controlled wait-handle surface.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.semaphoreslim.ctor.initial` | `new System.Threading.SemaphoreSlim(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Create(System.Int32)` | Controlled |
| `clockwork.semaphoreslim.ctor.initial.max` | `new System.Threading.SemaphoreSlim(System.Int32,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Create(System.Int32,System.Int32)` | Controlled |
| `clockwork.semaphoreslim.get_currentcount` | `System.Threading.SemaphoreSlim::get_CurrentCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::CurrentCount(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.wait` | `System.Threading.SemaphoreSlim::Wait()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.wait.cancellationtoken` | `System.Threading.SemaphoreSlim::Wait(System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.wait.milliseconds` | `System.Threading.SemaphoreSlim::Wait(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.Int32)` | Controlled |
| `clockwork.semaphoreslim.wait.milliseconds.cancellationtoken` | `System.Threading.SemaphoreSlim::Wait(System.Int32,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.Int32,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.wait.timespan` | `System.Threading.SemaphoreSlim::Wait(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.TimeSpan)` | Controlled |
| `clockwork.semaphoreslim.wait.timespan.cancellationtoken` | `System.Threading.SemaphoreSlim::Wait(System.TimeSpan,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.TimeSpan,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.waitasync` | `System.Threading.SemaphoreSlim::WaitAsync()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.waitasync.cancellationtoken` | `System.Threading.SemaphoreSlim::WaitAsync(System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.waitasync.milliseconds` | `System.Threading.SemaphoreSlim::WaitAsync(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.Int32)` | Controlled |
| `clockwork.semaphoreslim.waitasync.milliseconds.cancellationtoken` | `System.Threading.SemaphoreSlim::WaitAsync(System.Int32,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.Int32,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.waitasync.timespan` | `System.Threading.SemaphoreSlim::WaitAsync(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.TimeSpan)` | Controlled |
| `clockwork.semaphoreslim.waitasync.timespan.cancellationtoken` | `System.Threading.SemaphoreSlim::WaitAsync(System.TimeSpan,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.TimeSpan,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.release` | `System.Threading.SemaphoreSlim::Release()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Release(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.release.count` | `System.Threading.SemaphoreSlim::Release(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Release(System.Threading.SemaphoreSlim,System.Int32)` | Controlled |
| `clockwork.semaphoreslim.dispose` | `System.Threading.SemaphoreSlim::Dispose()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::Dispose(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.get_availablewaithandle` | `System.Threading.SemaphoreSlim::get_AvailableWaitHandle()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphoreSlim::AvailableWaitHandle(System.Threading.SemaphoreSlim)` | Controlled |

## Interlocked family

Policy: **Controlled**. The full .NET 10 `Interlocked` surface - `Increment`/`Decrement`/`Add`/`And`/`Or` (`int`/`long`/`uint`/`ulong`), `Exchange`/`CompareExchange` (every primitive, native-int, floating-point, reference, and generic reference overload), `Read` (`long`/`ulong`), and the memory barriers - redirects each call site to a shim with the identical `ref`-first signature. Clockwork's cooperative single-logical-thread scheduler makes every read-modify-write an indivisible step (never split, never interleaved mid-operation), so the shim delegates to the real primitive and preserves exact atomic return, overflow, and reference-write semantics under the active simulation. The exploration policy injects no mid-operation scheduling point; the single delegation site is the race-exploration access-tracking attachment point.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.interlocked.increment.int32` | `System.Threading.Interlocked::Increment(System.Int32&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Increment(System.Int32&)` | Controlled |
| `clockwork.interlocked.increment.int64` | `System.Threading.Interlocked::Increment(System.Int64&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Increment(System.Int64&)` | Controlled |
| `clockwork.interlocked.increment.uint32` | `System.Threading.Interlocked::Increment(System.UInt32&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Increment(System.UInt32&)` | Controlled |
| `clockwork.interlocked.increment.uint64` | `System.Threading.Interlocked::Increment(System.UInt64&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Increment(System.UInt64&)` | Controlled |
| `clockwork.interlocked.decrement.int32` | `System.Threading.Interlocked::Decrement(System.Int32&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Decrement(System.Int32&)` | Controlled |
| `clockwork.interlocked.decrement.int64` | `System.Threading.Interlocked::Decrement(System.Int64&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Decrement(System.Int64&)` | Controlled |
| `clockwork.interlocked.decrement.uint32` | `System.Threading.Interlocked::Decrement(System.UInt32&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Decrement(System.UInt32&)` | Controlled |
| `clockwork.interlocked.decrement.uint64` | `System.Threading.Interlocked::Decrement(System.UInt64&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Decrement(System.UInt64&)` | Controlled |
| `clockwork.interlocked.add.int32` | `System.Threading.Interlocked::Add(System.Int32&,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Add(System.Int32&,System.Int32)` | Controlled |
| `clockwork.interlocked.add.int64` | `System.Threading.Interlocked::Add(System.Int64&,System.Int64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Add(System.Int64&,System.Int64)` | Controlled |
| `clockwork.interlocked.add.uint32` | `System.Threading.Interlocked::Add(System.UInt32&,System.UInt32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Add(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.interlocked.add.uint64` | `System.Threading.Interlocked::Add(System.UInt64&,System.UInt64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Add(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.interlocked.and.int32` | `System.Threading.Interlocked::And(System.Int32&,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::And(System.Int32&,System.Int32)` | Controlled |
| `clockwork.interlocked.and.uint32` | `System.Threading.Interlocked::And(System.UInt32&,System.UInt32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::And(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.interlocked.and.int64` | `System.Threading.Interlocked::And(System.Int64&,System.Int64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::And(System.Int64&,System.Int64)` | Controlled |
| `clockwork.interlocked.and.uint64` | `System.Threading.Interlocked::And(System.UInt64&,System.UInt64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::And(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.interlocked.or.int32` | `System.Threading.Interlocked::Or(System.Int32&,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Or(System.Int32&,System.Int32)` | Controlled |
| `clockwork.interlocked.or.uint32` | `System.Threading.Interlocked::Or(System.UInt32&,System.UInt32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Or(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.interlocked.or.int64` | `System.Threading.Interlocked::Or(System.Int64&,System.Int64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Or(System.Int64&,System.Int64)` | Controlled |
| `clockwork.interlocked.or.uint64` | `System.Threading.Interlocked::Or(System.UInt64&,System.UInt64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Or(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.interlocked.exchange.int32` | `System.Threading.Interlocked::Exchange(System.Int32&,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.Int32&,System.Int32)` | Controlled |
| `clockwork.interlocked.exchange.int64` | `System.Threading.Interlocked::Exchange(System.Int64&,System.Int64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.Int64&,System.Int64)` | Controlled |
| `clockwork.interlocked.exchange.object` | `System.Threading.Interlocked::Exchange(System.Object&,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.Object&,System.Object)` | Controlled |
| `clockwork.interlocked.exchange.sbyte` | `System.Threading.Interlocked::Exchange(System.SByte&,System.SByte)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.SByte&,System.SByte)` | Controlled |
| `clockwork.interlocked.exchange.int16` | `System.Threading.Interlocked::Exchange(System.Int16&,System.Int16)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.Int16&,System.Int16)` | Controlled |
| `clockwork.interlocked.exchange.byte` | `System.Threading.Interlocked::Exchange(System.Byte&,System.Byte)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.Byte&,System.Byte)` | Controlled |
| `clockwork.interlocked.exchange.uint16` | `System.Threading.Interlocked::Exchange(System.UInt16&,System.UInt16)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.UInt16&,System.UInt16)` | Controlled |
| `clockwork.interlocked.exchange.uint32` | `System.Threading.Interlocked::Exchange(System.UInt32&,System.UInt32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.interlocked.exchange.uint64` | `System.Threading.Interlocked::Exchange(System.UInt64&,System.UInt64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.interlocked.exchange.single` | `System.Threading.Interlocked::Exchange(System.Single&,System.Single)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.Single&,System.Single)` | Controlled |
| `clockwork.interlocked.exchange.double` | `System.Threading.Interlocked::Exchange(System.Double&,System.Double)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.Double&,System.Double)` | Controlled |
| `clockwork.interlocked.exchange.intptr` | `System.Threading.Interlocked::Exchange(System.IntPtr&,System.IntPtr)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.IntPtr&,System.IntPtr)` | Controlled |
| `clockwork.interlocked.exchange.uintptr` | `System.Threading.Interlocked::Exchange(System.UIntPtr&,System.UIntPtr)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(System.UIntPtr&,System.UIntPtr)` | Controlled |
| `clockwork.interlocked.exchange.generic` | `System.Threading.Interlocked::Exchange(!!0&,!!0)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Exchange(T&,T)` | Controlled |
| `clockwork.interlocked.compareexchange.int32` | `System.Threading.Interlocked::CompareExchange(System.Int32&,System.Int32,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.Int32&,System.Int32,System.Int32)` | Controlled |
| `clockwork.interlocked.compareexchange.int64` | `System.Threading.Interlocked::CompareExchange(System.Int64&,System.Int64,System.Int64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.Int64&,System.Int64,System.Int64)` | Controlled |
| `clockwork.interlocked.compareexchange.object` | `System.Threading.Interlocked::CompareExchange(System.Object&,System.Object,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.Object&,System.Object,System.Object)` | Controlled |
| `clockwork.interlocked.compareexchange.sbyte` | `System.Threading.Interlocked::CompareExchange(System.SByte&,System.SByte,System.SByte)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.SByte&,System.SByte,System.SByte)` | Controlled |
| `clockwork.interlocked.compareexchange.int16` | `System.Threading.Interlocked::CompareExchange(System.Int16&,System.Int16,System.Int16)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.Int16&,System.Int16,System.Int16)` | Controlled |
| `clockwork.interlocked.compareexchange.byte` | `System.Threading.Interlocked::CompareExchange(System.Byte&,System.Byte,System.Byte)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.Byte&,System.Byte,System.Byte)` | Controlled |
| `clockwork.interlocked.compareexchange.uint16` | `System.Threading.Interlocked::CompareExchange(System.UInt16&,System.UInt16,System.UInt16)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.UInt16&,System.UInt16,System.UInt16)` | Controlled |
| `clockwork.interlocked.compareexchange.uint32` | `System.Threading.Interlocked::CompareExchange(System.UInt32&,System.UInt32,System.UInt32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.UInt32&,System.UInt32,System.UInt32)` | Controlled |
| `clockwork.interlocked.compareexchange.uint64` | `System.Threading.Interlocked::CompareExchange(System.UInt64&,System.UInt64,System.UInt64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.UInt64&,System.UInt64,System.UInt64)` | Controlled |
| `clockwork.interlocked.compareexchange.single` | `System.Threading.Interlocked::CompareExchange(System.Single&,System.Single,System.Single)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.Single&,System.Single,System.Single)` | Controlled |
| `clockwork.interlocked.compareexchange.double` | `System.Threading.Interlocked::CompareExchange(System.Double&,System.Double,System.Double)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.Double&,System.Double,System.Double)` | Controlled |
| `clockwork.interlocked.compareexchange.intptr` | `System.Threading.Interlocked::CompareExchange(System.IntPtr&,System.IntPtr,System.IntPtr)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.IntPtr&,System.IntPtr,System.IntPtr)` | Controlled |
| `clockwork.interlocked.compareexchange.uintptr` | `System.Threading.Interlocked::CompareExchange(System.UIntPtr&,System.UIntPtr,System.UIntPtr)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(System.UIntPtr&,System.UIntPtr,System.UIntPtr)` | Controlled |
| `clockwork.interlocked.compareexchange.generic` | `System.Threading.Interlocked::CompareExchange(!!0&,!!0,!!0)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::CompareExchange(T&,T,T)` | Controlled |
| `clockwork.interlocked.read.int64` | `System.Threading.Interlocked::Read(System.Int64&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Read(System.Int64&)` | Controlled |
| `clockwork.interlocked.read.uint64` | `System.Threading.Interlocked::Read(System.UInt64&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::Read(System.UInt64&)` | Controlled |
| `clockwork.interlocked.memorybarrier` | `System.Threading.Interlocked::MemoryBarrier()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::MemoryBarrier()` | Controlled |
| `clockwork.interlocked.memorybarrierprocesswide` | `System.Threading.Interlocked::MemoryBarrierProcessWide()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledInterlocked::MemoryBarrierProcessWide()` | Controlled |

## Volatile family

Policy: **Controlled**. The full .NET 10 `Volatile` surface - `Read`/`Write` (every primitive, native-int, floating-point, and generic reference overload) and the `ReadBarrier`/`WriteBarrier` fences - redirects each call site to a shim with the identical `ref`-first signature. Under the cooperative single-logical-thread scheduler a volatile access is an indivisible step, so the shim delegates to the real primitive and preserves the exact value read/written together with the acquire (read) / release (write) fence intent. The single delegation site is the race-exploration access-tracking attachment point.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.volatile.read.boolean` | `System.Threading.Volatile::Read(System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.Boolean&)` | Controlled |
| `clockwork.volatile.read.sbyte` | `System.Threading.Volatile::Read(System.SByte&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.SByte&)` | Controlled |
| `clockwork.volatile.read.byte` | `System.Threading.Volatile::Read(System.Byte&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.Byte&)` | Controlled |
| `clockwork.volatile.read.int16` | `System.Threading.Volatile::Read(System.Int16&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.Int16&)` | Controlled |
| `clockwork.volatile.read.uint16` | `System.Threading.Volatile::Read(System.UInt16&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.UInt16&)` | Controlled |
| `clockwork.volatile.read.int32` | `System.Threading.Volatile::Read(System.Int32&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.Int32&)` | Controlled |
| `clockwork.volatile.read.uint32` | `System.Threading.Volatile::Read(System.UInt32&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.UInt32&)` | Controlled |
| `clockwork.volatile.read.int64` | `System.Threading.Volatile::Read(System.Int64&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.Int64&)` | Controlled |
| `clockwork.volatile.read.uint64` | `System.Threading.Volatile::Read(System.UInt64&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.UInt64&)` | Controlled |
| `clockwork.volatile.read.intptr` | `System.Threading.Volatile::Read(System.IntPtr&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.IntPtr&)` | Controlled |
| `clockwork.volatile.read.uintptr` | `System.Threading.Volatile::Read(System.UIntPtr&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.UIntPtr&)` | Controlled |
| `clockwork.volatile.read.single` | `System.Threading.Volatile::Read(System.Single&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.Single&)` | Controlled |
| `clockwork.volatile.read.double` | `System.Threading.Volatile::Read(System.Double&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(System.Double&)` | Controlled |
| `clockwork.volatile.read.generic` | `System.Threading.Volatile::Read(!!0&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Read(T&)` | Controlled |
| `clockwork.volatile.write.boolean` | `System.Threading.Volatile::Write(System.Boolean&,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.Boolean&,System.Boolean)` | Controlled |
| `clockwork.volatile.write.sbyte` | `System.Threading.Volatile::Write(System.SByte&,System.SByte)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.SByte&,System.SByte)` | Controlled |
| `clockwork.volatile.write.byte` | `System.Threading.Volatile::Write(System.Byte&,System.Byte)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.Byte&,System.Byte)` | Controlled |
| `clockwork.volatile.write.int16` | `System.Threading.Volatile::Write(System.Int16&,System.Int16)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.Int16&,System.Int16)` | Controlled |
| `clockwork.volatile.write.uint16` | `System.Threading.Volatile::Write(System.UInt16&,System.UInt16)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.UInt16&,System.UInt16)` | Controlled |
| `clockwork.volatile.write.int32` | `System.Threading.Volatile::Write(System.Int32&,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.Int32&,System.Int32)` | Controlled |
| `clockwork.volatile.write.uint32` | `System.Threading.Volatile::Write(System.UInt32&,System.UInt32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.volatile.write.int64` | `System.Threading.Volatile::Write(System.Int64&,System.Int64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.Int64&,System.Int64)` | Controlled |
| `clockwork.volatile.write.uint64` | `System.Threading.Volatile::Write(System.UInt64&,System.UInt64)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.volatile.write.intptr` | `System.Threading.Volatile::Write(System.IntPtr&,System.IntPtr)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.IntPtr&,System.IntPtr)` | Controlled |
| `clockwork.volatile.write.uintptr` | `System.Threading.Volatile::Write(System.UIntPtr&,System.UIntPtr)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.UIntPtr&,System.UIntPtr)` | Controlled |
| `clockwork.volatile.write.single` | `System.Threading.Volatile::Write(System.Single&,System.Single)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.Single&,System.Single)` | Controlled |
| `clockwork.volatile.write.double` | `System.Threading.Volatile::Write(System.Double&,System.Double)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(System.Double&,System.Double)` | Controlled |
| `clockwork.volatile.write.generic` | `System.Threading.Volatile::Write(!!0&,!!0)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::Write(T&,T)` | Controlled |
| `clockwork.volatile.readbarrier` | `System.Threading.Volatile::ReadBarrier()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::ReadBarrier()` | Controlled |
| `clockwork.volatile.writebarrier` | `System.Threading.Volatile::WriteBarrier()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledVolatile::WriteBarrier()` | Controlled |

## SpinWait family

Policy: **Controlled**. `System.Threading.SpinWait` is a value type retargeted by whole-type substitution (like `System.Threading.Lock`): every local/field/parameter typed `SpinWait`, each `new SpinWait()`/`default`, the instance members (`Count`, `NextSpinWillYield`, `Reset`, both `SpinOnce` overloads) and the static `SpinUntil` overloads remap onto the controlled struct. Inside a simulation a spin never burns CPU or consumes real time: `SpinOnce` is a cooperative no-op that only advances the observable spin count, and `SpinUntil` pumps the deterministic loop until its predicate holds (a never-satisfiable predicate surfaces as the loop-model deadlock diagnostic). The finite `SpinUntil` overloads use a first-winner virtual-time deadline.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.spinwait.type` | `System.Threading.SpinWait` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSpinWait` | Controlled |

## WaitHandle family

Policy: **Controlled**. The controlled event / wait-handle surface - `AutoResetEvent`, `ManualResetEvent`, `EventWaitHandle`, and the shared `WaitHandle` operations. Each concrete event is a sealed BCL class, so the real object is retained as an identity handle while its signaled state and a deterministic FIFO waiter set live in a side table; every `new` redirects to a `Create` factory and each instance member is a receiver-first shim. `WaitOne` (all five overloads) pumps the deterministic loop until the event is signaled - a never-satisfiable wait surfaces as the loop-model deadlock diagnostic rather than hanging - and `Set`/`Reset` model exact reset-mode semantics: an auto-reset `Set` wakes and consumes exactly one eligible waiter (or leaves the event signaled until the next `WaitOne` consumes it), while a manual-reset `Set` releases every waiter and stays signaled until `Reset`. The static multi-handle operations `WaitAny` (returns the lowest-index signaled handle) and `WaitAll` (waits until every handle is simultaneously signaled, then consumes them atomically so an auto-reset handle is never partially consumed) register across all handles with no lost signals, validating null/empty/over-64 arrays and - for `WaitAll` - duplicate handles; `SignalAndWait` atomically signals the first handle then waits on the second. Finite timeouts use a first-winner virtual-time deadline (zero polls, infinite never times out); `Dispose`/`Close` mark the modelled state disposed. Named / cross-process APIs (named constructors, `OpenExisting`, `TryOpenExisting`) and the raw native-handle accessors (`Handle`, `SafeWaitHandle`) cannot be modelled in a single simulated process and are rejected with a precise diagnostic.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.autoresetevent.ctor` | `new System.Threading.AutoResetEvent(System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::CreateAutoResetEvent(System.Boolean)` | Controlled |
| `clockwork.manualresetevent.ctor` | `new System.Threading.ManualResetEvent(System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::CreateManualResetEvent(System.Boolean)` | Controlled |
| `clockwork.eventwaithandle.ctor.mode` | `new System.Threading.EventWaitHandle(System.Boolean,System.Threading.EventResetMode)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::CreateEvent(System.Boolean,System.Threading.EventResetMode)` | Controlled |
| `clockwork.eventwaithandle.ctor.named` | `new System.Threading.EventWaitHandle(System.Boolean,System.Threading.EventResetMode,System.String)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::CreateNamedEvent(System.Boolean,System.Threading.EventResetMode,System.String)` | Controlled |
| `clockwork.eventwaithandle.ctor.named.creatednew` | `new System.Threading.EventWaitHandle(System.Boolean,System.Threading.EventResetMode,System.String,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::CreateNamedEvent(System.Boolean,System.Threading.EventResetMode,System.String,System.Boolean&)` | Controlled |
| `clockwork.eventwaithandle.ctor.named.options` | `new System.Threading.EventWaitHandle(System.Boolean,System.Threading.EventResetMode,System.String,System.Threading.NamedWaitHandleOptions)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::CreateNamedEvent(System.Boolean,System.Threading.EventResetMode,System.String,System.Threading.NamedWaitHandleOptions)` | Controlled |
| `clockwork.eventwaithandle.ctor.named.options.creatednew` | `new System.Threading.EventWaitHandle(System.Boolean,System.Threading.EventResetMode,System.String,System.Threading.NamedWaitHandleOptions,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::CreateNamedEvent(System.Boolean,System.Threading.EventResetMode,System.String,System.Threading.NamedWaitHandleOptions,System.Boolean&)` | Controlled |
| `clockwork.waithandle.waitone` | `System.Threading.WaitHandle::WaitOne()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitOne(System.Threading.WaitHandle)` | Controlled |
| `clockwork.waithandle.waitone.milliseconds` | `System.Threading.WaitHandle::WaitOne(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitOne(System.Threading.WaitHandle,System.Int32)` | Controlled |
| `clockwork.waithandle.waitone.timespan` | `System.Threading.WaitHandle::WaitOne(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitOne(System.Threading.WaitHandle,System.TimeSpan)` | Controlled |
| `clockwork.waithandle.waitone.milliseconds.exitcontext` | `System.Threading.WaitHandle::WaitOne(System.Int32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitOne(System.Threading.WaitHandle,System.Int32,System.Boolean)` | Controlled |
| `clockwork.waithandle.waitone.timespan.exitcontext` | `System.Threading.WaitHandle::WaitOne(System.TimeSpan,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitOne(System.Threading.WaitHandle,System.TimeSpan,System.Boolean)` | Controlled |
| `clockwork.waithandle.waitany` | `System.Threading.WaitHandle::WaitAny(System.Threading.WaitHandle[])` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAny(System.Threading.WaitHandle[])` | Controlled |
| `clockwork.waithandle.waitany.milliseconds` | `System.Threading.WaitHandle::WaitAny(System.Threading.WaitHandle[],System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAny(System.Threading.WaitHandle[],System.Int32)` | Controlled |
| `clockwork.waithandle.waitany.timespan` | `System.Threading.WaitHandle::WaitAny(System.Threading.WaitHandle[],System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAny(System.Threading.WaitHandle[],System.TimeSpan)` | Controlled |
| `clockwork.waithandle.waitany.milliseconds.exitcontext` | `System.Threading.WaitHandle::WaitAny(System.Threading.WaitHandle[],System.Int32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAny(System.Threading.WaitHandle[],System.Int32,System.Boolean)` | Controlled |
| `clockwork.waithandle.waitany.timespan.exitcontext` | `System.Threading.WaitHandle::WaitAny(System.Threading.WaitHandle[],System.TimeSpan,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAny(System.Threading.WaitHandle[],System.TimeSpan,System.Boolean)` | Controlled |
| `clockwork.waithandle.waitall` | `System.Threading.WaitHandle::WaitAll(System.Threading.WaitHandle[])` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAll(System.Threading.WaitHandle[])` | Controlled |
| `clockwork.waithandle.waitall.milliseconds` | `System.Threading.WaitHandle::WaitAll(System.Threading.WaitHandle[],System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAll(System.Threading.WaitHandle[],System.Int32)` | Controlled |
| `clockwork.waithandle.waitall.timespan` | `System.Threading.WaitHandle::WaitAll(System.Threading.WaitHandle[],System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAll(System.Threading.WaitHandle[],System.TimeSpan)` | Controlled |
| `clockwork.waithandle.waitall.milliseconds.exitcontext` | `System.Threading.WaitHandle::WaitAll(System.Threading.WaitHandle[],System.Int32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAll(System.Threading.WaitHandle[],System.Int32,System.Boolean)` | Controlled |
| `clockwork.waithandle.waitall.timespan.exitcontext` | `System.Threading.WaitHandle::WaitAll(System.Threading.WaitHandle[],System.TimeSpan,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::WaitAll(System.Threading.WaitHandle[],System.TimeSpan,System.Boolean)` | Controlled |
| `clockwork.waithandle.signalandwait` | `System.Threading.WaitHandle::SignalAndWait(System.Threading.WaitHandle,System.Threading.WaitHandle)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::SignalAndWait(System.Threading.WaitHandle,System.Threading.WaitHandle)` | Controlled |
| `clockwork.waithandle.signalandwait.milliseconds.exitcontext` | `System.Threading.WaitHandle::SignalAndWait(System.Threading.WaitHandle,System.Threading.WaitHandle,System.Int32,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::SignalAndWait(System.Threading.WaitHandle,System.Threading.WaitHandle,System.Int32,System.Boolean)` | Controlled |
| `clockwork.waithandle.signalandwait.timespan.exitcontext` | `System.Threading.WaitHandle::SignalAndWait(System.Threading.WaitHandle,System.Threading.WaitHandle,System.TimeSpan,System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::SignalAndWait(System.Threading.WaitHandle,System.Threading.WaitHandle,System.TimeSpan,System.Boolean)` | Controlled |
| `clockwork.waithandle.dispose` | `System.Threading.WaitHandle::Dispose()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::Dispose(System.Threading.WaitHandle)` | Controlled |
| `clockwork.waithandle.close` | `System.Threading.WaitHandle::Close()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::Close(System.Threading.WaitHandle)` | Controlled |
| `clockwork.waithandle.get_handle` | `System.Threading.WaitHandle::get_Handle()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::GetHandle(System.Threading.WaitHandle)` | Rejected |
| `clockwork.waithandle.set_handle` | `System.Threading.WaitHandle::set_Handle(System.IntPtr)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::SetHandle(System.Threading.WaitHandle,System.IntPtr)` | Rejected |
| `clockwork.waithandle.get_safewaithandle` | `System.Threading.WaitHandle::get_SafeWaitHandle()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::GetSafeWaitHandle(System.Threading.WaitHandle)` | Rejected |
| `clockwork.waithandle.set_safewaithandle` | `System.Threading.WaitHandle::set_SafeWaitHandle(Microsoft.Win32.SafeHandles.SafeWaitHandle)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledWaitHandle::SetSafeWaitHandle(System.Threading.WaitHandle,Microsoft.Win32.SafeHandles.SafeWaitHandle)` | Rejected |
| `clockwork.eventwaithandle.set` | `System.Threading.EventWaitHandle::Set()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::Set(System.Threading.EventWaitHandle)` | Controlled |
| `clockwork.eventwaithandle.reset` | `System.Threading.EventWaitHandle::Reset()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::Reset(System.Threading.EventWaitHandle)` | Controlled |
| `clockwork.eventwaithandle.openexisting` | `System.Threading.EventWaitHandle::OpenExisting(System.String)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::OpenExisting(System.String)` | Rejected |
| `clockwork.eventwaithandle.openexisting.options` | `System.Threading.EventWaitHandle::OpenExisting(System.String,System.Threading.NamedWaitHandleOptions)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::OpenExisting(System.String,System.Threading.NamedWaitHandleOptions)` | Rejected |
| `clockwork.eventwaithandle.tryopenexisting` | `System.Threading.EventWaitHandle::TryOpenExisting(System.String,System.Threading.EventWaitHandle&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::TryOpenExisting(System.String,System.Threading.EventWaitHandle&)` | Rejected |
| `clockwork.eventwaithandle.tryopenexisting.options` | `System.Threading.EventWaitHandle::TryOpenExisting(System.String,System.Threading.NamedWaitHandleOptions,System.Threading.EventWaitHandle&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledEventWaitHandle::TryOpenExisting(System.String,System.Threading.NamedWaitHandleOptions,System.Threading.EventWaitHandle&)` | Rejected |

## ReaderWriterLockSlim family

Policy: **Controlled**. Every public .NET 10 `ReaderWriterLockSlim` constructor, property, enter/try-enter/exit overload, and `Dispose` member redirects to receiver-first controlled shims. The real BCL instance is only an identity key; logical-strand ownership, recursion, wait queues, and deadlines are modelled without blocking a physical thread.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.readerwriterlockslim.ctor` | `new System.Threading.ReaderWriterLockSlim()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::Create()` | Controlled |
| `clockwork.readerwriterlockslim.ctor.recursionpolicy` | `new System.Threading.ReaderWriterLockSlim(System.Threading.LockRecursionPolicy)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::Create(System.Threading.LockRecursionPolicy)` | Controlled |
| `clockwork.readerwriterlockslim.get_recursionpolicy` | `System.Threading.ReaderWriterLockSlim::get_RecursionPolicy()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::RecursionPolicy(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_currentreadcount` | `System.Threading.ReaderWriterLockSlim::get_CurrentReadCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::CurrentReadCount(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_isreadlockheld` | `System.Threading.ReaderWriterLockSlim::get_IsReadLockHeld()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::IsReadLockHeld(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_isupgradeablereadlockheld` | `System.Threading.ReaderWriterLockSlim::get_IsUpgradeableReadLockHeld()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::IsUpgradeableReadLockHeld(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_iswritelockheld` | `System.Threading.ReaderWriterLockSlim::get_IsWriteLockHeld()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::IsWriteLockHeld(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_recursivereadcount` | `System.Threading.ReaderWriterLockSlim::get_RecursiveReadCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::RecursiveReadCount(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_recursiveupgradecount` | `System.Threading.ReaderWriterLockSlim::get_RecursiveUpgradeCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::RecursiveUpgradeCount(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_recursivewritecount` | `System.Threading.ReaderWriterLockSlim::get_RecursiveWriteCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::RecursiveWriteCount(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_waitingreadcount` | `System.Threading.ReaderWriterLockSlim::get_WaitingReadCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::WaitingReadCount(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_waitingupgradecount` | `System.Threading.ReaderWriterLockSlim::get_WaitingUpgradeCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::WaitingUpgradeCount(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.get_waitingwritecount` | `System.Threading.ReaderWriterLockSlim::get_WaitingWriteCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::WaitingWriteCount(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.enterreadlock` | `System.Threading.ReaderWriterLockSlim::EnterReadLock()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::EnterReadLock(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.tryenterreadlock.milliseconds` | `System.Threading.ReaderWriterLockSlim::TryEnterReadLock(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::TryEnterReadLock(System.Threading.ReaderWriterLockSlim,System.Int32)` | Controlled |
| `clockwork.readerwriterlockslim.tryenterreadlock.timespan` | `System.Threading.ReaderWriterLockSlim::TryEnterReadLock(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::TryEnterReadLock(System.Threading.ReaderWriterLockSlim,System.TimeSpan)` | Controlled |
| `clockwork.readerwriterlockslim.exitreadlock` | `System.Threading.ReaderWriterLockSlim::ExitReadLock()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::ExitReadLock(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.enterupgradeablereadlock` | `System.Threading.ReaderWriterLockSlim::EnterUpgradeableReadLock()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::EnterUpgradeableReadLock(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.tryenterupgradeablereadlock.milliseconds` | `System.Threading.ReaderWriterLockSlim::TryEnterUpgradeableReadLock(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::TryEnterUpgradeableReadLock(System.Threading.ReaderWriterLockSlim,System.Int32)` | Controlled |
| `clockwork.readerwriterlockslim.tryenterupgradeablereadlock.timespan` | `System.Threading.ReaderWriterLockSlim::TryEnterUpgradeableReadLock(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::TryEnterUpgradeableReadLock(System.Threading.ReaderWriterLockSlim,System.TimeSpan)` | Controlled |
| `clockwork.readerwriterlockslim.exitupgradeablereadlock` | `System.Threading.ReaderWriterLockSlim::ExitUpgradeableReadLock()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::ExitUpgradeableReadLock(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.enterwritelock` | `System.Threading.ReaderWriterLockSlim::EnterWriteLock()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::EnterWriteLock(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.tryenterwritelock.milliseconds` | `System.Threading.ReaderWriterLockSlim::TryEnterWriteLock(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::TryEnterWriteLock(System.Threading.ReaderWriterLockSlim,System.Int32)` | Controlled |
| `clockwork.readerwriterlockslim.tryenterwritelock.timespan` | `System.Threading.ReaderWriterLockSlim::TryEnterWriteLock(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::TryEnterWriteLock(System.Threading.ReaderWriterLockSlim,System.TimeSpan)` | Controlled |
| `clockwork.readerwriterlockslim.exitwritelock` | `System.Threading.ReaderWriterLockSlim::ExitWriteLock()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::ExitWriteLock(System.Threading.ReaderWriterLockSlim)` | Controlled |
| `clockwork.readerwriterlockslim.dispose` | `System.Threading.ReaderWriterLockSlim::Dispose()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledReaderWriterLockSlim::Dispose(System.Threading.ReaderWriterLockSlim)` | Controlled |

## ManualResetEventSlim family

Policy: **Controlled**. Every public .NET 10 `ManualResetEventSlim` constructor, property, set/reset/wait overload, and `Dispose` redirects to receiver-first controlled shims. Signal state, waiters, cancellation, deadlines, and the exposed wait-handle bridge are modelled in side state.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.manualreseteventslim.ctor` | `new System.Threading.ManualResetEventSlim()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Create()` | Controlled |
| `clockwork.manualreseteventslim.ctor.initialstate` | `new System.Threading.ManualResetEventSlim(System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Create(System.Boolean)` | Controlled |
| `clockwork.manualreseteventslim.ctor.initialstate.spincount` | `new System.Threading.ManualResetEventSlim(System.Boolean,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Create(System.Boolean,System.Int32)` | Controlled |
| `clockwork.manualreseteventslim.get_isset` | `System.Threading.ManualResetEventSlim::get_IsSet()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::IsSet(System.Threading.ManualResetEventSlim)` | Controlled |
| `clockwork.manualreseteventslim.get_spincount` | `System.Threading.ManualResetEventSlim::get_SpinCount()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::SpinCount(System.Threading.ManualResetEventSlim)` | Controlled |
| `clockwork.manualreseteventslim.get_waithandle` | `System.Threading.ManualResetEventSlim::get_WaitHandle()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::WaitHandle(System.Threading.ManualResetEventSlim)` | Controlled |
| `clockwork.manualreseteventslim.set` | `System.Threading.ManualResetEventSlim::Set()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Set(System.Threading.ManualResetEventSlim)` | Controlled |
| `clockwork.manualreseteventslim.reset` | `System.Threading.ManualResetEventSlim::Reset()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Reset(System.Threading.ManualResetEventSlim)` | Controlled |
| `clockwork.manualreseteventslim.wait` | `System.Threading.ManualResetEventSlim::Wait()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Wait(System.Threading.ManualResetEventSlim)` | Controlled |
| `clockwork.manualreseteventslim.wait.cancellationtoken` | `System.Threading.ManualResetEventSlim::Wait(System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Wait(System.Threading.ManualResetEventSlim,System.Threading.CancellationToken)` | Controlled |
| `clockwork.manualreseteventslim.wait.milliseconds` | `System.Threading.ManualResetEventSlim::Wait(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Wait(System.Threading.ManualResetEventSlim,System.Int32)` | Controlled |
| `clockwork.manualreseteventslim.wait.milliseconds.cancellationtoken` | `System.Threading.ManualResetEventSlim::Wait(System.Int32,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Wait(System.Threading.ManualResetEventSlim,System.Int32,System.Threading.CancellationToken)` | Controlled |
| `clockwork.manualreseteventslim.wait.timespan` | `System.Threading.ManualResetEventSlim::Wait(System.TimeSpan)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Wait(System.Threading.ManualResetEventSlim,System.TimeSpan)` | Controlled |
| `clockwork.manualreseteventslim.wait.timespan.cancellationtoken` | `System.Threading.ManualResetEventSlim::Wait(System.TimeSpan,System.Threading.CancellationToken)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Wait(System.Threading.ManualResetEventSlim,System.TimeSpan,System.Threading.CancellationToken)` | Controlled |
| `clockwork.manualreseteventslim.dispose` | `System.Threading.ManualResetEventSlim::Dispose()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledManualResetEventSlim::Dispose(System.Threading.ManualResetEventSlim)` | Controlled |

## Mutex family

Policy: **Controlled**. Unnamed `Mutex` construction and `ReleaseMutex` are controlled through the wait-handle kernel. Named constructors (including null-name forms that the shim conditionally treats as unnamed) and `OpenExisting`/`TryOpenExisting` are classified Rejected because a non-null name is cross-process kernel state. Ownership and recursion are logical-strand state; owner exit without `ReleaseMutex` leaves the mutex owned so a later indefinite wait reports the controlled deadlock diagnostic rather than simulating `AbandonedMutexException`.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.mutex.ctor` | `new System.Threading.Mutex()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::Create()` | Controlled |
| `clockwork.mutex.ctor.initiallyowned` | `new System.Threading.Mutex(System.Boolean)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::Create(System.Boolean)` | Controlled |
| `clockwork.mutex.release` | `System.Threading.Mutex::ReleaseMutex()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::ReleaseMutex(System.Threading.Mutex)` | Controlled |
| `clockwork.mutex.ctor.named` | `new System.Threading.Mutex(System.Boolean,System.String)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::CreateNamed(System.Boolean,System.String)` | Rejected |
| `clockwork.mutex.ctor.named.creatednew` | `new System.Threading.Mutex(System.Boolean,System.String,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::CreateNamed(System.Boolean,System.String,System.Boolean&)` | Rejected |
| `clockwork.mutex.ctor.named.options` | `new System.Threading.Mutex(System.Boolean,System.String,System.Threading.NamedWaitHandleOptions)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::CreateNamed(System.Boolean,System.String,System.Threading.NamedWaitHandleOptions)` | Rejected |
| `clockwork.mutex.ctor.named.options.creatednew` | `new System.Threading.Mutex(System.Boolean,System.String,System.Threading.NamedWaitHandleOptions,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::CreateNamed(System.Boolean,System.String,System.Threading.NamedWaitHandleOptions,System.Boolean&)` | Rejected |
| `clockwork.mutex.ctor.name.options` | `new System.Threading.Mutex(System.String,System.Threading.NamedWaitHandleOptions)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::CreateNamed(System.String,System.Threading.NamedWaitHandleOptions)` | Rejected |
| `clockwork.mutex.openexisting` | `System.Threading.Mutex::OpenExisting(System.String)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::OpenExisting(System.String)` | Rejected |
| `clockwork.mutex.openexisting.options` | `System.Threading.Mutex::OpenExisting(System.String,System.Threading.NamedWaitHandleOptions)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::OpenExisting(System.String,System.Threading.NamedWaitHandleOptions)` | Rejected |
| `clockwork.mutex.tryopenexisting` | `System.Threading.Mutex::TryOpenExisting(System.String,System.Threading.Mutex&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::TryOpenExisting(System.String,System.Threading.Mutex&)` | Rejected |
| `clockwork.mutex.tryopenexisting.options` | `System.Threading.Mutex::TryOpenExisting(System.String,System.Threading.NamedWaitHandleOptions,System.Threading.Mutex&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledMutex::TryOpenExisting(System.String,System.Threading.NamedWaitHandleOptions,System.Threading.Mutex&)` | Rejected |

## KernelSemaphore family

Policy: **Controlled**. The unnamed kernel `Semaphore` constructor and both `Release` overloads are controlled through the wait-handle kernel. Named constructors and `OpenExisting`/`TryOpenExisting` are Rejected because cross-process semaphore state cannot be represented by one simulation.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.semaphore.ctor` | `new System.Threading.Semaphore(System.Int32,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::Create(System.Int32,System.Int32)` | Controlled |
| `clockwork.semaphore.release` | `System.Threading.Semaphore::Release()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::Release(System.Threading.Semaphore)` | Controlled |
| `clockwork.semaphore.release.count` | `System.Threading.Semaphore::Release(System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::Release(System.Threading.Semaphore,System.Int32)` | Controlled |
| `clockwork.semaphore.ctor.named` | `new System.Threading.Semaphore(System.Int32,System.Int32,System.String)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::CreateNamed(System.Int32,System.Int32,System.String)` | Rejected |
| `clockwork.semaphore.ctor.named.creatednew` | `new System.Threading.Semaphore(System.Int32,System.Int32,System.String,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::CreateNamed(System.Int32,System.Int32,System.String,System.Boolean&)` | Rejected |
| `clockwork.semaphore.ctor.named.options` | `new System.Threading.Semaphore(System.Int32,System.Int32,System.String,System.Threading.NamedWaitHandleOptions)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::CreateNamed(System.Int32,System.Int32,System.String,System.Threading.NamedWaitHandleOptions)` | Rejected |
| `clockwork.semaphore.ctor.named.options.creatednew` | `new System.Threading.Semaphore(System.Int32,System.Int32,System.String,System.Threading.NamedWaitHandleOptions,System.Boolean&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::CreateNamed(System.Int32,System.Int32,System.String,System.Threading.NamedWaitHandleOptions,System.Boolean&)` | Rejected |
| `clockwork.semaphore.openexisting` | `System.Threading.Semaphore::OpenExisting(System.String)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::OpenExisting(System.String)` | Rejected |
| `clockwork.semaphore.openexisting.options` | `System.Threading.Semaphore::OpenExisting(System.String,System.Threading.NamedWaitHandleOptions)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::OpenExisting(System.String,System.Threading.NamedWaitHandleOptions)` | Rejected |
| `clockwork.semaphore.tryopenexisting` | `System.Threading.Semaphore::TryOpenExisting(System.String,System.Threading.Semaphore&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::TryOpenExisting(System.String,System.Threading.Semaphore&)` | Rejected |
| `clockwork.semaphore.tryopenexisting.options` | `System.Threading.Semaphore::TryOpenExisting(System.String,System.Threading.NamedWaitHandleOptions,System.Threading.Semaphore&)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSemaphore::TryOpenExisting(System.String,System.Threading.NamedWaitHandleOptions,System.Threading.Semaphore&)` | Rejected |

## SpinLock family

Policy: **Controlled**. `SpinLock` is wholly substituted with `ControlledSpinLock`, preserving its value-type surface while replacing CPU spinning with deterministic scheduler pumping.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.spinlock.type` | `System.Threading.SpinLock` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSpinLock` | Controlled |

## ExecutionContext family

Policy: **Controlled**. `ExecutionContext` capture, run, flow-control, copy, and disposal members redirect to controlled shims. The legacy `GetObjectData` serialization surface is Rejected before it can invoke BCL serialization behavior.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.executioncontext.capture` | `System.Threading.ExecutionContext::Capture()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledExecutionContext::Capture()` | Controlled |
| `clockwork.executioncontext.run` | `System.Threading.ExecutionContext::Run(System.Threading.ExecutionContext,System.Threading.ContextCallback,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledExecutionContext::Run(System.Threading.ExecutionContext,System.Threading.ContextCallback,System.Object)` | Controlled |
| `clockwork.executioncontext.suppressflow` | `System.Threading.ExecutionContext::SuppressFlow()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledExecutionContext::SuppressFlow()` | Controlled |
| `clockwork.executioncontext.restoreflow` | `System.Threading.ExecutionContext::RestoreFlow()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledExecutionContext::RestoreFlow()` | Controlled |
| `clockwork.executioncontext.isflowsuppressed` | `System.Threading.ExecutionContext::IsFlowSuppressed()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledExecutionContext::IsFlowSuppressed()` | Controlled |
| `clockwork.executioncontext.restore` | `System.Threading.ExecutionContext::Restore(System.Threading.ExecutionContext)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledExecutionContext::Restore(System.Threading.ExecutionContext)` | Controlled |
| `clockwork.executioncontext.createcopy` | `System.Threading.ExecutionContext::CreateCopy()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledExecutionContext::CreateCopy(System.Threading.ExecutionContext)` | Controlled |
| `clockwork.executioncontext.dispose` | `System.Threading.ExecutionContext::Dispose()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledExecutionContext::Dispose(System.Threading.ExecutionContext)` | Controlled |
| `clockwork.executioncontext.getobjectdata` | `System.Threading.ExecutionContext::GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledExecutionContext::GetObjectData(System.Threading.ExecutionContext,System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext)` | Rejected |

## SynchronizationContext family

Policy: **Controlled**. `SynchronizationContext` ambient-context and callback-dispatch members redirect to controlled shims. `Post` queues through the coordinator and `Send` runs on the current logical strand; custom context dispatch is not invoked. Its raw native-handle `Wait` member is Rejected before it can block a physical thread.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.synchronizationcontext.get_current` | `System.Threading.SynchronizationContext::get_Current()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSynchronizationContext::Current()` | Controlled |
| `clockwork.synchronizationcontext.set_current` | `System.Threading.SynchronizationContext::SetSynchronizationContext(System.Threading.SynchronizationContext)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSynchronizationContext::SetSynchronizationContext(System.Threading.SynchronizationContext)` | Controlled |
| `clockwork.synchronizationcontext.createcopy` | `System.Threading.SynchronizationContext::CreateCopy()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSynchronizationContext::CreateCopy(System.Threading.SynchronizationContext)` | Controlled |
| `clockwork.synchronizationcontext.iswaitnotificationrequired` | `System.Threading.SynchronizationContext::IsWaitNotificationRequired()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSynchronizationContext::IsWaitNotificationRequired(System.Threading.SynchronizationContext)` | Controlled |
| `clockwork.synchronizationcontext.operationstarted` | `System.Threading.SynchronizationContext::OperationStarted()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSynchronizationContext::OperationStarted(System.Threading.SynchronizationContext)` | Controlled |
| `clockwork.synchronizationcontext.operationcompleted` | `System.Threading.SynchronizationContext::OperationCompleted()` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSynchronizationContext::OperationCompleted(System.Threading.SynchronizationContext)` | Controlled |
| `clockwork.synchronizationcontext.post` | `System.Threading.SynchronizationContext::Post(System.Threading.SendOrPostCallback,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSynchronizationContext::Post(System.Threading.SynchronizationContext,System.Threading.SendOrPostCallback,System.Object)` | Controlled |
| `clockwork.synchronizationcontext.send` | `System.Threading.SynchronizationContext::Send(System.Threading.SendOrPostCallback,System.Object)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSynchronizationContext::Send(System.Threading.SynchronizationContext,System.Threading.SendOrPostCallback,System.Object)` | Controlled |
| `clockwork.synchronizationcontext.wait` | `System.Threading.SynchronizationContext::Wait(System.IntPtr[],System.Boolean,System.Int32)` | `Clockwork!Clockwork.Shims.System.Threading.ControlledSynchronizationContext::Wait(System.Threading.SynchronizationContext,System.IntPtr[],System.Boolean,System.Int32)` | Rejected |

## Barrier family

Policy: **Controlled**. `Barrier` is wholly substituted with `ControlledBarrier`, including generic occurrences such as `Action<Barrier>`, so participant state and post-phase callbacks remain under simulation.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.barrier.type` | `System.Threading.Barrier` | `Clockwork!Clockwork.Shims.System.Threading.ControlledBarrier` | Controlled |

## CountdownEvent family

Policy: **Controlled**. `CountdownEvent` is wholly substituted with `ControlledCountdownEvent`, so all count updates, waits, bridge handles, and disposal run under the deterministic scheduler.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.countdownevent.type` | `System.Threading.CountdownEvent` | `Clockwork!Clockwork.Shims.System.Threading.ControlledCountdownEvent` | Controlled |

## UncontrolledInvocation family

Policy: **Rejected**. Process control and abrupt-termination APIs (`Process.Start`/`Start` instance/`Kill`/`WaitForExit`/`WaitForExitAsync`, `Environment.Exit`/`FailFast`) cannot be modelled inside a single simulated process at all. A throwing guard is injected before each call site so a rewritten assembly can never launch, kill, wait on, or terminate a real OS process; unlike the controlled shims the rejection is unconditional (it fires whether or not a simulation is active).

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.process.start.filename` | `System.Diagnostics.Process::Start(System.String)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.startinfo` | `System.Diagnostics.Process::Start(System.Diagnostics.ProcessStartInfo)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.filename.arguments` | `System.Diagnostics.Process::Start(System.String,System.String)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.filename.argumentlist` | `System.Diagnostics.Process::Start(System.String,System.Collections.Generic.IEnumerable`1<System.String>)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.filename.credentials` | `System.Diagnostics.Process::Start(System.String,System.String,System.Security.SecureString,System.String)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.filename.arguments.credentials` | `System.Diagnostics.Process::Start(System.String,System.String,System.String,System.Security.SecureString,System.String)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.instance` | `System.Diagnostics.Process::Start()` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.kill` | `System.Diagnostics.Process::Kill()` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.kill.tree` | `System.Diagnostics.Process::Kill(System.Boolean)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.waitforexit` | `System.Diagnostics.Process::WaitForExit()` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.waitforexit.milliseconds` | `System.Diagnostics.Process::WaitForExit(System.Int32)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.waitforexit.timespan` | `System.Diagnostics.Process::WaitForExit(System.TimeSpan)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.waitforexitasync` | `System.Diagnostics.Process::WaitForExitAsync(System.Threading.CancellationToken)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.environment.exit` | `System.Environment::Exit(System.Int32)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.environment.failfast.message` | `System.Environment::FailFast(System.String)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.environment.failfast.exception` | `System.Environment::FailFast(System.String,System.Exception)` | `Clockwork!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |

# Race exploration instrumentation inventory

This inventory is enabled only when instrumentation mode is `RaceExploration`; `Controlled` mode injects none of these calls.

| Surface | Instrumentation | Tracked identity |
| --- | --- | --- |
| `ldfld` / `stfld` on reference types | Read/write scheduling point | Weak object identity + field member |
| `ldsfld` / `stsfld` | Read/write scheduling point | Static field member |
| volatile field access | Schedule-only point before the `volatile.` prefix | Not race-tracked |
| `ldelem.*` / `stelem.*` vector arrays | Read/write scheduling point | Weak array identity + element index |
| field-address, indirect/object, and `ldelema` access | Schedule-only point | Not race-tracked |
| `brtrue` / `brfalse` | Control-flow scheduling point | n/a |
| `List<T>`, `Dictionary<TKey,TValue>`, `HashSet<T>` direct concrete members | Read/write/iteration point after the original call | Weak collection identity |
| `ConcurrentBag<T>`, `ConcurrentDictionary<TKey,TValue>`, `ConcurrentQueue<T>`, `ConcurrentStack<T>` direct concrete members | Interleaving point after the original call | Not reported as a race |

Limits: constructors and property accessors are excluded; generated `MoveNext` methods are visited, with generated value-type state fields schedule-only. Multidimensional arrays, interface-typed and tail-prefixed collection calls, reflection/dynamic dispatch, spans, unmanaged memory, and arbitrary pointer offsets are not assigned tracked locations.

## Documented holes (not rewritten in these rule sets)

These nondeterministic or entropy-drawing surfaces are intentionally **not** covered and
remain real BCL calls even under simulation:

- `Stopwatch` instance APIs (`Start`/`Stop`/`Restart`/`Elapsed`/`ElapsedMilliseconds`/`ElapsedTicks`) remain uncontrolled because their mutable lifecycle would require whole-type substitution. `GetElapsedTime(long, long)` is intentionally not rewritten or analyzed: it is deterministic arithmetic over caller-supplied timestamps (use controlled `GetTimestamp()` values).
- Any `RandomNumberGenerator` overloads beyond those listed above.
- `DateTime`/`DateTimeOffset` parsing/formatting and any culture-, timezone-, or kind-conversion helpers other than the `Now`/`UtcNow`/`Today` clocks above.
- Synchronous blocking on `ValueTask`/`ValueTask<T>` (`.Result`/`.GetResult()` outside an awaiter): a value task may be consumed only once, so a blocking drain is unsafe. `await` is the supported controlled path.
- Named/cross-process synchronization (named `EventWaitHandle`/`Mutex`/`Semaphore` and their `OpenExisting`/`TryOpenExisting` APIs): a single-process simulation cannot model kernel-object sharing, so these are rejected.
- Custom `TimeProvider` implementations are rejected by timer-consuming controlled APIs unless Clockwork explicitly recognizes them. `System.Timers.Timer` rejects non-null `SynchronizingObject` and designer `Site` integration because those paths can marshal callbacks to uncontrolled UI or native threads. `Timer.Dispose(WaitHandle)` accepts controlled event handles only.

Determinism is claimed **only** for the exact rules tabulated above.

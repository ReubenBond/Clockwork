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

Policy: **Controlled**. `Task.WhenAll`/`WhenAny` (the non-generic `Task[]`, `IEnumerable<Task>`, .NET 9+ params `ReadOnlySpan<Task>`, and two-argument overloads, plus their generic `Task<TResult>` counterparts) and the `TaskExtensions.Unwrap` extension methods redirect to controlled combinators. Completion and the returned winner become a deterministic function of when the antecedents complete on the logical thread instead of a physical thread-pool race.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.whenall.array` | `System.Threading.Tasks.Task::WhenAll(System.Threading.Tasks.Task[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.whenall.span` | `System.Threading.Tasks.Task::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenall.enumerable` | `System.Threading.Tasks.Task::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenany.array` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.whenany.span` | `System.Threading.Tasks.Task::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenany.pair` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task,System.Threading.Tasks.Task)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task,System.Threading.Tasks.Task)` | Controlled |
| `clockwork.tasks.whenany.enumerable` | `System.Threading.Tasks.Task::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.whenall.generic.array` | `System.Threading.Tasks.Task::WhenAll(System.Threading.Tasks.Task`1<!!0>[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.Threading.Tasks.Task`1<TResult>[])` | Controlled |
| `clockwork.tasks.whenall.generic.span` | `System.Threading.Tasks.Task::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.whenall.generic.enumerable` | `System.Threading.Tasks.Task::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.whenany.generic.array` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task`1<!!0>[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task`1<TResult>[])` | Controlled |
| `clockwork.tasks.whenany.generic.span` | `System.Threading.Tasks.Task::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.whenany.generic.pair` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task`1<!!0>,System.Threading.Tasks.Task`1<!!0>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task`1<TResult>,System.Threading.Tasks.Task`1<TResult>)` | Controlled |
| `clockwork.tasks.whenany.generic.enumerable` | `System.Threading.Tasks.Task::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.unwrap` | `System.Threading.Tasks.TaskExtensions::Unwrap(System.Threading.Tasks.Task`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Unwrap(System.Threading.Tasks.Task`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.unwrap.generic` | `System.Threading.Tasks.TaskExtensions::Unwrap(System.Threading.Tasks.Task`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Unwrap(System.Threading.Tasks.Task`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |

## TaskSynchronization family

Policy: **Controlled**. Blocking `Task.Wait()`, `Task.WaitAll`, and `Task.WaitAny` redirect to controlled waits that pump the deterministic loop rather than blocking a physical thread; a never-satisfiable wait surfaces as a precise deadlock diagnostic instead of hanging the scheduler.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.wait.instance` | `System.Threading.Tasks.Task::Wait()` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Wait(System.Threading.Tasks.Task)` | Controlled |
| `clockwork.tasks.waitall.array` | `System.Threading.Tasks.Task::WaitAll(System.Threading.Tasks.Task[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WaitAll(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.waitany.array` | `System.Threading.Tasks.Task::WaitAny(System.Threading.Tasks.Task[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WaitAny(System.Threading.Tasks.Task[])` | Controlled |
| `clockwork.tasks.result.generic` | `System.Threading.Tasks.Task`1::get_Result()` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Result(System.Threading.Tasks.Task`1<TResult>)` | Controlled |

## TaskContinuations family

Policy: **Controlled**. `Task.ContinueWith(Action<Task>)`, `Task<T>.ContinueWith(Action<Task<T>>)`, and the result-producing `Task<T>.ContinueWith<TNewResult>(Func<Task<T>,TNewResult>)` redirect so the continuation is scheduled on the controlled coordinator and runs on the logical thread after the antecedent completes.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.continuewith.action` | `System.Threading.Tasks.Task::ContinueWith(System.Action`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::ContinueWith(System.Threading.Tasks.Task,System.Action`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.continuewith.generic.action` | `System.Threading.Tasks.Task`1::ContinueWith(System.Action`1<System.Threading.Tasks.Task`1<!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::ContinueWith(System.Threading.Tasks.Task`1<TResult>,System.Action`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.continuewith.generic.func` | `System.Threading.Tasks.Task`1::ContinueWith(System.Func`2<System.Threading.Tasks.Task`1<!0>,!!0>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::ContinueWith(System.Threading.Tasks.Task`1<TResult>,System.Func`2<System.Threading.Tasks.Task`1<TResult>,TNewResult>)` | Controlled |

## TaskDeferred family

Policy: **Rejected**. `Task.Delay` (virtual timers, Phase 8) is rejected under simulation with a precise diagnostic rather than silently using wall time. Outside simulation it runs the real BCL API unchanged.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.delay.milliseconds` | `System.Threading.Tasks.Task::Delay(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Delay(System.Int32)` | Rejected |

## TaskScheduling family

Policy: **Controlled**. `Task.Run` (all `Action`/`Func<TResult>`/`Func<Task>`/`Func<Task<TResult>>` overloads, with and without a `CancellationToken`) offloads work that Phase 6A left uncontrolled onto the thread pool. Each overload redirects to a controlled equivalent that schedules the delegate as a controlled operation on the simulation coordinator, preserving cancellation and unwrap semantics; outside simulation it runs the real BCL API unchanged.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.run.action` | `System.Threading.Tasks.Task::Run(System.Action)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Run(System.Action)` | Controlled |
| `clockwork.tasks.run.action.cancellationtoken` | `System.Threading.Tasks.Task::Run(System.Action,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Run(System.Action,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.run.func` | `System.Threading.Tasks.Task::Run(System.Func`1<!!0>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Run(System.Func`1<TResult>)` | Controlled |
| `clockwork.tasks.run.func.cancellationtoken` | `System.Threading.Tasks.Task::Run(System.Func`1<!!0>,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Run(System.Func`1<TResult>,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.run.func.task` | `System.Threading.Tasks.Task::Run(System.Func`1<System.Threading.Tasks.Task>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Run(System.Func`1<System.Threading.Tasks.Task>)` | Controlled |
| `clockwork.tasks.run.func.task.cancellationtoken` | `System.Threading.Tasks.Task::Run(System.Func`1<System.Threading.Tasks.Task>,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Run(System.Func`1<System.Threading.Tasks.Task>,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.run.func.task.generic` | `System.Threading.Tasks.Task::Run(System.Func`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Run(System.Func`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.run.func.task.generic.cancellationtoken` | `System.Threading.Tasks.Task::Run(System.Func`1<System.Threading.Tasks.Task`1<!!0>>,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::Run(System.Func`1<System.Threading.Tasks.Task`1<TResult>>,System.Threading.CancellationToken)` | Controlled |

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

## ValueTaskMachinery family

Policy: **Controlled**. The compiler-generated builder and awaiter types of an `async ValueTask`/`async ValueTask<T>` state machine (`AsyncValueTaskMethodBuilder`, `ValueTaskAwaiter`, `ConfiguredValueTaskAwaitable` and their awaiters, generic and non-generic) are substituted onto Clockwork's controlled equivalents by the member-aware pass, so every awaited value-task continuation is scheduled through the simulation coordinator. `ConfigureAwait(false)` stays controlled in simulation while preserving normal semantics outside. Synchronous blocking on a value task is not rewritten (a value task may be consumed only once); `await` is the supported controlled path.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.builder.valuetask` | `System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledAsyncValueTaskMethodBuilder` | Controlled |
| `clockwork.tasks.builder.valuetask.generic` | `System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder`1` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledAsyncValueTaskMethodBuilder`1` | Controlled |
| `clockwork.tasks.awaiter.valuetask` | `System.Runtime.CompilerServices.ValueTaskAwaiter` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledValueTaskAwaiter` | Controlled |
| `clockwork.tasks.awaiter.valuetask.generic` | `System.Runtime.CompilerServices.ValueTaskAwaiter`1` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledValueTaskAwaiter`1` | Controlled |
| `clockwork.tasks.configured.valuetask.awaitable` | `System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledConfiguredValueTaskAwaitable` | Controlled |
| `clockwork.tasks.configured.valuetask.awaitable.generic` | `System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledConfiguredValueTaskAwaitable`1` | Controlled |
| `clockwork.tasks.configured.valuetask.awaiter` | `System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable/ConfiguredValueTaskAwaiter` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledConfiguredValueTaskAwaiter` | Controlled |
| `clockwork.tasks.configured.valuetask.awaiter.generic` | `System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1/ConfiguredValueTaskAwaiter` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.CompilerServices.ControlledConfiguredValueTaskAwaiter`1` | Controlled |

## TaskFactory family

Policy: **Controlled**. `TaskFactory.StartNew` and `TaskFactory<T>.StartNew` (the `Action`/`Func<TResult>` overloads with and without a `CancellationToken` or `TaskCreationOptions`) offload work onto a task scheduler that Phase 6A left uncontrolled. Each redirects to a controlled equivalent that schedules the delegate as a controlled operation on the simulation coordinator; `TaskCreationOptions` are honoured where they have a controlled meaning and an unsupported combination is rejected with a precise diagnostic. Outside simulation they run the real BCL API unchanged.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.tasks.factory.startnew.action` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action)` | Controlled |
| `clockwork.tasks.factory.startnew.action.cancellationtoken` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.factory.startnew.action.options` | `System.Threading.Tasks.TaskFactory::StartNew(System.Action,System.Threading.Tasks.TaskCreationOptions)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Action,System.Threading.Tasks.TaskCreationOptions)` | Controlled |
| `clockwork.tasks.factory.startnew.func` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`1<!!0>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`1<TResult>)` | Controlled |
| `clockwork.tasks.factory.startnew.func.cancellationtoken` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`1<!!0>,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`1<TResult>,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.factory.startnew.func.options` | `System.Threading.Tasks.TaskFactory::StartNew(System.Func`1<!!0>,System.Threading.Tasks.TaskCreationOptions)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory,System.Func`1<TResult>,System.Threading.Tasks.TaskCreationOptions)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`1<!0>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`1<TResult>)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func.cancellationtoken` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`1<!0>,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`1<TResult>,System.Threading.CancellationToken)` | Controlled |
| `clockwork.tasks.factory.generic.startnew.func.options` | `System.Threading.Tasks.TaskFactory`1::StartNew(System.Func`1<!0>,System.Threading.Tasks.TaskCreationOptions)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTaskFactory::StartNew(System.Threading.Tasks.TaskFactory`1<TResult>,System.Func`1<TResult>,System.Threading.Tasks.TaskCreationOptions)` | Controlled |

## Thread family

Policy: **Controlled**. `Thread` construction (`ThreadStart`/`ParameterizedThreadStart`, with and without a stack size), `Start`, `Join` (all overloads), `Sleep`, `Yield`, and `SpinWait` redirect to a controlled thread that maps each thread to a controlled operation on the simulation coordinator; `Join`/`Sleep` yield the logical thread via the deterministic loop rather than blocking a physical thread or consuming real time. OS-specific priority, apartment-state, and `Interrupt` operations cannot be modelled faithfully and are rejected with a precise diagnostic. Outside simulation the shims run the real BCL `Thread` unchanged.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.thread.ctor.threadstart` | `new System.Threading.Thread(System.Threading.ThreadStart)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Create(System.Threading.ThreadStart)` | Controlled |
| `clockwork.thread.ctor.threadstart.stacksize` | `new System.Threading.Thread(System.Threading.ThreadStart,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Create(System.Threading.ThreadStart,System.Int32)` | Controlled |
| `clockwork.thread.ctor.parameterized` | `new System.Threading.Thread(System.Threading.ParameterizedThreadStart)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Create(System.Threading.ParameterizedThreadStart)` | Controlled |
| `clockwork.thread.ctor.parameterized.stacksize` | `new System.Threading.Thread(System.Threading.ParameterizedThreadStart,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Create(System.Threading.ParameterizedThreadStart,System.Int32)` | Controlled |
| `clockwork.thread.start` | `System.Threading.Thread::Start()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Start(System.Threading.Thread)` | Controlled |
| `clockwork.thread.start.parameter` | `System.Threading.Thread::Start(System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Start(System.Threading.Thread,System.Object)` | Controlled |
| `clockwork.thread.join` | `System.Threading.Thread::Join()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Join(System.Threading.Thread)` | Controlled |
| `clockwork.thread.join.milliseconds` | `System.Threading.Thread::Join(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Join(System.Threading.Thread,System.Int32)` | Controlled |
| `clockwork.thread.join.timespan` | `System.Threading.Thread::Join(System.TimeSpan)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Join(System.Threading.Thread,System.TimeSpan)` | Controlled |
| `clockwork.thread.sleep.milliseconds` | `System.Threading.Thread::Sleep(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Sleep(System.Int32)` | Controlled |
| `clockwork.thread.sleep.timespan` | `System.Threading.Thread::Sleep(System.TimeSpan)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Sleep(System.TimeSpan)` | Controlled |
| `clockwork.thread.spinwait` | `System.Threading.Thread::SpinWait(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::SpinWait(System.Int32)` | Controlled |
| `clockwork.thread.yield` | `System.Threading.Thread::Yield()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Yield()` | Controlled |
| `clockwork.thread.set_priority` | `System.Threading.Thread::set_Priority(System.Threading.ThreadPriority)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::SetPriority(System.Threading.Thread,System.Threading.ThreadPriority)` | Rejected |
| `clockwork.thread.interrupt` | `System.Threading.Thread::Interrupt()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::Interrupt(System.Threading.Thread)` | Rejected |
| `clockwork.thread.setapartmentstate` | `System.Threading.Thread::SetApartmentState(System.Threading.ApartmentState)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::SetApartmentState(System.Threading.Thread,System.Threading.ApartmentState)` | Rejected |
| `clockwork.thread.trysetapartmentstate` | `System.Threading.Thread::TrySetApartmentState(System.Threading.ApartmentState)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThread::TrySetApartmentState(System.Threading.Thread,System.Threading.ApartmentState)` | Rejected |

## ThreadPool family

Policy: **Controlled**. `ThreadPool.QueueUserWorkItem` (the `WaitCallback`, `WaitCallback`+state, and generic `Action<TState>`+state+preferLocal forms) and `UnsafeQueueUserWorkItem` (the `WaitCallback`+state, `IThreadPoolWorkItem`, and generic forms) queue the callback as a controlled operation on the simulation coordinator; the safe variants flow `ExecutionContext` while the unsafe variants do not, matching the BCL. `UnsafeQueueNativeOverlapped` and the registered-wait APIs (`RegisterWaitForSingleObject`/`UnsafeRegisterWaitForSingleObject`) depend on native I/O and wait-handle primitives that arrive in Phase 7, so they are rejected with a precise diagnostic.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.threadpool.queue.waitcallback` | `System.Threading.ThreadPool::QueueUserWorkItem(System.Threading.WaitCallback)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::QueueUserWorkItem(System.Threading.WaitCallback)` | Controlled |
| `clockwork.threadpool.queue.waitcallback.state` | `System.Threading.ThreadPool::QueueUserWorkItem(System.Threading.WaitCallback,System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::QueueUserWorkItem(System.Threading.WaitCallback,System.Object)` | Controlled |
| `clockwork.threadpool.queue.generic` | `System.Threading.ThreadPool::QueueUserWorkItem(System.Action`1<!!0>,!!0,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::QueueUserWorkItem(System.Action`1<TState>,TState,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsafequeue.waitcallback.state` | `System.Threading.ThreadPool::UnsafeQueueUserWorkItem(System.Threading.WaitCallback,System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::UnsafeQueueUserWorkItem(System.Threading.WaitCallback,System.Object)` | Controlled |
| `clockwork.threadpool.unsafequeue.workitem` | `System.Threading.ThreadPool::UnsafeQueueUserWorkItem(System.Threading.IThreadPoolWorkItem,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::UnsafeQueueUserWorkItem(System.Threading.IThreadPoolWorkItem,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsafequeue.generic` | `System.Threading.ThreadPool::UnsafeQueueUserWorkItem(System.Action`1<!!0>,!!0,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::UnsafeQueueUserWorkItem(System.Action`1<TState>,TState,System.Boolean)` | Controlled |
| `clockwork.threadpool.unsafequeuenativeoverlapped` | `System.Threading.ThreadPool::UnsafeQueueNativeOverlapped(System.Threading.NativeOverlapped*)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::RejectNativeOverlapped(System.String)` | Rejected |
| `clockwork.threadpool.registerwait.uint32` | `System.Threading.ThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.UInt32,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::RejectRegisteredWait(System.String)` | Rejected |
| `clockwork.threadpool.registerwait.int32` | `System.Threading.ThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int32,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::RejectRegisteredWait(System.String)` | Rejected |
| `clockwork.threadpool.registerwait.int64` | `System.Threading.ThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int64,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::RejectRegisteredWait(System.String)` | Rejected |
| `clockwork.threadpool.registerwait.timespan` | `System.Threading.ThreadPool::RegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.TimeSpan,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::RejectRegisteredWait(System.String)` | Rejected |
| `clockwork.threadpool.unsaferegisterwait.uint32` | `System.Threading.ThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.UInt32,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::RejectRegisteredWait(System.String)` | Rejected |
| `clockwork.threadpool.unsaferegisterwait.int32` | `System.Threading.ThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int32,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::RejectRegisteredWait(System.String)` | Rejected |
| `clockwork.threadpool.unsaferegisterwait.int64` | `System.Threading.ThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.Int64,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::RejectRegisteredWait(System.String)` | Rejected |
| `clockwork.threadpool.unsaferegisterwait.timespan` | `System.Threading.ThreadPool::UnsafeRegisterWaitForSingleObject(System.Threading.WaitHandle,System.Threading.WaitOrTimerCallback,System.Object,System.TimeSpan,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledThreadPool::RejectRegisteredWait(System.String)` | Rejected |

## Parallel family

Policy: **Controlled**. `Parallel.Invoke`, `Parallel.For` (`int`/`long`, with and without `ParallelOptions`), and `Parallel.ForEach(IEnumerable<T>)` run their bodies as controlled operations on the simulation coordinator, preserving results, cancellation, and exception aggregation. The `ParallelLoopState` break/stop overloads cannot be modelled deterministically yet and are rejected with a precise diagnostic.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.parallel.invoke` | `System.Threading.Tasks.Parallel::Invoke(System.Action[])` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::Invoke(System.Action[])` | Controlled |
| `clockwork.parallel.invoke.options` | `System.Threading.Tasks.Parallel::Invoke(System.Threading.Tasks.ParallelOptions,System.Action[])` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::Invoke(System.Threading.Tasks.ParallelOptions,System.Action[])` | Controlled |
| `clockwork.parallel.for.int32` | `System.Threading.Tasks.Parallel::For(System.Int32,System.Int32,System.Action`1<System.Int32>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::For(System.Int32,System.Int32,System.Action`1<System.Int32>)` | Controlled |
| `clockwork.parallel.for.int32.options` | `System.Threading.Tasks.Parallel::For(System.Int32,System.Int32,System.Threading.Tasks.ParallelOptions,System.Action`1<System.Int32>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::For(System.Int32,System.Int32,System.Threading.Tasks.ParallelOptions,System.Action`1<System.Int32>)` | Controlled |
| `clockwork.parallel.for.int64` | `System.Threading.Tasks.Parallel::For(System.Int64,System.Int64,System.Action`1<System.Int64>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::For(System.Int64,System.Int64,System.Action`1<System.Int64>)` | Controlled |
| `clockwork.parallel.for.int64.options` | `System.Threading.Tasks.Parallel::For(System.Int64,System.Int64,System.Threading.Tasks.ParallelOptions,System.Action`1<System.Int64>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::For(System.Int64,System.Int64,System.Threading.Tasks.ParallelOptions,System.Action`1<System.Int64>)` | Controlled |
| `clockwork.parallel.foreach` | `System.Threading.Tasks.Parallel::ForEach(System.Collections.Generic.IEnumerable`1<!!0>,System.Action`1<!!0>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::ForEach(System.Collections.Generic.IEnumerable`1<TSource>,System.Action`1<TSource>)` | Controlled |
| `clockwork.parallel.foreach.options` | `System.Threading.Tasks.Parallel::ForEach(System.Collections.Generic.IEnumerable`1<!!0>,System.Threading.Tasks.ParallelOptions,System.Action`1<!!0>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::ForEach(System.Collections.Generic.IEnumerable`1<TSource>,System.Threading.Tasks.ParallelOptions,System.Action`1<TSource>)` | Controlled |
| `clockwork.parallel.for.int32.loopstate` | `System.Threading.Tasks.Parallel::For(System.Int32,System.Int32,System.Action`2<System.Int32,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.for.int32.loopstate.options` | `System.Threading.Tasks.Parallel::For(System.Int32,System.Int32,System.Threading.Tasks.ParallelOptions,System.Action`2<System.Int32,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.for.int64.loopstate` | `System.Threading.Tasks.Parallel::For(System.Int64,System.Int64,System.Action`2<System.Int64,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.for.int64.loopstate.options` | `System.Threading.Tasks.Parallel::For(System.Int64,System.Int64,System.Threading.Tasks.ParallelOptions,System.Action`2<System.Int64,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.foreach.loopstate` | `System.Threading.Tasks.Parallel::ForEach(System.Collections.Generic.IEnumerable`1<!!0>,System.Action`2<!!0,System.Threading.Tasks.ParallelLoopState>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::RejectUnsupported(System.String)` | Rejected |
| `clockwork.parallel.foreach.loopstate.index` | `System.Threading.Tasks.Parallel::ForEach(System.Collections.Generic.IEnumerable`1<!!0>,System.Action`3<!!0,System.Threading.Tasks.ParallelLoopState,System.Int64>)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledParallel::RejectUnsupported(System.String)` | Rejected |

## Monitor family

Policy: **Controlled**. 

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.monitor.enter` | `System.Threading.Monitor::Enter(System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::Enter(System.Object)` | Controlled |
| `clockwork.monitor.enter.locktaken` | `System.Threading.Monitor::Enter(System.Object,System.Boolean&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::Enter(System.Object,System.Boolean&)` | Controlled |
| `clockwork.monitor.exit` | `System.Threading.Monitor::Exit(System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::Exit(System.Object)` | Controlled |
| `clockwork.monitor.isentered` | `System.Threading.Monitor::IsEntered(System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::IsEntered(System.Object)` | Controlled |
| `clockwork.monitor.tryenter` | `System.Threading.Monitor::TryEnter(System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::TryEnter(System.Object)` | Controlled |
| `clockwork.monitor.tryenter.locktaken` | `System.Threading.Monitor::TryEnter(System.Object,System.Boolean&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::TryEnter(System.Object,System.Boolean&)` | Controlled |
| `clockwork.monitor.tryenter.milliseconds` | `System.Threading.Monitor::TryEnter(System.Object,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::TryEnter(System.Object,System.Int32)` | Controlled |
| `clockwork.monitor.tryenter.milliseconds.locktaken` | `System.Threading.Monitor::TryEnter(System.Object,System.Int32,System.Boolean&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::TryEnter(System.Object,System.Int32,System.Boolean&)` | Controlled |
| `clockwork.monitor.tryenter.timespan` | `System.Threading.Monitor::TryEnter(System.Object,System.TimeSpan)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::TryEnter(System.Object,System.TimeSpan)` | Controlled |
| `clockwork.monitor.tryenter.timespan.locktaken` | `System.Threading.Monitor::TryEnter(System.Object,System.TimeSpan,System.Boolean&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::TryEnter(System.Object,System.TimeSpan,System.Boolean&)` | Controlled |
| `clockwork.monitor.wait` | `System.Threading.Monitor::Wait(System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::Wait(System.Object)` | Controlled |
| `clockwork.monitor.wait.milliseconds` | `System.Threading.Monitor::Wait(System.Object,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::Wait(System.Object,System.Int32)` | Controlled |
| `clockwork.monitor.wait.milliseconds.exitcontext` | `System.Threading.Monitor::Wait(System.Object,System.Int32,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::Wait(System.Object,System.Int32,System.Boolean)` | Controlled |
| `clockwork.monitor.wait.timespan` | `System.Threading.Monitor::Wait(System.Object,System.TimeSpan)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::Wait(System.Object,System.TimeSpan)` | Controlled |
| `clockwork.monitor.wait.timespan.exitcontext` | `System.Threading.Monitor::Wait(System.Object,System.TimeSpan,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::Wait(System.Object,System.TimeSpan,System.Boolean)` | Controlled |
| `clockwork.monitor.pulse` | `System.Threading.Monitor::Pulse(System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::Pulse(System.Object)` | Controlled |
| `clockwork.monitor.pulseall` | `System.Threading.Monitor::PulseAll(System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledMonitor::PulseAll(System.Object)` | Controlled |

## Lock family

Policy: **Controlled**. 

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.lock.type` | `System.Threading.Lock` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledLock` | Controlled |
| `clockwork.lock.scope.type` | `System.Threading.Lock/Scope` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledLock/Scope` | Controlled |

## Semaphore family

Policy: **Controlled**. 

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.semaphoreslim.ctor.initial` | `new System.Threading.SemaphoreSlim(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Create(System.Int32)` | Controlled |
| `clockwork.semaphoreslim.ctor.initial.max` | `new System.Threading.SemaphoreSlim(System.Int32,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Create(System.Int32,System.Int32)` | Controlled |
| `clockwork.semaphoreslim.get_currentcount` | `System.Threading.SemaphoreSlim::get_CurrentCount()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::CurrentCount(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.wait` | `System.Threading.SemaphoreSlim::Wait()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.wait.cancellationtoken` | `System.Threading.SemaphoreSlim::Wait(System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.wait.milliseconds` | `System.Threading.SemaphoreSlim::Wait(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.Int32)` | Controlled |
| `clockwork.semaphoreslim.wait.milliseconds.cancellationtoken` | `System.Threading.SemaphoreSlim::Wait(System.Int32,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.Int32,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.wait.timespan` | `System.Threading.SemaphoreSlim::Wait(System.TimeSpan)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.TimeSpan)` | Controlled |
| `clockwork.semaphoreslim.wait.timespan.cancellationtoken` | `System.Threading.SemaphoreSlim::Wait(System.TimeSpan,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Wait(System.Threading.SemaphoreSlim,System.TimeSpan,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.waitasync` | `System.Threading.SemaphoreSlim::WaitAsync()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.waitasync.cancellationtoken` | `System.Threading.SemaphoreSlim::WaitAsync(System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.waitasync.milliseconds` | `System.Threading.SemaphoreSlim::WaitAsync(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.Int32)` | Controlled |
| `clockwork.semaphoreslim.waitasync.milliseconds.cancellationtoken` | `System.Threading.SemaphoreSlim::WaitAsync(System.Int32,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.Int32,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.waitasync.timespan` | `System.Threading.SemaphoreSlim::WaitAsync(System.TimeSpan)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.TimeSpan)` | Controlled |
| `clockwork.semaphoreslim.waitasync.timespan.cancellationtoken` | `System.Threading.SemaphoreSlim::WaitAsync(System.TimeSpan,System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::WaitAsync(System.Threading.SemaphoreSlim,System.TimeSpan,System.Threading.CancellationToken)` | Controlled |
| `clockwork.semaphoreslim.release` | `System.Threading.SemaphoreSlim::Release()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Release(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.release.count` | `System.Threading.SemaphoreSlim::Release(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Release(System.Threading.SemaphoreSlim,System.Int32)` | Controlled |
| `clockwork.semaphoreslim.dispose` | `System.Threading.SemaphoreSlim::Dispose()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::Dispose(System.Threading.SemaphoreSlim)` | Controlled |
| `clockwork.semaphoreslim.get_availablewaithandle` | `System.Threading.SemaphoreSlim::get_AvailableWaitHandle()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSemaphoreSlim::AvailableWaitHandle(System.Threading.SemaphoreSlim)` | Rejected |

## Interlocked family

Policy: **Controlled**. The full .NET 10 `Interlocked` surface - `Increment`/`Decrement`/`Add`/`And`/`Or` (`int`/`long`/`uint`/`ulong`), `Exchange`/`CompareExchange` (every primitive, native-int, floating-point, reference, and generic reference overload), `Read` (`long`/`ulong`), and the memory barriers - redirects each call site to a shim with the identical `ref`-first signature. Clockwork's cooperative single-logical-thread scheduler makes every read-modify-write an indivisible step (never split, never interleaved mid-operation), so the shim delegates to the real primitive and preserves exact atomic return, overflow, and reference-write semantics inside and outside simulation. The exploration policy injects no mid-operation scheduling point; the single delegation site is the future Phase 9 race-hook attachment point.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.interlocked.increment.int32` | `System.Threading.Interlocked::Increment(System.Int32&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Increment(System.Int32&)` | Controlled |
| `clockwork.interlocked.increment.int64` | `System.Threading.Interlocked::Increment(System.Int64&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Increment(System.Int64&)` | Controlled |
| `clockwork.interlocked.increment.uint32` | `System.Threading.Interlocked::Increment(System.UInt32&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Increment(System.UInt32&)` | Controlled |
| `clockwork.interlocked.increment.uint64` | `System.Threading.Interlocked::Increment(System.UInt64&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Increment(System.UInt64&)` | Controlled |
| `clockwork.interlocked.decrement.int32` | `System.Threading.Interlocked::Decrement(System.Int32&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Decrement(System.Int32&)` | Controlled |
| `clockwork.interlocked.decrement.int64` | `System.Threading.Interlocked::Decrement(System.Int64&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Decrement(System.Int64&)` | Controlled |
| `clockwork.interlocked.decrement.uint32` | `System.Threading.Interlocked::Decrement(System.UInt32&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Decrement(System.UInt32&)` | Controlled |
| `clockwork.interlocked.decrement.uint64` | `System.Threading.Interlocked::Decrement(System.UInt64&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Decrement(System.UInt64&)` | Controlled |
| `clockwork.interlocked.add.int32` | `System.Threading.Interlocked::Add(System.Int32&,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Add(System.Int32&,System.Int32)` | Controlled |
| `clockwork.interlocked.add.int64` | `System.Threading.Interlocked::Add(System.Int64&,System.Int64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Add(System.Int64&,System.Int64)` | Controlled |
| `clockwork.interlocked.add.uint32` | `System.Threading.Interlocked::Add(System.UInt32&,System.UInt32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Add(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.interlocked.add.uint64` | `System.Threading.Interlocked::Add(System.UInt64&,System.UInt64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Add(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.interlocked.and.int32` | `System.Threading.Interlocked::And(System.Int32&,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::And(System.Int32&,System.Int32)` | Controlled |
| `clockwork.interlocked.and.uint32` | `System.Threading.Interlocked::And(System.UInt32&,System.UInt32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::And(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.interlocked.and.int64` | `System.Threading.Interlocked::And(System.Int64&,System.Int64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::And(System.Int64&,System.Int64)` | Controlled |
| `clockwork.interlocked.and.uint64` | `System.Threading.Interlocked::And(System.UInt64&,System.UInt64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::And(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.interlocked.or.int32` | `System.Threading.Interlocked::Or(System.Int32&,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Or(System.Int32&,System.Int32)` | Controlled |
| `clockwork.interlocked.or.uint32` | `System.Threading.Interlocked::Or(System.UInt32&,System.UInt32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Or(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.interlocked.or.int64` | `System.Threading.Interlocked::Or(System.Int64&,System.Int64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Or(System.Int64&,System.Int64)` | Controlled |
| `clockwork.interlocked.or.uint64` | `System.Threading.Interlocked::Or(System.UInt64&,System.UInt64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Or(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.interlocked.exchange.int32` | `System.Threading.Interlocked::Exchange(System.Int32&,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.Int32&,System.Int32)` | Controlled |
| `clockwork.interlocked.exchange.int64` | `System.Threading.Interlocked::Exchange(System.Int64&,System.Int64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.Int64&,System.Int64)` | Controlled |
| `clockwork.interlocked.exchange.object` | `System.Threading.Interlocked::Exchange(System.Object&,System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.Object&,System.Object)` | Controlled |
| `clockwork.interlocked.exchange.sbyte` | `System.Threading.Interlocked::Exchange(System.SByte&,System.SByte)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.SByte&,System.SByte)` | Controlled |
| `clockwork.interlocked.exchange.int16` | `System.Threading.Interlocked::Exchange(System.Int16&,System.Int16)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.Int16&,System.Int16)` | Controlled |
| `clockwork.interlocked.exchange.byte` | `System.Threading.Interlocked::Exchange(System.Byte&,System.Byte)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.Byte&,System.Byte)` | Controlled |
| `clockwork.interlocked.exchange.uint16` | `System.Threading.Interlocked::Exchange(System.UInt16&,System.UInt16)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.UInt16&,System.UInt16)` | Controlled |
| `clockwork.interlocked.exchange.uint32` | `System.Threading.Interlocked::Exchange(System.UInt32&,System.UInt32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.interlocked.exchange.uint64` | `System.Threading.Interlocked::Exchange(System.UInt64&,System.UInt64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.interlocked.exchange.single` | `System.Threading.Interlocked::Exchange(System.Single&,System.Single)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.Single&,System.Single)` | Controlled |
| `clockwork.interlocked.exchange.double` | `System.Threading.Interlocked::Exchange(System.Double&,System.Double)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.Double&,System.Double)` | Controlled |
| `clockwork.interlocked.exchange.intptr` | `System.Threading.Interlocked::Exchange(System.IntPtr&,System.IntPtr)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.IntPtr&,System.IntPtr)` | Controlled |
| `clockwork.interlocked.exchange.uintptr` | `System.Threading.Interlocked::Exchange(System.UIntPtr&,System.UIntPtr)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(System.UIntPtr&,System.UIntPtr)` | Controlled |
| `clockwork.interlocked.exchange.generic` | `System.Threading.Interlocked::Exchange(!!0&,!!0)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Exchange(T&,T)` | Controlled |
| `clockwork.interlocked.compareexchange.int32` | `System.Threading.Interlocked::CompareExchange(System.Int32&,System.Int32,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.Int32&,System.Int32,System.Int32)` | Controlled |
| `clockwork.interlocked.compareexchange.int64` | `System.Threading.Interlocked::CompareExchange(System.Int64&,System.Int64,System.Int64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.Int64&,System.Int64,System.Int64)` | Controlled |
| `clockwork.interlocked.compareexchange.object` | `System.Threading.Interlocked::CompareExchange(System.Object&,System.Object,System.Object)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.Object&,System.Object,System.Object)` | Controlled |
| `clockwork.interlocked.compareexchange.sbyte` | `System.Threading.Interlocked::CompareExchange(System.SByte&,System.SByte,System.SByte)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.SByte&,System.SByte,System.SByte)` | Controlled |
| `clockwork.interlocked.compareexchange.int16` | `System.Threading.Interlocked::CompareExchange(System.Int16&,System.Int16,System.Int16)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.Int16&,System.Int16,System.Int16)` | Controlled |
| `clockwork.interlocked.compareexchange.byte` | `System.Threading.Interlocked::CompareExchange(System.Byte&,System.Byte,System.Byte)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.Byte&,System.Byte,System.Byte)` | Controlled |
| `clockwork.interlocked.compareexchange.uint16` | `System.Threading.Interlocked::CompareExchange(System.UInt16&,System.UInt16,System.UInt16)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.UInt16&,System.UInt16,System.UInt16)` | Controlled |
| `clockwork.interlocked.compareexchange.uint32` | `System.Threading.Interlocked::CompareExchange(System.UInt32&,System.UInt32,System.UInt32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.UInt32&,System.UInt32,System.UInt32)` | Controlled |
| `clockwork.interlocked.compareexchange.uint64` | `System.Threading.Interlocked::CompareExchange(System.UInt64&,System.UInt64,System.UInt64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.UInt64&,System.UInt64,System.UInt64)` | Controlled |
| `clockwork.interlocked.compareexchange.single` | `System.Threading.Interlocked::CompareExchange(System.Single&,System.Single,System.Single)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.Single&,System.Single,System.Single)` | Controlled |
| `clockwork.interlocked.compareexchange.double` | `System.Threading.Interlocked::CompareExchange(System.Double&,System.Double,System.Double)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.Double&,System.Double,System.Double)` | Controlled |
| `clockwork.interlocked.compareexchange.intptr` | `System.Threading.Interlocked::CompareExchange(System.IntPtr&,System.IntPtr,System.IntPtr)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.IntPtr&,System.IntPtr,System.IntPtr)` | Controlled |
| `clockwork.interlocked.compareexchange.uintptr` | `System.Threading.Interlocked::CompareExchange(System.UIntPtr&,System.UIntPtr,System.UIntPtr)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(System.UIntPtr&,System.UIntPtr,System.UIntPtr)` | Controlled |
| `clockwork.interlocked.compareexchange.generic` | `System.Threading.Interlocked::CompareExchange(!!0&,!!0,!!0)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::CompareExchange(T&,T,T)` | Controlled |
| `clockwork.interlocked.read.int64` | `System.Threading.Interlocked::Read(System.Int64&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Read(System.Int64&)` | Controlled |
| `clockwork.interlocked.read.uint64` | `System.Threading.Interlocked::Read(System.UInt64&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::Read(System.UInt64&)` | Controlled |
| `clockwork.interlocked.memorybarrier` | `System.Threading.Interlocked::MemoryBarrier()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::MemoryBarrier()` | Controlled |
| `clockwork.interlocked.memorybarrierprocesswide` | `System.Threading.Interlocked::MemoryBarrierProcessWide()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledInterlocked::MemoryBarrierProcessWide()` | Controlled |

## Volatile family

Policy: **Controlled**. The full .NET 10 `Volatile` surface - `Read`/`Write` (every primitive, native-int, floating-point, and generic reference overload) and the `ReadBarrier`/`WriteBarrier` fences - redirects each call site to a shim with the identical `ref`-first signature. Under the cooperative single-logical-thread scheduler a volatile access is an indivisible step, so the shim delegates to the real primitive and preserves the exact value read/written together with the acquire (read) / release (write) fence intent. The single delegation site is the future Phase 9 race-hook attachment point.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.volatile.read.boolean` | `System.Threading.Volatile::Read(System.Boolean&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.Boolean&)` | Controlled |
| `clockwork.volatile.read.sbyte` | `System.Threading.Volatile::Read(System.SByte&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.SByte&)` | Controlled |
| `clockwork.volatile.read.byte` | `System.Threading.Volatile::Read(System.Byte&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.Byte&)` | Controlled |
| `clockwork.volatile.read.int16` | `System.Threading.Volatile::Read(System.Int16&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.Int16&)` | Controlled |
| `clockwork.volatile.read.uint16` | `System.Threading.Volatile::Read(System.UInt16&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.UInt16&)` | Controlled |
| `clockwork.volatile.read.int32` | `System.Threading.Volatile::Read(System.Int32&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.Int32&)` | Controlled |
| `clockwork.volatile.read.uint32` | `System.Threading.Volatile::Read(System.UInt32&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.UInt32&)` | Controlled |
| `clockwork.volatile.read.int64` | `System.Threading.Volatile::Read(System.Int64&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.Int64&)` | Controlled |
| `clockwork.volatile.read.uint64` | `System.Threading.Volatile::Read(System.UInt64&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.UInt64&)` | Controlled |
| `clockwork.volatile.read.intptr` | `System.Threading.Volatile::Read(System.IntPtr&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.IntPtr&)` | Controlled |
| `clockwork.volatile.read.uintptr` | `System.Threading.Volatile::Read(System.UIntPtr&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.UIntPtr&)` | Controlled |
| `clockwork.volatile.read.single` | `System.Threading.Volatile::Read(System.Single&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.Single&)` | Controlled |
| `clockwork.volatile.read.double` | `System.Threading.Volatile::Read(System.Double&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(System.Double&)` | Controlled |
| `clockwork.volatile.read.generic` | `System.Threading.Volatile::Read(!!0&)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Read(T&)` | Controlled |
| `clockwork.volatile.write.boolean` | `System.Threading.Volatile::Write(System.Boolean&,System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.Boolean&,System.Boolean)` | Controlled |
| `clockwork.volatile.write.sbyte` | `System.Threading.Volatile::Write(System.SByte&,System.SByte)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.SByte&,System.SByte)` | Controlled |
| `clockwork.volatile.write.byte` | `System.Threading.Volatile::Write(System.Byte&,System.Byte)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.Byte&,System.Byte)` | Controlled |
| `clockwork.volatile.write.int16` | `System.Threading.Volatile::Write(System.Int16&,System.Int16)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.Int16&,System.Int16)` | Controlled |
| `clockwork.volatile.write.uint16` | `System.Threading.Volatile::Write(System.UInt16&,System.UInt16)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.UInt16&,System.UInt16)` | Controlled |
| `clockwork.volatile.write.int32` | `System.Threading.Volatile::Write(System.Int32&,System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.Int32&,System.Int32)` | Controlled |
| `clockwork.volatile.write.uint32` | `System.Threading.Volatile::Write(System.UInt32&,System.UInt32)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.UInt32&,System.UInt32)` | Controlled |
| `clockwork.volatile.write.int64` | `System.Threading.Volatile::Write(System.Int64&,System.Int64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.Int64&,System.Int64)` | Controlled |
| `clockwork.volatile.write.uint64` | `System.Threading.Volatile::Write(System.UInt64&,System.UInt64)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.UInt64&,System.UInt64)` | Controlled |
| `clockwork.volatile.write.intptr` | `System.Threading.Volatile::Write(System.IntPtr&,System.IntPtr)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.IntPtr&,System.IntPtr)` | Controlled |
| `clockwork.volatile.write.uintptr` | `System.Threading.Volatile::Write(System.UIntPtr&,System.UIntPtr)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.UIntPtr&,System.UIntPtr)` | Controlled |
| `clockwork.volatile.write.single` | `System.Threading.Volatile::Write(System.Single&,System.Single)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.Single&,System.Single)` | Controlled |
| `clockwork.volatile.write.double` | `System.Threading.Volatile::Write(System.Double&,System.Double)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(System.Double&,System.Double)` | Controlled |
| `clockwork.volatile.write.generic` | `System.Threading.Volatile::Write(!!0&,!!0)` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::Write(T&,T)` | Controlled |
| `clockwork.volatile.readbarrier` | `System.Threading.Volatile::ReadBarrier()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::ReadBarrier()` | Controlled |
| `clockwork.volatile.writebarrier` | `System.Threading.Volatile::WriteBarrier()` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledVolatile::WriteBarrier()` | Controlled |

## SpinWait family

Policy: **Controlled**. `System.Threading.SpinWait` is a value type retargeted by whole-type substitution (like `System.Threading.Lock`): every local/field/parameter typed `SpinWait`, each `new SpinWait()`/`default`, the instance members (`Count`, `NextSpinWillYield`, `Reset`, both `SpinOnce` overloads) and the static `SpinUntil` overloads remap onto the controlled struct. Inside a simulation a spin never burns CPU or consumes real time: `SpinOnce` is a cooperative no-op that only advances the observable spin count, and `SpinUntil` pumps the deterministic loop until its predicate holds (a never-satisfiable predicate surfaces as the loop-model deadlock diagnostic). The finite `SpinUntil` overloads use a first-winner virtual-time deadline. Outside a simulation every member delegates to a real wrapped `SpinWait`.

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.spinwait.type` | `System.Threading.SpinWait` | `Clockwork.Runtime!Clockwork.Runtime.Threading.ControlledSpinWait` | Controlled |

## UncontrolledInvocation family

Policy: **Rejected**. Process control and abrupt-termination APIs (`Process.Start`/`Start` instance/`Kill`/`WaitForExit`/`WaitForExitAsync`, `Environment.Exit`/`FailFast`) cannot be modelled inside a single simulated process at all. A throwing guard is injected before each call site so a rewritten assembly can never launch, kill, wait on, or terminate a real OS process; unlike the controlled shims the rejection is unconditional (it fires whether or not a simulation is active).

| Rule id | BCL target | Shim | Policy |
| --- | --- | --- | --- |
| `clockwork.process.start.filename` | `System.Diagnostics.Process::Start(System.String)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.startinfo` | `System.Diagnostics.Process::Start(System.Diagnostics.ProcessStartInfo)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.filename.arguments` | `System.Diagnostics.Process::Start(System.String,System.String)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.filename.argumentlist` | `System.Diagnostics.Process::Start(System.String,System.Collections.Generic.IEnumerable`1<System.String>)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.filename.credentials` | `System.Diagnostics.Process::Start(System.String,System.String,System.Security.SecureString,System.String)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.filename.arguments.credentials` | `System.Diagnostics.Process::Start(System.String,System.String,System.String,System.Security.SecureString,System.String)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.start.instance` | `System.Diagnostics.Process::Start()` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.kill` | `System.Diagnostics.Process::Kill()` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.kill.tree` | `System.Diagnostics.Process::Kill(System.Boolean)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.waitforexit` | `System.Diagnostics.Process::WaitForExit()` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.waitforexit.milliseconds` | `System.Diagnostics.Process::WaitForExit(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.waitforexit.timespan` | `System.Diagnostics.Process::WaitForExit(System.TimeSpan)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.process.waitforexitasync` | `System.Diagnostics.Process::WaitForExitAsync(System.Threading.CancellationToken)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.environment.exit` | `System.Environment::Exit(System.Int32)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.environment.failfast.message` | `System.Environment::FailFast(System.String)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |
| `clockwork.environment.failfast.exception` | `System.Environment::FailFast(System.String,System.Exception)` | `Clockwork.Runtime!Clockwork.Runtime.UncontrolledInvocationGuard::Reject(System.String)` | Rejected |

## Documented holes (not rewritten in these rule sets)

These nondeterministic or entropy-drawing surfaces are intentionally **not** covered and
remain real BCL calls even under simulation:

- `Stopwatch` instance APIs (`Start`/`Stop`/`Restart`/`Elapsed`/`ElapsedMilliseconds`/`ElapsedTicks`) and the `GetElapsedTime(long, long)` overload.
- Generic cryptographic helpers `RandomNumberGenerator.GetItems<T>` and `Shuffle<T>`, and any `GetString`/`GetHexString` overloads beyond those listed above.
- `DateTime`/`DateTimeOffset` parsing/formatting and any culture-, timezone-, or kind-conversion helpers other than the `Now`/`UtcNow`/`Today` clocks above.
- Synchronous blocking on `ValueTask`/`ValueTask<T>` (`.Result`/`.GetResult()` outside an awaiter): a value task may be consumed only once, so a blocking drain is unsafe. `await` is the supported controlled path.
- `Monitor`, semaphores, and wait handles (including the `ThreadPool` registered-wait APIs, which are rejected until then). These are Phase 7 scope.
- Timers, `PeriodicTimer`, the `Task.Delay` implementation, and cancellation timers. These are Phase 8 scope (`Thread.Sleep` is a controlled virtual wait now).

Determinism is claimed **only** for the exact rules tabulated above.

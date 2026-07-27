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
| `clockwork.tasks.whenall.generic.array` | `System.Threading.Tasks.Task::WhenAll(System.Threading.Tasks.Task`1<!!0>[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.Threading.Tasks.Task`1<TResult>[])` | Controlled |
| `clockwork.tasks.whenall.generic.span` | `System.Threading.Tasks.Task::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.whenall.generic.enumerable` | `System.Threading.Tasks.Task::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAll(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.whenany.generic.array` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task`1<!!0>[])` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task`1<TResult>[])` | Controlled |
| `clockwork.tasks.whenany.generic.span` | `System.Threading.Tasks.Task::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.ReadOnlySpan`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |
| `clockwork.tasks.whenany.generic.pair` | `System.Threading.Tasks.Task::WhenAny(System.Threading.Tasks.Task`1<!!0>,System.Threading.Tasks.Task`1<!!0>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Threading.Tasks.Task`1<TResult>,System.Threading.Tasks.Task`1<TResult>)` | Controlled |
| `clockwork.tasks.whenany.generic.enumerable` | `System.Threading.Tasks.Task::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<!!0>>)` | `Clockwork.Runtime!Clockwork.Runtime.Tasks.ControlledTask::WhenAny(System.Collections.Generic.IEnumerable`1<System.Threading.Tasks.Task`1<TResult>>)` | Controlled |

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

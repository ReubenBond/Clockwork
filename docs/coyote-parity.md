# Coyote parity matrix — threads, tasks, thread pool, Parallel

This document is the explicit parity ledger for Clockwork's Phase 6A/6B controlled-concurrency
surface against **[Microsoft Coyote](https://github.com/microsoft/coyote)** (MIT-licensed prior
art). Coyote's controlled rewriting types live under
[`Source/Test/Rewriting/Types/Threading`](https://github.com/microsoft/coyote/tree/main/Source/Test/Rewriting/Types/Threading)
(and its `Tasks` subfolder). Every Coyote thread / task / thread-pool / Parallel surface is
classified here as one of:

| Status | Meaning |
| --- | --- |
| ✅ **Controlled** | Clockwork rewrites the call site to a controlled shim. The exact rule id is cited; the full signature list is in [`rule-inventory.md`](rule-inventory.md). |
| ⛔ **Rejected (tested)** | Clockwork deliberately rejects the call at the rewritten site with a precise diagnostic, because the semantics cannot be modelled faithfully by the cooperative scheduler yet. A test asserts the rejection. |
| 🏛 **Controlled by architecture** | No dedicated rule is needed: Clockwork controls the *awaiter*, not the `Task` type, so the surface is already controlled whenever the produced task is awaited or waited on. |
| 🕗 **Deferred (Phase 7/8)** | Out of Phase 6B scope by the phase plan; tracked to a named later phase. |
| n/a | Not applicable on .NET 10 / not a concurrency-scheduling surface. |

The single largest **architectural difference**: Coyote substitutes the `Task`/`Task<T>` *types*
wholesale, so it must also wrap `TaskCompletionSource`, `TaskExtensions`, `ValueTask`, etc. to keep
every task a *controlled* task. Clockwork instead substitutes the compiler-generated **builder and
awaiter** types (see the `AsyncMachinery`/`ValueTaskMachinery` families) and redirects the direct
`Task` call sites (combinators, waits, `Result`, continuations, `Run`). A plain BCL `Task` produced
by any means is therefore already controlled the moment it is awaited or waited on — which is why
several Coyote wrapper types map to "controlled by architecture" rather than a dedicated rule.

Clockwork additionally goes **beyond** Coyote for `System.Threading.ThreadPool`, which Coyote does
not model at all.

---

## `System.Threading.Thread` — Coyote `…Types.Threading.Thread`

Coyote's controlled `Thread` exposes exactly `Create` (×4), `Start` (×2), `Sleep` (×2), `SpinWait`,
`Yield`, and `Join` (×3). Clockwork mirrors all of them and, going further, **explicitly rejects**
the OS-specific surface Coyote simply leaves as an uncontrolled real call.

| Coyote surface | .NET 10 signature | Clockwork status | Rule / reason |
| --- | --- | --- | --- |
| `Create(ThreadStart)` | `new Thread(ThreadStart)` | ✅ Controlled | `clockwork.thread.ctor.threadstart` |
| `Create(ThreadStart, maxStackSize)` | `new Thread(ThreadStart, int)` | ✅ Controlled | `clockwork.thread.ctor.threadstart.stacksize` |
| `Create(ParameterizedThreadStart)` | `new Thread(ParameterizedThreadStart)` | ✅ Controlled | `clockwork.thread.ctor.parameterized` |
| `Create(ParameterizedThreadStart, maxStackSize)` | `new Thread(ParameterizedThreadStart, int)` | ✅ Controlled | `clockwork.thread.ctor.parameterized.stacksize` |
| `Start(instance)` | `Thread.Start()` | ✅ Controlled | `clockwork.thread.start` |
| `Start(instance, parameter)` | `Thread.Start(object)` | ✅ Controlled | `clockwork.thread.start.parameter` |
| `Join(instance)` | `Thread.Join()` | ✅ Controlled | `clockwork.thread.join` (virtual wait) |
| `Join(instance, ms)` | `Thread.Join(int)` | ✅ Controlled | `clockwork.thread.join.milliseconds` |
| `Join(instance, TimeSpan)` | `Thread.Join(TimeSpan)` | ✅ Controlled | `clockwork.thread.join.timespan` |
| `Sleep(ms)` | `Thread.Sleep(int)` | ✅ Controlled | `clockwork.thread.sleep.milliseconds` (virtual wait) |
| `Sleep(TimeSpan)` | `Thread.Sleep(TimeSpan)` | ✅ Controlled | `clockwork.thread.sleep.timespan` (virtual wait) |
| `SpinWait(iterations)` | `Thread.SpinWait(int)` | ✅ Controlled | `clockwork.thread.spinwait` |
| `Yield()` | `Thread.Yield()` | ✅ Controlled | `clockwork.thread.yield` |
| *(not modelled by Coyote — left uncontrolled)* | `Thread.set_Priority(ThreadPriority)` | ⛔ Rejected (tested) | `clockwork.thread.set_priority` — thread priority has no faithful meaning in a cooperative single-logical-thread scheduler |
| *(not modelled by Coyote)* | `Thread.Interrupt()` | ⛔ Rejected (tested) | `clockwork.thread.interrupt` — OS thread interruption cannot be modelled |
| *(not modelled by Coyote)* | `Thread.SetApartmentState(ApartmentState)` | ⛔ Rejected (tested) | `clockwork.thread.setapartmentstate` — COM apartments are an OS concept |
| *(not modelled by Coyote)* | `Thread.TrySetApartmentState(ApartmentState)` | ⛔ Rejected (tested) | `clockwork.thread.trysetapartmentstate` |

**Deviation:** `Join`/`Sleep` are *virtual* waits (yield through the deterministic loop, no real
time) — identical in spirit to Coyote's `DelayCurrentOperation`/`PauseUntilThreadCompletes`.
`Thread.Name`/`ManagedThreadId` remain the real BCL members (logical identity is preserved by the
underlying real `Thread` object each controlled operation runs on), matching Coyote.

---

## `System.Threading.Tasks.Task` static surface

The direct `Task` call-site surface is split across Phase 6A (combinators, waits, `Result`,
continuations, async machinery, `Yield`) and Phase 6B (`Run`). All are controlled.

| Coyote surface | Clockwork status | Rule / family |
| --- | --- | --- |
| `Task.Run` (8 overloads: `Action`/`Func<TResult>`/`Func<Task>`/`Func<Task<TResult>>`, ± `CancellationToken`) | ✅ Controlled **(Phase 6B)** | `clockwork.tasks.run.*` (TaskScheduling family) |
| `Task.WhenAll` / `Task.WhenAny` (array, span, pair, enumerable; non-generic + generic) | ✅ Controlled | `clockwork.tasks.whenall.*` / `whenany.*` (TaskCombinators) |
| `Task.Wait()` / `WaitAll(Task[])` / `WaitAny(Task[])` | ✅ Controlled | `clockwork.tasks.wait.*` (TaskSynchronization) |
| `Task<T>.Result` (blocking get) | ✅ Controlled | `clockwork.tasks.result.generic` |
| `Task.ContinueWith(Action<Task>)` | ✅ Controlled | `clockwork.tasks.continuewith.action` |
| `Task<T>.ContinueWith(Action<Task<T>>)` | ✅ Controlled **(Phase 6B gap-closure)** | `clockwork.tasks.continuewith.generic.action` |
| `Task<T>.ContinueWith<TNewResult>(Func<Task<T>,TNewResult>)` | ✅ Controlled **(Phase 6B gap-closure)** | `clockwork.tasks.continuewith.generic.func` |
| `Task.Yield()` | ✅ Controlled | `clockwork.tasks.yield.call` (AsyncMachinery) |
| `async` builder + awaiter types (`AsyncTaskMethodBuilder`, `TaskAwaiter`, `ConfiguredTaskAwaitable`, `YieldAwaitable`, generic + non-generic) | ✅ Controlled | AsyncMachinery family (type substitution) |
| `Task.Delay` | 🕗 Deferred (Phase 8) | `clockwork.tasks.delay.milliseconds` — **Rejected** until virtual timers land; a virtual-time delay is Phase 8 |
| `Task.FromResult` / `FromException` / `FromCanceled` / `CompletedTask` | 🏛 Controlled by architecture | already-completed tasks need no scheduling; awaiting one routes through the controlled awaiter |

---

## `System.Threading.Tasks.ValueTask`

| Coyote surface | Clockwork status | Rule / family |
| --- | --- | --- |
| `async ValueTask`/`ValueTask<T>` builder + awaiter types (`AsyncValueTaskMethodBuilder`, `ValueTaskAwaiter`, `ConfiguredValueTaskAwaitable`, generic + non-generic) | ✅ Controlled | ValueTaskMachinery family (type substitution) |
| Synchronous blocking on `ValueTask`/`ValueTask<T>` (`.Result` / `.GetResult()` outside an awaiter) | ⛔ Not rewritten (documented hole) | a value task may be consumed only once, so a blocking drain is unsafe — `await` is the supported controlled path (see `rule-inventory.md` holes) |

---

## `System.Threading.Tasks.TaskFactory` / `TaskFactory<TResult>`

Coyote wraps the full `StartNew` overload set plus the read-only property getters. Clockwork controls
the `Action`/`Func<TResult>` `StartNew` overloads (± `CancellationToken`/`TaskCreationOptions`) — the
forms that actually offload work.

| Coyote surface | Clockwork status | Rule |
| --- | --- | --- |
| `TaskFactory.StartNew(Action)` | ✅ Controlled | `clockwork.tasks.factory.startnew.action` |
| `TaskFactory.StartNew(Action, CancellationToken)` | ✅ Controlled | `clockwork.tasks.factory.startnew.action.cancellationtoken` |
| `TaskFactory.StartNew(Action, TaskCreationOptions)` | ✅ Controlled | `clockwork.tasks.factory.startnew.action.options` |
| `TaskFactory.StartNew<TResult>(Func<TResult>)` (+ token / options) | ✅ Controlled | `clockwork.tasks.factory.startnew.func[.cancellationtoken|.options]` |
| `TaskFactory<TResult>.StartNew(Func<TResult>)` (+ token / options) | ✅ Controlled | `clockwork.tasks.factory.generic.startnew.func[.cancellationtoken|.options]` |
| `StartNew(…, TaskCreationOptions, TaskScheduler)` (explicit non-default scheduler) | ⛔ Rejected (tested) | folded into the options overload's guard: a non-default `TaskScheduler` / `AttachedToParent` cannot be honoured by the coordinator, so an unsupported combination is rejected with a precise diagnostic |
| `StartNew(Action<object>, object state, …)` (state overloads) | 🏛 Controlled by architecture | the delegate is scheduled the same way; the state-carrying overloads reduce to the controlled forms |
| `get_ContinuationOptions` / `get_CancellationToken` / `get_CreationOptions` / `get_Scheduler` | n/a | pure read-only property getters — no scheduling, no interception needed |

---

## `System.Threading.Tasks.TaskCompletionSource` / `TaskCompletionSource<TResult>`

Coyote wraps TCS so the produced task is a controlled task. Clockwork does **not** need a rewrite
rule: because the *awaiter* is controlled, a plain BCL `TaskCompletionSource`'s task is already
controlled when awaited or waited on. For code that wants an explicit controlled TCS, Clockwork ships
`Clockwork.Runtime.Tasks.ControlledTaskCompletionSource` / `ControlledTaskCompletionSource<TResult>`
mirroring the Coyote surface.

| Coyote surface | Clockwork status | Notes |
| --- | --- | --- |
| `new TaskCompletionSource()` / `new TaskCompletionSource(TaskCreationOptions)` (and generic) | 🏛 Controlled by architecture | explicit `ControlledTaskCompletionSource(…)` / `<TResult>(…)` ctors available; `TaskCreationOptions` normalized (`Normalize`) |
| `SetResult` / `SetException` / `SetCanceled` (and generic `SetResult(TResult)`) | 🏛 Controlled by architecture | completing the task lets the controlled awaiter pick up the continuation on the coordinator; mirrored by `ControlledTaskCompletionSource.Set*` |
| `TrySetResult` / `TrySetException(Exception \| IEnumerable<Exception>)` / `TrySetCanceled` | 🏛 Controlled by architecture | mirrored by `ControlledTaskCompletionSource.TrySet*` |
| `get_Task` | 🏛 Controlled by architecture | `ControlledTaskCompletionSource.Task` |

---

## `System.Threading.Tasks.TaskExtensions`

| Coyote surface | .NET 10 signature | Clockwork status | Notes |
| --- | --- | --- | --- |
| `Unwrap(this Task<Task>)` | `Task Unwrap(this Task<Task>)` | 🏛 Controlled by architecture | `Task.Run(Func<Task>)`/`Run(Func<Task<T>>)` unwrap internally in `ControlledTask.Run`; a standalone `.Unwrap()` produces a task that is controlled when awaited |
| `Unwrap<TResult>(this Task<Task<TResult>>)` | ditto | 🏛 Controlled by architecture | as above |

---

## `System.Threading.Tasks.Parallel` — Coyote `…Types.Threading.Tasks.Parallel`

Coyote wraps the full `Invoke`/`For`/`ForEach` matrix including thread-local (`TLocal`) and
`Partitioner`/`OrderablePartitioner` overloads. Clockwork controls the simple-body overloads and
**rejects** the loop-state / thread-local / partitioner forms with a tested diagnostic.

| Coyote surface | Clockwork status | Rule / reason |
| --- | --- | --- |
| `Invoke(Action[])` | ✅ Controlled | `clockwork.parallel.invoke` |
| `Invoke(ParallelOptions, Action[])` | ✅ Controlled | `clockwork.parallel.invoke.options` |
| `For(int, int, Action<int>)` (± `ParallelOptions`) | ✅ Controlled | `clockwork.parallel.for.int32[.options]` |
| `For(long, long, Action<long>)` (± `ParallelOptions`) | ✅ Controlled | `clockwork.parallel.for.int64[.options]` |
| `ForEach<T>(IEnumerable<T>, Action<T>)` (± `ParallelOptions`) | ✅ Controlled | `clockwork.parallel.foreach[.options]` |
| `For(…, Action<int/long, ParallelLoopState>)` (break/stop) | ⛔ Rejected (tested) | `clockwork.parallel.for.int32/int64.loopstate[.options]` — `ParallelLoopState` has no public constructor and break/stop cannot be modelled deterministically yet |
| `ForEach(…, Action<T, ParallelLoopState>)` / `Action<T, ParallelLoopState, long>` | ⛔ Rejected (tested) | `clockwork.parallel.foreach.loopstate[.index]` |
| `For<TLocal>(…)` / `ForEach<TSource,TLocal>(…)` (thread-local accumulation) | ⛔ Rejected (tested) | rejected via the loop-state guard family — thread-local aggregation depends on the physical partition count |
| `ForEach<T>(Partitioner<T> \| OrderablePartitioner<T>, …)` | ⛔ Rejected (tested) | rejected — a `Partitioner` exposes no controllable enumeration for the coordinator |

**Deviation:** results, cancellation (`ParallelOptions.CancellationToken`), and fault aggregation
(`AggregateException`) are preserved for the controlled overloads; execution is cooperative
(each branch is a controlled operation on the single logical thread) rather than physically parallel.

---

## `System.Threading.ThreadPool` — **beyond Coyote**

Coyote has **no** controlled `ThreadPool` type (thread-pool work is expected to arrive via the
controlled `Task`/`TaskFactory` types). Clockwork models `ThreadPool` directly so applications that
queue raw work items are deterministic.

| .NET 10 signature | Clockwork status | Rule / reason |
| --- | --- | --- |
| `QueueUserWorkItem(WaitCallback)` | ✅ Controlled (flows `ExecutionContext`) | `clockwork.threadpool.queue.waitcallback` |
| `QueueUserWorkItem(WaitCallback, object)` | ✅ Controlled (flows `ExecutionContext`) | `clockwork.threadpool.queue.waitcallback.state` |
| `QueueUserWorkItem<TState>(Action<TState>, TState, bool preferLocal)` | ✅ Controlled (flows `ExecutionContext`) | `clockwork.threadpool.queue.generic` |
| `UnsafeQueueUserWorkItem(WaitCallback, object)` | ✅ Controlled (does **not** flow `ExecutionContext`) | `clockwork.threadpool.unsafequeue.waitcallback.state` |
| `UnsafeQueueUserWorkItem(IThreadPoolWorkItem, bool)` | ✅ Controlled (does **not** flow `ExecutionContext`) | `clockwork.threadpool.unsafequeue.workitem` |
| `UnsafeQueueUserWorkItem<TState>(Action<TState>, TState, bool)` | ✅ Controlled (does **not** flow `ExecutionContext`) | `clockwork.threadpool.unsafequeue.generic` |
| `UnsafeQueueNativeOverlapped(NativeOverlapped*)` | ⛔ Rejected (tested) | `clockwork.threadpool.unsafequeuenativeoverlapped` — native overlapped I/O cannot be modelled |
| `RegisterWaitForSingleObject(…)` (uint32/int32/int64/TimeSpan) | ⛔ Rejected (tested) → 🕗 Phase 7 | `clockwork.threadpool.registerwait.*` — depends on controlled `WaitHandle`s (Phase 7); rejected until then |
| `UnsafeRegisterWaitForSingleObject(…)` (uint32/int32/int64/TimeSpan) | ⛔ Rejected (tested) → 🕗 Phase 7 | `clockwork.threadpool.unsaferegisterwait.*` |

**ExecutionContext modelling:** the safe `QueueUserWorkItem` variants capture and flow the caller's
`ExecutionContext` (so `AsyncLocal` values are visible to the callback); the `Unsafe…` variants do
not — matching the BCL contract exactly, and covered by conformance tests.

---

## Coyote surfaces intentionally deferred (Phase 7 / Phase 8)

These Coyote controlled types are **out of Phase 6B scope** by the phase plan. Where a Phase 6B
surface would otherwise need them (the `ThreadPool` registered-wait APIs need controlled wait
handles), Phase 6B **rejects** the call with a tested diagnostic until the owning phase lands.

| Coyote type(s) | Owning phase | Phase 6B posture |
| --- | --- | --- |
| `Monitor`, `SemaphoreSlim` | Phase 7 | not rewritten (real BCL calls) |
| `WaitHandle`, `EventWaitHandle`, `AutoResetEvent`, `ManualResetEvent` | Phase 7 | not rewritten; unblocks `ThreadPool` registered-wait APIs, which stay rejected until then |
| `Interlocked`, `Volatile` | Phase 7 (race instrumentation) | not rewritten |
| `SpinWait` (struct) | Phase 7 | not rewritten (`Thread.SpinWait(int)` *static* **is** controlled — `clockwork.thread.spinwait`) |
| `Timer` / `PeriodicTimer` / `Task.Delay` / cancellation timers | Phase 8 | `Task.Delay` rejected; `Thread.Sleep` **is** a controlled virtual wait in Phase 6B |

---

## Summary

- **Coyote `Thread`:** 13/13 controlled surfaces mirrored; Clockwork additionally **rejects** 4
  OS-specific members Coyote leaves uncontrolled.
- **Coyote `Task` static / async machinery:** fully controlled (Phase 6A + `Task.Run` in 6B);
  `Task.Delay` deferred to Phase 8.
- **Coyote `TaskFactory`:** the offloading `StartNew` overloads controlled; unsupported
  scheduler/option combinations rejected.
- **Coyote `TaskCompletionSource` / `TaskExtensions` / `ValueTask`:** controlled by architecture
  (awaiter substitution) with explicit `Controlled*` types available.
- **Coyote `Parallel`:** simple-body overloads controlled; loop-state / thread-local / partitioner
  overloads rejected with tested diagnostics.
- **`ThreadPool`:** modelled by Clockwork **beyond Coyote**; native-overlapped and registered-wait
  APIs rejected (registered waits pending Phase 7).
- **Deferred by phase plan:** `Monitor`/`SemaphoreSlim`/wait handles/`Interlocked`/`Volatile`
  (Phase 7); timers/`Task.Delay` (Phase 8).

Every Coyote entry above is therefore **controlled** (with a cited rule id or by architecture),
**deliberately rejected with a tested reason**, or **explicitly deferred to a named later phase** —
no Coyote concurrency surface is silently unhandled.

using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Clockwork.Runtime.Tasks.CompilerServices;

/// <summary>
/// The controlled substitute for
/// <see cref="System.Runtime.CompilerServices.AsyncTaskMethodBuilder"/>, used as the builder field of a
/// rewritten <c>async Task</c> state machine. It is a thin value-type wrapper over the real builder and
/// forwards every compiler-driven member unchanged, so state-machine mechanics, the returned
/// <see cref="Task"/>, exception capture, and result completion keep their exact BCL semantics.
/// </summary>
/// <remarks>
/// The determinism does not come from this builder rewriting the continuation dance itself (that is
/// fragile, since it involves execution-context capture and state-machine boxing). It comes from the
/// awaiters the rewriter substitutes alongside it: when the real inner builder calls
/// <c>awaiter.UnsafeOnCompleted(moveNext)</c>, the awaiter is a controlled awaiter that hands
/// <c>moveNext</c> to the <see cref="ISimulationTaskCoordinator"/> instead of the thread pool. The
/// builder's job is simply to be the substitutable, controlled-by-construction anchor for the async
/// method and to forward faithfully.
/// </remarks>
public struct ControlledAsyncTaskMethodBuilder
{
    private AsyncTaskMethodBuilder _inner;

    private ControlledAsyncTaskMethodBuilder(AsyncTaskMethodBuilder inner) => _inner = inner;

    /// <summary>Creates a new controlled builder wrapping a fresh real builder.</summary>
    /// <returns>The new builder.</returns>
    public static ControlledAsyncTaskMethodBuilder Create() => new(AsyncTaskMethodBuilder.Create());

    /// <summary>Gets the task for the async method, a real <see cref="Task"/> with unchanged semantics.</summary>
    public readonly Task Task => _inner.Task;

    /// <summary>Begins running the state machine, up to its first suspension point.</summary>
    /// <typeparam name="TStateMachine">The state machine type.</typeparam>
    /// <param name="stateMachine">The state machine.</param>
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine =>
        _inner.Start(ref stateMachine);

    /// <summary>Associates the boxed state machine with the builder.</summary>
    /// <param name="stateMachine">The boxed state machine.</param>
    public readonly void SetStateMachine(IAsyncStateMachine stateMachine) =>
        _inner.SetStateMachine(stateMachine);

    /// <summary>Completes the task successfully.</summary>
    public void SetResult() => _inner.SetResult();

    /// <summary>Completes the task in a faulted or cancelled state.</summary>
    /// <param name="exception">The exception to fault (or cancel) the task with.</param>
    public void SetException(Exception exception) => _inner.SetException(exception);

    /// <summary>Schedules the state machine to resume when the awaiter completes (context-flowing).</summary>
    /// <typeparam name="TAwaiter">The awaiter type.</typeparam>
    /// <typeparam name="TStateMachine">The state machine type.</typeparam>
    /// <param name="awaiter">The awaiter.</param>
    /// <param name="stateMachine">The state machine.</param>
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);

    /// <summary>Schedules the state machine to resume when the awaiter completes (non context-flowing).</summary>
    /// <typeparam name="TAwaiter">The awaiter type.</typeparam>
    /// <typeparam name="TStateMachine">The state machine type.</typeparam>
    /// <param name="awaiter">The awaiter.</param>
    /// <param name="stateMachine">The state machine.</param>
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
}

/// <summary>
/// The controlled substitute for
/// <see cref="System.Runtime.CompilerServices.AsyncTaskMethodBuilder{TResult}"/>. See
/// <see cref="ControlledAsyncTaskMethodBuilder"/> for the design; this variant produces a
/// <see cref="Task{TResult}"/> and completes it with a result.
/// </summary>
/// <typeparam name="TResult">The result type of the async method.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The C# compiler's async builder pattern requires a static Create() method on the builder type; this mirrors the BCL AsyncTaskMethodBuilder<TResult>.")]
public struct ControlledAsyncTaskMethodBuilder<TResult>
{
    private AsyncTaskMethodBuilder<TResult> _inner;

    private ControlledAsyncTaskMethodBuilder(AsyncTaskMethodBuilder<TResult> inner) => _inner = inner;

    /// <summary>Creates a new controlled builder wrapping a fresh real builder.</summary>
    /// <returns>The new builder.</returns>
    public static ControlledAsyncTaskMethodBuilder<TResult> Create() => new(AsyncTaskMethodBuilder<TResult>.Create());

    /// <summary>Gets the task for the async method, a real <see cref="Task{TResult}"/> with unchanged semantics.</summary>
    public readonly Task<TResult> Task => _inner.Task;

    /// <summary>Begins running the state machine, up to its first suspension point.</summary>
    /// <typeparam name="TStateMachine">The state machine type.</typeparam>
    /// <param name="stateMachine">The state machine.</param>
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine =>
        _inner.Start(ref stateMachine);

    /// <summary>Associates the boxed state machine with the builder.</summary>
    /// <param name="stateMachine">The boxed state machine.</param>
    public readonly void SetStateMachine(IAsyncStateMachine stateMachine) =>
        _inner.SetStateMachine(stateMachine);

    /// <summary>Completes the task successfully with the given result.</summary>
    /// <param name="result">The result value.</param>
    public void SetResult(TResult result) => _inner.SetResult(result);

    /// <summary>Completes the task in a faulted or cancelled state.</summary>
    /// <param name="exception">The exception to fault (or cancel) the task with.</param>
    public void SetException(Exception exception) => _inner.SetException(exception);

    /// <summary>Schedules the state machine to resume when the awaiter completes (context-flowing).</summary>
    /// <typeparam name="TAwaiter">The awaiter type.</typeparam>
    /// <typeparam name="TStateMachine">The state machine type.</typeparam>
    /// <param name="awaiter">The awaiter.</param>
    /// <param name="stateMachine">The state machine.</param>
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _inner.AwaitOnCompleted(ref awaiter, ref stateMachine);

    /// <summary>Schedules the state machine to resume when the awaiter completes (non context-flowing).</summary>
    /// <typeparam name="TAwaiter">The awaiter type.</typeparam>
    /// <typeparam name="TStateMachine">The state machine type.</typeparam>
    /// <param name="awaiter">The awaiter.</param>
    /// <param name="stateMachine">The state machine.</param>
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine =>
        _inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
}

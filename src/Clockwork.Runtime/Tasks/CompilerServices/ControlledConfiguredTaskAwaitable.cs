using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tasks.CompilerServices;

/// <summary>
/// The controlled substitute for
/// <see cref="System.Runtime.CompilerServices.ConfiguredTaskAwaitable"/> - the value produced by
/// <c>task.ConfigureAwait(continueOnCapturedContext)</c>. This is the type that makes
/// "<c>ConfigureAwait(false)</c> stays controlled" true: inside a simulation the
/// <c>continueOnCapturedContext</c> flag is deliberately ignored because there is no captured
/// <see cref="System.Threading.SynchronizationContext"/> to honour or discard - every continuation is
/// routed through the coordinator onto the one logical thread regardless.
/// </summary>
public readonly struct ControlledConfiguredTaskAwaitable
{
    private readonly Task _task;
    private readonly bool _continueOnCapturedContext;

    /// <summary>Initializes a new controlled configured awaitable.</summary>
    /// <param name="task">The task being awaited.</param>
    /// <param name="continueOnCapturedContext">The requested continuation-context behaviour (ignored by controlled scheduling).</param>
    public ControlledConfiguredTaskAwaitable(Task task, bool continueOnCapturedContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.ConfigureAwait");
        ArgumentNullException.ThrowIfNull(task);
        _task = task;
        _continueOnCapturedContext = continueOnCapturedContext;
    }

    /// <summary>Gets the awaiter for this configured awaitable.</summary>
    /// <returns>A <see cref="ControlledConfiguredTaskAwaiter"/>.</returns>
    public ControlledConfiguredTaskAwaiter GetAwaiter()
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.ConfigureAwait");
        return new(_task, _continueOnCapturedContext);
    }
}

/// <summary>The awaiter for <see cref="ControlledConfiguredTaskAwaitable"/>.</summary>
public readonly struct ControlledConfiguredTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
{
    private const string ApiName = "System.Threading.Tasks.Task.ConfigureAwait";
    private readonly Task _task;
    private readonly bool _continueOnCapturedContext;

    internal ControlledConfiguredTaskAwaiter(Task task, bool continueOnCapturedContext)
    {
        _task = task;
        _continueOnCapturedContext = continueOnCapturedContext;
    }

    /// <summary>Gets a value indicating whether the awaited task has already completed.</summary>
    public bool IsCompleted => (SimulationRuntimeDispatch.RequireActiveSimulation(ApiName), _task.IsCompleted).Item2;

    /// <summary>Completes the await, throwing the task's fault or cancellation.</summary>
    public void GetResult()
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName);
        _task.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => Register(continuation, flowExecutionContext: true);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => Register(continuation, flowExecutionContext: false);

    private void Register(Action continuation, bool flowExecutionContext)
    {
        ControlledTaskRuntime.ScheduleContinuation(_task, continuation, ApiName, flowExecutionContext);
    }
}

/// <summary>
/// The controlled substitute for
/// <see cref="System.Runtime.CompilerServices.ConfiguredTaskAwaitable{TResult}"/>. Behaves exactly like
/// <see cref="ControlledConfiguredTaskAwaitable"/> but yields the awaited task's result.
/// </summary>
/// <typeparam name="TResult">The result type of the awaited task.</typeparam>
public readonly struct ControlledConfiguredTaskAwaitable<TResult>
{
    private readonly Task<TResult> _task;
    private readonly bool _continueOnCapturedContext;

    /// <summary>Initializes a new controlled configured awaitable.</summary>
    /// <param name="task">The task being awaited.</param>
    /// <param name="continueOnCapturedContext">The requested continuation-context behaviour (ignored by controlled scheduling).</param>
    public ControlledConfiguredTaskAwaitable(Task<TResult> task, bool continueOnCapturedContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.ConfigureAwait");
        ArgumentNullException.ThrowIfNull(task);
        _task = task;
        _continueOnCapturedContext = continueOnCapturedContext;
    }

    /// <summary>Gets the awaiter for this configured awaitable.</summary>
    /// <returns>A <see cref="ControlledConfiguredTaskAwaiter{TResult}"/>.</returns>
    public ControlledConfiguredTaskAwaiter<TResult> GetAwaiter()
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.ConfigureAwait");
        return new(_task, _continueOnCapturedContext);
    }
}

/// <summary>The awaiter for <see cref="ControlledConfiguredTaskAwaitable{TResult}"/>.</summary>
/// <typeparam name="TResult">The result type of the awaited task.</typeparam>
public readonly struct ControlledConfiguredTaskAwaiter<TResult> : ICriticalNotifyCompletion, INotifyCompletion
{
    private const string ApiName = "System.Threading.Tasks.Task`1.ConfigureAwait";
    private readonly Task<TResult> _task;
    private readonly bool _continueOnCapturedContext;

    internal ControlledConfiguredTaskAwaiter(Task<TResult> task, bool continueOnCapturedContext)
    {
        _task = task;
        _continueOnCapturedContext = continueOnCapturedContext;
    }

    /// <summary>Gets a value indicating whether the awaited task has already completed.</summary>
    public bool IsCompleted => (SimulationRuntimeDispatch.RequireActiveSimulation(ApiName), _task.IsCompleted).Item2;

    /// <summary>Completes the await, returning the result or throwing the task's fault/cancellation.</summary>
    /// <returns>The awaited task's result.</returns>
    public TResult GetResult() =>
        (SimulationRuntimeDispatch.RequireActiveSimulation(ApiName), _task.GetAwaiter().GetResult()).Item2;

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => Register(continuation, flowExecutionContext: true);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => Register(continuation, flowExecutionContext: false);

    private void Register(Action continuation, bool flowExecutionContext)
    {
        ControlledTaskRuntime.ScheduleContinuation(_task, continuation, ApiName, flowExecutionContext);
    }
}

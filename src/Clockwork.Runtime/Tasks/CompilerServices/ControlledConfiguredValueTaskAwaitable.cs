using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Clockwork.Runtime.Tasks.CompilerServices;

/// <summary>
/// The controlled substitute for
/// <see cref="System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable"/> - the value produced by
/// <c>valueTask.ConfigureAwait(continueOnCapturedContext)</c>. Like
/// <see cref="ControlledConfiguredTaskAwaitable"/> it makes "<c>ConfigureAwait(false)</c> stays
/// controlled" true for <see cref="ValueTask"/>: inside a simulation the flag is ignored because every
/// continuation is routed through the coordinator onto the one logical thread; outside a simulation the
/// flag is honoured exactly by delegating to the real configured value-task awaiter.
/// </summary>
public readonly struct ControlledConfiguredValueTaskAwaitable
{
    private readonly ValueTask _valueTask;
    private readonly bool _continueOnCapturedContext;

    /// <summary>Initializes a new controlled configured value-task awaitable.</summary>
    /// <param name="valueTask">The value task being awaited.</param>
    /// <param name="continueOnCapturedContext">The requested continuation-context behaviour (honoured only outside simulation).</param>
    public ControlledConfiguredValueTaskAwaitable(in ValueTask valueTask, bool continueOnCapturedContext)
    {
        _valueTask = valueTask;
        _continueOnCapturedContext = continueOnCapturedContext;
    }

    /// <summary>Gets the awaiter for this configured awaitable.</summary>
    /// <returns>A <see cref="ControlledConfiguredValueTaskAwaiter"/>.</returns>
    public ControlledConfiguredValueTaskAwaiter GetAwaiter() => new(_valueTask, _continueOnCapturedContext);
}

/// <summary>The awaiter for <see cref="ControlledConfiguredValueTaskAwaitable"/>.</summary>
public readonly struct ControlledConfiguredValueTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
{
    private const string ApiName = "System.Threading.Tasks.ValueTask.ConfigureAwait";
    private readonly ValueTask _valueTask;
    private readonly bool _continueOnCapturedContext;

    internal ControlledConfiguredValueTaskAwaiter(ValueTask valueTask, bool continueOnCapturedContext)
    {
        _valueTask = valueTask;
        _continueOnCapturedContext = continueOnCapturedContext;
    }

    /// <summary>Gets a value indicating whether the awaited value task has already completed.</summary>
    public bool IsCompleted => _valueTask.IsCompleted;

    /// <summary>Completes the await, throwing the value task's fault or cancellation.</summary>
    public void GetResult() => _valueTask.GetAwaiter().GetResult();

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => Register(continuation, flowExecutionContext: true);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => Register(continuation, flowExecutionContext: false);

    private void Register(Action continuation, bool flowExecutionContext)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (ControlledTaskRuntime.TryGetCoordinator(ApiName, out var coordinator, out var node))
        {
            var valueTask = _valueTask;
            coordinator.ScheduleWhenReady(node, () => valueTask.IsCompleted, continuation);
        }
        else if (flowExecutionContext)
        {
            _valueTask.ConfigureAwait(_continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
        }
        else
        {
            _valueTask.ConfigureAwait(_continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
        }
    }
}

/// <summary>
/// The controlled substitute for
/// <see cref="System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable{TResult}"/>. Behaves exactly
/// like <see cref="ControlledConfiguredValueTaskAwaitable"/> but yields the awaited value task's result.
/// </summary>
/// <typeparam name="TResult">The result type of the awaited value task.</typeparam>
public readonly struct ControlledConfiguredValueTaskAwaitable<TResult>
{
    private readonly ValueTask<TResult> _valueTask;
    private readonly bool _continueOnCapturedContext;

    /// <summary>Initializes a new controlled configured value-task awaitable.</summary>
    /// <param name="valueTask">The value task being awaited.</param>
    /// <param name="continueOnCapturedContext">The requested continuation-context behaviour (honoured only outside simulation).</param>
    public ControlledConfiguredValueTaskAwaitable(in ValueTask<TResult> valueTask, bool continueOnCapturedContext)
    {
        _valueTask = valueTask;
        _continueOnCapturedContext = continueOnCapturedContext;
    }

    /// <summary>Gets the awaiter for this configured awaitable.</summary>
    /// <returns>A <see cref="ControlledConfiguredValueTaskAwaiter{TResult}"/>.</returns>
    public ControlledConfiguredValueTaskAwaiter<TResult> GetAwaiter() => new(_valueTask, _continueOnCapturedContext);
}

/// <summary>The awaiter for <see cref="ControlledConfiguredValueTaskAwaitable{TResult}"/>.</summary>
/// <typeparam name="TResult">The result type of the awaited value task.</typeparam>
public readonly struct ControlledConfiguredValueTaskAwaiter<TResult> : ICriticalNotifyCompletion, INotifyCompletion
{
    private const string ApiName = "System.Threading.Tasks.ValueTask`1.ConfigureAwait";
    private readonly ValueTask<TResult> _valueTask;
    private readonly bool _continueOnCapturedContext;

    internal ControlledConfiguredValueTaskAwaiter(ValueTask<TResult> valueTask, bool continueOnCapturedContext)
    {
        _valueTask = valueTask;
        _continueOnCapturedContext = continueOnCapturedContext;
    }

    /// <summary>Gets a value indicating whether the awaited value task has already completed.</summary>
    public bool IsCompleted => _valueTask.IsCompleted;

    /// <summary>Completes the await, returning the result or throwing the value task's fault/cancellation.</summary>
    /// <returns>The awaited value task's result.</returns>
    public TResult GetResult() => _valueTask.GetAwaiter().GetResult();

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => Register(continuation, flowExecutionContext: true);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => Register(continuation, flowExecutionContext: false);

    private void Register(Action continuation, bool flowExecutionContext)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (ControlledTaskRuntime.TryGetCoordinator(ApiName, out var coordinator, out var node))
        {
            var valueTask = _valueTask;
            coordinator.ScheduleWhenReady(node, () => valueTask.IsCompleted, continuation);
        }
        else if (flowExecutionContext)
        {
            _valueTask.ConfigureAwait(_continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
        }
        else
        {
            _valueTask.ConfigureAwait(_continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
        }
    }
}

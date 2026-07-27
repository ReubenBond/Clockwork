using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tasks.CompilerServices;

/// <summary>
/// The controlled substitute for <see cref="System.Runtime.CompilerServices.ValueTaskAwaiter"/>. Like
/// <see cref="ControlledTaskAwaiter"/> it routes the continuation through the ambient
/// <see cref="ISimulationTaskCoordinator"/> so the await resumes on the simulation's logical thread. The
/// awaited <see cref="ValueTask"/> is consumed exactly once (via <see cref="GetResult"/>); its
/// <see cref="ValueTask.IsCompleted"/> status is used as the coordinator's readiness gate.
/// </summary>
public readonly struct ControlledValueTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
{
    private const string ApiName = "System.Threading.Tasks.ValueTask.GetAwaiter";
    private readonly ValueTask _valueTask;

    /// <summary>Initializes a new controlled awaiter over <paramref name="valueTask"/>.</summary>
    /// <param name="valueTask">The value task being awaited.</param>
    public ControlledValueTaskAwaiter(in ValueTask valueTask)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName);
        _valueTask = valueTask;
    }

    /// <summary>Gets a value indicating whether the awaited value task has already completed.</summary>
    public bool IsCompleted => (SimulationRuntimeDispatch.RequireActiveSimulation(ApiName), _valueTask.IsCompleted).Item2;

    /// <summary>Completes the await, throwing the value task's fault or cancellation.</summary>
    public void GetResult()
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName);
        _valueTask.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => Register(continuation, flowExecutionContext: true);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => Register(continuation, flowExecutionContext: false);

    private void Register(Action continuation, bool flowExecutionContext)
    {
        var (coordinator, node) = ControlledTaskRuntime.RequireCoordinator(ApiName);
        ArgumentNullException.ThrowIfNull(continuation);
        var valueTask = _valueTask;
        coordinator.ScheduleWhenReady(node, () => valueTask.IsCompleted, continuation);
    }
}

/// <summary>
/// The controlled substitute for
/// <see cref="System.Runtime.CompilerServices.ValueTaskAwaiter{TResult}"/>. Behaves exactly like
/// <see cref="ControlledValueTaskAwaiter"/> but yields the awaited value task's result.
/// </summary>
/// <typeparam name="TResult">The result type of the awaited value task.</typeparam>
public readonly struct ControlledValueTaskAwaiter<TResult> : ICriticalNotifyCompletion, INotifyCompletion
{
    private const string ApiName = "System.Threading.Tasks.ValueTask`1.GetAwaiter";
    private readonly ValueTask<TResult> _valueTask;

    /// <summary>Initializes a new controlled awaiter over <paramref name="valueTask"/>.</summary>
    /// <param name="valueTask">The value task being awaited.</param>
    public ControlledValueTaskAwaiter(in ValueTask<TResult> valueTask)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName);
        _valueTask = valueTask;
    }

    /// <summary>Gets a value indicating whether the awaited value task has already completed.</summary>
    public bool IsCompleted => (SimulationRuntimeDispatch.RequireActiveSimulation(ApiName), _valueTask.IsCompleted).Item2;

    /// <summary>Completes the await, returning the result or throwing the value task's fault/cancellation.</summary>
    /// <returns>The awaited value task's result.</returns>
    public TResult GetResult() =>
        (SimulationRuntimeDispatch.RequireActiveSimulation(ApiName), _valueTask.GetAwaiter().GetResult()).Item2;

    /// <inheritdoc />
    public void OnCompleted(Action continuation) => Register(continuation, flowExecutionContext: true);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) => Register(continuation, flowExecutionContext: false);

    private void Register(Action continuation, bool flowExecutionContext)
    {
        var (coordinator, node) = ControlledTaskRuntime.RequireCoordinator(ApiName);
        ArgumentNullException.ThrowIfNull(continuation);
        var valueTask = _valueTask;
        coordinator.ScheduleWhenReady(node, () => valueTask.IsCompleted, continuation);
    }
}

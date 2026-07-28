using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tasks.CompilerServices;

/// <summary>
/// The controlled substitute for <see cref="System.Runtime.CompilerServices.TaskAwaiter"/>. A rewritten
/// state machine stores this in place of the real awaiter and the compiler-generated code drives it
/// through the identical surface (<see cref="IsCompleted"/>, <see cref="OnCompleted"/> /
/// <see cref="UnsafeOnCompleted"/>, <see cref="GetResult"/>). The only behavioural change is where the
/// continuation goes: instead of the awaited task's real completion callback (which may run inline or on
/// the thread pool, and honours a captured <see cref="System.Threading.SynchronizationContext"/>), the
/// continuation is handed to the ambient <see cref="ISimulationTaskCoordinator"/> so it always resumes
/// on the simulation's single logical thread.
/// </summary>
public readonly struct ControlledTaskAwaiter : ICriticalNotifyCompletion, INotifyCompletion
{
    private const string ApiName = "System.Threading.Tasks.Task.GetAwaiter";
    private readonly Task _task;

    /// <summary>Initializes a new controlled awaiter over <paramref name="task"/>.</summary>
    /// <param name="task">The task being awaited.</param>
    public ControlledTaskAwaiter(Task task)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName);
        ArgumentNullException.ThrowIfNull(task);
        _task = task;
    }

    /// <summary>Gets a value indicating whether the awaited task has already completed.</summary>
    public bool IsCompleted => (SimulationRuntimeDispatch.RequireActiveSimulation(ApiName), _task.IsCompleted).Item2;

    /// <summary>
    /// Completes the await, throwing the task's fault or cancellation exactly as the real awaiter would
    /// (the first exception unwrapped for a fault, <see cref="TaskCanceledException"/> for cancellation).
    /// </summary>
    public void GetResult()
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName);
        RaceSynchronization.Wait(_task);
        _task.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnCompleted(Action continuation) =>
        ControlledTaskRuntime.ScheduleContinuation(_task, continuation, ApiName, flowExecutionContext: true);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) =>
        ControlledTaskRuntime.ScheduleContinuation(_task, continuation, ApiName, flowExecutionContext: false);
}

/// <summary>
/// The controlled substitute for <see cref="System.Runtime.CompilerServices.TaskAwaiter{TResult}"/>.
/// Behaves exactly like <see cref="ControlledTaskAwaiter"/> but yields the awaited task's result.
/// </summary>
/// <typeparam name="TResult">The result type of the awaited task.</typeparam>
public readonly struct ControlledTaskAwaiter<TResult> : ICriticalNotifyCompletion, INotifyCompletion
{
    private const string ApiName = "System.Threading.Tasks.Task`1.GetAwaiter";
    private readonly Task<TResult> _task;

    /// <summary>Initializes a new controlled awaiter over <paramref name="task"/>.</summary>
    /// <param name="task">The task being awaited.</param>
    public ControlledTaskAwaiter(Task<TResult> task)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName);
        ArgumentNullException.ThrowIfNull(task);
        _task = task;
    }

    /// <summary>Gets a value indicating whether the awaited task has already completed.</summary>
    public bool IsCompleted => (SimulationRuntimeDispatch.RequireActiveSimulation(ApiName), _task.IsCompleted).Item2;

    /// <summary>Completes the await, returning the result or throwing the task's fault/cancellation.</summary>
    /// <returns>The awaited task's result.</returns>
    public TResult GetResult()
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName);
        RaceSynchronization.Wait(_task);
        return _task.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void OnCompleted(Action continuation) =>
        ControlledTaskRuntime.ScheduleContinuation(_task, continuation, ApiName, flowExecutionContext: true);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) =>
        ControlledTaskRuntime.ScheduleContinuation(_task, continuation, ApiName, flowExecutionContext: false);
}

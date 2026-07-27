using System.Runtime.CompilerServices;

namespace Clockwork.Runtime.Tasks.CompilerServices;

/// <summary>
/// The controlled substitute for <see cref="System.Runtime.CompilerServices.YieldAwaitable"/> - the
/// value produced by <c>Task.Yield()</c>. Awaiting it always suspends (its awaiter reports
/// <see cref="ControlledYieldAwaiter.IsCompleted"/> as <see langword="false"/>) and then resumes as
/// immediately-runnable work on the simulation's logical thread, giving other logical operations a
/// deterministic chance to run first. This is the controlled analogue of a real yield's "post the
/// continuation back asynchronously", but ordered by the coordinator instead of a
/// <see cref="System.Threading.SynchronizationContext"/> or the thread pool.
/// </summary>
public readonly struct ControlledYieldAwaitable
{
    /// <summary>Gets the awaiter for this yield awaitable.</summary>
    /// <returns>A <see cref="ControlledYieldAwaiter"/>.</returns>
    public ControlledYieldAwaiter GetAwaiter() => default;
}

/// <summary>The awaiter for <see cref="ControlledYieldAwaitable"/>.</summary>
public readonly struct ControlledYieldAwaiter : ICriticalNotifyCompletion, INotifyCompletion
{
    private const string ApiName = "System.Threading.Tasks.Task.Yield";

    /// <summary>
    /// Always <see langword="false"/> so that awaiting a yield always suspends and reschedules the
    /// continuation, exactly like the real yield awaiter.
    /// </summary>
    public bool IsCompleted => false;

    /// <summary>Completes the yield. A no-op, as a yield produces no result and cannot fault.</summary>
    public void GetResult()
    {
    }

    /// <inheritdoc />
    public void OnCompleted(Action continuation) =>
        ControlledTaskRuntime.ScheduleYield(continuation, ApiName, flowExecutionContext: true);

    /// <inheritdoc />
    public void UnsafeOnCompleted(Action continuation) =>
        ControlledTaskRuntime.ScheduleYield(continuation, ApiName, flowExecutionContext: false);
}

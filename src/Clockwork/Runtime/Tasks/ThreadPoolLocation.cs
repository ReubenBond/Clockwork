namespace Clockwork.Runtime.Tasks;

/// <summary>
/// <para>
/// Runs a completion which Clockwork makes on behalf of rewritten code where the BCL runs the framework continuations it
/// completes inline: with no <see cref="SynchronizationContext"/> current and with <see cref="TaskScheduler.Current"/>
/// the default scheduler, as on a thread-pool thread.
/// </para>
/// <para>
/// A continuation which uninstrumented code registers as a bare state machine (an await with
/// <c>ConfigureAwait(false)</c>, or one where no context or scheduler was current, as in the
/// <c>System.Linq.AsyncEnumerable</c> operators) runs inline in the call which completes its task only where the current
/// synchronization context is absent or the base one and the current scheduler is the default one
/// (<c>AwaitTaskContinuation.IsValidLocationForInlining</c>); anywhere else the BCL queues it to the real thread pool.
/// Rewritten code runs in such places: a scheduler lane's work runs with the lane's
/// <see cref="SimulationSynchronizationContext"/>, the root task of <c>SimulationCluster.RunToCompletion</c> and any other
/// task on a <see cref="SimulationTaskScheduler"/> run visibly on it, and a task which uninstrumented code constructs and
/// rewritten code starts runs visibly on the coordinator's scheduler. Those stay current for the rest of that work, so
/// uninstrumented code there continues and schedules work on the simulation; only the completion runs without them.
/// </para>
/// </summary>
internal static class ThreadPoolLocation
{
    /// <summary>Runs <paramref name="complete"/> as on a thread-pool thread.</summary>
    /// <typeparam name="TState">The state <paramref name="complete"/> takes.</typeparam>
    /// <typeparam name="TResult">The completion's result.</typeparam>
    /// <param name="state">The state passed to <paramref name="complete"/>.</param>
    /// <param name="complete">The completion.</param>
    /// <returns>What <paramref name="complete"/> returns.</returns>
    public static TResult Run<TState, TResult>(TState state, Func<TState, TResult> complete)
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        bool hasContext = context is not null && context.GetType() != typeof(SynchronizationContext);
        if (!hasContext && TaskScheduler.Current == TaskScheduler.Default)
        {
            return complete(state);
        }

        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            if (TaskScheduler.Current == TaskScheduler.Default)
            {
                return complete(state);
            }

            // Inside a task which hides its scheduler, TaskScheduler.Current is the default scheduler.
            Task<TResult> hidden = new(
                static boxed =>
                {
                    var (state, complete) = ((TState, Func<TState, TResult>))boxed!;
                    return complete(state);
                },
                (state, complete),
                CancellationToken.None,
                TaskCreationOptions.HideScheduler | TaskCreationOptions.DenyChildAttach);
            hidden.RunSynchronously(InlineScheduler.Instance);
            return hidden.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    // Runs a task only inline, on the thread which runs it synchronously. The BCL queues a task it declines to run
    // inline, which it does only when the thread's stack is nearly exhausted; waiting for it to run elsewhere would
    // block the calling thread, so that fails instead.
    private sealed class InlineScheduler : TaskScheduler
    {
        public static InlineScheduler Instance { get; } = new();

        protected override void QueueTask(Task task) => throw new InsufficientExecutionStackException();

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) =>
            !taskWasPreviouslyQueued && TryExecuteTask(task);

        protected override IEnumerable<Task> GetScheduledTasks() => [];
    }
}

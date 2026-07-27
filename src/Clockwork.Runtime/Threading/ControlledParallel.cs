using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Threading;

/// <summary>
/// <para>
/// Static shims for the <see cref="Parallel"/> surface. The rewriter redirects the supported static call
/// sites here. Instead of dispatching the loop body across physical thread-pool threads, each branch is
/// queued as a fresh controlled operation on the simulation coordinator and the call drains the
/// deterministic loop until every branch has completed, so the work runs on the single logical thread and
/// interleaves with all other controlled work at explicit yield points. Outside a simulation every shim
/// delegates to the real BCL API unchanged.
/// </para>
/// <para>
/// <b>Parity with Microsoft Coyote.</b> Coyote (MIT-licensed) controls only the simple-body overloads -
/// <c>For(int, int, Action&lt;int&gt;)</c> (+options) and <c>ForEach&lt;TSource&gt;(IEnumerable&lt;TSource&gt;,
/// Action&lt;TSource&gt;)</c> (+options) - by routing them through its controlled task scheduler, and treats
/// everything else as an uncontrolled invocation. Clockwork controls the same simple-body overloads and,
/// because its cooperative decomposition needs no framework scheduler, additionally controls
/// <see cref="Invoke(System.Action[])"/> and the 64-bit <c>For(long, long, Action&lt;long&gt;)</c> family.
/// </para>
/// <para>
/// <b>Rejected overloads.</b> The overloads whose body receives a <see cref="ParallelLoopState"/>
/// (break/stop), the thread-local (<c>TLocal</c>) overloads, and the <c>Partitioner</c> overloads cannot
/// be modelled without constructing framework types that have no public surface, so they are rejected
/// precisely at the call site - see <see cref="ControlledParallelUnsupportedException"/>. This mirrors
/// Coyote treating those as uncontrolled invocations.
/// </para>
/// <para>
/// <b>Deviations.</b> The controlled loop aggregates every branch fault into an
/// <see cref="AggregateException"/> (a real <see cref="Parallel"/> may stop launching iterations after the
/// first fault); cancellation is observed when the loop starts rather than mid-iteration. Both deviations
/// are documented in <c>docs/compatibility.md</c>.
/// </para>
/// </summary>
public static class ControlledParallel
{
    private const string InvokeApi = "System.Threading.Tasks.Parallel.Invoke";
    private const string ForApi = "System.Threading.Tasks.Parallel.For";
    private const string ForEachApi = "System.Threading.Tasks.Parallel.ForEach";

    /// <summary>Controlled <see cref="Parallel.Invoke(System.Action[])"/>.</summary>
    /// <param name="actions">The actions to execute as controlled operations.</param>
    public static void Invoke(params Action[] actions) => Invoke(new ParallelOptions(), actions);

    /// <summary>Controlled <see cref="Parallel.Invoke(ParallelOptions, System.Action[])"/>.</summary>
    /// <param name="parallelOptions">The options; only <see cref="ParallelOptions.CancellationToken"/> is observed under simulation.</param>
    /// <param name="actions">The actions to execute as controlled operations.</param>
    public static void Invoke(ParallelOptions parallelOptions, params Action[] actions)
    {
        ArgumentNullException.ThrowIfNull(parallelOptions);
        ArgumentNullException.ThrowIfNull(actions);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            Parallel.Invoke(parallelOptions, actions);
            return;
        }

        var bodies = new List<Action>(actions.Length);
        foreach (Action action in actions)
        {
            ArgumentNullException.ThrowIfNull(action);
            bodies.Add(action);
        }

        RunControlled(bodies, InvokeApi, parallelOptions.CancellationToken);
    }

    /// <summary>Controlled <see cref="Parallel.For(int, int, System.Action{int})"/>.</summary>
    /// <param name="fromInclusive">The start index, inclusive.</param>
    /// <param name="toExclusive">The end index, exclusive.</param>
    /// <param name="body">The per-iteration body.</param>
    /// <returns>A completed <see cref="ParallelLoopResult"/>.</returns>
    public static ParallelLoopResult For(int fromInclusive, int toExclusive, Action<int> body) =>
        For(fromInclusive, toExclusive, new ParallelOptions(), body);

    /// <summary>Controlled <see cref="Parallel.For(int, int, ParallelOptions, System.Action{int})"/>.</summary>
    /// <param name="fromInclusive">The start index, inclusive.</param>
    /// <param name="toExclusive">The end index, exclusive.</param>
    /// <param name="parallelOptions">The options; only <see cref="ParallelOptions.CancellationToken"/> is observed under simulation.</param>
    /// <param name="body">The per-iteration body.</param>
    /// <returns>A completed <see cref="ParallelLoopResult"/>.</returns>
    public static ParallelLoopResult For(int fromInclusive, int toExclusive, ParallelOptions parallelOptions, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(parallelOptions);
        ArgumentNullException.ThrowIfNull(body);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return Parallel.For(fromInclusive, toExclusive, parallelOptions, body);
        }

        var bodies = new List<Action>(Math.Max(0, toExclusive - fromInclusive));
        for (int i = fromInclusive; i < toExclusive; i++)
        {
            int captured = i;
            bodies.Add(() => body(captured));
        }

        RunControlled(bodies, ForApi, parallelOptions.CancellationToken);
        return CompletedResult();
    }

    /// <summary>Controlled <see cref="Parallel.For(long, long, System.Action{long})"/>.</summary>
    /// <param name="fromInclusive">The start index, inclusive.</param>
    /// <param name="toExclusive">The end index, exclusive.</param>
    /// <param name="body">The per-iteration body.</param>
    /// <returns>A completed <see cref="ParallelLoopResult"/>.</returns>
    public static ParallelLoopResult For(long fromInclusive, long toExclusive, Action<long> body) =>
        For(fromInclusive, toExclusive, new ParallelOptions(), body);

    /// <summary>Controlled <see cref="Parallel.For(long, long, ParallelOptions, System.Action{long})"/>.</summary>
    /// <param name="fromInclusive">The start index, inclusive.</param>
    /// <param name="toExclusive">The end index, exclusive.</param>
    /// <param name="parallelOptions">The options; only <see cref="ParallelOptions.CancellationToken"/> is observed under simulation.</param>
    /// <param name="body">The per-iteration body.</param>
    /// <returns>A completed <see cref="ParallelLoopResult"/>.</returns>
    public static ParallelLoopResult For(long fromInclusive, long toExclusive, ParallelOptions parallelOptions, Action<long> body)
    {
        ArgumentNullException.ThrowIfNull(parallelOptions);
        ArgumentNullException.ThrowIfNull(body);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return Parallel.For(fromInclusive, toExclusive, parallelOptions, body);
        }

        var bodies = new List<Action>();
        for (long i = fromInclusive; i < toExclusive; i++)
        {
            long captured = i;
            bodies.Add(() => body(captured));
        }

        RunControlled(bodies, ForApi, parallelOptions.CancellationToken);
        return CompletedResult();
    }

    /// <summary>Controlled <see cref="Parallel.ForEach{TSource}(IEnumerable{TSource}, System.Action{TSource})"/>.</summary>
    /// <typeparam name="TSource">The element type.</typeparam>
    /// <param name="source">The source sequence.</param>
    /// <param name="body">The per-element body.</param>
    /// <returns>A completed <see cref="ParallelLoopResult"/>.</returns>
    public static ParallelLoopResult ForEach<TSource>(IEnumerable<TSource> source, Action<TSource> body) =>
        ForEach(source, new ParallelOptions(), body);

    /// <summary>Controlled <see cref="Parallel.ForEach{TSource}(IEnumerable{TSource}, ParallelOptions, System.Action{TSource})"/>.</summary>
    /// <typeparam name="TSource">The element type.</typeparam>
    /// <param name="source">The source sequence.</param>
    /// <param name="parallelOptions">The options; only <see cref="ParallelOptions.CancellationToken"/> is observed under simulation.</param>
    /// <param name="body">The per-element body.</param>
    /// <returns>A completed <see cref="ParallelLoopResult"/>.</returns>
    public static ParallelLoopResult ForEach<TSource>(IEnumerable<TSource> source, ParallelOptions parallelOptions, Action<TSource> body)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parallelOptions);
        ArgumentNullException.ThrowIfNull(body);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return Parallel.ForEach(source, parallelOptions, body);
        }

        var bodies = new List<Action>();
        foreach (TSource item in source)
        {
            TSource captured = item;
            bodies.Add(() => body(captured));
        }

        RunControlled(bodies, ForEachApi, parallelOptions.CancellationToken);
        return CompletedResult();
    }

    /// <summary>
    /// Rejection injected before an unsupported <see cref="Parallel"/> call site (the
    /// <see cref="ParallelLoopState"/>, <c>TLocal</c>, or <c>Partitioner</c> overloads).
    /// </summary>
    /// <param name="apiName">The unsupported API, supplied by the rewriter.</param>
    public static void RejectUnsupported(string apiName) =>
        throw new ControlledParallelUnsupportedException(
            apiName,
            "the break/stop (ParallelLoopState), thread-local (TLocal), and Partitioner overloads cannot be " +
            "modelled deterministically without constructing framework types that have no public surface; " +
            "use a simple-body For/ForEach/Invoke overload instead.");

    // Queues every branch as a fresh controlled operation, drains the deterministic loop until all branches
    // complete, then re-throws any faults aggregated into an AggregateException (matching Parallel).
    private static void RunControlled(List<Action> bodies, string api, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bodies.Count == 0)
        {
            return;
        }

        var completions = new TaskCompletionSource[bodies.Count];
        var faults = new List<Exception>();
        for (int i = 0; i < bodies.Count; i++)
        {
            var completion = new TaskCompletionSource();
            completions[i] = completion;
            Action body = bodies[i];
            ControlledTaskRuntime.QueueWork(
                () =>
                {
                    try
                    {
                        body();
                    }
                    catch (Exception exception)
                    {
                        faults.Add(exception);
                    }
                    finally
                    {
                        completion.SetResult();
                    }
                },
                api);
        }

        ControlledTaskRuntime.DrainUntil(() => AllCompleted(completions), api);

        if (faults.Count > 0)
        {
            throw new AggregateException(faults);
        }
    }

    private static bool AllCompleted(TaskCompletionSource[] completions)
    {
        foreach (TaskCompletionSource completion in completions)
        {
            if (!completion.Task.IsCompleted)
            {
                return false;
            }
        }

        return true;
    }

    // A canonical completed ParallelLoopResult (IsCompleted = true, LowestBreakIteration = null). The struct
    // has no public constructor; running a real zero-iteration loop yields the completed value without
    // reflection and never dispatches thread-pool work.
    private static ParallelLoopResult CompletedResult() => Parallel.For(0, 0, static _ => { });
}

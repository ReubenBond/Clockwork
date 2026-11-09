using System.Threading.Tasks;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Tests.Scheduling;
using Clockwork.Shims.System.Runtime.CompilerServices;

namespace Clockwork.Runtime.Tests.Racing;

/// <summary>
/// Race-detection edges through task completion. A producer writes a location and then completes a real
/// <see cref="TaskCompletionSource{TResult}"/> through a redirected completion member, which publishes the release edge;
/// a consumer observes the task through an await, a synchronous wait, a controlled combinator, a continuation, or a
/// value-task conversion, which acquires it, and then reads the location. Each pair is checked with the consumer
/// starting both after the completion and before it (so the combinators take both their synchronous and their
/// coordinator-completed paths). A write after the completion is not ordered before the read, which the detector
/// reports.
/// </summary>
public sealed class TaskCompletionRaceEdgeTests
{
    private const string Location = "Fx.State::Value";

    private static readonly string[] Producers = ["SetResult", "TrySetResult", "SetException", "TrySetCanceled", "SetFromTask"];

    private static readonly string[] Consumers =
        ["await", "wait", "result", "whenall", "whenany", "unwrap", "continuewith", "continuewith-await", "astask"];

    public static TheoryData<string, string, bool> Pairs
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach (string producer in Producers)
            {
                foreach (string consumer in Consumers)
                {
                    data.Add(producer, consumer, false);
                    data.Add(producer, consumer, true);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, bool> ConsumerOrders
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (string consumer in Consumers)
            {
                data.Add(consumer, false);
                data.Add(consumer, true);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void CompletionThenObservationOrdersTheAccesses(string producer, string consumer, bool consumerFirst)
    {
        using SimulationScheduler scheduler = Run(producer, consumer, consumerFirst, writeAfterCompletion: false);

        Assert.Null(scheduler.FirstRace);
    }

    [Theory]
    [MemberData(nameof(ConsumerOrders))]
    public void WriteAfterCompletionRacesWithTheObservingRead(string consumer, bool consumerFirst)
    {
        using SimulationScheduler scheduler = Run("SetResult", consumer, consumerFirst, writeAfterCompletion: true);

        RaceReport race = Assert.IsType<RaceReport>(scheduler.FirstRace);
        Assert.Equal(Location, race.FirstAccess.Location.Member);
    }

    private static SimulationScheduler Run(string producer, string consumer, bool consumerFirst, bool writeAfterCompletion)
    {
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        SimulationScheduler scheduler = SchedulerTestHarness.NewScheduler();
        Action produce = () =>
        {
            if (!writeAfterCompletion)
            {
                RaceInstrumentation.WriteStatic(Location, "producer", 1, null, -1);
            }

            Complete(producer, source);
            if (writeAfterCompletion)
            {
                RaceInstrumentation.WriteStatic(Location, "producer", 2, null, -1);
            }
        };
        Action consume = () => Observe(consumer, source.Task);

        if (consumerFirst)
        {
            scheduler.Schedule("consumer", consume);
            scheduler.Schedule("producer", produce);
        }
        else
        {
            scheduler.Schedule("producer", produce);
            scheduler.Drain(TestContext.Current.CancellationToken);
            scheduler.Schedule("consumer", consume);
        }

        scheduler.Drain(TestContext.Current.CancellationToken);
        Assert.True(source.Task.IsCompleted);
        return scheduler;
    }

    private static void Complete(string producer, TaskCompletionSource<int> source)
    {
        switch (producer)
        {
            case "SetResult":
                ControlledTaskCompletionSource.SetResult(source, 1);
                break;
            case "TrySetResult":
                Assert.True(ControlledTaskCompletionSource.TrySetResult(source, 1));
                break;
            case "SetException":
                ControlledTaskCompletionSource.SetException(source, new InvalidOperationException("boom"));
                break;
            case "TrySetCanceled":
                Assert.True(ControlledTaskCompletionSource.TrySetCanceled(source));
                break;
            default:
                ControlledTaskCompletionSource.SetFromTask(source, Task.FromResult(1));
                break;
        }
    }

    private static void Observe(string consumer, Task<int> task)
    {
        switch (consumer)
        {
            case "await":
                Ignore(() => new ControlledTaskAwaiter<int>(task).GetResult());
                Read();
                break;
            case "wait":
                Ignore(() => ControlledTask.Wait(task));
                Read();
                break;
            case "result":
                Ignore(() => ControlledTask.Result(task));
                Read();
                break;
            case "whenall":
                Ignore(() => new ControlledTaskAwaiter(ControlledTask.WhenAll(task, Task.CompletedTask)).GetResult());
                Read();
                break;
            case "whenany":
                Task<Task<int>> any = ControlledTask.WhenAny(task, new TaskCompletionSource<int>().Task);
                Assert.Same(task, new ControlledTaskAwaiter<Task<int>>(any).GetResult());
                Read();
                break;
            case "unwrap":
                var outer = new TaskCompletionSource<Task<int>>();
                Task<int> unwrapped = ControlledTask.Unwrap(outer.Task);
                ControlledTaskCompletionSource.SetResult(outer, task);
                Ignore(() => new ControlledTaskAwaiter<int>(unwrapped).GetResult());
                Read();
                break;
            case "continuewith":
                _ = ControlledTask.ContinueWith(task, _ => Read());
                break;
            case "continuewith-await":
                Task continuation = ControlledTask.ContinueWith(task, _ => { });
                new ControlledTaskAwaiter(continuation).GetResult();
                Read();
                break;
            default:
                Task<int> converted = ControlledValueTask.AsTask(new ValueTask<int>(task));
                Assert.Same(task, converted);
                Ignore(() => new ControlledTaskAwaiter<int>(converted).GetResult());
                Read();
                break;
        }
    }

    private static void Read() => RaceInstrumentation.ReadStatic(Location, "consumer", 3, null, -1);

    private static void Ignore(Action observe)
    {
        try
        {
            observe();
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or AggregateException)
        {
        }
    }
}

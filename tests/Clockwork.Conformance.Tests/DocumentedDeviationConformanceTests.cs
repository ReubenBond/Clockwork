using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// Pins the documented deviations from the BCL, with Clockwork as the oracle; each assertion's comment gives the BCL's
/// value. The coordinator observes completions between scheduling steps, so antecedents which complete within one step
/// are ordered by argument position, and a result whose antecedents complete later completes on a controlled operation
/// of its own, after the completing call. Both Release and Debug codegen are staged.
/// </summary>
public sealed class DocumentedDeviationConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Conf { public static class DeviationProbe {
            // c and then a complete in one step, before b.
            public static async Task<string> WhenEachSameStep()
            {
                var a = new TaskCompletionSource();
                var b = new TaskCompletionSource();
                var c = new TaskCompletionSource();
                Task[] tasks = [a.Task, b.Task, c.Task];
                IAsyncEnumerable<Task> each = Task.WhenEach(tasks);
                await Task.Yield();
                c.SetResult();
                a.SetResult();
                await Task.Yield();
                b.SetResult();
                var order = new List<int>();
                await foreach (Task task in each)
                {
                    order.Add(Array.IndexOf(tasks, task));
                }

                return string.Join(",", order);
            }

            // b and then a complete in one step.
            public static async Task<string> ContinueWhenAnySameStep()
            {
                var a = new TaskCompletionSource();
                var b = new TaskCompletionSource();
                Task<int> winner = Task.Factory.ContinueWhenAny(new[] { a.Task, b.Task }, task => task == a.Task ? 0 : 1);
                await Task.Yield();
                b.SetResult();
                a.SetResult();
                return (await winner).ToString();
            }

            // c and then a fault in one step, before b completes.
            public static async Task<string> WhenAllSameStep()
            {
                var a = new TaskCompletionSource();
                var b = new TaskCompletionSource();
                var c = new TaskCompletionSource();
                Task all = Task.WhenAll(a.Task, b.Task, c.Task);
                await Task.Yield();
                c.SetException(new FormatException("x"));
                a.SetException(new FormatException("y"));
                await Task.Yield();
                b.SetResult();
                try
                {
                    await all;
                }
                catch (FormatException)
                {
                }

                return string.Join(",", all.Exception!.InnerExceptions.Select(exception => exception.Message));
            }

            // b and then a complete in one step.
            public static async Task<string> WhenAnySameStep()
            {
                var a = new TaskCompletionSource();
                var b = new TaskCompletionSource();
                Task<Task> any = Task.WhenAny(a.Task, b.Task);
                await Task.Yield();
                b.SetResult();
                a.SetResult();
                return await any == a.Task ? "a" : "b";
            }

            // The last antecedent completes; the result is observed at once, inside the completing operation.
            public static async Task<string> CompletionAfterTheLastAntecedent()
            {
                var a = new TaskCompletionSource<int>();
                var b = new TaskCompletionSource<int>();
                Task<int[]> all = Task.WhenAll(a.Task, b.Task);
                await Task.Yield();
                a.SetResult(1);
                b.SetResult(2);
                bool completedInline = all.IsCompleted;
                await all;
                return completedInline.ToString();
            }

            // DisposeAsync while the callback runs returns a task which completes when the callback returns.
            public static async Task<string> TimerDisposalWhileTheCallbackRuns()
            {
                var entered = new TaskCompletionSource();
                var release = new TaskCompletionSource();
                var timer = new Timer(
                    _ =>
                    {
                        entered.TrySetResult();
                        release.Task.Wait();
                    },
                    null,
                    0,
                    Timeout.Infinite);
                await entered.Task;
                Task disposal = timer.DisposeAsync().AsTask();
                string observed = disposal.CreationOptions + " completed=" + disposal.IsCompleted;
                release.SetResult();
                await disposal;
                return observed;
            }
        } }
        """;

    private const string ProbeType = "Conf.DeviationProbe";

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _release;
    private readonly Lazy<StagedProbe> _debug;

    public DocumentedDeviationConformanceTests()
    {
        _release = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.DeviationRel", ProbeType, Source, optimize: true));
        _debug = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.DeviationDbg", ProbeType, Source, optimize: false));
    }

    public static TheoryData<bool> Optimizations => new() { true, false };

    [Theory]
    [MemberData(nameof(Optimizations))]
    public void WhenEachYieldsTasksCompletedInOneStepInArgumentOrder(bool optimize)
    {
        // The BCL yields "2,0,1": c, then a, in the order they completed.
        Assert.Equal("0,2,1", Simulated("WhenEachSameStep", optimize));
    }

    [Theory]
    [MemberData(nameof(Optimizations))]
    public void ContinueWhenAnyContinuesWithTheFirstInArgumentOrderOfTasksCompletedInOneStep(bool optimize)
    {
        // The BCL continues with b ("1"), which completed first.
        Assert.Equal("0", Simulated("ContinueWhenAnySameStep", optimize));
    }

    [Theory]
    [MemberData(nameof(Optimizations))]
    public void WhenAllAggregatesFaultsFromOneStepInArgumentOrder(bool optimize)
    {
        // The BCL aggregates "x,y": c's fault, then a's, in the order they completed.
        Assert.Equal("y,x", Simulated("WhenAllSameStep", optimize));
    }

    [Theory]
    [MemberData(nameof(Optimizations))]
    public void WhenAnyReturnsTheFirstInArgumentOrderOfTasksCompletedInOneStep(bool optimize)
    {
        // The BCL returns b, which completed first.
        Assert.Equal("a", Simulated("WhenAnySameStep", optimize));
    }

    [Theory]
    [MemberData(nameof(Optimizations))]
    public void AResultCompletesAfterTheOperationWhichCompletesItsLastAntecedent(bool optimize)
    {
        // The BCL completes the WhenAll inside the final SetResult ("True").
        Assert.Equal("False", Simulated("CompletionAfterTheLastAntecedent", optimize));
    }

    [Theory]
    [MemberData(nameof(Optimizations))]
    public void TimerDisposalTaskHasNoCreationOptions(bool optimize)
    {
        // The BCL's task runs its continuations asynchronously ("RunContinuationsAsynchronously completed=False").
        Assert.Equal("None completed=False", Simulated("TimerDisposalWhileTheCallbackRuns", optimize));
    }

    private string Simulated(string name, bool optimize)
    {
        using var host = new SimulationHost(Start);
        MethodInfo method = (optimize ? _release : _debug).Value.Method(name);
        var task = (Task<string>)host.Invoke(method)!;
        Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        return task.GetAwaiter().GetResult();
    }

    public void Dispose() => _fixture.Dispose();
}

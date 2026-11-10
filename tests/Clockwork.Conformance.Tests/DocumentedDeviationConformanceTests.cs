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

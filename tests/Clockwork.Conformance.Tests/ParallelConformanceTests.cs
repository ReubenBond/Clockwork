using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// End-to-end conformance for the controlled <see cref="System.Threading.Tasks.Parallel"/> surface (Phase
/// 6B). Once a fixture is rewritten with the controlled-task rule set, <c>Parallel.Invoke</c>/<c>For</c>/
/// <c>ForEach</c> decompose their branches into controlled operations on the coordinator and drain the
/// deterministic cluster drive until all complete, so every branch runs on the single logical thread;
/// faults aggregate into an <see cref="AggregateException"/>; and the break/stop (<c>ParallelLoopState</c>)
/// overloads are rejected precisely - proving the rule shapes resolve at real call sites.
/// </summary>
public sealed class ParallelConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        namespace Conf { public static class ParallelProbe {
            // Parallel.For runs every iteration on the single logical thread.
            public static Task<int> ForSumsAllIterations()
            {
                int sum = 0;
                Parallel.For(0, 5, i => sum += i);
                return Task.FromResult(sum);
            }

            // Parallel.Invoke runs every action.
            public static Task<int> InvokeRunsAllActions()
            {
                int count = 0;
                Parallel.Invoke(() => count += 1, () => count += 10, () => count += 100);
                return Task.FromResult(count);
            }

            // Parallel.ForEach processes every element.
            public static Task<int> ForEachSumsAllElements()
            {
                int sum = 0;
                var items = new List<int> { 3, 4, 5 };
                Parallel.ForEach(items, x => sum += x);
                return Task.FromResult(sum);
            }

            // A body fault surfaces as an AggregateException.
            public static Task<bool> BodyFaultAggregates()
            {
                try
                {
                    Parallel.For(0, 3, i => { if (i == 2) throw new InvalidOperationException("boom"); });
                    return Task.FromResult(false);
                }
                catch (AggregateException ex)
                {
                    return Task.FromResult(ex.InnerException is InvalidOperationException);
                }
            }

            // The break/stop overload is rejected until it can be modelled deterministically.
            public static Task<int> ForLoopStateIsRejected()
            {
                Parallel.For(0, 3, (i, state) => state.Break());
                return Task.FromResult(0);
            }
        } }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _probe;

    public ParallelConformanceTests() =>
        _probe = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.Parallel", "Conf.ParallelProbe", Source, optimize: true));

    [Fact]
    public void ForRunsEveryIterationUnderTheClusterDrive()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("ForSumsAllIterations"))!;
        Assert.Equal(0 + 1 + 2 + 3 + 4, Result<int>(task));
    }

    [Fact]
    public void InvokeRunsEveryAction()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("InvokeRunsAllActions"))!;
        Assert.Equal(111, Result<int>(task));
    }

    [Fact]
    public void ForEachProcessesEveryElement()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("ForEachSumsAllElements"))!;
        Assert.Equal(12, Result<int>(task));
    }

    [Fact]
    public void BodyFaultAggregatesIntoAggregateException()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("BodyFaultAggregates"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public void ForLoopStateOverloadIsRejected()
    {
        using var host = new SimulationHost(Start);
        var ex = Assert.ThrowsAny<Exception>(() => host.Invoke(Method("ForLoopStateIsRejected")));
        var unsupported = Unwrap(ex);
        Assert.Equal(
            "Clockwork.Runtime.Threading.ControlledParallelUnsupportedException",
            unsupported.GetType().FullName);
    }

    [Fact]
    public async Task RewrittenForDelegatesToRealParallelOutsideAnySimulation()
    {
        var task = (Task<int>)Method("ForSumsAllIterations").Invoke(null, null)!;
        Assert.Equal(0 + 1 + 2 + 3 + 4, await task);
    }

    private MethodInfo Method(string name) => _probe.Value.Method(name);

    private static T Result<T>(object task)
    {
        var typed = (Task)task;
        Assert.True(typed.IsCompleted);
        PropertyInfo result = typed.GetType().GetProperty("Result")!;
        return (T)result.GetValue(typed)!;
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException or AggregateException && ex.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    public void Dispose() => _fixture.Dispose();
}

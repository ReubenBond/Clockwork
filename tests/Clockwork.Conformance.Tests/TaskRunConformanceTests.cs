using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// End-to-end conformance for the controlled <see cref="Task.Run(System.Action)"/> family (Phase 6B):
/// once a fixture is rewritten with the controlled-task rule set, <c>Task.Run</c> queues its body as a
/// fresh controlled operation on the simulation coordinator rather than an uncontrolled physical
/// thread-pool thread, so the work runs deterministically on the single logical thread with correct
/// result, unwrap, fault, and cancellation semantics, and passes through to the real BCL outside any
/// simulation.
/// </summary>
public sealed class TaskRunConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace Conf { public static class RunProbe {
            // Task.Run(Func<TResult>) computes on the logical thread and returns its value.
            public static Task<int> RunValue() => Impl();
            private static async Task<int> Impl() => await Task.Run(() => 42);

            // Task.Run(Func<Task<TResult>>) unwraps the inner async task.
            public static Task<int> RunUnwrap() => UnwrapImpl();
            private static async Task<int> UnwrapImpl() => await Task.Run(async () => { await Task.Yield(); return 21 + 21; });

            // Fire-and-forget Task.Run advances under the cluster drive and mutates shared state.
            public static Task<int> FireAndForget(int[] sink) => FfImpl(sink);
            private static async Task<int> FfImpl(int[] sink)
            {
                var t = Task.Run(() => { sink[0] = 99; });
                await t;
                return sink[0];
            }

            // A faulting body surfaces the original exception through the awaited task.
            public static Task Faulting() => FaultImpl();
            private static async Task FaultImpl() => await Task.Run(() => throw new InvalidTimeZoneException("boom"));

            // A pre-canceled token cancels the task and never runs the body.
            public static Task<bool> Canceled()
            {
                var cts = new CancellationTokenSource();
                cts.Cancel();
                bool ran = false;
                var t = Task.Run(() => { ran = true; }, cts.Token);
                return WaitCanceled(t, () => ran);
            }
            private static async Task<bool> WaitCanceled(Task t, Func<bool> ran)
            {
                try { await t; } catch (OperationCanceledException) { }
                return t.IsCanceled && !ran();
            }
        } }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _probe;

    public TaskRunConformanceTests() =>
        _probe = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.TaskRun", "Conf.RunProbe", Source, optimize: true));

    [Fact]
    public void RunComputesResultOnTheLogicalThread()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("RunValue"))!;
        Assert.Equal(42, Result<int>(task));
    }

    [Fact]
    public void RunOfAsyncDelegateUnwrapsInnerTask()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("RunUnwrap"))!;
        Assert.Equal(42, Result<int>(task));
    }

    [Fact]
    public void FireAndForgetRunAdvancesUnderTheClusterDrive()
    {
        using var host = new SimulationHost(Start);
        var sink = new int[1];
        var task = (Task<int>)host.Invoke(Method("FireAndForget"), sink)!;
        Assert.Equal(99, Result<int>(task));
        Assert.Equal(99, sink[0]);
    }

    [Fact]
    public void RunPropagatesTheBodyFault()
    {
        using var host = new SimulationHost(Start);
        var task = (Task)host.Invoke(Method("Faulting"))!;
        Assert.Equal(TaskStatus.Faulted, task.Status);
        Assert.IsType<InvalidTimeZoneException>(task.Exception!.InnerException);
    }

    [Fact]
    public void RunWithCanceledTokenCancelsWithoutRunningBody()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("Canceled"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public async Task RewrittenRunDelegatesToRealBclOutsideAnySimulation()
    {
        var task = (Task<int>)Method("RunValue").Invoke(null, null)!;
        Assert.Equal(42, await task);
    }

    private MethodInfo Method(string name) => _probe.Value.Method(name);

    private static T Result<T>(object task)
    {
        var typed = (Task)task;
        Assert.True(typed.IsCompleted);
        PropertyInfo result = typed.GetType().GetProperty("Result")!;
        return (T)result.GetValue(typed)!;
    }

    public void Dispose() => _fixture.Dispose();
}

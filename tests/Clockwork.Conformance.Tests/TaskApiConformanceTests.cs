using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// End-to-end conformance for the ordinary <see cref="Task"/> surface that application code calls
/// directly (combinators, synchronous waits, continuations) once rewritten with the controlled-task
/// rule set, plus the cross-cutting guarantees the phase must hold: multiple awaiters on one antecedent,
/// deterministic <c>WhenAny</c> ordering, fault propagation through <c>WhenAll</c>, synchronous waits
/// that pump the loop instead of dead-locking, per-node isolation, and correct pass-through when the
/// rewritten assembly runs outside any simulation.
/// </summary>
/// <remarks>
/// These tests drive the non-generic <c>WhenAll</c>/<c>WhenAny</c>, <c>Task.Wait()</c>, and
/// <c>ContinueWith(Action&lt;Task&gt;)</c> overloads through non-generic <see cref="Task"/> antecedents;
/// the generic <c>Task&lt;T&gt;</c> combinators and the blocking <c>Result</c> accessor have their own
/// coverage in <see cref="GenericTaskConformanceTests"/>.
/// </remarks>
public sealed class TaskApiConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System.Threading.Tasks;
        namespace Conf { public static class TaskProbe {
            // ---- combinators (non-generic antecedents) ----
            public static Task WhenAllTwo(Task a, Task b) => WhenAllImpl(a, b);
            private static async Task WhenAllImpl(Task a, Task b) => await Task.WhenAll(a, b);

            public static Task<Task> WhenAnyTwo(Task a, Task b) => WhenAnyImpl(a, b);
            private static async Task<Task> WhenAnyImpl(Task a, Task b) => await Task.WhenAny(a, b);

            // ---- multiple awaiters share one antecedent ----
            public static Task<int> MultipleAwaiters(Task<int> shared) => Combine(AwaitOnce(shared), AwaitOnce(shared));
            private static async Task<int> AwaitOnce(Task<int> t) => await t;
            private static async Task<int> Combine(Task<int> a, Task<int> b) => await a + await b;

            // ---- continuation registered before the antecedent completes ----
            public static Task ContinueAfter(Task a, int[] sink) => a.ContinueWith(_ => { sink[0] = 7; });

            // ---- synchronous wait on a controlled producer must pump, not deadlock ----
            public static int WaitOnControlled()
            {
                Task<int> t = Producer();
                t.Wait();
                return t.Result;
            }

            public static Task<int> Producer() => ProducerImpl();
            private static async Task<int> ProducerImpl()
            {
                int sum = 0;
                for (int i = 0; i < 3; i++) { await Task.Yield(); sum += 10; }
                return sum;
            }
        } }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _probe;

    public TaskApiConformanceTests() =>
        _probe = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.TaskApi", "Conf.TaskProbe", Source, optimize: true));

    [Fact]
    public void WhenAllCompletesOnlyAfterEveryAntecedent()
    {
        using var host = new SimulationHost(Start);
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();

        var task = (Task)host.InvokeWithWork(
            Method("WhenAllTwo"),
            [a.Task, b.Task],
            () => b.SetResult(),
            () => a.SetResult())!;

        Assert.Equal(TaskStatus.RanToCompletion, task.Status);
    }

    [Fact]
    public void WhenAllPropagatesTheFaultedAntecedentsException()
    {
        using var host = new SimulationHost(Start);
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();
        var boom = new InvalidTimeZoneException("boom");

        var task = (Task)host.InvokeWithWork(
            Method("WhenAllTwo"),
            [a.Task, b.Task],
            () => a.SetException(boom),
            () => b.SetResult())!;

        Assert.Equal(TaskStatus.Faulted, task.Status);
        Assert.Same(boom, task.Exception!.InnerException);
    }

    [Fact]
    public void WhenAnyPicksTheFirstCompleterDeterministically()
    {
        using var host = new SimulationHost(Start);
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();

        // a completes before b on the single logical thread, so the winner is always a.
        var task = (Task<Task>)host.InvokeWithWork(
            Method("WhenAnyTwo"),
            [a.Task, b.Task],
            () => a.SetResult(),
            () => b.SetResult())!;

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(a.Task, Result<Task>(task));
    }

    [Fact]
    public void MultipleAwaitersOnOneAntecedentAllResume()
    {
        using var host = new SimulationHost(Start);
        var shared = new TaskCompletionSource<int>();

        var task = (Task<int>)host.InvokeWithWork(
            Method("MultipleAwaiters"),
            [shared.Task],
            () => shared.SetResult(21))!;

        Assert.Equal(42, Result<int>(task));
    }

    [Fact]
    public void ContinuationRunsOnTheLogicalThreadAfterTheAntecedent()
    {
        using var host = new SimulationHost(Start);
        var a = new TaskCompletionSource();
        var sink = new int[1];

        var task = (Task)host.InvokeWithWork(
            Method("ContinueAfter"),
            [a.Task, sink],
            () => a.SetResult())!;

        Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        Assert.Equal(7, sink[0]);
    }

    [Fact]
    public void SynchronousWaitPumpsTheLoopWithoutDeadlocking()
    {
        using var host = new SimulationHost(Start);

        object? result = host.Invoke(Method("WaitOnControlled"));

        Assert.Equal(30, result);
    }

    [Fact]
    public void EachNodePumpsItsOwnControlledWorkInIsolation()
    {
        using var host = new SimulationHost(Start, nodeAddresses: ["alpha", "beta"]);

        object? alpha = host.InvokeOnNode("alpha", Method("WaitOnControlled"));
        object? beta = host.InvokeOnNode("beta", Method("WaitOnControlled"));

        Assert.Equal(30, alpha);
        Assert.Equal(30, beta);
    }

    [Fact]
    public async Task RewrittenAssemblyDelegatesToRealBclOutsideAnySimulation()
    {
        // No SimulationHost: the ambient simulation runtime is inactive, so every controlled awaiter and
        // the controlled yield must fall through to the real BCL and complete on the thread pool.
        var task = (Task<int>)Method("Producer").Invoke(null, null)!;
        int value = await task;

        Assert.Equal(30, value);
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

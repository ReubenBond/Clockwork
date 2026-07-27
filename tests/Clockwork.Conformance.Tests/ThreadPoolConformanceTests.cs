using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// End-to-end conformance for the controlled <see cref="System.Threading.ThreadPool"/> queueing surface
/// (Phase 6B, beyond Coyote). Once a fixture is rewritten with the controlled-task rule set,
/// <c>ThreadPool.QueueUserWorkItem</c> / <c>UnsafeQueueUserWorkItem</c> queue their callback as a fresh
/// controlled operation that runs deterministically on the single logical thread under the cluster drive,
/// callbacks run in FIFO order with exactly one running at a time, and the safe-vs-unsafe
/// <c>ExecutionContext</c> flow distinction is observable at real call sites.
/// </summary>
public sealed class ThreadPoolConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System.Threading;
        using System.Threading.Tasks;
        namespace Conf { public static class PoolProbe {
            // QueueUserWorkItem runs its callback under the cluster drive and completes the awaited task.
            public static async Task<int> QueueRunsCallback()
            {
                var tcs = new TaskCompletionSource<int>();
                ThreadPool.QueueUserWorkItem(_ => tcs.SetResult(42));
                return await tcs.Task;
            }

            // Generic QueueUserWorkItem<TState> passes strongly-typed state.
            public static async Task<int> GenericQueuePassesState()
            {
                var tcs = new TaskCompletionSource<int>();
                ThreadPool.QueueUserWorkItem(s => tcs.SetResult(s), 7, false);
                return await tcs.Task;
            }

            // UnsafeQueueUserWorkItem(IThreadPoolWorkItem) executes the work item.
            public static async Task<bool> UnsafeWorkItemExecutes()
            {
                var tcs = new TaskCompletionSource<bool>();
                ThreadPool.UnsafeQueueUserWorkItem(new Item(tcs), false);
                return await tcs.Task;
            }

            private sealed class Item : IThreadPoolWorkItem
            {
                private readonly TaskCompletionSource<bool> _tcs;
                public Item(TaskCompletionSource<bool> tcs) => _tcs = tcs;
                public void Execute() => _tcs.SetResult(true);
            }

            // Callbacks run in FIFO order on the single logical thread (exactly one running at a time).
            public static async Task<string> CallbacksRunInOrder()
            {
                var sb = new System.Text.StringBuilder();
                var tcs = new TaskCompletionSource();
                ThreadPool.QueueUserWorkItem(_ => sb.Append('a'));
                ThreadPool.QueueUserWorkItem(_ => sb.Append('b'));
                ThreadPool.QueueUserWorkItem(_ => { sb.Append('c'); tcs.SetResult(); });
                await tcs.Task;
                return sb.ToString();
            }

            // Safe QueueUserWorkItem flows the caller's ExecutionContext (snapshot at enqueue time).
            public static async Task<int> SafeFlowsContext()
            {
                var ambient = new AsyncLocal<int>();
                ambient.Value = 5;
                var tcs = new TaskCompletionSource<int>();
                ThreadPool.QueueUserWorkItem(_ => tcs.SetResult(ambient.Value));
                ambient.Value = 9;
                return await tcs.Task;
            }

            // Unsafe variant does not flow the caller's ExecutionContext, so the callback observes the
            // drive loop's ambient context where the caller's AsyncLocal is unset (default 0).
            public static async Task<int> UnsafeDoesNotFlowContext()
            {
                var ambient = new AsyncLocal<int>();
                ambient.Value = 5;
                var tcs = new TaskCompletionSource<int>();
                ThreadPool.UnsafeQueueUserWorkItem(_ => tcs.SetResult(ambient.Value), null);
                ambient.Value = 9;
                return await tcs.Task;
            }
        } }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _probe;

    public ThreadPoolConformanceTests() =>
        _probe = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.ThreadPool", "Conf.PoolProbe", Source, optimize: true));

    [Fact]
    public void QueueUserWorkItemRunsCallbackUnderTheClusterDrive()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("QueueRunsCallback"))!;
        Assert.Equal(42, Result<int>(task));
    }

    [Fact]
    public void GenericQueueUserWorkItemPassesTypedState()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("GenericQueuePassesState"))!;
        Assert.Equal(7, Result<int>(task));
    }

    [Fact]
    public void UnsafeWorkItemExecutesUnderTheClusterDrive()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("UnsafeWorkItemExecutes"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public void CallbacksRunInFifoOrderOnTheSingleLogicalThread()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<string>)host.Invoke(Method("CallbacksRunInOrder"))!;
        Assert.Equal("abc", Result<string>(task));
    }

    [Fact]
    public void SafeQueueFlowsExecutionContext()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("SafeFlowsContext"))!;
        Assert.Equal(5, Result<int>(task));
    }

    [Fact]
    public void UnsafeQueueDoesNotFlowExecutionContext()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("UnsafeDoesNotFlowContext"))!;
        Assert.Equal(0, Result<int>(task));
    }

    [Fact]
    public async Task RewrittenQueueDelegatesToRealThreadPoolOutsideAnySimulation()
    {
        var task = (Task<int>)Method("QueueRunsCallback").Invoke(null, null)!;
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

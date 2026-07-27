using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// End-to-end conformance for the controlled <see cref="TaskFactory"/> / <see cref="TaskFactory{TResult}"/>
/// <c>StartNew</c> family and the generic <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;</c> closure
/// (Phase 6B, task-gap slice). Once a fixture is rewritten with the controlled-task rule set,
/// <c>Task.Factory.StartNew</c> queues its body as a fresh controlled operation and the generic
/// continuation runs on the single logical thread — proving the rewriter's combined
/// generic-method-on-generic-type binding resolves correctly at a real call site.
/// </summary>
public sealed class TaskFactoryConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System;
        using System.Globalization;
        using System.Threading;
        using System.Threading.Tasks;
        namespace Conf { public static class FactoryProbe {
            // Task.Factory.StartNew(Func<TResult>) computes on the logical thread and returns its value.
            public static Task<int> StartValue() => ValueImpl();
            private static async Task<int> ValueImpl() => await Task.Factory.StartNew(() => 42);

            // Task.Factory.StartNew(Action) mutates shared state under the cluster drive.
            public static Task<int> StartAction(int[] sink) => ActionImpl(sink);
            private static async Task<int> ActionImpl(int[] sink)
            {
                await Task.Factory.StartNew(() => { sink[0] = 7; });
                return sink[0];
            }

            // Generic Task<T>.ContinueWith<TNewResult> projects the antecedent result.
            public static Task<string> Projected() => ProjectImpl();
            private static async Task<string> ProjectImpl()
            {
                var antecedent = Task.Factory.StartNew(() => 21);
                var projected = antecedent.ContinueWith(t => (t.Result * 2).ToString(CultureInfo.InvariantCulture));
                return await projected;
            }

            // Generic Task<T>.ContinueWith(Action<Task<T>>) observes the typed antecedent.
            public static Task<int> Observed() => ObserveImpl();
            private static async Task<int> ObserveImpl()
            {
                int seen = 0;
                var antecedent = Task.Factory.StartNew(() => 11);
                await antecedent.ContinueWith(t => { seen = t.Result; });
                return seen;
            }

            // AttachedToParent is rejected precisely inside a simulation.
            public static Task<bool> RejectsAttached()
            {
                try
                {
                    Task.Factory.StartNew(() => { }, TaskCreationOptions.AttachedToParent);
                    return Task.FromResult(false);
                }
                catch (Exception ex) when (ex.GetType().Name == "ControlledTaskUnsupportedException")
                {
                    return Task.FromResult(true);
                }
            }
        } }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _probe;

    public TaskFactoryConformanceTests() =>
        _probe = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.TaskFactory", "Conf.FactoryProbe", Source, optimize: true));

    [Fact]
    public void StartNewComputesResultOnTheLogicalThread()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("StartValue"))!;
        Assert.Equal(42, Result<int>(task));
    }

    [Fact]
    public void StartNewActionAdvancesUnderTheClusterDrive()
    {
        using var host = new SimulationHost(Start);
        var sink = new int[1];
        var task = (Task<int>)host.Invoke(Method("StartAction"), sink)!;
        Assert.Equal(7, Result<int>(task));
        Assert.Equal(7, sink[0]);
    }

    [Fact]
    public void GenericContinueWithProjectsTheAntecedentResult()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<string>)host.Invoke(Method("Projected"))!;
        Assert.Equal("42", Result<string>(task));
    }

    [Fact]
    public void GenericContinueWithActionObservesTypedAntecedent()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("Observed"))!;
        Assert.Equal(11, Result<int>(task));
    }

    [Fact]
    public void StartNewRejectsAttachedToParentInsideSimulation()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("RejectsAttached"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public async Task RewrittenStartNewDelegatesToRealBclOutsideAnySimulation()
    {
        var task = (Task<int>)Method("StartValue").Invoke(null, null)!;
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

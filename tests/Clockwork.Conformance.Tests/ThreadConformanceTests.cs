using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// End-to-end conformance for the controlled <see cref="System.Threading.Thread"/> surface (Phase 6B).
/// Once a fixture is rewritten with the controlled-task rule set, <c>new Thread(...)</c> becomes a real
/// thread object whose body is queued as a fresh controlled operation, <c>Start</c> schedules it on the
/// single logical thread, and <c>Join</c> pumps the deterministic cluster drive until it terminates —
/// proving the constructor/instance/static rule shapes resolve at real call sites and run deterministically.
/// </summary>
public sealed class ThreadConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace Conf { public static class ThreadProbe {
            // A single controlled thread mutates shared state; Join observes its termination.
            public static Task<int> StartAndJoin()
            {
                int result = 0;
                var t = new Thread(() => result = 42);
                t.Start();
                t.Join();
                return Task.FromResult(result);
            }

            // Several controlled threads run cooperatively on the single logical thread; all are joined.
            public static Task<int> ManyThreads()
            {
                int total = 0;
                var threads = new Thread[4];
                for (int i = 0; i < threads.Length; i++)
                {
                    int local = i + 1;
                    threads[i] = new Thread(() => total += local);
                }
                foreach (var t in threads) t.Start();
                foreach (var t in threads) t.Join();
                return Task.FromResult(total);
            }

            // A parameterized thread receives its start argument.
            public static Task<string> Parameterized()
            {
                string captured = null;
                var t = new Thread(o => captured = (string)o);
                t.Start("payload");
                t.Join();
                return Task.FromResult(captured);
            }

            // Joining the same thread repeatedly observes the single termination without re-running the body.
            public static Task<int> RepeatedJoins()
            {
                int count = 0;
                var t = new Thread(() => count++);
                t.Start();
                t.Join();
                t.Join();
                t.Join();
                return Task.FromResult(count);
            }

            // A faulting thread body terminates deterministically; Join neither throws nor hangs.
            public static Task<bool> FaultingThreadJoins()
            {
                bool reached = false;
                var t = new Thread(() => { reached = true; throw new InvalidOperationException("boom"); });
                t.Start();
                t.Join();
                return Task.FromResult(reached);
            }

            // Thread.Sleep is a cooperative no-op inside a simulation (never blocks or uses real time).
            public static Task<int> SleepDoesNotBlock()
            {
                int result = 0;
                var t = new Thread(() => { Thread.Sleep(60_000); result = 5; });
                t.Start();
                t.Join();
                return Task.FromResult(result);
            }

            // OS thread priority is rejected precisely inside a simulation.
            public static Task<bool> RejectsPriority()
            {
                var t = new Thread(() => { });
                try
                {
                    t.Priority = ThreadPriority.Highest;
                    return Task.FromResult(false);
                }
                catch (Exception ex) when (ex.GetType().Name == "ControlledThreadUnsupportedException")
                {
                    return Task.FromResult(true);
                }
            }
        } }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _probe;

    public ThreadConformanceTests() =>
        _probe = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.Thread", "Conf.ThreadProbe", Source, optimize: true));

    [Fact]
    public void SingleThreadStartsAndJoinsUnderTheClusterDrive()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("StartAndJoin"))!;
        Assert.Equal(42, Result<int>(task));
    }

    [Fact]
    public void ManyThreadsAllRunAndJoinDeterministically()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("ManyThreads"))!;
        Assert.Equal(10, Result<int>(task));
    }

    [Fact]
    public void ParameterizedThreadReceivesArgument()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<string>)host.Invoke(Method("Parameterized"))!;
        Assert.Equal("payload", Result<string>(task));
    }

    [Fact]
    public void RepeatedJoinsObserveSingleTermination()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("RepeatedJoins"))!;
        Assert.Equal(1, Result<int>(task));
    }

    [Fact]
    public void FaultingThreadJoinsWithoutTearingDownTheHost()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("FaultingThreadJoins"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public void SleepInsideAThreadDoesNotBlockTheDrive()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("SleepDoesNotBlock"))!;
        Assert.Equal(5, Result<int>(task));
    }

    [Fact]
    public void PriorityIsRejectedInsideSimulation()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("RejectsPriority"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public async Task RewrittenThreadDelegatesToRealBclOutsideAnySimulation()
    {
        // No active simulation: the rewritten shims delegate to the real BCL Thread, running on a real
        // OS thread and joining normally.
        var task = (Task<int>)Method("StartAndJoin").Invoke(null, null)!;
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

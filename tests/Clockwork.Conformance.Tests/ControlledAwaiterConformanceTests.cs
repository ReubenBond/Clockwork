using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// Conformance of the controlled task and value-task awaiters outside the compiler's await pattern: a
/// rethrown exception's stack trace matches the one the BCL awaiter produces, with no Clockwork frame,
/// and a synchronous <c>GetAwaiter().GetResult()</c> on a task which later simulation work completes pumps
/// the simulation instead of blocking its only logical thread.
/// </summary>
public sealed class ControlledAwaiterConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System.Threading.Tasks;
        namespace Conf { public static class AwaiterProbe {
            public static async Task<int> Fail()
            {
                await Task.CompletedTask;
                throw new System.InvalidTimeZoneException("boom");
            }

            // ---- each await form over a faulted task, and ConfigureAwait calls which the BCL rejects ----
            public static async Task AwaitTask() { await (Task)Fail(); }
            public static async Task AwaitGenericTask() { _ = await Fail(); }
            public static async Task AwaitConfiguredTask() { await ((Task)Fail()).ConfigureAwait(false); }
            public static async Task AwaitConfiguredTaskOptions() { await ((Task)Fail()).ConfigureAwait(ConfigureAwaitOptions.None); }
            public static async Task AwaitConfiguredGenericTask() { _ = await Fail().ConfigureAwait(false); }
            public static async Task AwaitConfiguredGenericTaskOptions() { _ = await Fail().ConfigureAwait(ConfigureAwaitOptions.None); }
            public static async Task AwaitValueTask() { await new ValueTask(Fail()); }
            public static async Task AwaitGenericValueTask() { _ = await new ValueTask<int>(Fail()); }
            public static async Task AwaitConfiguredValueTask() { await new ValueTask(Fail()).ConfigureAwait(false); }
            public static async Task AwaitConfiguredGenericValueTask() { _ = await new ValueTask<int>(Fail()).ConfigureAwait(false); }
            public static async Task RejectTaskOptions() { await Task.CompletedTask.ConfigureAwait((ConfigureAwaitOptions)8); }
            public static async Task RejectGenericTaskOptions() { _ = await Task.FromResult(1).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing); }

            // ---- synchronous GetResult on an antecedent which later simulation work completes ----
            public static object GetTaskResult(Task<int> antecedent)
            {
                ((Task)antecedent).GetAwaiter().GetResult();
                return antecedent.Status;
            }

            public static object GetGenericTaskResult(Task<int> antecedent) => antecedent.GetAwaiter().GetResult();

            public static object GetConfiguredTaskResult(Task<int> antecedent)
            {
                ((Task)antecedent).ConfigureAwait(false).GetAwaiter().GetResult();
                return antecedent.Status;
            }

            public static object GetSuppressedTaskResult(Task<int> antecedent)
            {
                ((Task)antecedent).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).GetAwaiter().GetResult();
                return antecedent.Status;
            }

            public static object GetConfiguredGenericTaskResult(Task<int> antecedent) =>
                antecedent.ConfigureAwait(ConfigureAwaitOptions.ForceYielding).GetAwaiter().GetResult();

            public static object GetValueTaskResult(Task<int> antecedent)
            {
                new ValueTask(antecedent).GetAwaiter().GetResult();
                return antecedent.Status;
            }

            public static object GetGenericValueTaskResult(Task<int> antecedent) =>
                new ValueTask<int>(antecedent).GetAwaiter().GetResult();

            public static object GetConfiguredValueTaskResult(Task<int> antecedent)
            {
                new ValueTask(antecedent).ConfigureAwait(false).GetAwaiter().GetResult();
                return antecedent.Status;
            }

            public static object GetConfiguredGenericValueTaskResult(Task<int> antecedent) =>
                new ValueTask<int>(antecedent).ConfigureAwait(false).GetAwaiter().GetResult();
        } }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _release;
    private readonly Lazy<StagedProbe> _debug;
    private readonly Lazy<UninstrumentedProbe> _bclRelease;
    private readonly Lazy<UninstrumentedProbe> _bclDebug;

    public ControlledAwaiterConformanceTests()
    {
        _release = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.AwaiterRel", "Conf.AwaiterProbe", Source, optimize: true));
        _debug = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.AwaiterDbg", "Conf.AwaiterProbe", Source, optimize: false));
        _bclRelease = new Lazy<UninstrumentedProbe>(() =>
            _fixture.CompileUninstrumented("Conf.AwaiterBclRel", "Conf.AwaiterProbe", Source, optimize: true));
        _bclDebug = new Lazy<UninstrumentedProbe>(() =>
            _fixture.CompileUninstrumented("Conf.AwaiterBclDbg", "Conf.AwaiterProbe", Source, optimize: false));
    }

    public static TheoryData<bool, string> FaultingAwaits
    {
        get
        {
            string[] probes =
            [
                "AwaitTask",
                "AwaitGenericTask",
                "AwaitConfiguredTask",
                "AwaitConfiguredTaskOptions",
                "AwaitConfiguredGenericTask",
                "AwaitConfiguredGenericTaskOptions",
                "AwaitValueTask",
                "AwaitGenericValueTask",
                "AwaitConfiguredValueTask",
                "AwaitConfiguredGenericValueTask",
                "RejectTaskOptions",
                "RejectGenericTaskOptions",
            ];
            var data = new TheoryData<bool, string>();
            foreach (bool optimize in new[] { true, false })
            {
                foreach (string probe in probes)
                {
                    data.Add(optimize, probe);
                }
            }

            return data;
        }
    }

    /// <summary>The probe, whether the antecedent faults, and the probe's expected return value.</summary>
    public static TheoryData<string, bool, string> SynchronousGetResults => new()
    {
        { "GetTaskResult", false, nameof(TaskStatus.RanToCompletion) },
        { "GetGenericTaskResult", false, "41" },
        { "GetConfiguredTaskResult", false, nameof(TaskStatus.RanToCompletion) },
        { "GetSuppressedTaskResult", true, nameof(TaskStatus.Faulted) },
        { "GetConfiguredGenericTaskResult", false, "41" },
        { "GetValueTaskResult", false, nameof(TaskStatus.RanToCompletion) },
        { "GetGenericValueTaskResult", false, "41" },
        { "GetConfiguredValueTaskResult", false, nameof(TaskStatus.RanToCompletion) },
        { "GetConfiguredGenericValueTaskResult", false, "41" },
    };

    [Theory]
    [MemberData(nameof(FaultingAwaits))]
    public void RethrownExceptionStackTraceMatchesTheBclAwaiter(bool optimize, string probe)
    {
        var bclTask = (Task)(optimize ? _bclRelease : _bclDebug).Value.Method(probe).Invoke(null, null)!;
        string expected = FaultStackTrace(bclTask);
        using var host = new SimulationHost(Start);

        var task = (Task)host.Invoke((optimize ? _release : _debug).Value.Method(probe))!;

        string actual = FaultStackTrace(task);
        Assert.Contains("Conf.AwaiterProbe", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("Clockwork", actual, StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Theory(Timeout = 30_000)]
    [MemberData(nameof(SynchronousGetResults))]
    public async Task SynchronousGetResultPumpsTheSimulationUntilTheTaskCompletes(string probe, bool faults, string expected)
    {
        MethodInfo method = _release.Value.Method(probe);

        // A blocking GetResult would hang this thread forever, so it runs off the test thread and the
        // theory's timeout reports the hang.
        object? result = await Task.Run(
            () =>
            {
                using var host = new SimulationHost(Start);
                var antecedent = new TaskCompletionSource<int>();
                return host.InvokeWithWork(
                    method,
                    [antecedent.Task],
                    () =>
                    {
                        if (faults)
                        {
                            antecedent.SetException(new InvalidTimeZoneException("boom"));
                        }
                        else
                        {
                            antecedent.SetResult(41);
                        }
                    });
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, Convert.ToString(result, CultureInfo.InvariantCulture));
    }

    private static string FaultStackTrace(Task task)
    {
        Assert.Equal(TaskStatus.Faulted, task.Status);
        return Assert.Single(task.Exception!.InnerExceptions).StackTrace ?? string.Empty;
    }

    public void Dispose() => _fixture.Dispose();
}

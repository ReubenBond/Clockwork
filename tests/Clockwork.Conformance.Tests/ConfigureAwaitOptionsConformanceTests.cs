using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// End-to-end conformance for <c>Task.ConfigureAwait(ConfigureAwaitOptions)</c> and
/// <c>Task&lt;TResult&gt;.ConfigureAwait(ConfigureAwaitOptions)</c> in rewritten code. The rewriter constructs the
/// controlled configured awaitable from the options overload, so every await resumes through the coordinator
/// while <see cref="ConfigureAwaitOptions.SuppressThrowing"/>, <see cref="ConfigureAwaitOptions.ForceYielding"/>,
/// and invalid options behave as they do on the BCL awaiter. The probe passes its options through as values,
/// and the probe is staged in both Release and Debug codegen because their async state machines differ.
/// </summary>
public sealed class ConfigureAwaitOptionsConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        namespace Conf { public static class ConfigureAwaitProbe {
            // Reports the antecedent's status once the await itself has completed normally.
            public static async Task<TaskStatus> Await(Task antecedent, ConfigureAwaitOptions options)
            {
                await antecedent.ConfigureAwait(options);
                return antecedent.Status;
            }

            public static async Task<int> AwaitGeneric(Task<int> antecedent, ConfigureAwaitOptions options) =>
                await antecedent.ConfigureAwait(options);

            public static async Task<int> AwaitBool(Task<int> antecedent, bool continueOnCapturedContext) =>
                await antecedent.ConfigureAwait(continueOnCapturedContext);

            // Logs whether the async method returned to its caller before it resumed from the await.
            public static Task Logged(Task antecedent, ConfigureAwaitOptions options, List<string> log)
            {
                Task pending = LoggedImpl(antecedent, options, log);
                log.Add(pending.IsCompleted ? "returned completed" : "returned pending");
                return pending;
            }

            public static Task<int> LoggedGeneric(Task<int> antecedent, ConfigureAwaitOptions options, List<string> log)
            {
                Task<int> pending = LoggedGenericImpl(antecedent, options, log);
                log.Add(pending.IsCompleted ? "returned completed" : "returned pending");
                return pending;
            }

            private static async Task LoggedImpl(Task antecedent, ConfigureAwaitOptions options, List<string> log)
            {
                log.Add("awaiting");
                await antecedent.ConfigureAwait(options);
                log.Add("resumed");
            }

            private static async Task<int> LoggedGenericImpl(Task<int> antecedent, ConfigureAwaitOptions options, List<string> log)
            {
                log.Add("awaiting");
                int value = await antecedent.ConfigureAwait(options);
                log.Add("resumed");
                return value;
            }
        } }
        """;

    private static readonly string[] Yielded = ["awaiting", "returned pending", "resumed"];
    private static readonly string[] RanSynchronously = ["awaiting", "resumed", "returned completed"];

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _release;
    private readonly Lazy<StagedProbe> _debug;

    public ConfigureAwaitOptionsConformanceTests()
    {
        _release = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.ConfigureAwaitRel", "Conf.ConfigureAwaitProbe", Source, optimize: true));
        _debug = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.ConfigureAwaitDbg", "Conf.ConfigureAwaitProbe", Source, optimize: false));
    }

    /// <summary>Optimization, the antecedent's outcome, and whether it completes only after the await suspends.</summary>
    public static TheoryData<bool, TaskStatus, bool> AntecedentOutcomes => new()
    {
        { true, TaskStatus.Faulted, true },
        { true, TaskStatus.Faulted, false },
        { true, TaskStatus.Canceled, true },
        { true, TaskStatus.Canceled, false },
        { false, TaskStatus.Faulted, true },
        { false, TaskStatus.Faulted, false },
        { false, TaskStatus.Canceled, true },
        { false, TaskStatus.Canceled, false },
    };

    public static TheoryData<bool, ConfigureAwaitOptions> CompletedTaskOptions => new()
    {
        { true, ConfigureAwaitOptions.None },
        { true, ConfigureAwaitOptions.ForceYielding },
        { true, ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext },
        { false, ConfigureAwaitOptions.None },
        { false, ConfigureAwaitOptions.ForceYielding },
        { false, ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext },
    };

    public static TheoryData<bool, ConfigureAwaitOptions> InvalidTaskOptions => new()
    {
        { true, (ConfigureAwaitOptions)8 },
        { false, (ConfigureAwaitOptions)8 },
    };

    public static TheoryData<bool, ConfigureAwaitOptions> InvalidGenericTaskOptions => new()
    {
        { true, ConfigureAwaitOptions.SuppressThrowing },
        { true, ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding },
        { true, (ConfigureAwaitOptions)8 },
        { false, ConfigureAwaitOptions.SuppressThrowing },
        { false, ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding },
        { false, (ConfigureAwaitOptions)8 },
    };

    public static TheoryData<bool, bool> BoolOverloads => new()
    {
        { true, false },
        { true, true },
        { false, false },
        { false, true },
    };

    [Theory]
    [MemberData(nameof(AntecedentOutcomes))]
    public void SuppressThrowingCompletesTheAwaitNormally(bool optimize, TaskStatus outcome, bool completesWhileSuspended)
    {
        using var host = new SimulationHost(Start);
        var antecedent = new TaskCompletionSource();

        var task = (Task<TaskStatus>)host.InvokeWithWork(
            Method("Await", optimize),
            [antecedent.Task, ConfigureAwaitOptions.SuppressThrowing],
            CompletionWork(antecedent, outcome, completesWhileSuspended))!;

        Assert.Equal(outcome, Result<TaskStatus>(task));
    }

    [Theory]
    [MemberData(nameof(AntecedentOutcomes))]
    public void WithoutSuppressThrowingTheAwaitRethrows(bool optimize, TaskStatus outcome, bool completesWhileSuspended)
    {
        using var host = new SimulationHost(Start);
        var antecedent = new TaskCompletionSource();

        var task = (Task)host.InvokeWithWork(
            Method("Await", optimize),
            [antecedent.Task, ConfigureAwaitOptions.None],
            CompletionWork(antecedent, outcome, completesWhileSuspended))!;

        Assert.Equal(outcome, task.Status);
        if (outcome == TaskStatus.Faulted)
        {
            Assert.IsType<InvalidTimeZoneException>(task.Exception!.InnerException);
        }
    }

    [Theory]
    [MemberData(nameof(CompletedTaskOptions))]
    public void AwaitOnACompletedTaskYieldsThroughTheCoordinatorOnlyWhenForced(bool optimize, ConfigureAwaitOptions options)
    {
        using var host = new SimulationHost(Start);
        var log = new List<string>();

        var task = (Task)host.Invoke(Method("Logged", optimize), Task.CompletedTask, options, log)!;

        Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        Assert.Equal(ExpectedLog(options), log);
    }

    [Theory]
    [MemberData(nameof(CompletedTaskOptions))]
    public void AwaitOnACompletedGenericTaskYieldsThroughTheCoordinatorOnlyWhenForced(bool optimize, ConfigureAwaitOptions options)
    {
        using var host = new SimulationHost(Start);
        var log = new List<string>();

        var task = (Task<int>)host.Invoke(Method("LoggedGeneric", optimize), Task.FromResult(41), options, log)!;

        Assert.Equal(41, Result<int>(task));
        Assert.Equal(ExpectedLog(options), log);
    }

    [Theory]
    [MemberData(nameof(InvalidTaskOptions))]
    public void InvalidOptionsThrowAsTheBclDoes(bool optimize, ConfigureAwaitOptions options)
    {
        ArgumentOutOfRangeException expected = Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = Task.CompletedTask.ConfigureAwait(options); });
        using var host = new SimulationHost(Start);

        var task = (Task)host.Invoke(Method("Await", optimize), Task.CompletedTask, options)!;

        AssertFaultedWith(expected, task);
    }

    [Theory]
    [MemberData(nameof(InvalidGenericTaskOptions))]
    public void GenericTaskRejectsSuppressThrowingAndInvalidOptionsAsTheBclDoes(bool optimize, ConfigureAwaitOptions options)
    {
        ArgumentOutOfRangeException expected = Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = Task.FromResult(1).ConfigureAwait(options); });
        using var host = new SimulationHost(Start);

        var task = (Task)host.Invoke(Method("AwaitGeneric", optimize), Task.FromResult(1), options)!;

        AssertFaultedWith(expected, task);
    }

    [Theory]
    [MemberData(nameof(BoolOverloads))]
    public void BoolOverloadResumesThroughTheCoordinator(bool optimize, bool continueOnCapturedContext)
    {
        using var host = new SimulationHost(Start);
        var antecedent = new TaskCompletionSource<int>();

        var task = (Task<int>)host.InvokeWithWork(
            Method("AwaitBool", optimize),
            [antecedent.Task, continueOnCapturedContext],
            () => antecedent.SetResult(41))!;

        Assert.Equal(41, Result<int>(task));
    }

    [Theory]
    [MemberData(nameof(BoolOverloads))]
    public void BoolOverloadRethrowsTheFault(bool optimize, bool continueOnCapturedContext)
    {
        using var host = new SimulationHost(Start);
        var antecedent = new TaskCompletionSource<int>();

        var task = (Task)host.InvokeWithWork(
            Method("AwaitBool", optimize),
            [antecedent.Task, continueOnCapturedContext],
            () => antecedent.SetException(new InvalidTimeZoneException("boom")))!;

        Assert.Equal(TaskStatus.Faulted, task.Status);
        Assert.IsType<InvalidTimeZoneException>(task.Exception!.InnerException);
    }

    private MethodInfo Method(string name, bool optimize) =>
        (optimize ? _release : _debug).Value.Method(name);

    /// <summary>
    /// Completes <paramref name="antecedent"/> with <paramref name="outcome"/>, either now (so the await
    /// observes a completed task) or as simulation work that runs after the probe has suspended.
    /// </summary>
    private static Action[] CompletionWork(TaskCompletionSource antecedent, TaskStatus outcome, bool completesWhileSuspended)
    {
        Action complete = () =>
        {
            if (outcome == TaskStatus.Faulted)
            {
                antecedent.SetException(new InvalidTimeZoneException("boom"));
            }
            else
            {
                antecedent.SetCanceled();
            }
        };

        if (completesWhileSuspended)
        {
            return [complete];
        }

        complete();
        return [];
    }

    private static string[] ExpectedLog(ConfigureAwaitOptions options) =>
        (options & ConfigureAwaitOptions.ForceYielding) != 0 ? Yielded : RanSynchronously;

    private static void AssertFaultedWith(ArgumentOutOfRangeException expected, Task task)
    {
        Assert.Equal(TaskStatus.Faulted, task.Status);
        var actual = Assert.IsType<ArgumentOutOfRangeException>(task.Exception!.InnerException);
        Assert.Equal(expected.ParamName, actual.ParamName);
        Assert.Equal(expected.Message, actual.Message);
    }

    private static T Result<T>(object task)
    {
        var typed = (Task)task;
        Assert.Equal(TaskStatus.RanToCompletion, typed.Status);
        PropertyInfo result = typed.GetType().GetProperty("Result")!;
        return (T)result.GetValue(typed)!;
    }

    public void Dispose() => _fixture.Dispose();
}

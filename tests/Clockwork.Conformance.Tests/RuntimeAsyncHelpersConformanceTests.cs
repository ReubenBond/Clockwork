using Clockwork.Runtime;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// The runtime-async <c>AsyncHelpers.Await</c> intrinsics that take a configured awaitable resume their caller through
/// the runtime's own continuation machinery, which bypasses the simulation coordinator. Rewritten code reaches them only
/// through the rejecting shims, which throw a <see cref="SimulationApiException"/> naming the overload instead of
/// suspending outside the simulation's control.
/// </summary>
public sealed class RuntimeAsyncHelpersConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        #pragma warning disable SYSLIB5007
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        namespace Conf { public static class RuntimeAsyncProbe {
            public static void AwaitTask() => AsyncHelpers.Await(Task.CompletedTask.ConfigureAwait(false));
            public static int AwaitTaskOfT() => AsyncHelpers.Await(Task.FromResult(1).ConfigureAwait(false));
            public static void AwaitValueTask() => AsyncHelpers.Await(default(ValueTask).ConfigureAwait(false));
            public static int AwaitValueTaskOfT() => AsyncHelpers.Await(new ValueTask<int>(1).ConfigureAwait(false));
        } }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _probe;

    public RuntimeAsyncHelpersConformanceTests()
    {
        _probe = new(() => _fixture.StageControlledTasks("Conf.RuntimeAsync", "Conf.RuntimeAsyncProbe", Source, optimize: true));
    }

    public static TheoryData<string, string> Overloads => new()
    {
        { "AwaitTask", "System.Runtime.CompilerServices.AsyncHelpers.Await(System.Runtime.CompilerServices.ConfiguredTaskAwaitable)" },
        { "AwaitTaskOfT", "System.Runtime.CompilerServices.AsyncHelpers.Await<T>(System.Runtime.CompilerServices.ConfiguredTaskAwaitable<T>)" },
        { "AwaitValueTask", "System.Runtime.CompilerServices.AsyncHelpers.Await(System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable)" },
        { "AwaitValueTaskOfT", "System.Runtime.CompilerServices.AsyncHelpers.Await<T>(System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<T>)" },
    };

    [Theory]
    [MemberData(nameof(Overloads))]
    public void ConfiguredAwaitIntrinsicIsRejectedByName(string probe, string apiName)
    {
        using var host = new SimulationHost(Start);

        SimulationApiException exception = Assert.Throws<SimulationApiException>(() => host.Invoke(_probe.Value.Method(probe)));

        Assert.Equal(SimulationApiCategory.Task, exception.Category);
        Assert.Equal(apiName, exception.ApiName);
        Assert.Contains("compile the method without runtime async", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _fixture.Dispose();
}

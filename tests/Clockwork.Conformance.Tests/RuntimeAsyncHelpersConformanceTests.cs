using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Runtime;
using Mono.Cecil;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// A method compiled with runtime async suspends and resumes through the runtime's own continuation machinery, which
/// bypasses the simulation coordinator, so rewriting one fails with <c>CWR0013</c>. The runtime-async
/// <c>AsyncHelpers.Await</c> intrinsics that take a configured awaitable resume their caller through that machinery, and
/// <c>AsyncHelpers.HandleAsyncEntryPoint</c> blocks the calling thread on a runtime-async entry point's task. Rewritten
/// code reaches them only through the rejecting shims, which throw a <see cref="SimulationApiException"/> naming the
/// overload instead.
/// </summary>
public sealed class RuntimeAsyncHelpersConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // MethodImplAttributes.Async, which Mono.Cecil's MethodImplAttributes does not name.
    private const MethodImplAttributes RuntimeAsync = (MethodImplAttributes)0x2000;

    private const string RuntimeAsyncSource = """
        using System.Threading.Tasks;
        namespace Conf { public static class RuntimeAsyncMethods {
            public static async Task<int> IncrementAsync(Task<int> value) => await value + 1;
        } }
        """;

    private const string Source = """
        #pragma warning disable SYSLIB5007
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        namespace Conf { public static class RuntimeAsyncProbe {
            public static void AwaitTask() => AsyncHelpers.Await(Task.CompletedTask.ConfigureAwait(false));
            public static int AwaitTaskOfT() => AsyncHelpers.Await(Task.FromResult(1).ConfigureAwait(false));
            public static void AwaitValueTask() => AsyncHelpers.Await(default(ValueTask).ConfigureAwait(false));
            public static int AwaitValueTaskOfT() => AsyncHelpers.Await(new ValueTask<int>(1).ConfigureAwait(false));
            public static void HandleEntryPoint() => AsyncHelpers.HandleAsyncEntryPoint(Task.CompletedTask);
            public static int HandleEntryPointOfInt32() => AsyncHelpers.HandleAsyncEntryPoint(Task.FromResult(1));
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
        { "HandleEntryPoint", "System.Runtime.CompilerServices.AsyncHelpers.HandleAsyncEntryPoint(System.Threading.Tasks.Task)" },
        { "HandleEntryPointOfInt32", "System.Runtime.CompilerServices.AsyncHelpers.HandleAsyncEntryPoint(System.Threading.Tasks.Task<int>)" },
    };

    [Theory]
    [MemberData(nameof(Overloads))]
    public void RuntimeAsyncHelperIsRejectedByName(string probe, string apiName)
    {
        using var host = new SimulationHost(Start);

        SimulationApiException exception = Assert.Throws<SimulationApiException>(() => host.Invoke(_probe.Value.Method(probe)));

        Assert.Equal(SimulationApiCategory.Task, exception.Category);
        Assert.Equal(apiName, exception.ApiName);
        Assert.Contains("compile the method without runtime async", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeAsyncMethodFailsTheRewrite(bool optimize)
    {
        (string sourceDll, RewriteResult result) = _fixture.RewriteRuntimeAsync(
            optimize ? "Conf.RuntimeAsyncRel" : "Conf.RuntimeAsyncDbg", RuntimeAsyncSource, optimize);

        using (ModuleDefinition module = ModuleDefinition.ReadModule(sourceDll))
        {
            MethodDefinition method = module.GetType("Conf.RuntimeAsyncMethods").Methods.Single(method => method.Name == "IncrementAsync");
            Assert.True((method.ImplAttributes & RuntimeAsync) != 0, "The compiler did not emit a runtime-async method.");
        }

        Assert.False(result.Succeeded);
        RewriteDiagnostic diagnostic = Assert.Single(
            result.Manifest.Diagnostics, diagnostic => diagnostic.Id == RewriteDiagnosticIds.RuntimeAsyncMethod);
        Assert.Equal("Conf.RuntimeAsyncMethods.IncrementAsync", diagnostic.Method);
        Assert.Contains("remove the 'runtime-async=on' compiler feature", diagnostic.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _fixture.Dispose();
}

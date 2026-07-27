using System.Collections.Immutable;
using Clockwork.Instrumentation.Manifest;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Clockwork.Runtime.Policy;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>
/// End-to-end golden tests for the shipped controlled-task rule set
/// (<see cref="BuiltInRuleSets.ControlledTasksId"/>): they rewrite real <see cref="System.Threading.Tasks.Task"/>
/// call sites in a compiled fixture against the real <c>Clockwork.Runtime</c> shim assembly and assert
/// on the rewritten IL and manifest. This proves the static/instance signatures in
/// <c>BuiltInRuleSets.BuildControlledTasks</c> line up with the actual <c>ControlledTask</c> members, so
/// the inventory is not merely declarative.
/// </summary>
public sealed class ControlledTaskRuleGoldenTests
{
    private const string Fixture = """
        using System.Collections.Generic;
        using System.Threading.Tasks;

        namespace Fx
        {
            public static class TaskUser
            {
                public static Task All(Task a, Task b) => Task.WhenAll(a, b);
                public static Task AllList(IEnumerable<Task> tasks) => Task.WhenAll(tasks);
                public static Task<Task> Any(Task a, Task b) => Task.WhenAny(a, b);
                public static void Block(Task t) => t.Wait();
                public static Task Delayed() => Task.Delay(5);
                public static Task Offloaded() => Task.Run(() => { });
            }
        }
        """;

    private static string RuntimeAssemblyPath =>
        typeof(Clockwork.Runtime.Tasks.ControlledTask).Assembly.Location;

    private static RewriteResult RewriteFixture(RewriteTestContext context, string assemblyName)
    {
        string fixturePath = context.CompileFixture(assemblyName, Fixture);
        string outputPath = Path.Combine(
            context.Directory, Path.GetFileNameWithoutExtension(fixturePath) + ".rewritten.dll");

        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = [RuntimeAssemblyPath],
            ReferenceSearchDirectories = [context.Directory, Path.GetDirectoryName(RuntimeAssemblyPath)!],
        };

        RewriteRuleSet ruleSet = BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies);
        var result = RewriteEngine.Rewrite(new RewriteRequest(fixturePath, outputPath, ruleSet, options));
        result.EnsureSuccess();
        return result;
    }

    [Fact]
    public void StaticCombinatorsAreRedirectedToControlledTask()
    {
        using var context = RewriteTestContext.Create();
        RewriteFixture(context, "Fx.TaskCombinators");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.TaskCombinators.rewritten.dll"));

        MethodDefinition all = CecilInspect.GetMethod(module, "Fx.TaskUser", "All");
        Assert.True(CecilInspect.CallsAnyContaining(all, "ControlledTask::WhenAll"));
        Assert.False(CecilInspect.CallsAnyContaining(all, "Threading.Tasks.Task::WhenAll"));

        MethodDefinition allList = CecilInspect.GetMethod(module, "Fx.TaskUser", "AllList");
        Assert.True(CecilInspect.CallsAnyContaining(allList, "ControlledTask::WhenAll"));

        MethodDefinition any = CecilInspect.GetMethod(module, "Fx.TaskUser", "Any");
        Assert.True(CecilInspect.CallsAnyContaining(any, "ControlledTask::WhenAny"));
        Assert.False(CecilInspect.CallsAnyContaining(any, "Threading.Tasks.Task::WhenAny"));
    }

    [Fact]
    public void InstanceWaitIsRedirectedToStaticShimTakingReceiver()
    {
        using var context = RewriteTestContext.Create();
        RewriteFixture(context, "Fx.TaskWait");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.TaskWait.rewritten.dll"));

        MethodDefinition block = CecilInspect.GetMethod(module, "Fx.TaskUser", "Block");
        Assert.True(CecilInspect.CallsAnyContaining(block, "ControlledTask::Wait"));
        Assert.False(CecilInspect.CallsAnyContaining(block, "Threading.Tasks.Task::Wait"));
    }

    [Fact]
    public void DeferredSurfacesAreRedirectedToRejectingShim()
    {
        using var context = RewriteTestContext.Create();
        var result = RewriteFixture(context, "Fx.TaskDeferred");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.TaskDeferred.rewritten.dll"));

        MethodDefinition delayed = CecilInspect.GetMethod(module, "Fx.TaskUser", "Delayed");
        Assert.True(CecilInspect.CallsAnyContaining(delayed, "ControlledTask::Delay"));
        Assert.False(CecilInspect.CallsAnyContaining(delayed, "Threading.Tasks.Task::Delay"));

        ImmutableArray<ManifestTransformation> transformations = result.Manifest.Transformations;
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.tasks.delay.milliseconds" && t.Policy == SimulationApiPolicy.Rejected);
    }

    [Fact]
    public void TaskRunIsRedirectedToControlledShim()
    {
        using var context = RewriteTestContext.Create();
        var result = RewriteFixture(context, "Fx.TaskRun");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.TaskRun.rewritten.dll"));

        MethodDefinition offloaded = CecilInspect.GetMethod(module, "Fx.TaskUser", "Offloaded");
        Assert.True(CecilInspect.CallsAnyContaining(offloaded, "ControlledTask::Run"));
        Assert.False(CecilInspect.CallsAnyContaining(offloaded, "Threading.Tasks.Task::Run"));

        ImmutableArray<ManifestTransformation> transformations = result.Manifest.Transformations;
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.tasks.run.action" && t.Policy == SimulationApiPolicy.Controlled);
    }
}

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

                public static Task<int[]> AllT(Task<int> a, Task<int> b) => Task.WhenAll(a, b);
                public static Task<int[]> AllListT(IEnumerable<Task<int>> tasks) => Task.WhenAll(tasks);
                public static Task<Task<int>> AnyT(Task<int> a, Task<int> b) => Task.WhenAny(a, b);
                public static int BlockResult(Task<int> t) => t.Result;

                public static async ValueTask VtAsync(ValueTask inner) => await inner.ConfigureAwait(false);
                public static async ValueTask<int> VtAsyncT(ValueTask<int> inner) => await inner.ConfigureAwait(false);

                public static Task Factory() => Task.Factory.StartNew(() => { });
                public static Task<int> FactoryFunc() => Task.Factory.StartNew(() => 5);
                public static Task<int> FactoryGeneric() => new TaskFactory<int>().StartNew(() => 7);

                public static Task Unwrapped(Task<Task> t) => t.Unwrap();
                public static Task<int> UnwrappedT(Task<Task<int>> t) => t.Unwrap();
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

        MethodDefinition offloaded = CecilInspect.GetMethod(module, "Fx.TaskUser", "Offloaded");
        Assert.True(CecilInspect.CallsAnyContaining(offloaded, "ControlledTask::Run"));

        ImmutableArray<ManifestTransformation> transformations = result.Manifest.Transformations;
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.tasks.delay.milliseconds" && t.Policy == SimulationApiPolicy.Rejected);
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.tasks.run.action" && t.Policy == SimulationApiPolicy.Rejected);
    }

    [Fact]
    public void GenericCombinatorsAreRedirectedToControlledTask()
    {
        using var context = RewriteTestContext.Create();
        RewriteFixture(context, "Fx.TaskGenericCombinators");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.TaskGenericCombinators.rewritten.dll"));

        MethodDefinition allT = CecilInspect.GetMethod(module, "Fx.TaskUser", "AllT");
        Assert.True(CecilInspect.CallsAnyContaining(allT, "ControlledTask::WhenAll"));
        Assert.False(CecilInspect.CallsAnyContaining(allT, "Threading.Tasks.Task::WhenAll"));

        MethodDefinition allListT = CecilInspect.GetMethod(module, "Fx.TaskUser", "AllListT");
        Assert.True(CecilInspect.CallsAnyContaining(allListT, "ControlledTask::WhenAll"));

        MethodDefinition anyT = CecilInspect.GetMethod(module, "Fx.TaskUser", "AnyT");
        Assert.True(CecilInspect.CallsAnyContaining(anyT, "ControlledTask::WhenAny"));
        Assert.False(CecilInspect.CallsAnyContaining(anyT, "Threading.Tasks.Task::WhenAny"));
    }

    [Fact]
    public void BlockingGenericResultIsRedirectedToControlledTask()
    {
        using var context = RewriteTestContext.Create();
        RewriteFixture(context, "Fx.TaskResult");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.TaskResult.rewritten.dll"));

        MethodDefinition block = CecilInspect.GetMethod(module, "Fx.TaskUser", "BlockResult");
        Assert.True(CecilInspect.CallsAnyContaining(block, "ControlledTask::Result"));
        Assert.False(CecilInspect.CallsAnyContaining(block, "Task`1::get_Result"));
    }

    [Fact]
    public void ValueTaskAsyncMachineryIsSubstituted()
    {
        using var context = RewriteTestContext.Create();
        RewriteFixture(context, "Fx.ValueTaskMachinery");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.ValueTaskMachinery.rewritten.dll"));

        // The builder and configured awaiter live in the compiler-generated state machine, not the
        // user method - assert the whole module references the controlled types and no longer the BCL
        // AsyncValueTaskMethodBuilder create/start entry points.
        Assert.True(CecilInspect.AnyMethodCallsContaining(module, "ControlledAsyncValueTaskMethodBuilder"));
        Assert.True(CecilInspect.AnyMethodCallsContaining(module, "ControlledConfiguredValueTaskAwaitable"));
        Assert.False(CecilInspect.AnyMethodCallsContaining(
            module, "CompilerServices.AsyncValueTaskMethodBuilder"));
    }

    [Fact]
    public void TaskFactoryStartNewIsRedirectedToRejectingShim()
    {
        using var context = RewriteTestContext.Create();
        var result = RewriteFixture(context, "Fx.TaskFactory");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.TaskFactory.rewritten.dll"));

        MethodDefinition factory = CecilInspect.GetMethod(module, "Fx.TaskUser", "Factory");
        Assert.True(CecilInspect.CallsAnyContaining(factory, "ControlledTaskFactory::StartNew"));
        Assert.False(CecilInspect.CallsAnyContaining(factory, "Threading.Tasks.TaskFactory::StartNew"));

        MethodDefinition factoryFunc = CecilInspect.GetMethod(module, "Fx.TaskUser", "FactoryFunc");
        Assert.True(CecilInspect.CallsAnyContaining(factoryFunc, "ControlledTaskFactory::StartNew"));

        MethodDefinition factoryGeneric = CecilInspect.GetMethod(module, "Fx.TaskUser", "FactoryGeneric");
        Assert.True(CecilInspect.CallsAnyContaining(factoryGeneric, "ControlledTaskFactory::StartNew"));

        ImmutableArray<ManifestTransformation> transformations = result.Manifest.Transformations;
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.tasks.factory.startnew.action" && t.Policy == SimulationApiPolicy.Rejected);
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.tasks.factory.startnew.func" && t.Policy == SimulationApiPolicy.Rejected);
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.tasks.factory.generic.startnew.func" && t.Policy == SimulationApiPolicy.Rejected);
    }

    [Fact]
    public void UnwrapExtensionIsRedirectedToControlledTask()
    {
        using var context = RewriteTestContext.Create();
        RewriteFixture(context, "Fx.TaskUnwrap");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.TaskUnwrap.rewritten.dll"));

        MethodDefinition unwrapped = CecilInspect.GetMethod(module, "Fx.TaskUser", "Unwrapped");
        Assert.True(CecilInspect.CallsAnyContaining(unwrapped, "ControlledTask::Unwrap"));
        Assert.False(CecilInspect.CallsAnyContaining(unwrapped, "TaskExtensions::Unwrap"));

        MethodDefinition unwrappedT = CecilInspect.GetMethod(module, "Fx.TaskUser", "UnwrappedT");
        Assert.True(CecilInspect.CallsAnyContaining(unwrappedT, "ControlledTask::Unwrap"));
        Assert.False(CecilInspect.CallsAnyContaining(unwrappedT, "TaskExtensions::Unwrap"));
    }
}

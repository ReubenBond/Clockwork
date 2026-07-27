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
/// End-to-end golden tests for the shipped uncontrolled-invocation rule family (Phase 6B slice 7): they
/// rewrite real <see cref="System.Diagnostics.Process"/> and <see cref="System.Environment"/> call sites in
/// a compiled fixture against the real <c>Clockwork.Runtime</c> shim assembly and assert that each site is
/// rejected - a throwing <see cref="Clockwork.Runtime.UncontrolledInvocationGuard.Reject(string)"/> is
/// injected before the original call, which the manifest records as a <see cref="SimulationApiPolicy.Rejected"/>
/// transformation naming the exact API. This proves a rewritten assembly cannot launch, kill, wait on, or
/// terminate a real OS process.
/// </summary>
public sealed class UncontrolledInvocationRuleGoldenTests
{
    private const string Fixture = """
        using System;
        using System.Diagnostics;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Fx
        {
            public static class ProcessUser
            {
                public static Process StartFile() => Process.Start("app.exe");
                public static Process StartInfo(ProcessStartInfo psi) => Process.Start(psi);
                public static Process StartArgs() => Process.Start("app.exe", "--flag");
                public static bool StartInstance(Process p) => p.Start();
                public static void Kill(Process p) => p.Kill();
                public static void KillTree(Process p) => p.Kill(true);
                public static bool WaitMs(Process p) => p.WaitForExit(100);
                public static void WaitAll(Process p) => p.WaitForExit();
                public static Task WaitAsync(Process p, CancellationToken ct) => p.WaitForExitAsync(ct);
                public static void Exit() => Environment.Exit(1);
                public static void FailFast() => Environment.FailFast("boom");
                public static void FailFastEx(Exception ex) => Environment.FailFast("boom", ex);
            }
        }
        """;

    private static string RuntimeAssemblyPath =>
        typeof(Clockwork.Runtime.UncontrolledInvocationGuard).Assembly.Location;

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
    public void ProcessAndEnvironmentCallSitesAreRejected()
    {
        using var context = RewriteTestContext.Create();
        var result = RewriteFixture(context, "Fx.Uncontrolled");

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.Uncontrolled.rewritten.dll"));

        foreach (var method in new[]
        {
            "StartFile", "StartInfo", "StartArgs", "StartInstance", "Kill", "KillTree",
            "WaitMs", "WaitAll", "WaitAsync", "Exit", "FailFast", "FailFastEx",
        })
        {
            MethodDefinition definition = CecilInspect.GetMethod(module, "Fx.ProcessUser", method);
            Assert.True(
                CecilInspect.CallsAnyContaining(definition, "UncontrolledInvocationGuard::Reject"),
                $"{method} should be rejected");
        }

        ImmutableArray<ManifestTransformation> transformations = result.Manifest.Transformations;
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.process.start.filename" && t.Policy == SimulationApiPolicy.Rejected);
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.environment.exit" && t.Policy == SimulationApiPolicy.Rejected);
        Assert.Contains(transformations, t =>
            t.RuleId == "clockwork.process.waitforexitasync" && t.Policy == SimulationApiPolicy.Rejected);
    }
}

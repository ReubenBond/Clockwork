using System.Reflection;
using System.Runtime.Loader;
using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Orchestration;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Tests.Infrastructure;

namespace Clockwork.Instrumentation.Tests.Orchestration;

/// <summary>
/// Closure-level checks that a member reference whose signature a type substitution rewrites binds across the
/// closure's assemblies. <c>Fx.Lib</c> declares <c>Sink.Take(LegacyMarker)</c>, which <c>Fx.App</c> calls; the rule
/// substitutes <c>LegacyMarker</c> with <c>ModernMarker</c>. The reference binds only when both its declaring and its
/// referencing code are rewritten, so the runner rewrites a sibling consistently and fails the run with
/// <c>CWR0001</c> when only the referencing side is, instead of staging a closure that throws
/// <see cref="MissingMethodException"/>.
/// </summary>
public sealed class ClosureBindingTests : IDisposable
{
    private const string LibSource = """
        using ClockworkFixtures.Api;

        namespace Fx.Lib
        {
            public static class Sink
            {
                public static string Take(LegacyMarker marker) => "took " + marker;
            }
        }
        """;

    private const string AppSource = """
        using ClockworkFixtures.Api;
        using Fx.Lib;

        namespace Fx.App
        {
            public static class Caller
            {
                public static string Run() => Sink.Take(new LegacyMarker());
            }
        }
        """;

    private readonly string _root = TestArtifacts.CreateUnique("cwr-closure-binding");
    private readonly string _source;
    private readonly string _staging;

    public ClosureBindingTests()
    {
        _source = Path.Combine(_root, "source");
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_source);
    }

    [Fact]
    public void RewrittenSiblingBindsTheSubstitutedSignature()
    {
        BuildClosure(AppSource);

        Run().EnsureSuccess();

        var loadContext = new DirectoryLoadContext(_staging);
        try
        {
            Assembly app = loadContext.LoadFromAssemblyPath(Path.Combine(_staging, "Fx.App.dll"));
            MethodInfo run = app.GetType("Fx.App.Caller", throwOnError: true)!.GetMethod("Run")!;

            // Both assemblies take the substitute, so the call binds and receives it.
            Assert.Equal("took modern", run.Invoke(null, null));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void ExcludedSiblingFailsTheConsumersRewrite()
    {
        BuildClosure(AppSource);

        InstrumentationResult result = Run(new InstrumentationConfiguration { ExcludePatterns = ["Fx.Lib.dll"] });

        // The consumer's reference would take the substitute while the copied sibling keeps the original signature.
        Assert.False(result.Succeeded);
        RewriteDiagnostic error = Assert.Single(result.Errors);
        Assert.Equal(RewriteDiagnosticIds.UnresolvedReplacement, error.Id);
        Assert.Equal("Fx.App.Caller.Run", error.Method);
        Assert.Contains("'substitute-marker'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Fx.Lib.Sink::Take(ClockworkFixtures.Api.LegacyMarker)", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Fx.Lib', which is not rewritten in this closure", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreRewrittenAssemblyIsRefusedInAClosureThatRewritesDifferentAssemblies()
    {
        BuildClosure(AppSource);
        Run().EnsureSuccess();

        // The staged consumer was rewritten for a closure that also rewrites Fx.Lib, so it calls the substituted
        // signature. Reusing it as an input to a closure that copies Fx.Lib must not be a verified no-op.
        File.Copy(Path.Combine(_staging, "Fx.App.dll"), Path.Combine(_source, "Fx.App.dll"), overwrite: true);
        File.Copy(Path.Combine(_staging, "Fx.App.pdb"), Path.Combine(_source, "Fx.App.pdb"), overwrite: true);

        InstrumentationResult result = Run(new InstrumentationConfiguration { ExcludePatterns = ["Fx.Lib.dll"] });

        Assert.False(result.Succeeded);
        RewriteDiagnostic error = Assert.Single(result.Errors);
        Assert.Equal(RewriteDiagnosticIds.IncompatibleRewriteVersion, error.Id);
        Assert.Contains("'Fx.App' was already rewritten", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a loaded staged assembly can keep its file mapped on Windows.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private static RewriteRuleSet MarkerRuleSet() =>
        new(
            "clockwork.fixtures.closure-binding",
            "1.0",
            [
                RewriteRule.SubstituteType(
                    "substitute-marker",
                    "ClockworkFixtures.Api.LegacyMarker",
                    RewriteReplacement.Type(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.ModernMarker")),
            ]);

    private void BuildClosure(string appSource)
    {
        string api = FixtureCompiler.Compile(
            FixtureSources.ApiAssemblyName, FixtureSources.Api, _source, FixtureSymbols.PortableFile, optimize: false);
        FixtureCompiler.Compile(
            FixtureSources.ShimAssemblyName, FixtureSources.Shims, _source, FixtureSymbols.PortableFile, optimize: false,
            additionalReferencePaths: [api]);
        string lib = FixtureCompiler.Compile(
            "Fx.Lib", LibSource, _source, FixtureSymbols.PortableFile, optimize: false, additionalReferencePaths: [api]);
        FixtureCompiler.Compile(
            "Fx.App", appSource, _source, FixtureSymbols.PortableFile, optimize: false, additionalReferencePaths: [api, lib]);
    }

    private InstrumentationResult Run(InstrumentationConfiguration? configuration = null) =>
        InstrumentationRunner.Run(new InstrumentationRequest
        {
            SourceDirectory = _source,
            StagingDirectory = _staging,
            Configuration = configuration ?? new InstrumentationConfiguration(),
            RuleSet = MarkerRuleSet(),
        });

    private sealed class DirectoryLoadContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}

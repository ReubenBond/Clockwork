using System.Reflection;
using System.Runtime.Loader;
using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Orchestration;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Tests.Infrastructure;

namespace Clockwork.Instrumentation.Tests.Orchestration;

/// <summary>
/// Closure-level checks that a member reference, interface implementation, or override whose signature a type
/// substitution rewrites binds across the closure's assemblies. <c>Fx.Lib</c> declares <c>Sink.Take(LegacyMarker)</c>,
/// <c>IHandler</c>, and <c>HandlerBase</c>, which <c>Fx.App</c> calls, implements, and overrides; the rule substitutes
/// <c>LegacyMarker</c> with <c>ModernMarker</c>. Each binds only when both sides are rewritten, so the runner rewrites
/// a sibling consistently and fails the run with <c>CWR0001</c> when only one side is - an excluded sibling, an
/// excluded consumer, or a type marked <c>[DoNotRewrite]</c> - instead of staging a closure that throws
/// <see cref="MissingMethodException"/> or <see cref="TypeLoadException"/>.
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

            public interface IHandler
            {
                string Handle(LegacyMarker marker);
            }

            public abstract class HandlerBase
            {
                public abstract string Handle(LegacyMarker marker);
            }
        }
        """;

    private const string ImplementerAppSource = """
        using ClockworkFixtures.Api;
        using Fx.Lib;

        namespace Fx.App
        {
            public sealed class Handler : IHandler
            {
                public string Handle(LegacyMarker marker) => "handled " + marker;
            }

            public sealed class DerivedHandler : HandlerBase
            {
                public override string Handle(LegacyMarker marker) => "overrode " + marker;
            }

            public sealed class ExplicitHandler : IHandler
            {
                string IHandler.Handle(LegacyMarker marker) => "explicitly handled " + marker;
            }

            public static class Caller
            {
                public static string Run()
                {
                    var marker = new LegacyMarker();
                    IHandler handler = new Handler();
                    IHandler explicitHandler = new ExplicitHandler();
                    HandlerBase derived = new DerivedHandler();
                    return handler.Handle(marker) + ", " + explicitHandler.Handle(marker) + ", " + derived.Handle(marker);
                }
            }
        }
        """;

    // Without a call into Fx.Lib, nothing but the types' shapes crosses the boundary.
    private const string ShapeOnlyAppTemplate = """
        using ClockworkFixtures.Api;
        using Fx.Lib;

        namespace Clockwork.Instrumentation.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class DoNotRewriteAttribute(string reason) : System.Attribute
            {
                public string Reason { get; } = reason;
            }
        }

        namespace Fx.App
        {
            [ATTRIBUTE]
            public sealed class Handler : IHandler
            {
                public string Handle(LegacyMarker marker) => "handled " + marker;
            }

            [ATTRIBUTE]
            public sealed class DerivedHandler : HandlerBase
            {
                public override string Handle(LegacyMarker marker) => "overrode " + marker;
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

    private const string DoNotRewriteAppSource = """
        using ClockworkFixtures.Api;
        using Fx.Lib;

        namespace Clockwork.Instrumentation.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class DoNotRewriteAttribute(string reason) : System.Attribute
            {
                public string Reason { get; } = reason;
            }
        }

        namespace Fx.App
        {
            [Clockwork.Instrumentation.Attributes.DoNotRewrite("Runs before the simulation starts.")]
            public static class LegacyCaller
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
    public void ExcludedConsumerOfARewrittenSiblingFailsTheRun()
    {
        BuildClosure(AppSource);

        InstrumentationResult result = Run(new InstrumentationConfiguration { ExcludePatterns = ["Fx.App.dll"] });

        // The sibling's signature takes the substitute while the copied consumer keeps the original reference.
        Assert.False(result.Succeeded);
        RewriteDiagnostic error = Assert.Single(result.Errors);
        Assert.Equal(RewriteDiagnosticIds.UnresolvedReplacement, error.Id);
        Assert.Equal("Fx.App.dll", error.Method);
        Assert.Contains("'substitute-marker' rewrites 'Fx.Lib'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Fx.Lib.Sink::Take(ClockworkFixtures.Api.LegacyMarker)", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Fx.App' is not rewritten in this closure", error.Message, StringComparison.Ordinal);
        Assert.Contains(result.Assemblies, assembly => assembly.RelativePath == "Fx.Lib.dll" && assembly.WasRewritten);
    }

    [Fact]
    public void DoNotRewriteCallerOfARewrittenSiblingFailsTheRun()
    {
        BuildClosure(DoNotRewriteAppSource);

        InstrumentationResult result = Run();

        // Both assemblies are rewritten, but the excluded caller keeps the original reference.
        Assert.False(result.Succeeded);
        RewriteDiagnostic error = Assert.Single(result.Errors);
        Assert.Equal(RewriteDiagnosticIds.UnresolvedReplacement, error.Id);
        Assert.Equal("Fx.App.dll", error.Method);
        Assert.Contains("Fx.Lib.Sink::Take(ClockworkFixtures.Api.LegacyMarker)", error.Message, StringComparison.Ordinal);
        Assert.Contains(
            "the referencing type 'Fx.App.LegacyCaller' is excluded from rewriting and keeps the original signature",
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains(result.Assemblies, assembly => assembly.RelativePath == "Fx.App.dll" && assembly.WasRewritten);
    }

    [Fact]
    public void RewrittenSiblingsImplementAndOverrideEachOthersMembers()
    {
        BuildClosure(ImplementerAppSource);

        Run().EnsureSuccess();

        var loadContext = new DirectoryLoadContext(_staging);
        try
        {
            Assembly app = loadContext.LoadFromAssemblyPath(Path.Combine(_staging, "Fx.App.dll"));
            MethodInfo run = app.GetType("Fx.App.Caller", throwOnError: true)!.GetMethod("Run")!;

            // The explicit implementation's declaration is retargeted onto the sibling's rewritten signature.
            Assert.Equal("handled modern, explicitly handled modern, overrode modern", run.Invoke(null, null));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void CopiedImplementerOfARewrittenSiblingFailsTheRun()
    {
        BuildClosure(ShapeOnlyAppSource(doNotRewrite: false));

        InstrumentationResult result = Run(new InstrumentationConfiguration { ExcludePatterns = ["Fx.App.dll"] });

        // The sibling's members take the substitute while the copied implementations keep the original signatures.
        AssertShapeErrors(
            result,
            "Fx.App.dll",
            ("Fx.App.Handler::Handle", "implements 'Fx.Lib.IHandler::Handle(ClockworkFixtures.Shims.ModernMarker) : System.String' as " +
                "'Handle(ClockworkFixtures.Api.LegacyMarker) : System.String' after rewriting, because 'Fx.App.Handler' is declared " +
                "in 'Fx.App', which is not rewritten in this closure, so it keeps the original signature while 'Fx.Lib.IHandler' is rewritten"),
            ("Fx.App.DerivedHandler::Handle", "overrides 'Fx.Lib.HandlerBase::Handle(ClockworkFixtures.Shims.ModernMarker) : System.String'"));
        Assert.Contains(result.Assemblies, assembly => assembly.RelativePath == "Fx.Lib.dll" && assembly.WasRewritten);
    }

    [Fact]
    public void RewrittenImplementerOfACopiedSiblingFailsTheRun()
    {
        BuildClosure(ShapeOnlyAppSource(doNotRewrite: false));

        InstrumentationResult result = Run(new InstrumentationConfiguration { ExcludePatterns = ["Fx.Lib.dll"] });

        // The implementations take the substitute while the copied sibling's members keep the original signatures.
        AssertShapeErrors(
            result,
            method: null,
            ("Fx.App.Handler::Handle", "implements 'Fx.Lib.IHandler::Handle(ClockworkFixtures.Api.LegacyMarker) : System.String' as " +
                "'Handle(ClockworkFixtures.Shims.ModernMarker) : System.String' after rewriting, because 'Fx.Lib.IHandler' is " +
                "declared in 'Fx.Lib', which is not rewritten in this closure, so it keeps the original signature while " +
                "'Fx.App.Handler' is rewritten"),
            ("Fx.App.DerivedHandler::Handle", "overrides 'Fx.Lib.HandlerBase::Handle(ClockworkFixtures.Api.LegacyMarker) : System.String'"));
    }

    [Fact]
    public void DoNotRewriteImplementerOfARewrittenSiblingFailsTheRun()
    {
        BuildClosure(ShapeOnlyAppSource(doNotRewrite: true));

        InstrumentationResult result = Run();

        // Both assemblies are rewritten, but the excluded implementations keep the original signatures.
        AssertShapeErrors(
            result,
            "Fx.App.dll",
            ("Fx.App.Handler::Handle", "because 'Fx.App.Handler' is excluded from rewriting in 'Fx.App', so it keeps the original " +
                "signature while 'Fx.Lib.IHandler' is rewritten"),
            ("Fx.App.DerivedHandler::Handle", "because 'Fx.App.DerivedHandler' is excluded from rewriting in 'Fx.App', so it keeps " +
                "the original signature while 'Fx.Lib.HandlerBase' is rewritten"));
        Assert.Contains(result.Assemblies, assembly => assembly.RelativePath == "Fx.App.dll" && assembly.WasRewritten);
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

    private static string ShapeOnlyAppSource(bool doNotRewrite) =>
        ShapeOnlyAppTemplate.Replace(
            "[ATTRIBUTE]",
            doNotRewrite ? "[Clockwork.Instrumentation.Attributes.DoNotRewrite(\"Shared with code outside the simulation.\")]" : string.Empty,
            StringComparison.Ordinal);

    private static void AssertShapeErrors(
        InstrumentationResult result,
        string? method,
        params (string Member, string Reason)[] expected)
    {
        Assert.False(result.Succeeded);
        List<RewriteDiagnostic> errors = [.. result.Errors];
        Assert.Equal(expected.Length, errors.Count);
        Assert.All(errors, error =>
        {
            Assert.Equal(RewriteDiagnosticIds.UnresolvedReplacement, error.Id);
            Assert.Contains("'substitute-marker'", error.Message, StringComparison.Ordinal);
            if (method is not null)
            {
                Assert.Equal(method, error.Method);
            }
        });
        foreach ((string member, string reason) in expected)
        {
            Assert.Contains(errors, error => error.Message.Contains($"member '{member}'", StringComparison.Ordinal)
                && error.Message.Contains(reason, StringComparison.Ordinal));
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

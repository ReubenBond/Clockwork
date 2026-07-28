using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Closure;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Imaging;
using Clockwork.Instrumentation.Orchestration;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Instrumentation.Signing;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Clockwork.Runtime.Racing;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Orchestration;

/// <summary>
/// Verifies the end-to-end instrumentation orchestrator: it stages a runnable closure (rewriting
/// managed assemblies and copying every other asset verbatim), never mutates the source, is
/// incrementally cached and cache-invalidated on input changes, emits a deterministic closure
/// manifest, enforces the ReadyToRun and strong-name policies with clear errors, and re-signs a
/// strong-named closure to a consistent public-key token.
/// </summary>
public sealed class InstrumentationRunnerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwr-orch-tests", Guid.NewGuid().ToString("n"));
    private readonly string _source;
    private readonly string _staging;

    public InstrumentationRunnerTests()
    {
        _source = Path.Combine(_root, "source");
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_source);
    }

    [Fact]
    public void StagesClosureRewritingManagedAndCopyingAssets()
    {
        BuildStandardClosure();
        InstrumentationResult result = Run(new InstrumentationConfiguration(), EmptyRuleSet());

        Assert.True(result.Succeeded, string.Join("\n", result.Errors));
        Assert.False(result.WasIncrementalHit);
        Assert.Equal(2, result.RewrittenCount);

        // Rewritten managed assemblies are present in staging.
        Assert.True(File.Exists(Path.Combine(_staging, "app.dll")));
        Assert.True(File.Exists(Path.Combine(_staging, "thirdparty.dll")));

        // Non-rewritten assets are copied verbatim, keeping the closure runnable.
        Assert.True(File.Exists(Path.Combine(_staging, "System.Fake.dll")));
        Assert.True(File.Exists(Path.Combine(_staging, "native.dll")));
        Assert.True(File.Exists(Path.Combine(_staging, "app.deps.json")));
        Assert.True(File.Exists(Path.Combine(_staging, "app.runtimeconfig.json")));
        Assert.Contains("System.Fake.dll", result.CopiedAssets);
        Assert.Contains("native.dll", result.CopiedAssets);

        // The manifest is emitted to the predictable path outside the staged closure.
        Assert.True(File.Exists(result.ManifestPath));
        Assert.False(result.ManifestPath.StartsWith(_staging + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    [Fact]
    public void DoesNotMutateSourceDirectory()
    {
        BuildStandardClosure();
        byte[] before = File.ReadAllBytes(Path.Combine(_source, "app.dll"));

        Run(new InstrumentationConfiguration(), EmptyRuleSet());

        byte[] after = File.ReadAllBytes(Path.Combine(_source, "app.dll"));
        Assert.Equal(before, after);
    }

    [Fact]
    public void IncrementalRebuildIsVerifiedNoOp()
    {
        BuildStandardClosure();
        Run(new InstrumentationConfiguration(), EmptyRuleSet());

        InstrumentationResult second = Run(new InstrumentationConfiguration(), EmptyRuleSet());

        Assert.True(second.Succeeded);
        Assert.True(second.WasIncrementalHit);
        Assert.Empty(second.Assemblies);
    }

    [Fact]
    public void ChangedInputInvalidatesIncrementalCache()
    {
        BuildStandardClosure();
        Run(new InstrumentationConfiguration(), EmptyRuleSet());

        // Recompile the dependency with different content; the cache key must change.
        Compile("thirdparty", "namespace Third { public static class T { public static int V() => 42; } }");
        InstrumentationResult second = Run(new InstrumentationConfiguration(), EmptyRuleSet());

        Assert.True(second.Succeeded);
        Assert.False(second.WasIncrementalHit);
    }

    [Fact]
    public void ChangedInstrumentationModeInvalidatesIncrementalCache()
    {
        BuildStandardClosure();
        Run(new InstrumentationConfiguration(), EmptyRuleSet());

        InstrumentationResult second = Run(
            new InstrumentationConfiguration { Mode = InstrumentationMode.RaceExploration },
            EmptyRuleSet());

        Assert.True(second.Succeeded);
        Assert.False(second.WasIncrementalHit);
        Assert.Contains("\"mode\": \"RaceExploration\"", File.ReadAllText(second.ManifestPath));
    }

    [Fact]
    public void ControlledTaskRulesHardenBroadExceptionHandlers()
    {
        Compile(
            "app",
            """
            namespace App;

            public static class Handler
            {
                public static int Run()
                {
                    try
                    {
                        return 1;
                    }
                    catch (System.Exception)
                    {
                        return -1;
                    }
                }
            }
            """);
        File.Copy(
            typeof(Clockwork.Runtime.ControlledExceptionGuard).Assembly.Location,
            Path.Combine(_source, "Clockwork.Runtime.dll"));
        File.WriteAllText(Path.Combine(_source, "app.runtimeconfig.json"), "{}");

        InstrumentationResult result = Run(
            new InstrumentationConfiguration(),
            BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies));

        Assert.True(result.Succeeded, string.Join("\n", result.Errors));
        Assert.Contains(
            result.Assemblies.Single(assembly => assembly.RelativePath == "app.dll").Manifest!.Transformations,
            transformation => transformation.RuleId == "clockwork.exceptions.harden"
                && transformation.Method.EndsWith(".Run", StringComparison.Ordinal));
    }

    [Fact]
    public void RaceModeStagesTheExactRuntimeUsedByTheRewriter()
    {
        BuildMinimalApp();
        FixtureCompiler.Compile(
            "Clockwork.Runtime",
            "namespace Clockwork.Runtime { public static class LegacyRuntime { } }",
            _source,
            FixtureSymbols.None,
            optimize: true);

        InstrumentationResult result = Run(
            new InstrumentationConfiguration { Mode = InstrumentationMode.RaceExploration },
            EmptyRuleSet());

        Assert.True(result.Succeeded, string.Join("\n", result.Errors));
        Assert.Equal(
            File.ReadAllBytes(typeof(RaceInstrumentation).Assembly.Location),
            File.ReadAllBytes(Path.Combine(_staging, "Clockwork.Runtime.dll")));
    }

    [Theory]
    [MemberData(nameof(UnsafeStagingDirectories))]
    public void RejectsStagingDirectoryWhichOverlapsSource(string stagingSelector)
    {
        BuildMinimalApp();
        string staging = stagingSelector switch
        {
            "same" => Path.Combine(_source, "..", "source"),
            "parent" => _root,
            "child" => Path.Combine(_source, "instrumented"),
            _ => throw new InvalidOperationException($"Unknown staging selector '{stagingSelector}'."),
        };

        ClosureException exception = Assert.Throws<ClosureException>(() =>
            InstrumentationRunner.Run(new InstrumentationRequest
            {
                SourceDirectory = _source,
                StagingDirectory = staging,
                Configuration = new InstrumentationConfiguration(),
                RuleSet = EmptyRuleSet(),
            }));

        Assert.Contains("dedicated directory", exception.Message);
        Assert.True(File.Exists(Path.Combine(_source, "app.dll")));
    }

    public static TheoryData<string> UnsafeStagingDirectories => new()
    {
        "same",
        "parent",
        "child",
    };

    [Fact]
    public void ManifestIsDeterministic()
    {
        BuildStandardClosure();
        InstrumentationResult first = Run(new InstrumentationConfiguration(), EmptyRuleSet());
        string firstManifest = File.ReadAllText(first.ManifestPath);

        // Force a full re-run by clearing the cache, then compare manifests byte-for-byte.
        File.Delete(CachePath());
        InstrumentationResult second = Run(new InstrumentationConfiguration(), EmptyRuleSet());
        string secondManifest = File.ReadAllText(second.ManifestPath);

        Assert.Equal(firstManifest, secondManifest);
    }

    [Fact]
    public void RejectsReadyToRunByDefault()
    {
        string? r2r = FindReadyToRunAssembly();
        Assert.SkipWhen(r2r is null, "No ReadyToRun image found in the shared framework.");

        BuildMinimalApp();
        File.Copy(r2r!, Path.Combine(_source, "r2rdep.dll"));

        InstrumentationResult result = Run(new InstrumentationConfiguration(), EmptyRuleSet());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, d => d.Id == RewriteDiagnosticIds.ReadyToRunRejected);
        // A failed run must not leave a stale cache that would skip the next build.
        Assert.False(File.Exists(CachePath()));
    }

    [Fact]
    public void StripToILProcessesReadyToRunInput()
    {
        string? r2r = FindReadyToRunAssembly();
        Assert.SkipWhen(r2r is null, "No ReadyToRun image found in the shared framework.");

        BuildMinimalApp();
        File.Copy(r2r!, Path.Combine(_source, "r2rdep.dll"));

        // Framework ReadyToRun images are strong-named, so re-signing is required to process them.
        string keyPath = WriteKey();
        InstrumentationResult result = Run(
            new InstrumentationConfiguration
            {
                ReadyToRunPolicy = ReadyToRunPolicy.StripToIL,
                StrongNamePolicy = StrongNamePolicy.ReSign,
                StrongNameKeyPath = keyPath,
            },
            EmptyRuleSet());

        AssemblyInstrumentationResult stripped = result.Assemblies.Single(a => a.RelativePath == "r2rdep.dll");
        Assert.True(stripped.ReadyToRunStripped);
        Assert.DoesNotContain(stripped.Diagnostics, d => d.Id == RewriteDiagnosticIds.ReadyToRunRejected);
        Assert.True(File.Exists(Path.Combine(_staging, "r2rdep.dll")), string.Join("\n", stripped.Diagnostics));
        // The staged output is IL-only (Cecil drops the native header on write).
        Assert.False(AssemblyImageInfo.Inspect(Path.Combine(_staging, "r2rdep.dll")).IsReadyToRun);
    }

    [Fact]
    public void FailsOnStrongNamedInputWhenPolicyIsFail()
    {
        string keyPath = WriteKey();
        Compile("app", "namespace App { public static class A { public static int Go() => 1; } }", keyPath);
        File.WriteAllText(Path.Combine(_source, "app.runtimeconfig.json"), "{}");

        InstrumentationResult result = Run(new InstrumentationConfiguration(), EmptyRuleSet());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, d => d.Id == RewriteDiagnosticIds.StrongNameReSignRequired);
    }

    [Fact]
    public void ReSignsStrongNamedClosureConsistently()
    {
        string keyPath = WriteKey();
        string third = Compile(
            "thirdparty", "namespace Third { public static class T { public static int V() => 1; } }", keyPath);
        Compile(
            "app",
            "namespace App { public static class A { public static int Go() => Third.T.V(); } }",
            keyPath,
            references: [third]);
        File.WriteAllText(Path.Combine(_source, "app.runtimeconfig.json"), "{}");

        var config = new InstrumentationConfiguration
        {
            StrongNamePolicy = StrongNamePolicy.ReSign,
            StrongNameKeyPath = keyPath,
        };
        InstrumentationResult result = Run(config, EmptyRuleSet());

        Assert.True(result.Succeeded, string.Join("\n", result.Errors));
        Assert.All(result.Assemblies, a => Assert.True(a.WasReSigned));

        // Every re-signed assembly carries the same public-key token, and the app's reference to the
        // dependency still matches the dependency's token: the closure is signing-consistent.
        string appToken = TokenOf(Path.Combine(_staging, "app.dll"));
        string depToken = TokenOf(Path.Combine(_staging, "thirdparty.dll"));
        Assert.Equal(depToken, appToken);
        Assert.Equal(depToken, ReferenceTokenOf(Path.Combine(_staging, "app.dll"), "thirdparty"));
    }

    [Fact]
    public void FailsWhenReSignConfiguredWithoutKey()
    {
        BuildMinimalApp();
        InstrumentationResult result = Run(
            new InstrumentationConfiguration { StrongNamePolicy = StrongNamePolicy.ReSign }, EmptyRuleSet());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, d => d.Id == RewriteDiagnosticIds.StrongNameReSignRequired);
    }

    private InstrumentationResult Run(InstrumentationConfiguration configuration, RewriteRuleSet ruleSet) =>
        InstrumentationRunner.Run(new InstrumentationRequest
        {
            SourceDirectory = _source,
            StagingDirectory = _staging,
            Configuration = configuration,
            RuleSet = ruleSet,
        });

    private string CachePath() =>
        Path.GetFullPath(_staging).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".cache";

    private void BuildStandardClosure()
    {
        string third = Compile("thirdparty", "namespace Third { public static class T { public static int V() => 1; } }");
        Compile(
            "app",
            "namespace App { public static class A { public static int Go() => Third.T.V(); } }",
            references: [third]);
        Compile("System.Fake", "namespace SysFake { public static class C { public static int V() => 1; } }");

        File.WriteAllBytes(Path.Combine(_source, "native.dll"), [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        File.WriteAllText(Path.Combine(_source, "app.deps.json"), "{}");
        File.WriteAllText(Path.Combine(_source, "app.runtimeconfig.json"), "{}");
    }

    private void BuildMinimalApp()
    {
        Compile("app", "namespace App { public static class A { public static int Go() => 1; } }");
        File.WriteAllText(Path.Combine(_source, "app.runtimeconfig.json"), "{}");
    }

    private static RewriteRuleSet EmptyRuleSet() => new("clockwork.test", "1.0", []);

    private string Compile(string name, string source, string? keyPath = null, IEnumerable<string>? references = null) =>
        FixtureCompiler.Compile(
            name, source, _source, FixtureSymbols.PortableFile, optimize: false,
            additionalReferencePaths: references, strongNameKeyFile: keyPath);

    private string WriteKey()
    {
        string keyPath = Path.Combine(_root, "test.snk");
        File.WriteAllBytes(keyPath, StrongNameKeys.CreatePrivateKeyBlob());
        return keyPath;
    }

    private static string TokenOf(string assemblyPath) =>
        StrongNameInspector.Inspect(assemblyPath).PublicKeyToken
        ?? throw new InvalidOperationException($"'{assemblyPath}' is not strong-named.");

    private static string ReferenceTokenOf(string assemblyPath, string referenceName)
    {
        using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(
            assemblyPath, new ReaderParameters { ReadSymbols = false, InMemory = true });
        AssemblyNameReference reference = definition.MainModule.AssemblyReferences
            .Single(r => string.Equals(r.Name, referenceName, StringComparison.Ordinal));
        return StrongNameInspector.FormatToken(reference.PublicKeyToken)!;
    }

    private static string? FindReadyToRunAssembly()
    {
        string trusted = (string)(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty);
        foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                if (AssemblyImageInfo.Inspect(path).IsReadyToRun)
                {
                    return path;
                }
            }
            catch (BadImageFormatException)
            {
            }
        }

        return null;
    }

    /// <inheritdoc/>
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

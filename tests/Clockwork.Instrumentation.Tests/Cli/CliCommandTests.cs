using Clockwork.Instrumentation.Inspection;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Clockwork.Runtime.Shims;
using Clockwork.Tool;

namespace Clockwork.Instrumentation.Tests.Cli;

/// <summary>
/// Verifies the <c>clockwork</c> CLI in-process by driving <see cref="Program.Run"/> with explicit
/// streams: the <c>inspect</c> command reports the true managed/strong-name/symbol/idempotence state,
/// the <c>rewrite</c> command stages an instrumented closure and honors <c>--dry-run</c>, and every
/// failure class maps to its distinct exit code. Process-level packaging execution is covered
/// separately by the fixture and smoke tests.
/// </summary>
public sealed class CliCommandTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwr-cli-tests", Guid.NewGuid().ToString("n"));
    private readonly string _source;
    private readonly string _staging;

    public CliCommandTests()
    {
        _source = Path.Combine(_root, "source");
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_source);
    }

    [Fact]
    public void InspectReportsManagedIlOnlyUninstrumented()
    {
        string app = Compile("app", "namespace App { public static class A { public static int Go() => 1; } }");

        (ExitCode code, string output, _) = Invoke("inspect", app);

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("IL-only", output);
        Assert.Contains("instrumented: no", output);
    }

    [Fact]
    public void InspectJsonEmitsStructuredFacts()
    {
        string app = Compile("app", "namespace App { public static class A { public static int Go() => 1; } }");

        (ExitCode code, string output, _) = Invoke("inspect", app, "--json");

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("\"managed\": true", output);
        Assert.Contains("\"readyToRun\": false", output);
        Assert.Contains("\"instrumented\": false", output);
    }

    [Fact]
    public void InspectAcceptsDocumentedBuiltInOptions()
    {
        string app = Compile("app", "namespace App { public static class A { public static int Go() => 1; } }");

        (ExitCode code, string output, string errors) = Invoke(
            "inspect",
            app,
            "--builtin",
            "clockwork.bcl.deterministic",
            "--builtin-include",
            "Clock");

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("clockwork.bcl.deterministic", output);
        Assert.DoesNotContain("Unknown option", errors);
    }

    [Fact]
    public void HelpListsAcceptedInspectConfigurationOptions()
    {
        (ExitCode code, string output, _) = Invoke("--help");

        Assert.Equal(ExitCode.Success, code);
        foreach (string option in new[]
        {
            "--config", "--rule-set", "--builtin", "--builtin-include", "--builtin-exclude",
            "--builtin-strict", "--include", "--exclude", "--r2r", "--strong-name", "--key",
            "--exclude-framework", "--rewrite-dependencies", "--target-runtime", "--json",
        })
        {
            Assert.Contains(option, output);
        }
    }

    [Fact]
    public void RewriteStagesInstrumentedClosure()
    {
        BuildMinimalClosure();
        string ruleSet = WriteEmptyRuleSet();

        (ExitCode code, string output, string errors) = Invoke(
            "rewrite", "--source", _source, "--output", _staging, "--rule-set", ruleSet);

        Assert.Equal(ExitCode.Success, code);
        Assert.True(File.Exists(Path.Combine(_staging, "app.dll")), errors);

        // The staged app carries the idempotence marker; the original source app does not.
        Assert.True(AssemblyInspector.Inspect(Path.Combine(_staging, "app.dll")).IsInstrumented);
        Assert.False(AssemblyInspector.Inspect(Path.Combine(_source, "app.dll")).IsInstrumented);
        Assert.Contains("success", output);
    }

    [Fact]
    public void RewriteDryRunWritesNothingAndReportsPlan()
    {
        BuildMinimalClosure();
        string ruleSet = WriteEmptyRuleSet();

        (ExitCode code, string output, _) = Invoke(
            "rewrite", "--source", _source, "--rule-set", ruleSet, "--dry-run");

        Assert.Equal(ExitCode.Success, code);
        Assert.False(Directory.Exists(_staging));
        Assert.Contains("app.dll", output);
        Assert.Contains("rewrite", output);
    }

    [Fact]
    public void RewriteDryRunUsesBuiltInRuleSetWhenSelected()
    {
        BuildMinimalClosure();

        (ExitCode code, string output, _) = Invoke(
            "rewrite", "--source", _source, "--builtin", "clockwork.bcl.deterministic", "--dry-run");

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("clockwork.bcl.deterministic", output);
    }

    [Fact]
    public void RewriteDryRunBuiltInAllExpandsToEveryRuleSet()
    {
        BuildMinimalClosure();

        (ExitCode code, string output, _) = Invoke(
            "rewrite", "--source", _source, "--builtin", "all", "--dry-run");

        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("clockwork.bcl.deterministic", output);
    }

    [Fact]
    public void RewriteBuiltInStagesSimulationOnlyExecutable()
    {
        BuildSimulationOnlyClosure();

        AppRunResult source = ProcessAppRunner.Run(Path.Combine(_source, "app.dll"));
        Assert.True(
            source.ExitCode == 0,
            $"Source process failed ({source.ExitCode}):\n{source.StandardOutput}\n{source.StandardError}");
        Assert.Contains("reached-end", source.Output);
        string sourceSideEffect = Path.Combine(_source, "side-effect.txt");
        Assert.True(File.Exists(sourceSideEffect));
        File.Delete(sourceSideEffect);

        (ExitCode code, _, string errors) = Invoke(
            "rewrite",
            "--source",
            _source,
            "--output",
            _staging,
            "--builtin",
            "clockwork.bcl.deterministic");

        Assert.Equal(ExitCode.Success, code);
        AppRunResult staged = ProcessAppRunner.Run(Path.Combine(_staging, "app.dll"));
        Assert.NotEqual(0, staged.ExitCode);
        Assert.Empty(staged.Output);
        Assert.Contains(typeof(SimulationNotActiveException).FullName!, staged.StandardError, StringComparison.Ordinal);
        Assert.Contains(SimulationNotActiveException.DiagnosticMessage, staged.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_staging, "side-effect.txt")), errors);
    }

    [Fact]
    public void RewriteDryRunFlagsStrongNamedInputAsBlocking()
    {
        string keyPath = Path.Combine(_root, "test.snk");
        File.WriteAllBytes(keyPath, StrongNameKeys.CreatePrivateKeyBlob());
        FixtureCompiler.Compile(
            "app", "namespace App { public static class A { public static int Go() => 1; } }",
            _source, FixtureSymbols.PortableFile, optimize: false, strongNameKeyFile: keyPath);
        File.WriteAllText(Path.Combine(_source, "app.runtimeconfig.json"), "{}");
        string ruleSet = WriteEmptyRuleSet();

        (ExitCode code, string output, _) = Invoke(
            "rewrite", "--source", _source, "--rule-set", ruleSet, "--dry-run");

        Assert.Equal(ExitCode.InstrumentationError, code);
        Assert.Contains("strong-named", output);
    }

    [Fact]
    public void UnknownCommandIsUsageError()
    {
        (ExitCode code, _, string errors) = Invoke("frobnicate");

        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("Unknown command", errors);
    }

    [Fact]
    public void UnknownOptionIsUsageError()
    {
        BuildMinimalClosure();
        string ruleSet = WriteEmptyRuleSet();

        (ExitCode code, _, string errors) = Invoke(
            "rewrite", "--source", _source, "--output", _staging, "--rule-set", ruleSet, "--bogus", "x");

        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("Unknown option", errors);
    }

    [Fact]
    public void MissingConfigFileIsConfigurationError()
    {
        (ExitCode code, _, string errors) = Invoke(
            "rewrite", "--source", _source, "--output", _staging,
            "--config", Path.Combine(_root, "does-not-exist.json"));

        Assert.Equal(ExitCode.ConfigurationError, code);
        Assert.Contains("not found", errors);
    }

    [Fact]
    public void MissingSourceDirectoryIsClosureError()
    {
        string ruleSet = WriteEmptyRuleSet();

        (ExitCode code, _, string errors) = Invoke(
            "rewrite", "--source", Path.Combine(_root, "nope"), "--output", _staging, "--rule-set", ruleSet);

        Assert.Equal(ExitCode.ClosureError, code);
        Assert.Contains("not found", errors);
    }

    [Fact]
    public void NoRuleSetIsConfigurationError()
    {
        BuildMinimalClosure();

        (ExitCode code, _, string errors) = Invoke("rewrite", "--source", _source, "--output", _staging);

        Assert.Equal(ExitCode.ConfigurationError, code);
        Assert.Contains("rule-set", errors);
    }

    private static (ExitCode Code, string Output, string Errors) Invoke(params string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        ExitCode code = Program.Run(args, output, errors);
        return (code, output.ToString(), errors.ToString());
    }

    private void BuildMinimalClosure()
    {
        Compile("app", "namespace App { public static class A { public static int Go() => 1; } }");
        File.WriteAllText(Path.Combine(_source, "app.runtimeconfig.json"), "{}");
    }

    private void BuildSimulationOnlyClosure()
    {
        FixtureCompiler.Compile(
            "app",
            """
            using System;
            using System.IO;

            public static class Program
            {
                public static void Main()
                {
                    _ = DateTime.UtcNow;
                    File.WriteAllText("side-effect.txt", "unexpected");
                    Console.WriteLine("reached-end");
                }
            }
            """,
            _source,
            FixtureSymbols.PortableFile,
            optimize: false,
            outputKind: Microsoft.CodeAnalysis.OutputKind.ConsoleApplication);
        File.Copy(
            typeof(SimulationNotActiveException).Assembly.Location,
            Path.Combine(_source, "Clockwork.Runtime.dll"),
            overwrite: true);
        ProcessAppRunner.WriteRuntimeConfig(Path.Combine(_source, "app.dll"));
    }

    private string Compile(string name, string source) =>
        FixtureCompiler.Compile(name, source, _source, FixtureSymbols.PortableFile, optimize: false);

    private string WriteEmptyRuleSet()
    {
        string path = Path.Combine(_root, "rules.json");
        File.WriteAllText(path, "{\"id\":\"clockwork.test\",\"version\":\"1.0\",\"rules\":[]}");
        return path;
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

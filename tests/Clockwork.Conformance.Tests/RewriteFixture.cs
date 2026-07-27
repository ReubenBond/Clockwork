using System.Reflection;
using System.Text;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Runtime.Shims;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// Compiles ordinary C# source whose direct calls target the real .NET&#160;10 BCL APIs, rewrites it
/// in-process with a versioned built-in rule set, and loads the rewritten assembly. Rewritten code is
/// simulation-only, while separately loaded uninstrumented probes continue to use the real BCL.
/// </summary>
internal sealed class RewriteFixture : IDisposable
{
    private static readonly MetadataReference[] RuntimeReferences = LoadRuntimeReferences();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cwr-conformance", Guid.NewGuid().ToString("n"));

    public RewriteFixture()
    {
        Directory.CreateDirectory(SourceDir);
        Directory.CreateDirectory(StagingDir);
    }

    private string SourceDir => Path.Combine(_root, "src");

    private string StagingDir => Path.Combine(_root, "staged");

    /// <summary>Compiles, rewrites with the built-in rule set, and loads the rewritten probe type.</summary>
    public StagedProbe Stage(
        string assemblyName,
        string typeName,
        string source,
        IEnumerable<BuiltInRuleFamily>? families = null)
        => StageWith(
            assemblyName,
            typeName,
            source,
            BuiltInRuleSets.BuildDeterministicBcl(families ?? BuiltInRuleSets.AllFamilies));

    /// <summary>Compiles and loads an ordinary probe without applying instrumentation.</summary>
    public UninstrumentedProbe CompileUninstrumented(
        string assemblyName,
        string typeName,
        string source,
        bool optimize = true)
    {
        string sourceDll = Compile(assemblyName, source, optimize);
        Assembly assembly = Assembly.LoadFrom(sourceDll);
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        return new UninstrumentedProbe(type);
    }

    /// <summary>
    /// Compiles, rewrites with the controlled-task rule set, and loads the rewritten probe type. The
    /// <paramref name="optimize"/> flag selects Debug vs Release codegen, which the C# compiler lowers to
    /// materially different async state machines - both must be retargeted onto the controlled machinery.
    /// </summary>
    public StagedProbe StageControlledTasks(
        string assemblyName,
        string typeName,
        string source,
        bool optimize,
        IEnumerable<BuiltInRuleFamily>? families = null)
        => StageWith(
            assemblyName,
            typeName,
            source,
            BuiltInRuleSets.BuildControlledTasks(families ?? BuiltInRuleSets.AllFamilies),
            optimize);

    private StagedProbe StageWith(
        string assemblyName,
        string typeName,
        string source,
        RewriteRuleSet ruleSet,
        bool optimize = true)
    {
        string sourceDll = Compile(assemblyName, source, optimize);
        string stagedDll = Path.Combine(StagingDir, assemblyName + ".dll");

        string runtimeDll = typeof(ControlledDateTime).Assembly.Location;
        var options = new RewriteOptions
        {
            HardenExceptionHandlers = true,
            DetectUncontrolledTasks = true,
            ReplacementAssemblyPaths = [runtimeDll],
            ReferenceSearchDirectories = [AppContext.BaseDirectory],
            TargetRuntime = new Version(10, 0),
        };

        RewriteResult result = RewriteEngine.Rewrite(new RewriteRequest(sourceDll, stagedDll, ruleSet, options));
        if (!result.Succeeded)
        {
            string diagnostics = string.Join(
                Environment.NewLine,
                result.Manifest.Diagnostics.Select(d => $"{d.Severity} {d.Id}: {d.Message}"));
            throw new InvalidOperationException($"Rewrite of '{assemblyName}' failed:{Environment.NewLine}{diagnostics}");
        }

        Assembly staged = Assembly.LoadFrom(stagedDll);
        Type type = staged.GetType(typeName, throwOnError: true)!;
        return new StagedProbe(type, result, sourceDll, stagedDll, ruleSet, options);
    }

    private string Compile(string assemblyName, string source, bool optimize = true)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source, new CSharpParseOptions(LanguageVersion.Latest), path: assemblyName + ".cs", encoding: Encoding.UTF8);
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: optimize ? OptimizationLevel.Release : OptimizationLevel.Debug,
            deterministic: true);
        CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, [tree], RuntimeReferences, options);

        string path = Path.Combine(SourceDir, assemblyName + ".dll");
        EmitResult emit = compilation.Emit(path);
        if (!emit.Success)
        {
            string errors = string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Compilation of '{assemblyName}' failed:{Environment.NewLine}{errors}");
        }

        return path;
    }

    private static MetadataReference[] LoadRuntimeReferences()
    {
        string trusted = (string)(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty);
        return trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
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
            // Best-effort: the loaded staged assembly keeps its file mapped on Windows.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>An ordinary compiled probe which has not been instrumented.</summary>
internal sealed record UninstrumentedProbe(Type Type)
{
    public MethodInfo Method(string name) =>
        Type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Probe method '{name}' not found on '{Type.FullName}'.");
}

/// <summary>A rewritten, loaded probe type plus the rewrite outcome and inputs that produced it.</summary>
internal sealed record StagedProbe(
    Type Type,
    RewriteResult Result,
    string SourceDll,
    string StagedDll,
    RewriteRuleSet RuleSet,
    RewriteOptions Options)
{
    public MethodInfo Method(string name) =>
        Type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Probe method '{name}' not found on '{Type.FullName}'.");
}

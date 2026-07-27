using System.Collections.Immutable;
using System.Linq;
using Clockwork.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Clockwork.Analyzers.Tests;

/// <summary>
/// Verifies that <see cref="NondeterministicApiAnalyzer"/> reports the same controlled and rejected
/// BCL surface that the built-in <c>clockwork.bcl.deterministic</c> rewrite rule set redirects, so
/// compile-time guidance stays aligned with rewrite-time behaviour.
/// </summary>
public sealed class NondeterministicApiAnalyzerTests
{
    [Theory]
    [InlineData("_ = System.DateTime.Now;", "CW1001")]
    [InlineData("_ = System.DateTime.UtcNow;", "CW1001")]
    [InlineData("_ = System.DateTime.Today;", "CW1001")]
    [InlineData("_ = System.DateTimeOffset.Now;", "CW1001")]
    [InlineData("_ = System.DateTimeOffset.UtcNow;", "CW1001")]
    [InlineData("_ = System.Diagnostics.Stopwatch.GetTimestamp();", "CW1001")]
    [InlineData("_ = System.Diagnostics.Stopwatch.GetElapsedTime(0L);", "CW1001")]
    [InlineData("_ = System.Environment.TickCount;", "CW1001")]
    [InlineData("_ = System.Environment.TickCount64;", "CW1001")]
    [InlineData("_ = System.Guid.NewGuid();", "CW1001")]
    [InlineData("_ = System.Guid.CreateVersion7();", "CW1001")]
    [InlineData("_ = System.Random.Shared;", "CW1001")]
    [InlineData("_ = new System.Random();", "CW1001")]
    [InlineData("_ = new System.Random(42);", "CW1001")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(1);", "CW1002")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero);", "CW1002")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(1, default);", "CW1002")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, default(System.Threading.CancellationToken));", "CW1002")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, System.TimeProvider.System);", "CW1002")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, System.TimeProvider.System, default);", "CW1002")]
    [InlineData("_ = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);", "CW1002")]
    [InlineData("_ = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100);", "CW1002")]
    [InlineData("System.Security.Cryptography.RandomNumberGenerator.Fill(System.Span<byte>.Empty);", "CW1002")]
    [InlineData("using var r = System.Security.Cryptography.RandomNumberGenerator.Create();", "CW1002")]
    public async Task ReportsExpectedDiagnostic(string statement, string expectedId)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Wrap(statement));

        Diagnostic single = Assert.Single(diagnostics);
        Assert.Equal(expectedId, single.Id);
    }

    [Theory]
    [InlineData("_ = System.DateTime.MinValue;")]
    [InlineData("_ = System.DateTime.Now.Year;")] // property access on the result is fine once flagged; ensure only one report
    [InlineData("_ = new System.DateTime(2024, 1, 1);")]
    [InlineData("_ = System.Guid.Empty;")]
    [InlineData("var r = new System.Random(1); _ = r.Next();")]
    public async Task DoesNotOverReportSafeUsage(string statement)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Wrap(statement));

        // At most a single controlled diagnostic (e.g. DateTime.Now / new Random(int)); never a crypto report.
        Assert.DoesNotContain(diagnostics, d => d.Id == "CW1002");
        Assert.True(diagnostics.Length <= 1);
    }

    [Fact]
    public async Task GetElapsedTimeTwoArgOverloadIsNotFlaggedAsControlled()
    {
        // Only Stopwatch.GetElapsedTime(long) is redirected; the (long,long) overload is a documented hole.
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            Wrap("_ = System.Diagnostics.Stopwatch.GetElapsedTime(0L, 1L);"));

        Assert.Empty(diagnostics);
    }

    private static string Wrap(string statement) =>
        "class Probe { void M() { " + statement + " } }";

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerProbe",
            [tree],
            ReferenceAssemblies,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            [new NondeterministicApiAnalyzer()]);

        ImmutableArray<Diagnostic> analyzerDiagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return [.. analyzerDiagnostics.Where(d => d.Id is "CW1001" or "CW1002")];
    }

    private static readonly MetadataReference[] ReferenceAssemblies = LoadReferences();

    private static MetadataReference[] LoadReferences()
    {
        string trusted = (string)(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty);
        return trusted
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}

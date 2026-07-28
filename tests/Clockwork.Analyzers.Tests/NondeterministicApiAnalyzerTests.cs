using System.Collections.Immutable;
using System.Linq;
using Clockwork.Analyzers;
using Clockwork.Instrumentation.Rules.BuiltIn;
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
    [InlineData("_ = System.Threading.Tasks.Task.Delay(1);", "CW1001")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero);", "CW1001")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(1, default);", "CW1001")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, default(System.Threading.CancellationToken));", "CW1001")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, System.TimeProvider.System);", "CW1001")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(System.TimeSpan.Zero, System.TimeProvider.System, default);", "CW1001")]
    [InlineData("using var timer = new System.Threading.Timer(_ => { });", "CW1001")]
    [InlineData("using var timer = new System.Threading.PeriodicTimer(System.TimeSpan.FromSeconds(1));", "CW1001")]
    [InlineData("using var source = new System.Threading.CancellationTokenSource(1);", "CW1001")]
    [InlineData("_ = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);", "CW1002")]
    [InlineData("_ = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100);", "CW1002")]
    [InlineData("System.Security.Cryptography.RandomNumberGenerator.Fill(System.Span<byte>.Empty);", "CW1002")]
    [InlineData("using var r = System.Security.Cryptography.RandomNumberGenerator.Create();", "CW1002")]
    [InlineData("_ = System.Security.Cryptography.RandomNumberGenerator.GetItems<int>([1, 2], 1);", "CW1002")]
    [InlineData("System.Security.Cryptography.RandomNumberGenerator.Shuffle<int>(System.Span<int>.Empty);", "CW1002")]
    [InlineData("_ = System.Threading.Tasks.Task.Run(() => { });", "CW1001")]
    [InlineData("System.Threading.Thread.Sleep(1);", "CW1001")]
    [InlineData("System.Threading.ThreadPool.QueueUserWorkItem(_ => { });", "CW1001")]
    [InlineData("System.Threading.Tasks.Parallel.Invoke(() => { });", "CW1001")]
    [InlineData("System.Threading.Monitor.Enter(new object());", "CW1001")]
    [InlineData("using var semaphore = new System.Threading.SemaphoreSlim(1);", "CW1001")]
    [InlineData("using var readerWriter = new System.Threading.ReaderWriterLockSlim();", "CW1001")]
    [InlineData("using var reset = new System.Threading.ManualResetEventSlim();", "CW1001")]
    [InlineData("using var mutex = new System.Threading.Mutex();", "CW1001")]
    [InlineData("using var semaphore = new System.Threading.Semaphore(1, 1);", "CW1001")]
    [InlineData("var barrier = new System.Threading.Barrier(1);", "CW1001")]
    [InlineData("var countdown = new System.Threading.CountdownEvent(1);", "CW1001")]
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
    [InlineData("_ = System.Threading.Thread.CurrentThread.Priority;")]
    public async Task DoesNotOverReportSafeUsage(string statement)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Wrap(statement));

        // At most a single controlled diagnostic (e.g. DateTime.Now / new Random(int)); never a crypto report.
        Assert.DoesNotContain(diagnostics, d => d.Id == "CW1002");
        Assert.True(diagnostics.Length <= 1);
    }

    [Fact]
    public async Task ThreadPrioritySetterRequiresInstrumentationButGetterDoesNot()
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Wrap(
            "var thread = System.Threading.Thread.CurrentThread; " +
            "_ = thread.Priority; " +
            "thread.Priority = System.Threading.ThreadPriority.AboveNormal;"));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("CW1001", diagnostic.Id);
    }

    [Theory]
    [InlineData("System.Threading.Tasks.Task.CompletedTask.Wait(1);")]
    public async Task DoesNotFlagUninstrumentedTaskOverloads(string statement)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Wrap(statement));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task GetElapsedTimeTwoArgOverloadIsNotFlaggedAsControlled()
    {
        // Only Stopwatch.GetElapsedTime(long) is redirected; the (long,long) overload is a documented hole.
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
            Wrap("_ = System.Diagnostics.Stopwatch.GetElapsedTime(0L, 1L);"));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CoversEveryPublicStaticRandomNumberGeneratorApi()
    {
        const string source = """
            using System;
            using System.Security.Cryptography;

            class Probe
            {
                void M()
                {
                    Span<byte> bytes = stackalloc byte[8];
                    Span<char> chars = stackalloc char[8];
                    ReadOnlySpan<char> alphabet = "abcdef";
                    ReadOnlySpan<int> choices = [1, 2, 3];
                    Span<int> destination = stackalloc int[2];
                    using var a = RandomNumberGenerator.Create();
                    using var b = RandomNumberGenerator.Create("ignored");
                    RandomNumberGenerator.Fill(bytes);
                    _ = RandomNumberGenerator.GetBytes(8);
                    RandomNumberGenerator.GetHexString(chars, lowercase: false);
                    _ = RandomNumberGenerator.GetHexString(8, lowercase: false);
                    _ = RandomNumberGenerator.GetInt32(10);
                    _ = RandomNumberGenerator.GetInt32(1, 10);
                    RandomNumberGenerator.GetItems(choices, destination);
                    _ = RandomNumberGenerator.GetItems(choices, 2);
                    _ = RandomNumberGenerator.GetString(alphabet, 8);
                    RandomNumberGenerator.Shuffle(destination);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);
        int publicStaticCount = typeof(System.Security.Cryptography.RandomNumberGenerator)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Length;

        Assert.Equal(publicStaticCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("CW1002", diagnostic.Id));
    }

    [Fact]
    public async Task SeededRandomDiagnosticNamesTheSeededConstructor()
    {
        Diagnostic diagnostic = Assert.Single(await AnalyzeAsync(Wrap("_ = new System.Random(42);")));
        string message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains("new Random(int)", message);
        Assert.DoesNotContain("'new Random()'", message);
    }

    [Fact]
    public void InventoryCoversEveryShippedTaskAndSynchronizationRuleMember()
    {
        BuiltInRuleFamily[] analyzerFamilies =
        [
            BuiltInRuleFamily.TaskCombinators,
            BuiltInRuleFamily.TaskSynchronization,
            BuiltInRuleFamily.TaskContinuations,
            BuiltInRuleFamily.TaskTime,
            BuiltInRuleFamily.TaskScheduling,
            BuiltInRuleFamily.TaskFactory,
            BuiltInRuleFamily.Thread,
            BuiltInRuleFamily.ThreadPool,
            BuiltInRuleFamily.Timers,
            BuiltInRuleFamily.CancellationTimers,
            BuiltInRuleFamily.Parallel,
            BuiltInRuleFamily.Monitor,
            BuiltInRuleFamily.Lock,
            BuiltInRuleFamily.Semaphore,
            BuiltInRuleFamily.ReaderWriterLockSlim,
            BuiltInRuleFamily.ManualResetEventSlim,
            BuiltInRuleFamily.Mutex,
            BuiltInRuleFamily.KernelSemaphore,
            BuiltInRuleFamily.SpinLock,
            BuiltInRuleFamily.ExecutionContext,
            BuiltInRuleFamily.SynchronizationContext,
            BuiltInRuleFamily.Barrier,
            BuiltInRuleFamily.CountdownEvent,
        ];

        foreach ((BuiltInRuleFamily family, Clockwork.Instrumentation.Rules.RewriteRule rule) in
            BuiltInRuleSets.ControlledTasksInventory.Where(entry => analyzerFamilies.Contains(entry.Family)))
        {
            string typeName = rule.Target.DeclaringTypeFullName.Replace('/', '+');
            if (rule.Target.MemberName is null)
            {
                Assert.True(
                    InstrumentedApiInventory.ContainsType(typeName),
                    $"Analyzer inventory does not cover type rule '{rule.Id}' ({typeName}).");
                continue;
            }

            string memberName = rule.Target.MemberName switch
            {
                string name when name.StartsWith("get_", StringComparison.Ordinal)
                    || name.StartsWith("set_", StringComparison.Ordinal) => name[4..],
                _ => rule.Target.MemberName,
            };
            Assert.True(
                InstrumentedApiInventory.Contains(typeName, memberName),
                $"Analyzer inventory does not cover rule '{rule.Id}' ({typeName}::{memberName}).");
        }
    }

    [Theory]
    [InlineData("var values = new System.Collections.Generic.Dictionary<int, int>(); _ = System.Linq.Enumerable.First(values);")]
    [InlineData("var values = new System.Collections.Generic.HashSet<int>(); _ = System.Linq.Enumerable.ElementAt(values, 0);")]
    [InlineData("var values = new System.Collections.Generic.Dictionary<int, int>(); _ = string.Join(\",\", values.Keys);")]
    public async Task ReportsActionableUnstableCollectionOrdering(string source)
    {
        Diagnostic diagnostic = Assert.Single(await AnalyzeAsync("class Probe { void M() { " + source + " } }"));

        Assert.Equal("CW1003", diagnostic.Id);
        Assert.Contains(
            "OrderBy",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var values = new System.Collections.Generic.Dictionary<int, int>(); values[1] = 2; _ = values[1];")]
    [InlineData("var values = new System.Collections.Generic.HashSet<int>(); values.Add(1); _ = values.Contains(1);")]
    [InlineData("var values = new System.Collections.Generic.Dictionary<int, int>(); _ = System.Linq.Enumerable.First(System.Linq.Enumerable.OrderBy(values, pair => pair.Key));")]
    [InlineData("var values = new System.Collections.Generic.List<int>(); values.Add(1); _ = values[0];")]
    [InlineData("var values = new System.Collections.Generic.HashSet<int>(); _ = System.Linq.Enumerable.SequenceEqual(values, values);")]
    public async Task DoesNotBlanketWarnForCollectionUsage(string source)
    {
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync("class Probe { void M() { " + source + " } }");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CW1003");
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
        return [.. analyzerDiagnostics.Where(d => d.Id is "CW1001" or "CW1002" or "CW1003")];
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

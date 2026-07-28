using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Manifest;
using Clockwork.Instrumentation.Orchestration;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Clockwork.Runtime.Policy;

namespace Clockwork.Instrumentation.Tests.Execution;

/// <summary>
/// Full out-of-process execution tests. Each builds a real console-application closure (app +
/// third-party dependency + controlled API + shim), stages an instrumented copy with the
/// orchestrator, and launches both the original and the staged executables as independent
/// processes. Together they prove the central build/tool integration guarantee - an <em>enabled staged
/// executable dispatches to the test shim</em> while a <em>normal executable does not</em> - across
/// Debug/Release, symbols present/absent, config-loaded rules, a rejected call, an excluded
/// dependency, an incremental rebuild, and a signed closure.
/// </summary>
public sealed class ProcessExecutionTests
{
    public static TheoryData<bool> Optimize => new() { true, false };

    [Theory]
    [MemberData(nameof(Optimize))]
    public void BuiltInRulesRewriteThirdPartySynchronizationAndEveryDelayOverload(bool optimize)
    {
        using var fixture = BuiltInProcessFixture.Create(optimize);

        AppRunResult source = fixture.RunSource();
        Assert.True(
            source.ExitCode == 0,
            $"Source process failed ({source.ExitCode}):\n{source.StandardOutput}\n{source.StandardError}");
        Assert.Contains("monitor=real", source.Output);
        Assert.Contains("lock=System.Threading.Lock", source.Output);
        Assert.Contains("semaphore=signaled", source.Output);
        Assert.Contains("delays=6", source.Output);
        Assert.Contains("timer=System.Threading.Timer", source.Output);

        InstrumentationResult instrumentation = fixture.Instrument();
        Assert.True(instrumentation.Succeeded, string.Join("\n", instrumentation.Errors));
        AppRunResult staged = fixture.RunStaged();
        Assert.True(
            staged.ExitCode == 0,
            $"Staged process failed ({staged.ExitCode}):\n{staged.StandardOutput}\n{staged.StandardError}");
        Assert.Contains("monitor=rejected", staged.Output);
        Assert.Contains("lock=Clockwork.Shims.System.Threading.ControlledLock", staged.Output);
        Assert.Contains("semaphore=signaled", staged.Output);
        Assert.Contains("delays=6", staged.Output);
        Assert.Contains("timer=Clockwork.Shims.System.Threading.ControlledTimer", staged.Output);
    }

    [Fact]
    public void NormalExecutableDoesNotDispatchToShim()
    {
        using var fixture = ExecutionClosureFixture.Create();

        AppRunResult run = fixture.RunSource();

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("app.ticks=100", run.Output);
        Assert.Contains("dep.ticks=100", run.Output);
        Assert.Contains("app.value=3", run.Output);
        // The shim sentinel values must never appear in an ordinary build.
        Assert.DoesNotContain("999", run.Output);
        Assert.DoesNotContain("app.value=7", run.Output);
    }

    [Fact]
    public void EnabledStagedExecutableDispatchesToShim()
    {
        using var fixture = ExecutionClosureFixture.Create();

        InstrumentationResult result = fixture.Instrument();
        Assert.True(result.Succeeded, string.Join("\n", result.Errors));

        AppRunResult staged = fixture.RunStaged();
        Assert.Equal(0, staged.ExitCode);
        Assert.Contains("app.ticks=999", staged.Output);
        Assert.Contains("dep.ticks=999", staged.Output);
        Assert.Contains("app.value=7", staged.Output);

        // Instrumenting must never mutate the source: the original still behaves normally.
        AppRunResult source = fixture.RunSource();
        Assert.Contains("app.ticks=100", source.Output);
    }

    [Fact]
    public void ReleaseClosureWithoutSymbolsDispatchesToShim()
    {
        using var fixture = ExecutionClosureFixture.Create(FixtureSymbols.None, optimize: true);

        Assert.True(fixture.Instrument().Succeeded);

        AppRunResult staged = fixture.RunStaged();
        Assert.Equal(0, staged.ExitCode);
        Assert.Contains("app.ticks=999", staged.Output);
        Assert.Contains("dep.ticks=999", staged.Output);
    }

    [Fact]
    public void EmbeddedSymbolsClosureDispatchesToShim()
    {
        using var fixture = ExecutionClosureFixture.Create(FixtureSymbols.Embedded);

        Assert.True(fixture.Instrument().Succeeded);

        AppRunResult staged = fixture.RunStaged();
        Assert.Equal(0, staged.ExitCode);
        Assert.Contains("app.value=7", staged.Output);
    }

    [Fact]
    public void ExcludedDependencyRetainsOriginalBehavior()
    {
        using var fixture = ExecutionClosureFixture.Create();

        var configuration = new InstrumentationConfiguration
        {
            ExcludePatterns = [ExecutionClosureFixture.ThirdPartyAssemblyName + ".dll"],
        };
        Assert.True(fixture.Instrument(configuration).Succeeded);

        AppRunResult staged = fixture.RunStaged();
        Assert.Equal(0, staged.ExitCode);
        // The application assembly was rewritten, but the excluded dependency was copied verbatim.
        Assert.Contains("app.ticks=999", staged.Output);
        Assert.Contains("dep.ticks=100", staged.Output);
    }

    [Fact]
    public void RuleSetLoadedFromJsonConfigDispatchesToShim()
    {
        using var fixture = ExecutionClosureFixture.Create();

        // Author the rule set as a serialized JSON document and drive instrumentation through the
        // configuration's rule-set-loading path, exactly as the build task and CLI do.
        string ruleSetPath = Path.Combine(fixture.SourceDirectory, "clockwork.rules.json");
        File.WriteAllText(ruleSetPath, RuleSetJson.Write(ExecutionClosureFixture.StandardRuleSet()));

        var configuration = new InstrumentationConfiguration { RuleSetPaths = [ruleSetPath] };
        RewriteRuleSet merged = RuleSetMerge.LoadAndMerge(configuration).RuleSet;
        Assert.True(fixture.Instrument(configuration, merged).Succeeded);

        AppRunResult staged = fixture.RunStaged();
        Assert.Equal(0, staged.ExitCode);
        Assert.Contains("app.ticks=999", staged.Output);
    }

    [Fact]
    public void RejectedCallFailsStagedProcessAtRuntime()
    {
        using var fixture = ExecutionClosureFixture.Create(appSource: ExecutionClosureFixture.RejectionAppSource);

        // The uninstrumented application runs the (no-op) real API to completion.
        AppRunResult source = fixture.RunSource();
        Assert.Equal(0, source.ExitCode);
        Assert.Contains("reached-end", source.Output);

        Assert.True(fixture.Instrument().Succeeded);

        // The instrumented application throws deterministically before the rejected call executes.
        AppRunResult staged = fixture.RunStaged();
        Assert.NotEqual(0, staged.ExitCode);
        Assert.DoesNotContain("reached-end", staged.Output);
        Assert.Contains("Rejected", staged.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonPassThroughRuleLeavesProcessBehaviorUnchangedAndRecordsSites()
    {
        using var fixture = ExecutionClosureFixture.Create();
        var authored = new RewriteRuleSet(
            "clockwork.passthrough.process",
            "1.0",
            [
                RewriteRule.RedirectCall(
                    "passthrough-clock",
                    MemberSignature.Method("ClockworkFixtures.Api.RealClock", "UtcNowTicks"),
                    RewriteReplacement.Method(
                        "Missing.Replacement",
                        "Missing.Replacement.Shim",
                        "UtcNowTicks"),
                    SimulationApiPolicy.PassThrough) with
                {
                    Description = "Approved process boundary.",
                },
            ]);
        RewriteRuleSet parsed = RuleSetJson.Parse(RuleSetJson.Write(authored));

        InstrumentationResult result = fixture.Instrument(ruleSet: parsed);
        Assert.True(result.Succeeded, string.Join("\n", result.Errors));

        AppRunResult staged = fixture.RunStaged();
        Assert.Equal(0, staged.ExitCode);
        Assert.Contains("app.ticks=100", staged.Output);
        Assert.Contains("dep.ticks=100", staged.Output);
        Assert.All(
            result.Assemblies.SelectMany(assembly => assembly.Manifest?.Transformations ?? []),
            transformation =>
            {
                Assert.Equal(TransformationOutcome.PassedThrough, transformation.Outcome);
                Assert.Equal("Approved process boundary.", transformation.Reason);
            });
    }

    [Fact]
    public void IncrementalRebuildLeavesRunnableClosure()
    {
        using var fixture = ExecutionClosureFixture.Create();

        Assert.False(fixture.Instrument().WasIncrementalHit);

        // A second run with identical inputs is a verified no-op that retains the staged closure.
        InstrumentationResult second = fixture.Instrument();
        Assert.True(second.WasIncrementalHit);

        AppRunResult staged = fixture.RunStaged();
        Assert.Equal(0, staged.ExitCode);
        Assert.Contains("app.ticks=999", staged.Output);
    }

    [Fact]
    public void SignedClosureExecutesAfterReSigning()
    {
        using var fixture = ExecutionClosureFixture.Create(strongName: true);

        var configuration = new InstrumentationConfiguration
        {
            StrongNamePolicy = StrongNamePolicy.ReSign,
            StrongNameKeyPath = fixture.StrongNameKeyPath,
        };
        InstrumentationResult result = fixture.Instrument(configuration);
        Assert.True(result.Succeeded, string.Join("\n", result.Errors));

        // A re-signed strong-named closure with consistent public-key tokens loads and runs.
        AppRunResult staged = fixture.RunStaged();
        Assert.Equal(0, staged.ExitCode);
        Assert.Contains("app.ticks=999", staged.Output);
        Assert.Contains("dep.ticks=999", staged.Output);
    }
}

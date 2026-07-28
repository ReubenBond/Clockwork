using Clockwork.Runtime.Decisions;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Strategies;
using Clockwork.Runtime.Tests.Scheduling;

namespace Clockwork.Runtime.Tests.Racing;

/// <summary>Verifies injected scheduling points use the controlled scheduler and replayable strategy.</summary>
public sealed class RaceSchedulingPointTests
{
    [Fact]
    public void SchedulingPointEmitsExactMetadataAndYieldsThroughScheduler()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule(
            "writer",
            () => RaceInstrumentation.WriteStatic(
                "Fx.Subject::Value",
                "System.Void Fx.Subject::Run()",
                42,
                "Subject.cs",
                17));

        Assert.Equal(2, scheduler.Drain());

        RaceSchedulingPoint point = Assert.Single(scheduler.CaptureRaceSchedulingPoints());
        Assert.Equal(RaceAccessKind.Write, point.Kind);
        Assert.Equal("Fx.Subject::Value", point.Location);
        Assert.Equal("System.Void Fx.Subject::Run()", point.Source.Method);
        Assert.Equal(42, point.Source.ILOffset);
        Assert.Equal("Subject.cs", point.Source.SourceFile);
        Assert.Equal(17, point.Source.SourceLine);
    }

    [Fact]
    public void SameSeedReproducesTraceAndAlternateSeedsExploreDifferentInterleavings()
    {
        string first = Run(seed: 7);
        string replay = Run(seed: 7);
        Assert.Equal(first, replay);

        var traces = Enumerable.Range(1, 24).Select(Run).ToHashSet(StringComparer.Ordinal);
        Assert.True(traces.Count > 1, "Expected alternate scheduler seeds to explore more than one trace.");
    }

    [Fact]
    public void SchedulingPointOutsideControlledOperationIsNoOp()
    {
        RaceInstrumentation.ReadStatic("Fx.Subject::Value", "Fx.Subject::Run", 1, null, -1);
    }

    private static string Run(int seed)
    {
        using var scheduler = SchedulerTestHarness.NewScheduler(seed: seed);
        scheduler.SchedulingStrategy = new SeededRandomSchedulingStrategy(seed);
        scheduler.DecisionLog = new SimulationDecisionLog();
        scheduler.Schedule("a", () => AccessTwice("A"));
        scheduler.Schedule("b", () => AccessTwice("B"));
        scheduler.Drain();

        Assert.NotEmpty(scheduler.DecisionLog.Records);
        return string.Join(
            ",",
            scheduler.CaptureRaceSchedulingPoints().Select(point => $"{point.OperationId.Value}:{point.Source.Method}"));
    }

    private static void AccessTwice(string method)
    {
        RaceInstrumentation.WriteStatic("Fx.Subject::Value", method, 1, "Subject.cs", 10);
        RaceInstrumentation.ReadStatic("Fx.Subject::Value", method, 2, "Subject.cs", 11);
    }
}

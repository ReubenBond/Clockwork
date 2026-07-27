using Clockwork.Runtime.Decisions;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Tests.Scheduling;

/// <summary>
/// Verifies that the controlled-operation scheduler installs a logical execution identity into the
/// ambient <see cref="SimulationExecutionContext"/> such that decisions recorded from inside an
/// operation body automatically carry that operation's runtime, node, and logical identity - the
/// Phase 3A "logical identity wiring" requirement - without changing any Phase 2 decision API.
/// </summary>
public sealed class ControlledOperationDecisionWiringTests
{
    [Fact]
    public void DecisionRecordedInsideOperationCarriesItsLogicalAndRuntimeIdentity()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var log = new SimulationDecisionLog();
        SimulationDecisionRecord? recorded = null;

        var op = scheduler.Schedule("decide", () =>
        {
            var request = SimulationDecisionRequest.FromAmbientContext(
                SimulationSeedDomain.Scheduler,
                SimulationDecisionKind.Choice,
                selectedResult: "picked",
                sourceId: "unit-test");
            recorded = log.Record(request);
        });

        scheduler.Drain();

        Assert.NotNull(recorded);
        Assert.Equal(op.LogicalExecutionId, recorded!.LogicalExecutionId);
        Assert.Equal(scheduler.Runtime.Id, recorded.RuntimeId);
        Assert.Null(recorded.NodeId);
        Assert.Equal("picked", recorded.SelectedResult);
    }

    [Fact]
    public void DecisionRecordedInsideNodeScopedOperationCarriesTheNodeAddress()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var log = new SimulationDecisionLog();
        SimulationDecisionRecord? recorded = null;

        scheduler.Schedule(
            "decide",
            () => recorded = log.Record(SimulationDecisionRequest.FromAmbientContext(
                SimulationSeedDomain.Network,
                SimulationDecisionKind.RandomDraw,
                selectedResult: "7")),
            new SimulationNodeIdentity("node-7"));

        scheduler.Drain();

        Assert.NotNull(recorded);
        Assert.Equal("node-7", recorded!.NodeId);
    }

    [Fact]
    public void DistinctOperationsProduceDistinctLogicalIdentityOnTheirDecisions()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var log = new SimulationDecisionLog();

        var opA = scheduler.Schedule("a", () => log.Record(SimulationDecisionRequest.FromAmbientContext(
            SimulationSeedDomain.Scheduler, SimulationDecisionKind.Choice, "a")));
        var opB = scheduler.Schedule("b", () => log.Record(SimulationDecisionRequest.FromAmbientContext(
            SimulationSeedDomain.Scheduler, SimulationDecisionKind.Choice, "b")));

        scheduler.Drain();

        var records = log.Records;
        Assert.Equal(2, records.Count);
        Assert.NotEqual(opA.LogicalExecutionId, opB.LogicalExecutionId);
        Assert.Equal(opA.LogicalExecutionId, records[0].LogicalExecutionId);
        Assert.Equal(opB.LogicalExecutionId, records[1].LogicalExecutionId);
    }

    [Fact]
    public void FromAmbientContextThrowsWhenNoRuntimeScopeIsActive()
    {
        Assert.Throws<InvalidOperationException>(() => SimulationDecisionRequest.FromAmbientContext(
            SimulationSeedDomain.Scheduler,
            SimulationDecisionKind.Choice,
            selectedResult: "x"));
    }
}

using Clockwork.Runtime.Threading;

namespace Clockwork.Tests;

public sealed class ControlledTimerIntegrationTests
{
    [Fact]
    public async Task PendingTimerIsReportedAndTeardownCancelsIt()
    {
        var builder = new SimulationBuilder()
            .WithSeed(1)
            .WithStartDateTime(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        SimulationNodeHandle<object?> node = builder.AddNode("node");
        SimulationCluster simulation = builder.Build();
        ControlledTimer? timer = null;

        node.Context.TaskQueue.EnqueueAfter(
            () => timer = new ControlledTimer(
                _ => throw new InvalidOperationException("A teardown-canceled timer fired."),
                null,
                TimeSpan.FromHours(1),
                Timeout.InfiniteTimeSpan),
            TimeSpan.Zero);

        SimulationExecutionResult result = simulation.RunUntil(() => timer is not null);

        SimulationScheduledItemDiagnostic deadline = Assert.Single(
            result.PendingWork.Items,
            static item => item.QueueIdentity == "controlled-task-loop");
        Assert.Equal("PausedUntilTime", deadline.ItemType);
        Assert.Equal(1, result.PendingWork.WaitingCount);

        await simulation.DisposeAsync();
    }
}

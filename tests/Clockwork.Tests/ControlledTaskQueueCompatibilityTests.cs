using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Tests;

/// <summary>
/// <para>
/// Covers the opt-in controlled-operation kernel compatibility bridge in <see cref="SimulationTaskQueue"/>: when a
/// <see cref="ControlledOperationScheduler"/> is supplied, each ready item runs as a single
/// controlled operation on the kernel's permission baton instead of inline. These tests pin down the
/// two guarantees that make the bridge safe to adopt incrementally: the execution order and outcomes
/// are identical to the inline path, and the only observable difference is that controlled items
/// carry a logical execution identity (which is the whole point of the kernel).
/// </para>
/// <para>
/// The bridge is off by default - a queue constructed without a scheduler behaves exactly as before,
/// which is what keeps every existing trace snapshot byte-identical - so it is validated here by
/// constructing the controlled queue explicitly rather than by changing any default host wiring.
/// </para>
/// </summary>
public sealed class ControlledTaskQueueCompatibilityTests
{
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SimulationAmbientContextConfiguration NewAmbient(out SimulationRuntimeIdentity runtime, string? node = null)
    {
        var token = SimulationRuntimeActivation.CreateToken();
        runtime = new SimulationRuntimeIdentity(Guid.NewGuid(), 1, "compat-test");
        return new SimulationAmbientContextConfiguration(
            token,
            runtime,
            node is null ? null : new SimulationNodeIdentity(node));
    }

    private static SingleThreadedGuard BatonAwareGuard(ControlledOperationScheduler scheduler) =>
        new(() => scheduler.IsSimulationThread
            ? ControlledOperationScheduler.SimulationLogicalThreadOwnerId
            : Environment.CurrentManagedThreadId);

    [Fact]
    public void ControlledQueueRunsItemsInTheSameOrderAsTheInlineQueue()
    {
        var ambient = NewAmbient(out var runtime);

        var inlineOrder = new List<int>();
        var inlineQueue = new SimulationTaskQueue(new SimulationClock(Start), new SingleThreadedGuard(), ambient);
        for (var i = 0; i < 10; i++)
        {
            var captured = i;
            inlineQueue.Enqueue(new ScheduledActionItem(() => inlineOrder.Add(captured)));
        }

        var inlineRun = inlineQueue.RunUntilIdle();

        using var scheduler = new ControlledOperationScheduler(ambient.ActivationToken, runtime);
        var controlledOrder = new List<int>();
        var controlledQueue = new SimulationTaskQueue(new SimulationClock(Start), BatonAwareGuard(scheduler), ambient, scheduler);
        for (var i = 0; i < 10; i++)
        {
            var captured = i;
            controlledQueue.Enqueue(new ScheduledActionItem(() => controlledOrder.Add(captured)));
        }

        var controlledRun = controlledQueue.RunUntilIdle();

        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], inlineOrder);
        Assert.Equal(inlineOrder, controlledOrder);
        Assert.Equal(inlineRun, controlledRun);
    }

    [Fact]
    public void NestedEnqueuesRunInTheSameRelativeOrderInBothModes()
    {
        var ambient = NewAmbient(out var runtime);

        static void Seed(SimulationTaskQueue queue, List<string> log)
        {
            queue.Enqueue(new ScheduledActionItem(() =>
            {
                log.Add("outer-a");
                queue.Enqueue(new ScheduledActionItem(() => log.Add("inner-a")));
            }));
            queue.Enqueue(new ScheduledActionItem(() =>
            {
                log.Add("outer-b");
                queue.Enqueue(new ScheduledActionItem(() => log.Add("inner-b")));
            }));
        }

        var inlineLog = new List<string>();
        var inlineQueue = new SimulationTaskQueue(new SimulationClock(Start), new SingleThreadedGuard(), ambient);
        Seed(inlineQueue, inlineLog);
        inlineQueue.RunUntilIdle();

        using var scheduler = new ControlledOperationScheduler(ambient.ActivationToken, runtime);
        var controlledLog = new List<string>();
        var controlledQueue = new SimulationTaskQueue(new SimulationClock(Start), BatonAwareGuard(scheduler), ambient, scheduler);
        Seed(controlledQueue, controlledLog);
        controlledQueue.RunUntilIdle();

        Assert.Equal(["outer-a", "outer-b", "inner-a", "inner-b"], inlineLog);
        Assert.Equal(inlineLog, controlledLog);
    }

    [Fact]
    public void ControlledItemBodyObservesAmbientRuntimeNodeAndANonNullLogicalIdentity()
    {
        var ambient = NewAmbient(out var runtime, node: "node-x");

        using var scheduler = new ControlledOperationScheduler(ambient.ActivationToken, runtime);
        var queue = new SimulationTaskQueue(new SimulationClock(Start), BatonAwareGuard(scheduler), ambient, scheduler);

        SimulationExecutionSnapshot? observed = null;
        queue.Enqueue(new ScheduledActionItem(() => observed = SimulationExecutionContext.Current));
        queue.RunUntilIdle();

        Assert.NotNull(observed);
        Assert.Equal(runtime.Id, observed!.Runtime.Id);
        Assert.Equal("node-x", observed.Node?.Address);
        // The distinguishing feature of the controlled path: the item carries a real logical
        // execution identity assigned by the kernel, where the inline path would observe None.
        Assert.False(observed.LogicalExecutionId.IsNone);
    }

    [Fact]
    public void InlineItemBodyObservesNoLogicalIdentity()
    {
        var ambient = NewAmbient(out _, node: "node-x");
        var queue = new SimulationTaskQueue(new SimulationClock(Start), new SingleThreadedGuard(), ambient);

        SimulationExecutionSnapshot? observed = null;
        queue.Enqueue(new ScheduledActionItem(() => observed = SimulationExecutionContext.Current));
        queue.RunUntilIdle();

        Assert.NotNull(observed);
        Assert.True(observed!.LogicalExecutionId.IsNone);
    }

    [Fact]
    public void ExceptionFromControlledItemPropagatesOutOfRunOnceWithOriginalIdentity()
    {
        var ambient = NewAmbient(out var runtime);
        using var scheduler = new ControlledOperationScheduler(ambient.ActivationToken, runtime);
        var queue = new SimulationTaskQueue(new SimulationClock(Start), BatonAwareGuard(scheduler), ambient, scheduler);

        var boom = new InvalidOperationException("controlled-boom");
        queue.Enqueue(new ScheduledActionItem(() => throw boom));

        var thrown = Assert.Throws<InvalidOperationException>(() => queue.RunUntilIdle());
        Assert.Same(boom, thrown);
    }

    [Fact]
    public void ControlledQueueDrainsWithoutLeakingThreadsAfterManyItems()
    {
        var ambient = NewAmbient(out var runtime);
        using var scheduler = new ControlledOperationScheduler(ambient.ActivationToken, runtime);
        var queue = new SimulationTaskQueue(new SimulationClock(Start), BatonAwareGuard(scheduler), ambient, scheduler);

        var count = 0;
        for (var i = 0; i < 50; i++)
        {
            queue.Enqueue(new ScheduledActionItem(() => count++));
        }

        var ran = queue.RunUntilIdle();

        Assert.Equal(50, ran);
        Assert.Equal(50, count);
        // Every controlled operation reached a terminal state and its thread was reclaimed.
        Assert.Equal(0, scheduler.PendingOperationCount);
    }
}

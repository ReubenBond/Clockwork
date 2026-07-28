using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Tasks;

/// <summary>
/// <para>
/// The default <see cref="ISimulationTaskCoordinator"/>, backing all controlled async work for one
/// simulation runtime with a single <see cref="ControlledTaskLoop"/>. Because a simulation advances on
/// one logical thread, a single deterministic loop per runtime is the whole scheduler: continuations
/// from every node share it, so a cross-node await is just another readiness-gated entry on the same
/// loop and needs no special handling. Node identity flows through only as metadata for logging and
/// replay, never as a separate work queue.
/// </para>
/// <para>
/// A simulation host constructs one of these per runtime, registers it with
/// <see cref="SimulationTaskCoordination"/>, and pumps it - typically by driving the controlled root
/// task through <see cref="ISimulationTaskCoordinator.DrainUntil"/>. The coordinator itself performs no
/// locking: it inherits the loop's single-threaded discipline and must only be touched from the host's
/// logical drive thread.
/// </para>
/// </summary>
public sealed class ControlledTaskLoopCoordinator : ISimulationTaskCoordinator
{
    private readonly ControlledTaskLoop _loop;

    /// <summary>Initializes a new coordinator over a fresh <see cref="ControlledTaskLoop"/>.</summary>
    public ControlledTaskLoopCoordinator()
        : this(new ControlledTaskLoop())
    {
    }

    /// <summary>Initializes a new coordinator over the supplied loop, allowing the host to share or inspect it.</summary>
    /// <param name="loop">The loop that runs controlled continuations.</param>
    public ControlledTaskLoopCoordinator(ControlledTaskLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
    }

    /// <summary>Gets the underlying deterministic loop, for host drive loops and diagnostics.</summary>
    public ControlledTaskLoop Loop => _loop;

    /// <inheritdoc />
    public void Schedule(SimulationNodeIdentity? node, Action continuation) =>
        _loop.Schedule(continuation);

    /// <inheritdoc />
    public IControlledWorkRegistration ScheduleWhenReady(
        SimulationNodeIdentity? node,
        Func<bool> isReady,
        Action continuation) =>
        _loop.ScheduleWhenReady(isReady, continuation);

    /// <inheritdoc />
    public bool RunOne(SimulationNodeIdentity? node) => _loop.RunOnce();

    /// <inheritdoc />
    public void DrainUntil(SimulationNodeIdentity? node, Func<bool> completed) =>
        _loop.RunUntil(completed, "Clockwork.Runtime.Tasks synchronous wait");

    /// <inheritdoc />
    public IControlledTimeout RegisterTimeout(SimulationNodeIdentity? node, TimeSpan delay, Action? onElapsed) =>
        _loop.RegisterDeadline(delay, onElapsed);
}

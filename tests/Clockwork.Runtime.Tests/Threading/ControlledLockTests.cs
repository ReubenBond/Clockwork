using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

/// <summary>
/// Tests for <see cref="ControlledLock"/>, the controlled stand-in for <see cref="System.Threading.Lock"/>.
/// Inside a simulation the lock is modelled on the controlled monitor kernel (mutual exclusion,
/// reentrancy, <see cref="ControlledLock.Scope"/> disposal releasing exactly once); outside a simulation
/// it delegates to a real wrapped <see cref="System.Threading.Lock"/>.
/// </summary>
public sealed class ControlledLockTests
{
    [Fact]
    public void EnterScopeAcquiresAndDisposeReleases()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new ControlledLock();

            Assert.False(gate.IsHeldByCurrentThread);
            var scope = gate.EnterScope();
            Assert.True(gate.IsHeldByCurrentThread);
            scope.Dispose();
            Assert.False(gate.IsHeldByCurrentThread);
        });
    }

    [Fact]
    public void EnterExitTrackReentrancy()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new ControlledLock();

            gate.Enter();
            gate.Enter();
            gate.Exit();
            Assert.True(gate.IsHeldByCurrentThread);
            gate.Exit();
            Assert.False(gate.IsHeldByCurrentThread);
        });
    }

    [Fact]
    public void TryEnterContendedReturnsFalse()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new ControlledLock();
            var tookIt = true;

            gate.Enter();
            var contender = ControlledThread.Create(() => tookIt = gate.TryEnter());
            ControlledThread.Start(contender);
            ControlledThread.Join(contender);
            gate.Exit();

            Assert.False(tookIt);
        });
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new ControlledLock();
            var scope = gate.EnterScope();
            scope.Dispose();
            scope.Dispose();
            Assert.False(gate.IsHeldByCurrentThread);
        });
    }

    [Fact]
    public void OutsideSimulationDelegatesToRealLock()
    {
        var gate = new ControlledLock();

        using (gate.EnterScope())
        {
            Assert.True(gate.IsHeldByCurrentThread);
        }

        Assert.False(gate.IsHeldByCurrentThread);
    }
}

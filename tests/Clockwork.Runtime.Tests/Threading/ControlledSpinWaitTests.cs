using System;
using System.Threading;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

/// <summary>
/// Tests for the controlled <see cref="ControlledSpinWait"/> value type. Inside a simulation a spin never
/// burns CPU or consumes real time: <see cref="ControlledSpinWait.SpinOnce()"/> only advances the observable
/// spin count and <see cref="ControlledSpinWait.SpinUntil(Func{bool})"/> pumps the deterministic loop. Every
/// member delegates to a real <see cref="SpinWait"/> outside a simulation.
/// </summary>
public sealed class ControlledSpinWaitTests
{
    [Fact]
    public void SpinOnceAdvancesObservableCount()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var spin = new ControlledSpinWait();
            Assert.Equal(0, spin.Count);
            spin.SpinOnce();
            spin.SpinOnce();
            Assert.Equal(2, spin.Count);
            spin.Reset();
            Assert.Equal(0, spin.Count);
        });
    }

    [Fact]
    public void SpinOnceWithThresholdAdvancesCount()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var spin = new ControlledSpinWait();
            spin.SpinOnce(sleep1Threshold: -1);
            spin.SpinOnce(sleep1Threshold: 20);
            Assert.Equal(2, spin.Count);
        });
    }

    [Fact]
    public void SpinOnceRejectsThresholdBelowMinusOne()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var spin = new ControlledSpinWait();
            Assert.Throws<ArgumentOutOfRangeException>(() => spin.SpinOnce(-2));
        });
    }

    [Fact]
    public void NextSpinWillYieldFlipsPastThreshold()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var spin = new ControlledSpinWait();
            Assert.False(spin.NextSpinWillYield);
            for (int i = 0; i < 10; i++)
            {
                spin.SpinOnce();
            }

            Assert.True(spin.NextSpinWillYield);
        });
    }

    [Fact]
    public void SpinUntilReturnsWhenPredicateAlreadyHolds()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            ControlledSpinWait.SpinUntil(() => true);
            Assert.True(ControlledSpinWait.SpinUntil(() => true, 0));
            Assert.True(ControlledSpinWait.SpinUntil(() => true, TimeSpan.Zero));
        });
    }

    [Fact]
    public void SpinUntilZeroTimeoutDoesNotBlock()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.False(ControlledSpinWait.SpinUntil(() => false, 0));
        });
    }

    [Fact]
    public void SpinUntilFiniteTimeoutElapsesWhenPredicateNeverHolds()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.False(ControlledSpinWait.SpinUntil(() => false, 25));
            Assert.False(ControlledSpinWait.SpinUntil(() => false, TimeSpan.FromMilliseconds(25)));
        });
    }

    [Fact]
    public void SpinUntilRejectsInvalidTimeout()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ControlledSpinWait.SpinUntil(() => true, -2));
            Assert.Throws<ArgumentNullException>(() => ControlledSpinWait.SpinUntil(null!));
        });
    }

    [Fact]
    public void MembersDelegateToRealSpinWaitOutsideSimulation()
    {
        var spin = new ControlledSpinWait();
        Assert.Equal(0, spin.Count);
        spin.SpinOnce();
        Assert.True(spin.Count >= 0);
        spin.Reset();
        Assert.True(ControlledSpinWait.SpinUntil(() => true, 0));
        Assert.False(ControlledSpinWait.SpinUntil(() => false, 0));
    }
}

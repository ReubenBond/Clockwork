using System.Threading;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

public sealed class ControlledReaderWriterLockSlimTests
{
    [Fact]
    public void CreateSetsPolicyAndInitialProperties()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var @default = ControlledReaderWriterLockSlim.Create();
            var recursive = ControlledReaderWriterLockSlim.Create(LockRecursionPolicy.SupportsRecursion);

            Assert.Equal(LockRecursionPolicy.NoRecursion, ControlledReaderWriterLockSlim.RecursionPolicy(@default));
            Assert.Equal(LockRecursionPolicy.SupportsRecursion, ControlledReaderWriterLockSlim.RecursionPolicy(recursive));
            Assert.Equal(0, ControlledReaderWriterLockSlim.CurrentReadCount(@default));
            Assert.Equal(0, ControlledReaderWriterLockSlim.WaitingReadCount(@default));
            Assert.Equal(0, ControlledReaderWriterLockSlim.WaitingUpgradeCount(@default));
            Assert.Equal(0, ControlledReaderWriterLockSlim.WaitingWriteCount(@default));
            Assert.False(ControlledReaderWriterLockSlim.IsReadLockHeld(@default));
            Assert.False(ControlledReaderWriterLockSlim.IsUpgradeableReadLockHeld(@default));
            Assert.False(ControlledReaderWriterLockSlim.IsWriteLockHeld(@default));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ControlledReaderWriterLockSlim.Create((LockRecursionPolicy)42));
        });
    }

    [Fact]
    public void ConcurrentReadersOverlap()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var rw = ControlledReaderWriterLockSlim.Create();
            var observedConcurrentReadCount = 0;
            var second = ControlledThread.Create(() =>
            {
                ControlledReaderWriterLockSlim.EnterReadLock(rw);
                observedConcurrentReadCount = ControlledReaderWriterLockSlim.CurrentReadCount(rw);
                ControlledReaderWriterLockSlim.ExitReadLock(rw);
            });

            ControlledReaderWriterLockSlim.EnterReadLock(rw);
            ControlledThread.Start(second);
            ControlledThread.Join(second);
            ControlledReaderWriterLockSlim.ExitReadLock(rw);
            Assert.Equal(2, observedConcurrentReadCount);
            Assert.Equal(0, ControlledReaderWriterLockSlim.CurrentReadCount(rw));
        });
    }

    [Fact]
    public void WriterWaitsForReaderAndBlocksLaterReaders()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var rw = ControlledReaderWriterLockSlim.Create();
            var writerEntered = true;
            var writer = ControlledThread.Create(
                () => writerEntered = ControlledReaderWriterLockSlim.TryEnterWriteLock(rw, TimeSpan.Zero));

            ControlledReaderWriterLockSlim.EnterReadLock(rw);
            ControlledThread.Start(writer);
            ControlledThread.Join(writer);
            Assert.False(writerEntered);
            Assert.Equal(0, ControlledReaderWriterLockSlim.WaitingWriteCount(rw));

            ControlledReaderWriterLockSlim.ExitReadLock(rw);
            ControlledReaderWriterLockSlim.EnterWriteLock(rw);
            Assert.True(ControlledReaderWriterLockSlim.IsWriteLockHeld(rw));
            ControlledReaderWriterLockSlim.ExitWriteLock(rw);
        });
    }

    [Fact]
    public void UpgradeableOwnerCanUpgradeAndDowngrade()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var rw = ControlledReaderWriterLockSlim.Create();
            ControlledReaderWriterLockSlim.EnterUpgradeableReadLock(rw);
            ControlledReaderWriterLockSlim.EnterWriteLock(rw);

            Assert.True(ControlledReaderWriterLockSlim.IsUpgradeableReadLockHeld(rw));
            Assert.True(ControlledReaderWriterLockSlim.IsWriteLockHeld(rw));
            var blockedReader = false;
            var readerWhileWriting = ControlledThread.Create(() =>
                blockedReader = ControlledReaderWriterLockSlim.TryEnterReadLock(rw, 0));
            ControlledThread.Start(readerWhileWriting);
            ControlledThread.Join(readerWhileWriting);
            Assert.False(blockedReader);

            ControlledReaderWriterLockSlim.ExitWriteLock(rw);
            Assert.True(ControlledReaderWriterLockSlim.IsUpgradeableReadLockHeld(rw));
            Assert.False(ControlledReaderWriterLockSlim.IsWriteLockHeld(rw));

            var reader = ControlledThread.Create(() =>
            {
                Assert.True(ControlledReaderWriterLockSlim.TryEnterReadLock(rw, TimeSpan.Zero));
                ControlledReaderWriterLockSlim.ExitReadLock(rw);
            });
            ControlledThread.Start(reader);
            ControlledThread.Join(reader);
            ControlledReaderWriterLockSlim.ExitUpgradeableReadLock(rw);
        });
    }

    [Fact]
    public void RecursionPoliciesAndCountsAreObserved()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var noRecursion = ControlledReaderWriterLockSlim.Create();
            ControlledReaderWriterLockSlim.EnterReadLock(noRecursion);
            Assert.Throws<LockRecursionException>(() => ControlledReaderWriterLockSlim.EnterReadLock(noRecursion));
            ControlledReaderWriterLockSlim.ExitReadLock(noRecursion);

            var recursive = ControlledReaderWriterLockSlim.Create(LockRecursionPolicy.SupportsRecursion);
            ControlledReaderWriterLockSlim.EnterReadLock(recursive);
            ControlledReaderWriterLockSlim.EnterReadLock(recursive);
            Assert.Equal(2, ControlledReaderWriterLockSlim.RecursiveReadCount(recursive));
            ControlledReaderWriterLockSlim.ExitReadLock(recursive);
            ControlledReaderWriterLockSlim.ExitReadLock(recursive);

            ControlledReaderWriterLockSlim.EnterUpgradeableReadLock(recursive);
            ControlledReaderWriterLockSlim.EnterUpgradeableReadLock(recursive);
            Assert.Equal(2, ControlledReaderWriterLockSlim.RecursiveUpgradeCount(recursive));
            ControlledReaderWriterLockSlim.ExitUpgradeableReadLock(recursive);
            ControlledReaderWriterLockSlim.ExitUpgradeableReadLock(recursive);

            ControlledReaderWriterLockSlim.EnterWriteLock(recursive);
            ControlledReaderWriterLockSlim.EnterWriteLock(recursive);
            Assert.Equal(2, ControlledReaderWriterLockSlim.RecursiveWriteCount(recursive));
            ControlledReaderWriterLockSlim.ExitWriteLock(recursive);
            ControlledReaderWriterLockSlim.ExitWriteLock(recursive);
        });
    }

    [Fact]
    public void WaitingCountsIncludeReadAndUpgradeableWaiters()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var rw = ControlledReaderWriterLockSlim.Create();
            ControlledReaderWriterLockSlim.EnterWriteLock(rw);
            var reader = ControlledThread.Create(() =>
            {
                ControlledReaderWriterLockSlim.EnterReadLock(rw);
                ControlledReaderWriterLockSlim.ExitReadLock(rw);
            });
            var upgrader = ControlledThread.Create(() =>
            {
                ControlledReaderWriterLockSlim.EnterUpgradeableReadLock(rw);
                ControlledReaderWriterLockSlim.ExitUpgradeableReadLock(rw);
            });

            ControlledThread.Start(reader);
            ControlledThread.Start(upgrader);
            Pump();
            Assert.Equal(1, ControlledReaderWriterLockSlim.WaitingReadCount(rw));
            Assert.Equal(1, ControlledReaderWriterLockSlim.WaitingUpgradeCount(rw));
            ControlledReaderWriterLockSlim.ExitWriteLock(rw);
            ControlledThread.Join(reader);
            ControlledThread.Join(upgrader);
        });
    }

    [Fact]
    public void TryEnterHonorsZeroAndFiniteTimeouts()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var rw = ControlledReaderWriterLockSlim.Create();
            ControlledReaderWriterLockSlim.EnterReadLock(rw);
            var zeroResult = true;
            var finiteResult = false;
            var zero = ControlledThread.Create(() => zeroResult = ControlledReaderWriterLockSlim.TryEnterWriteLock(rw, 0));
            var finite = ControlledThread.Create(() =>
            {
                finiteResult = ControlledReaderWriterLockSlim.TryEnterWriteLock(rw, TimeSpan.FromMilliseconds(50));
                if (finiteResult)
                {
                    ControlledReaderWriterLockSlim.ExitWriteLock(rw);
                }
            });

            ControlledThread.Start(zero);
            ControlledThread.Join(zero);
            Assert.False(zeroResult);

            ControlledThread.Start(finite);
            ControlledReaderWriterLockSlim.ExitReadLock(rw);
            ControlledThread.Join(finite);
            Assert.True(finiteResult);

            ControlledReaderWriterLockSlim.EnterReadLock(rw);
            var timedOut = true;
            var timeout = ControlledThread.Create(
                () => timedOut = ControlledReaderWriterLockSlim.TryEnterWriteLock(rw, 1));
            ControlledThread.Start(timeout);
            ControlledThread.Join(timeout);
            Assert.False(timedOut);
            ControlledReaderWriterLockSlim.ExitReadLock(rw);
        });
    }

    [Fact]
    public void InvalidTimeoutAndDisposalAreValidated()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var rw = ControlledReaderWriterLockSlim.Create();
            Assert.Throws<ArgumentOutOfRangeException>(() => ControlledReaderWriterLockSlim.TryEnterReadLock(rw, -2));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ControlledReaderWriterLockSlim.TryEnterReadLock(rw, TimeSpan.FromMilliseconds(-2)));

            ControlledReaderWriterLockSlim.Dispose(rw);
            Assert.Throws<ObjectDisposedException>(() => ControlledReaderWriterLockSlim.EnterReadLock(rw));
            ControlledReaderWriterLockSlim.Dispose(rw);
        });
    }

    [Fact]
    public void ExitRequiresMatchingOwnership()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var rw = ControlledReaderWriterLockSlim.Create();
            Assert.Throws<SynchronizationLockException>(() => ControlledReaderWriterLockSlim.ExitReadLock(rw));
            Assert.Throws<SynchronizationLockException>(() => ControlledReaderWriterLockSlim.ExitUpgradeableReadLock(rw));
            Assert.Throws<SynchronizationLockException>(() => ControlledReaderWriterLockSlim.ExitWriteLock(rw));
        });
    }

    [Fact]
    public void InactiveSimulationIsRejected()
    {
        var exception = Assert.ThrowsAny<Exception>(() => ControlledReaderWriterLockSlim.Create());
        SimulationNotActiveExceptionAssert.Equal(exception, "System.Threading.ReaderWriterLockSlim..ctor");
    }

    private static void Pump()
    {
        var timer = ControlledSemaphoreSlim.Create(0);
        Assert.False(ControlledSemaphoreSlim.Wait(timer, 1));
    }
}

using System.Reflection;
using System.Threading.Tasks;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// End-to-end conformance for the controlled <see cref="System.Threading.SemaphoreSlim"/> surface (Phase
/// 7A). The rule set redirects the constructors to the controlled <c>Create</c> factories and every
/// instance member (<c>CurrentCount</c>, the synchronous <c>Wait</c> overloads, the asynchronous
/// <c>WaitAsync</c> overloads, <c>Release</c>, <c>Dispose</c>) to receiver-first controlled shims whose
/// permit count and waiter set live on the single logical thread. <c>AvailableWaitHandle</c> is rejected
/// precisely until Phase 7B.
/// </summary>
public sealed class SemaphoreSlimConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace Conf { public static class SemaphoreProbe {
            // A synchronous Wait blocks until a controlled thread releases a permit.
            public static Task<int> WaitBlocksUntilRelease()
            {
                var s = new SemaphoreSlim(0, 2);
                var releaser = new Thread(() => s.Release());
                releaser.Start();
                s.Wait();               // pumps the loop; the releaser runs and hands over a permit
                releaser.Join();
                return Task.FromResult(s.CurrentCount);
            }

            // The zero-timeout Wait is a faithful non-blocking try.
            public static Task<bool> ZeroTimeoutTry()
            {
                var s = new SemaphoreSlim(1, 1);
                bool first = s.Wait(0);   // succeeds, count 1 -> 0
                bool second = s.Wait(0);  // fails, count 0
                return Task.FromResult(first && !second);
            }

            // WaitAsync completes once a controlled thread releases a permit.
            public static async Task<int> WaitAsyncCompletesOnRelease()
            {
                var s = new SemaphoreSlim(0, 2);
                var releaser = new Thread(() => s.Release());
                releaser.Start();
                await s.WaitAsync();
                releaser.Join();
                return s.CurrentCount;
            }

            // WaitAsync with an available permit completes synchronously.
            public static async Task<bool> WaitAsyncImmediate()
            {
                var s = new SemaphoreSlim(1, 1);
                await s.WaitAsync();
                bool zero = s.CurrentCount == 0;
                s.Release();
                return zero;
            }

            // Releasing beyond the maximum count throws SemaphoreFullException.
            public static Task<bool> ReleaseBeyondMaxThrows()
            {
                var s = new SemaphoreSlim(1, 1);
                try { s.Release(); return Task.FromResult(false); }
                catch (SemaphoreFullException) { return Task.FromResult(true); }
            }

            // AvailableWaitHandle is rejected until Phase 7B.
            public static Task<bool> AvailableWaitHandleRejected()
            {
                var s = new SemaphoreSlim(1, 1);
                try
                {
                    _ = s.AvailableWaitHandle;
                    return Task.FromResult(false);
                }
                catch (Exception ex) when (ex.GetType().Name.Contains("Unsupported"))
                {
                    return Task.FromResult(true);
                }
            }
        } }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _probe;

    public SemaphoreSlimConformanceTests() =>
        _probe = new Lazy<StagedProbe>(() =>
            _fixture.StageControlledTasks("Conf.Semaphore", "Conf.SemaphoreProbe", Source, optimize: true));

    [Fact]
    public void WaitBlocksUntilRelease()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("WaitBlocksUntilRelease"))!;
        Assert.Equal(0, Result<int>(task));
    }

    [Fact]
    public void ZeroTimeoutTry()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("ZeroTimeoutTry"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public void WaitAsyncCompletesOnRelease()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<int>)host.Invoke(Method("WaitAsyncCompletesOnRelease"))!;
        Assert.Equal(0, Result<int>(task));
    }

    [Fact]
    public void WaitAsyncImmediate()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("WaitAsyncImmediate"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public void ReleaseBeyondMaxThrows()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("ReleaseBeyondMaxThrows"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public void AvailableWaitHandleRejected()
    {
        using var host = new SimulationHost(Start);
        var task = (Task<bool>)host.Invoke(Method("AvailableWaitHandleRejected"))!;
        Assert.True(Result<bool>(task));
    }

    [Fact]
    public async Task RewrittenSemaphoreDelegatesToRealBclOutsideAnySimulation()
    {
        var task = (Task<bool>)Method("ZeroTimeoutTry").Invoke(null, null)!;
        Assert.True(await task);
    }

    private MethodInfo Method(string name) => _probe.Value.Method(name);

    private static T Result<T>(object task)
    {
        var typed = (Task)task;
        Assert.True(typed.IsCompleted);
        PropertyInfo result = typed.GetType().GetProperty("Result")!;
        return (T)result.GetValue(typed)!;
    }

    public void Dispose() => _fixture.Dispose();
}

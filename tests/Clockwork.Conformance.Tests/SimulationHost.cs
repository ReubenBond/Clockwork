using System.Reflection;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// A minimal single- or multi-node <see cref="SimulationCluster"/> wrapper that invokes an instrumented
/// probe method as node work (so the ambient simulation runtime and its deterministic environment are
/// active) and returns the result, unwrapping reflection exceptions so tests can assert on the real
/// shim exception types.
/// </summary>
internal sealed class SimulationHost : IDisposable
{
    private readonly SimulationCluster _cluster;
    private readonly Dictionary<string, SimulationNodeHandle<object?>> _nodes = new(StringComparer.Ordinal);
    private readonly string _defaultAddress;

    public SimulationHost(
        DateTimeOffset start,
        int seed = 1,
        TimeZoneInfo? timeZone = null,
        CryptoRandomnessPolicy cryptoPolicy = CryptoRandomnessPolicy.Reject,
        IReadOnlyList<string>? nodeAddresses = null)
    {
        IReadOnlyList<string> addresses = nodeAddresses ?? ["node"];
        _defaultAddress = addresses[0];

        var builder = new SimulationBuilder()
            .WithSeed(seed)
            .WithStartDateTime(start)
            .WithCryptoRandomnessPolicy(cryptoPolicy);
        if (timeZone is not null)
        {
            builder = builder.WithTimeZone(timeZone);
        }

        foreach (string address in addresses)
        {
            _nodes[address] = builder.AddNode(address);
        }

        _cluster = builder.Build();
    }

    /// <summary>Invokes the probe method as work on the default node.</summary>
    public object? Invoke(MethodInfo method, params object?[] args) => InvokeOnNode(_defaultAddress, method, args);

    /// <summary>Invokes the probe method as work on the named node.</summary>
    public object? InvokeOnNode(string address, MethodInfo method, params object?[] args)
    {
        SimulationNodeHandle<object?> node = _nodes[address];

        object? result = null;
        Exception? error = null;
        node.Context.SchedulerLane.EnqueueAfter(
            () =>
            {
                try
                {
                    result = method.Invoke(null, args.Length == 0 ? null : args);
                }
                catch (TargetInvocationException ex)
                {
                    error = ex.InnerException ?? ex;
                }
            },
            TimeSpan.Zero);

        _cluster.RunUntilIdle();

        if (error is not null)
        {
            throw error;
        }

        return result;
    }

    /// <summary>
    /// Invokes the probe as node work and then enqueues <paramref name="afterWork"/> as further node
    /// work (each at the same logical instant, in order), so a probe that awaits an initially-incomplete
    /// task suspends first and is only resumed once the later work completes its antecedents. Everything
    /// runs on the single logical thread; a single <c>RunUntilIdle</c> drains the
    /// probe, the completions, and every controlled continuation deterministically.
    /// </summary>
    public object? InvokeWithWork(MethodInfo method, object?[] args, params Action[] afterWork)
    {
        SimulationNodeHandle<object?> node = _nodes[_defaultAddress];

        object? result = null;
        Exception? error = null;
        node.Context.SchedulerLane.EnqueueAfter(
            () =>
            {
                try
                {
                    result = method.Invoke(null, args.Length == 0 ? null : args);
                }
                catch (TargetInvocationException ex)
                {
                    error = ex.InnerException ?? ex;
                }
            },
            TimeSpan.Zero);

        foreach (Action work in afterWork)
        {
            Action captured = work;
            node.Context.SchedulerLane.EnqueueAfter(captured, TimeSpan.Zero);
        }

        _cluster.RunUntilIdle();

        if (error is not null)
        {
            throw error;
        }

        return result;
    }

    public void Dispose() => _cluster.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

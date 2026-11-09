using System.Threading;
using Clockwork.Runtime.Shims;
using Clockwork.Shims.System.Runtime.CompilerServices;

namespace Clockwork.Shims.System.Threading.Tasks;

/// <summary>
/// Controlled <see cref="global::System.Threading.Tasks.TaskAsyncEnumerableExtensions"/>. The rewriter redirects the
/// <c>ConfigureAwait</c> and <c>WithCancellation</c> extension calls here; each constructs the controlled configured
/// disposable or enumerable with the same arguments the BCL passes to its configured types, so <c>await using</c> and
/// <c>await foreach</c> resume through the coordinator.
/// </summary>
public static class ControlledTaskAsyncEnumerableExtensions
{
    private const string ApiName = "System.Threading.Tasks.TaskAsyncEnumerableExtensions";

    /// <summary>Controlled <c>TaskAsyncEnumerableExtensions.ConfigureAwait(IAsyncDisposable, bool)</c>.</summary>
    /// <param name="source">The source async disposable.</param>
    /// <param name="continueOnCapturedContext">The requested continuation-context behaviour.</param>
    /// <returns>The configured async disposable.</returns>
    public static ControlledConfiguredAsyncDisposable ConfigureAwait(IAsyncDisposable source, bool continueOnCapturedContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName + ".ConfigureAwait");
        return new(source, continueOnCapturedContext);
    }

    /// <summary>Controlled <c>TaskAsyncEnumerableExtensions.ConfigureAwait&lt;T&gt;(IAsyncEnumerable&lt;T&gt;, bool)</c>.</summary>
    /// <typeparam name="T">The type of the values being iterated.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="continueOnCapturedContext">The requested continuation-context behaviour.</param>
    /// <returns>The configured enumerable, with no cancellation token.</returns>
    public static ControlledConfiguredCancelableAsyncEnumerable<T> ConfigureAwait<T>(
        IAsyncEnumerable<T> source,
        bool continueOnCapturedContext)
        where T : allows ref struct
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName + ".ConfigureAwait");
        return new(source, continueOnCapturedContext, cancellationToken: default);
    }

    /// <summary>Controlled <c>TaskAsyncEnumerableExtensions.WithCancellation&lt;T&gt;(IAsyncEnumerable&lt;T&gt;, CancellationToken)</c>.</summary>
    /// <typeparam name="T">The type of the values being iterated.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="cancellationToken">The token passed to <see cref="IAsyncEnumerable{T}.GetAsyncEnumerator(CancellationToken)"/>.</param>
    /// <returns>The configured enumerable, continuing on the captured context as the BCL default does.</returns>
    public static ControlledConfiguredCancelableAsyncEnumerable<T> WithCancellation<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken)
        where T : allows ref struct
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(ApiName + ".WithCancellation");
        return new(source, continueOnCapturedContext: true, cancellationToken);
    }
}

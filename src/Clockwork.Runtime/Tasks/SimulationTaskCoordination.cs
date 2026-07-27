using System.Collections.Concurrent;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Tasks;

/// <summary>
/// <para>
/// The process-wide registry that maps an active simulation runtime (by
/// <see cref="SimulationRuntimeIdentity.Id"/>) to the <see cref="ISimulationTaskCoordinator"/> the
/// controlled async/task machinery routes continuations and synchronous waits through. This is the
/// async-scheduling sibling of <see cref="Clockwork.Runtime.Shims.SimulationRuntimeServices"/>: a host
/// registers exactly one coordinator per runtime it activates, and the controlled builders/awaiters/
/// task shims resolve the coordinator for the ambient runtime via <see cref="ControlledTaskRuntime"/>.
/// </para>
/// <para>
/// Registration is capability-gated the same way ambient activation is: it requires a
/// <see cref="SimulationActivationToken"/>, which only the simulation host can mint. Production
/// application code and third-party libraries cannot obtain a token and therefore cannot register - or
/// replace - a coordinator. Keying by runtime <see cref="Guid"/> lets multiple simulations run in the
/// same process (parallel tests, multiple clusters) without colliding.
/// </para>
/// </summary>
public static class SimulationTaskCoordination
{
    private static readonly ConcurrentDictionary<Guid, ISimulationTaskCoordinator> Coordinators = new();

    /// <summary>
    /// Registers <paramref name="coordinator"/> for <paramref name="runtime"/>. The returned scope
    /// unregisters it on disposal; the host should dispose it when the simulation ends.
    /// </summary>
    /// <param name="token">The activation token proving the caller is the simulation host.</param>
    /// <param name="runtime">The runtime the coordinator serves.</param>
    /// <param name="coordinator">The coordinator the controlled machinery will dispatch to.</param>
    /// <returns>A disposable registration scope that unregisters the coordinator when disposed.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if a coordinator is already registered for <paramref name="runtime"/>.
    /// </exception>
    public static IDisposable Register(
        SimulationActivationToken token,
        SimulationRuntimeIdentity runtime,
        ISimulationTaskCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(coordinator);

        if (!Coordinators.TryAdd(runtime.Id, coordinator))
        {
            throw new InvalidOperationException(
                $"A task coordinator is already registered for simulation runtime '{runtime.Id}'. " +
                "Each runtime may register exactly one coordinator.");
        }

        return new Registration(runtime.Id, coordinator);
    }

    /// <summary>Attempts to resolve the coordinator registered for the given runtime.</summary>
    /// <param name="runtime">The runtime to resolve a coordinator for.</param>
    /// <param name="coordinator">The registered coordinator, if any.</param>
    /// <returns><see langword="true"/> if a coordinator is registered for the runtime.</returns>
    public static bool TryGet(SimulationRuntimeIdentity runtime, out ISimulationTaskCoordinator? coordinator)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var found = Coordinators.TryGetValue(runtime.Id, out var value);
        coordinator = value;
        return found;
    }

    private sealed class Registration(Guid runtimeId, ISimulationTaskCoordinator coordinator) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Only remove the exact coordinator this registration installed, so an out-of-order dispose
            // can never tear down a different runtime's later registration.
            ((ICollection<KeyValuePair<Guid, ISimulationTaskCoordinator>>)Coordinators)
                .Remove(new KeyValuePair<Guid, ISimulationTaskCoordinator>(runtimeId, coordinator));
        }
    }
}

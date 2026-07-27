using System.Collections.Concurrent;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>
/// <para>
/// The process-wide registry that maps an active simulation runtime (by
/// <see cref="SimulationRuntimeIdentity.Id"/>) to the <see cref="ISimulationRuntimeEnvironment"/> the
/// deterministic BCL shims dispatch to. A simulation host registers exactly one environment per
/// runtime it activates; the deterministic shims resolve the environment for the ambient runtime via
/// <see cref="SimulationRuntimeDispatch"/>.
/// </para>
/// <para>
/// Registration is capability-gated the same way ambient activation is: it requires a
/// <see cref="SimulationActivationToken"/>, which only the simulation host can mint (see
/// <see cref="SimulationActivationToken"/>). Production application code and third-party libraries
/// cannot obtain a token and therefore cannot register - or replace - a runtime environment.
/// </para>
/// <para>
/// The registry is keyed by runtime <see cref="Guid"/> rather than by ambient state so that multiple
/// simulations can be active in the same process (e.g. parallel tests) without colliding; each shim
/// dispatch reads the ambient runtime and looks up that runtime's environment.
/// </para>
/// </summary>
public static class SimulationRuntimeServices
{
    private static readonly ConcurrentDictionary<Guid, ISimulationRuntimeEnvironment> Environments = new();

    /// <summary>
    /// Registers the deterministic <paramref name="environment"/> for the given
    /// <paramref name="runtime"/>. The returned scope unregisters it on disposal; the host should
    /// dispose it when the simulation ends.
    /// </summary>
    /// <param name="token">
    /// The activation token proving the caller is the simulation host. Cannot be forged or defaulted
    /// (see <see cref="SimulationActivationToken"/>).
    /// </param>
    /// <param name="runtime">The runtime the environment serves.</param>
    /// <param name="environment">The deterministic environment the shims will dispatch to.</param>
    /// <returns>A disposable registration scope that unregisters the environment when disposed.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if an environment is already registered for <paramref name="runtime"/>.
    /// </exception>
    public static IDisposable Register(
        SimulationActivationToken token,
        SimulationRuntimeIdentity runtime,
        ISimulationRuntimeEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(environment);

        if (!Environments.TryAdd(runtime.Id, environment))
        {
            throw new InvalidOperationException(
                $"A deterministic runtime environment is already registered for simulation runtime " +
                $"'{runtime.Id}'. Each runtime may register exactly one environment.");
        }

        return new Registration(runtime.Id, environment);
    }

    /// <summary>
    /// Attempts to resolve the deterministic environment registered for the given runtime.
    /// </summary>
    /// <param name="runtime">The runtime to resolve an environment for.</param>
    /// <param name="environment">The registered environment, if any.</param>
    /// <returns><see langword="true"/> if an environment is registered for the runtime.</returns>
    public static bool TryGet(SimulationRuntimeIdentity runtime, out ISimulationRuntimeEnvironment? environment)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var found = Environments.TryGetValue(runtime.Id, out var value);
        environment = value;
        return found;
    }

    private sealed class Registration(Guid runtimeId, ISimulationRuntimeEnvironment environment) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Only remove the exact environment this registration installed, so an out-of-order
            // dispose can never tear down a different runtime's later registration.
            ((ICollection<KeyValuePair<Guid, ISimulationRuntimeEnvironment>>)Environments)
                .Remove(new KeyValuePair<Guid, ISimulationRuntimeEnvironment>(runtimeId, environment));
        }
    }
}

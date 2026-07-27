using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>
/// Thrown by a deterministic BCL shim when a simulation is active on the calling logical call context
/// but no <see cref="ISimulationRuntimeEnvironment"/> has been registered for the active runtime (see
/// <see cref="SimulationRuntimeServices"/>). This is the deliberate "fail explicitly" behaviour
/// required by the deterministic instrumentation contract: while a simulation is active, rewritten
/// code must never silently fall back to real wall-clock time or real randomness. A shim raising this
/// exception means the closure was instrumented and entered a simulation, but the host forgot to
/// register the clock/random/identity services the shim needs.
/// </summary>
public sealed class SimulationServiceMissingException : SimulationShimException
{
    /// <summary>Initializes a new instance of the <see cref="SimulationServiceMissingException"/> class.</summary>
    /// <param name="runtime">The active simulation runtime that has no environment registered.</param>
    /// <param name="apiName">The fully-qualified name of the controlled API that could not be served.</param>
    public SimulationServiceMissingException(SimulationRuntimeIdentity runtime, string apiName)
        : base(BuildMessage(runtime, apiName))
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Runtime = runtime;
        ApiName = apiName;
    }

    /// <summary>Gets the active simulation runtime that has no deterministic environment registered.</summary>
    public SimulationRuntimeIdentity Runtime { get; }

    /// <summary>Gets the fully-qualified name of the controlled API the shim was serving.</summary>
    public string ApiName { get; }

    private static string BuildMessage(SimulationRuntimeIdentity runtime, string apiName)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return
            $"The deterministic shim for '{apiName}' cannot run: simulation runtime '{runtime.Id}' " +
            $"(seed {runtime.Seed}{(runtime.Description is null ? string.Empty : $", '{runtime.Description}'")}) " +
            "is active on this logical call context, but no deterministic runtime environment is " +
            "registered for it via SimulationRuntimeServices. Rewritten code must not silently use " +
            "real time or real randomness inside a simulation; register an ISimulationRuntimeEnvironment " +
            "before running instrumented code under this runtime.";
    }
}

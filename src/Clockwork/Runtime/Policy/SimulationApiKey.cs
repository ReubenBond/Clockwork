namespace Clockwork.Runtime.Policy;

/// <summary>
/// Identifies a single API for the purposes of <see cref="SimulationApiPolicyRegistry"/>
/// per-API overrides. Equality is exact/ordinal on both components - callers are responsible for
/// using a consistent, stable naming convention (e.g. <c>"System.Net.Http"</c> /
/// <c>"HttpClient.SendAsync"</c>) since this is a plain identifier, not a reflection-based lookup.
/// </summary>
/// <param name="AssemblyName">The simple name of the assembly the API belongs to (e.g. <c>"System.Net.Http"</c>).</param>
/// <param name="ApiName">A stable identifier for the API within that assembly (e.g. <c>"HttpClient.SendAsync"</c>).</param>
public readonly record struct SimulationApiKey(string AssemblyName, string ApiName)
{
    /// <inheritdoc/>
    public override string ToString() => $"{AssemblyName}::{ApiName}";
}

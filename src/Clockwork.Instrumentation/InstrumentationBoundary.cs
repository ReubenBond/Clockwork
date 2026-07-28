namespace Clockwork.Instrumentation;

/// <summary>
/// Marker type documenting this project's deterministic instrumentation role. The project contains
/// Mono.Cecil rewrite rules and passes used by controlled and race-exploration execution modes and
/// depends on Clockwork.Runtime for replacement APIs. See docs/compatibility.md for the supported boundary.
/// </summary>
public static class InstrumentationBoundary
{
    /// <summary>
    /// Anchors a real compile-time and metadata dependency on Clockwork.Runtime, matching the
    /// intended dependency direction.
    /// </summary>
    public static readonly Type RuntimeDependency = typeof(Runtime.RuntimeBoundary);
}

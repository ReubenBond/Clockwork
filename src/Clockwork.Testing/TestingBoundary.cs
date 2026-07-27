namespace Clockwork.Testing;

/// <summary>
/// Marker type documenting this project's intended future purpose: reusable test helpers and
/// fixtures (e.g., cluster/network scenario builders, deterministic assertion helpers) for
/// consumers building on Clockwork. Depends on Clockwork.Runtime. Placeholder for now.
/// </summary>
public static class TestingBoundary
{
    /// <summary>
    /// Anchors a real compile-time and metadata dependency on Clockwork.Runtime, matching the
    /// intended dependency direction, so the reference survives even though no test helpers
    /// exist yet.
    /// </summary>
    public static readonly Type RuntimeDependency = typeof(Runtime.RuntimeBoundary);
}

namespace Clockwork.Testing;

/// <summary>
/// Assembly marker for Clockwork's framework-neutral deterministic testing helpers.
/// </summary>
public static class TestingBoundary
{
    /// <summary>
    /// Anchors the runtime dependency used by replay-aware fixtures.
    /// </summary>
    public static readonly Type RuntimeDependency = typeof(Runtime.RuntimeBoundary);
}

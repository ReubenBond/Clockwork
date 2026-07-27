namespace Clockwork.Runtime;

/// <summary>
/// Marker type documenting this project's intended future purpose: the deterministic
/// simulation kernel (currently in the root Clockwork.csproj / Clockwork.Simulation package)
/// will migrate here so that Clockwork.Instrumentation, Clockwork.Hosting, Clockwork.Http, and
/// Clockwork.Testing can depend on the kernel without depending on each other.
/// See docs/compatibility.md for the overall roadmap.
/// </summary>
public static class RuntimeBoundary
{
}

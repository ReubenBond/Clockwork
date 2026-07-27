namespace Clockwork.Analyzers;

/// <summary>
/// Marker type documenting this project's intended future purpose: Roslyn analyzers that flag
/// determinism violations described in the README's "Determinism requirements" section (e.g.,
/// <c>Task.Run</c>, <c>Random.Shared</c>, wall-clock APIs). Placeholder for now - no analyzer
/// implementation or Microsoft.CodeAnalysis dependency yet.
/// </summary>
internal static class AnalyzerBoundary
{
}

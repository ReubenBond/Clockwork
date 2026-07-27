using System.Collections.Immutable;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>
/// Configures a single rewrite: where to resolve replacement ("shim") assemblies and additional
/// references, the target runtime the rules are evaluated against, type-name exclusions, and symbol
/// handling. Options are immutable; use the <c>with</c> expression to derive variants.
/// </summary>
public sealed record RewriteOptions
{
    /// <summary>
    /// Gets the paths of the replacement assemblies that declare the members rules redirect to. The
    /// engine loads these to import replacement references into the rewritten assembly.
    /// </summary>
    public ImmutableArray<string> ReplacementAssemblyPaths { get; init; } = [];

    /// <summary>Gets additional directories searched when resolving assembly references.</summary>
    public ImmutableArray<string> ReferenceSearchDirectories { get; init; } = [];

    /// <summary>
    /// Gets the target runtime version the rules are evaluated against. Rules whose
    /// <see cref="Rules.RewriteRule.SupportedRuntimes"/> exclude this version are diagnosed rather
    /// than silently skipped. <see langword="null"/> disables runtime filtering.
    /// </summary>
    public Version? TargetRuntime { get; init; }

    /// <summary>
    /// Gets the Cecil full names of types excluded from rewriting (in addition to types marked with
    /// <see cref="Attributes.DoNotRewriteAttribute"/>).
    /// </summary>
    public ImmutableArray<string> ExcludedTypeFullNames { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether an unresolved <em>optional</em> assembly reference (one not
    /// targeted by any rule) is reported as a warning. Unresolved <em>targeted</em> members always
    /// fail regardless of this setting. Defaults to <see langword="true"/>.
    /// </summary>
    public bool WarnOnUnresolvedReferences { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to compute the SHA-256 of the written output for the manifest.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool ComputeOutputHash { get; init; } = true;
}

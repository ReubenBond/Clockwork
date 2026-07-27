namespace Clockwork.Instrumentation.Diagnostics;

/// <summary>
/// Stable, machine-readable identifiers for the diagnostics the rewrite engine can emit. Kept as
/// constants (rather than an enum) so they serialize as stable strings in the manifest and so new
/// ids can be added without renumbering. The <c>CWR</c> prefix denotes "Clockwork rewrite".
/// </summary>
public static class RewriteDiagnosticIds
{
    /// <summary>A targeted member matched by a rule could not be resolved to a replacement.</summary>
    public const string UnresolvedReplacement = "CWR0001";

    /// <summary>A targeted call site could not be rewritten because the rule's shape is unsupported.</summary>
    public const string UnsupportedTargetShape = "CWR0002";

    /// <summary>An assembly reference could not be resolved.</summary>
    public const string UnresolvedReference = "CWR0003";

    /// <summary>Debug symbols were requested but are absent for the input assembly.</summary>
    public const string SymbolsAbsent = "CWR0004";

    /// <summary>The input assembly uses a symbol form the engine cannot preserve (e.g. native/Windows PDB).</summary>
    public const string UnsupportedSymbolForm = "CWR0005";

    /// <summary>A type was excluded from rewriting (via <see cref="Attributes.DoNotRewriteAttribute"/> or options).</summary>
    public const string TypeExcluded = "CWR0006";

    /// <summary>The assembly was already rewritten with a matching signature; rewriting was a verified no-op.</summary>
    public const string AlreadyRewritten = "CWR0007";

    /// <summary>The assembly was rewritten with an incompatible engine or rule-set version.</summary>
    public const string IncompatibleRewriteVersion = "CWR0008";

    /// <summary>Post-rewrite validation (read-back / integrity check) failed.</summary>
    public const string ValidationFailed = "CWR0009";

    /// <summary>A rule did not apply because the configured target runtime is outside its supported range.</summary>
    public const string RuntimeOutOfRange = "CWR0010";

    /// <summary>A mixed-mode assembly cannot be rewritten by Mono.Cecil.</summary>
    public const string MixedModeAssembly = "CWR0011";
}

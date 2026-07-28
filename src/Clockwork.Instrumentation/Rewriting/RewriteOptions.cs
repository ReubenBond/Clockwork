using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

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

    /// <summary>
    /// Gets a value indicating whether to run the exception-hardening pass, which injects a guard at the
    /// start of every broad <c>catch (Exception)</c> / <c>catch</c> block and every exception <c>filter</c>
    /// so a rewritten assembly's user handlers cannot swallow the scheduler's internal control-flow signal.
    /// When enabled, the shim assembly declaring
    /// <c>Clockwork.Runtime.SimulationExceptionGuard.ThrowIfControlSignal</c> must be supplied in
    /// <see cref="ReplacementAssemblyPaths"/>. Defaults to <see langword="false"/> (the built-in
    /// controlled-task activation turns it on); narrow typed catches, finally blocks, rethrow-only
    /// handlers, and compiler-generated async-state-machine handlers are never instrumented, so normal
    /// application exception handling is unchanged. The closure runner enables this automatically
    /// when the effective rule set contains built-in controlled-task rules.
    /// </summary>
    public bool HardenExceptionHandlers { get; init; }

    /// <summary>
    /// Gets a value indicating whether to run the cross-assembly task-detection pass, which emits a
    /// <see cref="Diagnostics.RewriteDiagnosticIds.UncontrolledTaskReturn"/> warning at every call site that
    /// invokes a method in an uncontrolled assembly (one that is neither being rewritten, nor part of the
    /// BCL, nor the Clockwork runtime shim) and returns a <see cref="System.Threading.Tasks.Task"/>,
    /// <see cref="System.Threading.Tasks.ValueTask"/>, or other awaitable whose continuation could escape the
    /// deterministic scheduler. The escape is surfaced with a precise source/IL call-site diagnostic rather
    /// than being silently accepted. Defaults to <see langword="false"/> and is available to direct rewrite-engine
    /// callers; the closure runner does not enable it because a per-assembly pass cannot distinguish an excluded
    /// dependency from a sibling assembly that is rewritten in the same closure. BCL <c>System.*</c> calls are
    /// intentionally not flagged here.
    /// </summary>
    public bool DetectUncontrolledTasks { get; init; }

    /// <summary>
    /// Gets a value indicating whether the rewrite injects fine-grained scheduling and race-access
    /// instrumentation. Defaults to <see langword="false"/> so controlled mode has no memory or
    /// control-flow instrumentation overhead.
    /// </summary>
    public bool InstrumentRaceExploration { get; init; }

    /// <summary>
    /// Computes a canonical fingerprint of every option which can affect rewritten output,
    /// diagnostics, or manifest content. Set-like exclusions are sorted so equivalent orderings
    /// produce the same fingerprint; resolver path order is preserved because it controls precedence.
    /// </summary>
    public string ComputeSemanticFingerprint()
    {
        var canonical = new StringBuilder();
        AppendPaths(canonical, "replacement", ReplacementAssemblyPaths, sort: false);
        AppendPaths(canonical, "references", ReferenceSearchDirectories, sort: false);
        canonical.Append("runtime=").Append(TargetRuntime?.ToString() ?? "*").Append('\n');
        AppendPaths(canonical, "excluded", ExcludedTypeFullNames, sort: true);
        canonical.Append("warnUnresolved=").Append(WarnOnUnresolvedReferences).Append('\n');
        canonical.Append("outputHash=").Append(ComputeOutputHash).Append('\n');
        canonical.Append("hardenExceptions=").Append(HardenExceptionHandlers).Append('\n');
        canonical.Append("detectTasks=").Append(DetectUncontrolledTasks).Append('\n');
        canonical.Append("raceExploration=").Append(InstrumentRaceExploration).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendPaths(
        StringBuilder canonical,
        string name,
        ImmutableArray<string> values,
        bool sort)
    {
        IEnumerable<string> normalized = values
            .Select(static value => value.Replace('\\', '/'))
            .Where(static value => value.Length > 0);
        if (sort)
        {
            normalized = normalized.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal);
        }

        canonical.Append(name).Append('=').AppendJoin(',', normalized).Append('\n');
    }
}

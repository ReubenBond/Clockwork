using Clockwork.Instrumentation.Closure;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Orchestration;

/// <summary>
/// Checks, once every assembly of a closure is staged, that the member references into its rewritten assemblies still
/// bind. A rewrite substitutes the types in its assembly's member signatures, so a reference into that assembly from
/// code the closure does not rewrite - an assembly it copies, or a type excluded from rewriting - keeps the original
/// signature and no longer binds. Each such reference that mentions a substituted or substitute type is resolved
/// against the staged closure, and one that does not bind fails the run with
/// <see cref="RewriteDiagnosticIds.UnresolvedReplacement"/> instead of throwing <see cref="MissingMemberException"/>
/// when the runtime binds it.
/// </summary>
internal static class ClosureBindingValidator
{
    /// <summary>Checks the references of every staged non-framework managed assembly into the rewritten assemblies.</summary>
    /// <param name="plan">The closure plan.</param>
    /// <param name="stagingDirectory">The directory the closure is staged in.</param>
    /// <param name="frameworkClassifier">The classifier that kept framework assemblies out of the rewrite.</param>
    /// <param name="ruleSet">The rule set the closure is rewritten with.</param>
    /// <param name="targetRuntime">The target runtime the rules are evaluated against.</param>
    /// <param name="rewrittenAssemblyNames">The simple names of the assemblies the closure rewrites.</param>
    /// <returns>An error for every reference that does not bind.</returns>
    public static IReadOnlyList<RewriteDiagnostic> Validate(
        ClosurePlan plan,
        string stagingDirectory,
        FrameworkAssemblyClassifier frameworkClassifier,
        RewriteRuleSet ruleSet,
        Version? targetRuntime,
        IReadOnlyCollection<string> rewrittenAssemblyNames)
    {
        var diagnostics = new List<RewriteDiagnostic>();
        var matcher = new RewriteRuleMatcher(ruleSet, targetRuntime);
        IReadOnlySet<string> signatureTypes = matcher.GetSubstitutionTypeNames();
        if (signatureTypes.Count == 0 || rewrittenAssemblyNames.Count == 0)
        {
            return diagnostics;
        }

        var ruleIdByType = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, RewriteRule> entry in matcher.TypeSubstitutionRules)
        {
            ruleIdByType.TryAdd(entry.Key, entry.Value.Id);
            ruleIdByType.TryAdd(entry.Value.Replacement.DeclaringTypeFullName, entry.Value.Id);
        }

        var rewritten = new HashSet<string>(rewrittenAssemblyNames, StringComparer.OrdinalIgnoreCase);
        ClosureAsset[] candidates =
        [
            .. plan.Assets.Where(asset => asset.Kind == AssetKind.ManagedAssembly
                && !frameworkClassifier.IsFrameworkAssembly(Path.GetFileNameWithoutExtension(asset.RelativePath))),
        ];

        using var resolver = new DefaultAssemblyResolver();
        foreach (string directory in candidates
            .Select(asset => Path.GetDirectoryName(StagedPath(stagingDirectory, asset))!)
            .Prepend(stagingDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            resolver.AddSearchDirectory(directory);
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (ClosureAsset asset in candidates)
        {
            string stagedPath = StagedPath(stagingDirectory, asset);
            if (!File.Exists(stagedPath))
            {
                continue;
            }

            using ModuleDefinition module = ModuleDefinition.ReadModule(
                stagedPath,
                new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false, InMemory = true });
            string consumer = module.Assembly?.Name.Name ?? Path.GetFileNameWithoutExtension(stagedPath);
            bool consumerRewritten = rewritten.Contains(consumer);
            foreach (MemberReference member in module.GetMemberReferences())
            {
                // The runtime provides the methods of every array type, which Cecil does not resolve.
                if (member.DeclaringType is ArrayType
                    || member.DeclaringType.Scope is not AssemblyNameReference declaring
                    || !rewritten.Contains(declaring.Name)
                    || string.Equals(declaring.Name, consumer, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IReadOnlyList<string> mentioned = SignatureMentions.Find(member, signatureTypes);
                if (mentioned.Count == 0 || BindingFailure(member, consumer, consumerRewritten) is not string failure)
                {
                    continue;
                }

                foreach (string ruleId in mentioned.Select(type => ruleIdByType[type]).Distinct(StringComparer.Ordinal))
                {
                    if (reported.Add($"{ruleId}|{asset.RelativePath}|{member.FullName}"))
                    {
                        diagnostics.Add(RewriteDiagnostic.Error(
                            RewriteDiagnosticIds.UnresolvedReplacement,
                            $"Member-aware substitution rule '{ruleId}' rewrites '{declaring.Name}', but '{asset.RelativePath}' " +
                            $"references its member '{member.FullName}', which {failure}.",
                            asset.RelativePath));
                    }
                }
            }
        }

        return diagnostics;
    }

    private static string? BindingFailure(MemberReference member, string consumer, bool consumerRewritten)
    {
        try
        {
            if (member.Resolve() is not null)
            {
                return null;
            }
        }
        catch (AssemblyResolutionException exception)
        {
            return $"could not be resolved against the staged closure: {exception.Message}";
        }

        return consumerRewritten
            ? "does not bind, because the referencing type or the member's declaring type is excluded from rewriting"
            : $"does not bind, because '{consumer}' is not rewritten in this closure and keeps the original signature";
    }

    private static string StagedPath(string stagingDirectory, ClosureAsset asset) =>
        Path.Combine(stagingDirectory, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
}

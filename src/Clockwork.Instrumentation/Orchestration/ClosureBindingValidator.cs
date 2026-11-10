using Clockwork.Instrumentation.Closure;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Orchestration;

/// <summary>
/// Checks, once every assembly of a closure is staged, that the code of each staged assembly still binds to its
/// rewritten assemblies. A rewrite substitutes the types in its assembly's member signatures, so code the closure does
/// not rewrite - an assembly it copies, or a type excluded from rewriting - keeps the original signatures, and neither
/// a member reference from it into a rewritten assembly nor an interface implementation or override across that
/// boundary binds any more. Each such reference, implementation, and override whose signature mentions a substituted or
/// substitute type is checked against the staged closure, and one that does not bind fails the run with
/// <see cref="RewriteDiagnosticIds.UnresolvedReplacement"/> instead of throwing <see cref="MissingMemberException"/> or
/// <see cref="TypeLoadException"/> when the runtime binds it.
/// </summary>
internal static class ClosureBindingValidator
{
    /// <summary>Checks every staged non-framework managed assembly against the rewritten assemblies.</summary>
    /// <param name="plan">The closure plan.</param>
    /// <param name="stagingDirectory">The directory the closure is staged in.</param>
    /// <param name="frameworkClassifier">The classifier that kept framework assemblies out of the rewrite.</param>
    /// <param name="ruleSet">The rule set the closure is rewritten with.</param>
    /// <param name="targetRuntime">The target runtime the rules are evaluated against.</param>
    /// <param name="rewrittenAssemblyNames">The simple names of the assemblies the closure rewrites.</param>
    /// <param name="excludedTypesByAssembly">
    /// The full names of the types each rewritten assembly excluded from rewriting, by assembly simple name, where known.
    /// </param>
    /// <returns>An error for every reference, implementation, and override that does not bind.</returns>
    public static IReadOnlyList<RewriteDiagnostic> Validate(
        ClosurePlan plan,
        string stagingDirectory,
        FrameworkAssemblyClassifier frameworkClassifier,
        RewriteRuleSet ruleSet,
        Version? targetRuntime,
        IReadOnlyCollection<string> rewrittenAssemblyNames,
        IReadOnlyDictionary<string, IReadOnlySet<string>> excludedTypesByAssembly)
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

        var closure = new Closure(
            new HashSet<string>(rewrittenAssemblyNames, StringComparer.OrdinalIgnoreCase),
            excludedTypesByAssembly);
        var typeShapes = new TypeShapeCheck(matcher.TypeSubstitutionRules);
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
            foreach (MemberReference member in module.GetMemberReferences())
            {
                // The runtime provides the methods of every array type, which Cecil does not resolve.
                if (member.DeclaringType is ArrayType
                    || member.DeclaringType.Scope is not AssemblyNameReference declaring
                    || !closure.IsRewritten(declaring.Name)
                    || string.Equals(declaring.Name, consumer, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IReadOnlyList<string> mentioned = SignatureMentions.Find(member, signatureTypes);
                if (mentioned.Count == 0 || closure.BindingFailure(member, module, consumer, declaring.Name) is not string failure)
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

            // The rewrite checked each type whose interfaces and base types it could see final; one reaching into
            // another assembly the closure rewrites, or of an assembly it does not rewrite, is checked here.
            foreach (TypeDefinition type in module.GetTypes().Where(type => closure.ReachesOtherRewrittenAssembly(type, consumer)))
            {
                foreach (TypeShapeMismatch mismatch in typeShapes.Check(type, TryResolve, _ => true))
                {
                    string failure = mismatch.Describe(closure.Unrewritten);
                    foreach (RewriteRule rule in typeShapes.RulesFor(mismatch))
                    {
                        if (reported.Add($"{rule.Id}|{asset.RelativePath}|{mismatch.MemberName}|{mismatch.Expected}"))
                        {
                            diagnostics.Add(RewriteDiagnostic.Error(
                                RewriteDiagnosticIds.UnresolvedReplacement,
                                $"Member-aware substitution rule '{rule.Id}' targets member '{mismatch.MemberName}' of " +
                                $"'{asset.RelativePath}', which {failure}.",
                                asset.RelativePath));
                        }
                    }
                }
            }
        }

        return diagnostics;
    }

    private static TypeDefinition? TryResolve(TypeReference type)
    {
        try
        {
            return type.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    private static string StagedPath(string stagingDirectory, ClosureAsset asset) =>
        Path.Combine(stagingDirectory, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed class Closure(
        HashSet<string> rewrittenAssemblyNames,
        IReadOnlyDictionary<string, IReadOnlySet<string>> excludedTypesByAssembly)
    {
        public bool IsRewritten(string assemblyName) => rewrittenAssemblyNames.Contains(assemblyName);

        public bool ReachesOtherRewrittenAssembly(TypeDefinition type, string assemblyName)
        {
            foreach (InterfaceImplementation implementation in type.Interfaces)
            {
                if (TryResolve(implementation.InterfaceType) is { } declaring && IsOtherRewritten(declaring, assemblyName))
                {
                    return true;
                }
            }

            for (TypeReference? baseType = type.BaseType; baseType is not null && TryResolve(baseType) is { } definition; baseType = definition.BaseType)
            {
                if (IsOtherRewritten(definition, assemblyName))
                {
                    return true;
                }
            }

            return false;
        }

        public string? Unrewritten(TypeDefinition definition)
        {
            string assembly = definition.Module.Assembly.Name.Name;
            return !IsRewritten(assembly)
                ? $"'{definition.FullName}' is declared in '{assembly}', which is not rewritten in this closure"
                : IsExcluded(assembly, definition.FullName)
                    ? $"'{definition.FullName}' is excluded from rewriting in '{assembly}'"
                    : null;
        }

        public string? BindingFailure(MemberReference member, ModuleDefinition consumerModule, string consumer, string declaringAssembly)
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

            string declaringType = member.DeclaringType.GetElementType().FullName;
            if (!IsRewritten(consumer))
            {
                return $"does not bind, because '{consumer}' is not rewritten in this closure and keeps the original signature";
            }

            if (IsExcluded(declaringAssembly, declaringType))
            {
                return $"does not bind, because its declaring type '{declaringType}' is excluded from rewriting in " +
                    $"'{declaringAssembly}' and keeps the original signature";
            }

            return ExcludedReferencingType(member, consumerModule, consumer) is string referencing
                ? $"does not bind, because the referencing type '{referencing}' is excluded from rewriting and keeps the original signature"
                : $"does not bind to a member of '{declaringType}' after rewriting";
        }

        private bool IsOtherRewritten(TypeDefinition definition, string assemblyName)
        {
            string declaring = definition.Module.Assembly.Name.Name;
            return IsRewritten(declaring) && !string.Equals(declaring, assemblyName, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsExcluded(string assemblyName, string typeFullName) =>
            excludedTypesByAssembly.TryGetValue(assemblyName, out IReadOnlySet<string>? excluded) && excluded.Contains(typeFullName);

        private string? ExcludedReferencingType(MemberReference member, ModuleDefinition consumerModule, string consumer)
        {
            if (!excludedTypesByAssembly.TryGetValue(consumer, out IReadOnlySet<string>? excluded))
            {
                return null;
            }

            foreach (TypeDefinition type in consumerModule.GetTypes().Where(type => excluded.Contains(type.FullName)))
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    if (method.Overrides.Any(implemented => implemented.FullName == member.FullName)
                        || (method.HasBody && method.Body.Instructions.Any(instruction =>
                            instruction.Operand is MemberReference operand && operand.FullName == member.FullName)))
                    {
                        return type.FullName;
                    }
                }
            }

            return null;
        }
    }
}

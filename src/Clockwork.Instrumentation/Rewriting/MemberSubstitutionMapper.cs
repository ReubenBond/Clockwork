using Clockwork.Instrumentation.Rules;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>
/// A recursive Mono.Cecil reference mapper that projects a set of
/// <see cref="RewriteOperationKind.SubstituteType"/> rules onto whole references - not just the
/// type-reference operands the <see cref="TypeReferenceRewritingPass"/> handles, but the declaring
/// types of method and field references, generic instantiations, by-ref and array element types,
/// field types, and local-variable types. This is what makes it possible to replace a
/// compiler-generated state machine's builder and awaiter types (for example
/// <c>AsyncTaskMethodBuilder&lt;int&gt;</c> and <c>TaskAwaiter&lt;int&gt;</c>) with their controlled
/// counterparts while preserving generic arguments, by-ref-ness, and method signatures.
/// </summary>
/// <remarks>
/// Every <c>Map*</c> method returns <see langword="null"/> when nothing changed, so callers can cheaply
/// skip untouched references. Substituted declaring types are resolved back to the replacement
/// assembly's definitions and re-imported so the produced references carry a signature that binds by
/// name at run time; when the original declaring type was a generic instance the imported open
/// reference is re-based onto the mapped generic instance.
/// </remarks>
internal sealed class MemberSubstitutionMapper
{
    private readonly ModuleDefinition _module;
    private readonly Dictionary<string, TypeReference> _byName;
    private readonly Dictionary<string, string> _ruleIdByName;

    private MemberSubstitutionMapper(
        ModuleDefinition module,
        Dictionary<string, TypeReference> byName,
        Dictionary<string, string> ruleIdByName)
    {
        _module = module;
        _byName = byName;
        _ruleIdByName = ruleIdByName;
    }

    /// <summary>Gets a value indicating whether any substitution was resolved.</summary>
    public bool IsEmpty => _byName.Count == 0;

    /// <summary>
    /// Builds a mapper by resolving every in-range type substitution to an imported reference. Rules
    /// whose replacement type cannot be resolved are reported through <paramref name="onUnresolved"/>
    /// and omitted (the pass then leaves those references untouched, and the missing-service guard at
    /// run time surfaces the gap).
    /// </summary>
    public static MemberSubstitutionMapper Build(RewriteSession session, Action<RewriteRule, string> onUnresolved)
    {
        var byName = new Dictionary<string, TypeReference>(StringComparer.Ordinal);
        var ruleIdByName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (KeyValuePair<string, RewriteRule> entry in session.Matcher.TypeSubstitutionRules)
        {
            if (session.Resolver.TryResolveType(session.TargetModule, entry.Value.Replacement, out TypeReference imported, out string? error))
            {
                byName[entry.Key] = imported;
                ruleIdByName[entry.Key] = entry.Value.Id;
            }
            else if (error is not null)
            {
                onUnresolved(entry.Value, error);
            }
        }

        return new MemberSubstitutionMapper(session.TargetModule, byName, ruleIdByName);
    }

    /// <summary>Gets the id of the rule that substitutes <paramref name="normalizedFullName"/>, if any.</summary>
    public string? RuleIdFor(string normalizedFullName) =>
        _ruleIdByName.TryGetValue(normalizedFullName, out string? id) ? id : null;

    /// <summary>Returns the substitute for a substituted type's open full name, or <see langword="null"/>.</summary>
    public TypeReference? OpenSubstituteFor(string normalizedFullName) =>
        _byName.TryGetValue(normalizedFullName, out TypeReference? imported) ? imported : null;

    /// <summary>
    /// Maps a type reference, returning the substituted reference or <see langword="null"/> when the
    /// type (and everything it is composed of) is unaffected.
    /// </summary>
    public TypeReference? MapType(TypeReference type)
    {
        switch (type)
        {
            case GenericInstanceType generic:
            {
                TypeReference? mappedElement = MapType(generic.ElementType);
                TypeReference[]? mappedArguments = MapMany(generic.GenericArguments);
                if (mappedElement is null && mappedArguments is null)
                {
                    return null;
                }

                var result = new GenericInstanceType(mappedElement ?? generic.ElementType);
                for (int i = 0; i < generic.GenericArguments.Count; i++)
                {
                    result.GenericArguments.Add(mappedArguments?[i] ?? generic.GenericArguments[i]);
                }

                return result;
            }

            case ByReferenceType byReference:
            {
                TypeReference? mapped = MapType(byReference.ElementType);
                return mapped is null ? null : new ByReferenceType(mapped);
            }

            case ArrayType array:
            {
                TypeReference? mapped = MapType(array.ElementType);
                if (mapped is null)
                {
                    return null;
                }

                return array.IsVector ? new ArrayType(mapped) : new ArrayType(mapped, array.Rank);
            }

            case PointerType pointer:
            {
                TypeReference? mapped = MapType(pointer.ElementType);
                return mapped is null ? null : new PointerType(mapped);
            }

            case GenericParameter:
                return null;

            default:
                return _byName.TryGetValue(type.FullName, out TypeReference? imported) ? imported : null;
        }
    }

    /// <summary>
    /// Maps a method reference whose declaring type (or generic arguments) are substituted, returning
    /// <see langword="null"/> when unaffected.
    /// </summary>
    public MethodReference? MapMethod(MethodReference method)
    {
        if (method is GenericInstanceMethod generic)
        {
            MethodReference? mappedElement = MapMethod(generic.ElementMethod);
            TypeReference[]? mappedArguments = MapMany(generic.GenericArguments);
            if (mappedElement is null && mappedArguments is null)
            {
                return null;
            }

            var result = new GenericInstanceMethod(mappedElement ?? generic.ElementMethod);
            for (int i = 0; i < generic.GenericArguments.Count; i++)
            {
                result.GenericArguments.Add(mappedArguments?[i] ?? generic.GenericArguments[i]);
            }

            return result;
        }

        TypeReference? mappedDeclaring = MapType(method.DeclaringType);
        if (mappedDeclaring is null)
        {
            return null;
        }

        TypeReference mappedElementType = mappedDeclaring is GenericInstanceType instance
            ? instance.ElementType
            : mappedDeclaring;

        TypeDefinition? definition = mappedElementType.Resolve();
        MethodDefinition? controlled = definition is null ? null : FindMatchingMethod(definition, method);
        if (controlled is null)
        {
            return null;
        }

        MethodReference importedOpen = _module.ImportReference(controlled);
        return mappedDeclaring is GenericInstanceType genericDeclaring
            ? Rebase(importedOpen, genericDeclaring)
            : importedOpen;
    }

    /// <summary>Maps a field reference whose declaring type or field type is substituted.</summary>
    public FieldReference? MapField(FieldReference field)
    {
        TypeReference? mappedFieldType = MapType(field.FieldType);
        TypeReference? mappedDeclaring = MapType(field.DeclaringType);
        if (mappedFieldType is null && mappedDeclaring is null)
        {
            return null;
        }

        return new FieldReference(
            field.Name,
            mappedFieldType ?? field.FieldType,
            mappedDeclaring ?? field.DeclaringType);
    }

    /// <summary>
    /// Builds a constructor reference on a mapped value type taking exactly <paramref name="arity"/>
    /// parameters. Used to turn an awaiter-source call (<c>Task.GetAwaiter()</c> /
    /// <c>Task.ConfigureAwait(bool)</c>) into a <c>newobj</c> of the controlled awaitable/awaiter, which
    /// consumes the same stack (the receiver, plus any arguments) and pushes the controlled value.
    /// </summary>
    public MethodReference? BuildConstructor(TypeReference mappedType, int arity)
    {
        TypeReference elementType = mappedType is GenericInstanceType instance ? instance.ElementType : mappedType;
        TypeDefinition? definition = elementType.Resolve();
        MethodDefinition? constructor = definition?.Methods
            .FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == arity);
        if (constructor is null)
        {
            return null;
        }

        MethodReference importedOpen = _module.ImportReference(constructor);
        return mappedType is GenericInstanceType genericType ? Rebase(importedOpen, genericType) : importedOpen;
    }

    /// <summary>
    /// Builds a constructor reference for the controlled awaitable/awaiter produced by an awaiter-source
    /// call (<c>Task.GetAwaiter()</c> / <c>Task.ConfigureAwait(bool)</c>). The BCL return type
    /// (<c>TaskAwaiter&lt;!0&gt;</c> / <c>ConfiguredTaskAwaitable&lt;!0&gt;</c>) parameterises over the
    /// awaited task's own generic parameter, so its concrete argument is taken from the receiver's
    /// declaring generic instance rather than mapped directly (mapping would carry the unbound <c>!0</c>
    /// into a non-generic state machine and produce a dangling reference).
    /// </summary>
    public MethodReference? BuildAwaiterSourceConstructor(MethodReference source, int arity)
    {
        TypeReference returnType = source.ReturnType;
        string openName = returnType is GenericInstanceType generic ? generic.ElementType.FullName : returnType.FullName;
        if (!_byName.TryGetValue(openName, out TypeReference? openSubstitute))
        {
            return null;
        }

        TypeReference mappedResult;
        if (returnType is GenericInstanceType)
        {
            if (source.DeclaringType is not GenericInstanceType declaring || declaring.GenericArguments.Count == 0)
            {
                return null;
            }

            var instance = new GenericInstanceType(openSubstitute);
            instance.GenericArguments.Add(declaring.GenericArguments[0]);
            mappedResult = instance;
        }
        else
        {
            mappedResult = openSubstitute;
        }

        return BuildConstructor(mappedResult, arity);
    }

    private static MethodDefinition? FindMatchingMethod(TypeDefinition definition, MethodReference original)
    {
        MethodReference elementOriginal = original is GenericInstanceMethod generic ? generic.ElementMethod : original;
        foreach (MethodDefinition candidate in definition.Methods)
        {
            if (candidate.Name == elementOriginal.Name
                && candidate.Parameters.Count == elementOriginal.Parameters.Count
                && candidate.GenericParameters.Count == elementOriginal.GenericParameters.Count)
            {
                return candidate;
            }
        }

        return null;
    }

    private static MethodReference Rebase(MethodReference importedOpen, GenericInstanceType declaringType)
    {
        var rebased = new MethodReference(importedOpen.Name, importedOpen.ReturnType, declaringType)
        {
            HasThis = importedOpen.HasThis,
            ExplicitThis = importedOpen.ExplicitThis,
            CallingConvention = importedOpen.CallingConvention,
        };

        foreach (GenericParameter parameter in importedOpen.GenericParameters)
        {
            rebased.GenericParameters.Add(new GenericParameter(parameter.Name, rebased));
        }

        foreach (ParameterDefinition parameter in importedOpen.Parameters)
        {
            rebased.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
        }

        return rebased;
    }

    private TypeReference[]? MapMany(Mono.Collections.Generic.Collection<TypeReference> types)
    {
        TypeReference[]? mapped = null;
        for (int i = 0; i < types.Count; i++)
        {
            TypeReference? result = MapType(types[i]);
            if (result is not null)
            {
                mapped ??= new TypeReference[types.Count];
                mapped[i] = result;
            }
        }

        return mapped;
    }
}

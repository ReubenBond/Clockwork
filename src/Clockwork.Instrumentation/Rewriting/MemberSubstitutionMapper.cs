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

            case RequiredModifierType modifier:
                {
                    TypeReference? mappedElement = MapType(modifier.ElementType);
                    TypeReference? mappedModifier = MapType(modifier.ModifierType);
                    return mappedElement is null && mappedModifier is null
                        ? null
                        : new RequiredModifierType(
                            mappedModifier ?? modifier.ModifierType,
                            mappedElement ?? modifier.ElementType);
                }

            case OptionalModifierType modifier:
                {
                    TypeReference? mappedElement = MapType(modifier.ElementType);
                    TypeReference? mappedModifier = MapType(modifier.ModifierType);
                    return mappedElement is null && mappedModifier is null
                        ? null
                        : new OptionalModifierType(
                            mappedModifier ?? modifier.ModifierType,
                            mappedElement ?? modifier.ElementType);
                }

            case PinnedType pinned:
                {
                    TypeReference? mapped = MapType(pinned.ElementType);
                    return mapped is null ? null : new PinnedType(mapped);
                }

            case SentinelType sentinel:
                {
                    TypeReference? mapped = MapType(sentinel.ElementType);
                    return mapped is null ? null : new SentinelType(mapped);
                }

            case GenericParameter:
                return null;

            default:
                return _byName.TryGetValue(type.FullName, out TypeReference? imported) ? imported : null;
        }
    }

    /// <summary>
    /// Maps a method reference whose declaring type, return type, parameter types, or generic arguments are substituted, returning
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
        TypeReference? mappedReturn = MapType(method.ReturnType);
        TypeReference[]? mappedParameters = MapParameters(method.Parameters);
        if (mappedDeclaring is null && mappedReturn is null && mappedParameters is null)
        {
            return null;
        }

        if (mappedDeclaring is null)
        {
            return Rebuild(method, method.DeclaringType, mappedReturn, mappedParameters);
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

    private MethodDefinition? FindMatchingMethod(TypeDefinition definition, MethodReference original)
    {
        MethodReference elementOriginal = original is GenericInstanceMethod generic ? generic.ElementMethod : original;

        MethodDefinition? arityMatch = null;
        foreach (MethodDefinition candidate in definition.Methods)
        {
            if (candidate.Name != elementOriginal.Name
                || candidate.HasThis != elementOriginal.HasThis
                || candidate.Parameters.Count != elementOriginal.Parameters.Count
                || candidate.GenericParameters.Count != elementOriginal.GenericParameters.Count)
            {
                continue;
            }

            TypeReference expectedReturn = MapType(elementOriginal.ReturnType) ?? elementOriginal.ReturnType;
            if (!SameTypeShape(candidate.ReturnType, expectedReturn))
            {
                continue;
            }

            bool parametersMatch = true;
            for (int i = 0; i < candidate.Parameters.Count; i++)
            {
                ParameterDefinition expectedParameter = elementOriginal.Parameters[i];
                ParameterDefinition candidateParameter = candidate.Parameters[i];
                TypeReference expectedType = MapType(expectedParameter.ParameterType) ?? expectedParameter.ParameterType;
                if (!SameTypeShape(candidateParameter.ParameterType, expectedType))
                {
                    parametersMatch = false;
                    break;
                }
            }

            if (parametersMatch)
            {
                arityMatch ??= candidate;

                // When the substitute declares several same-arity overloads (for example SpinWait's
                // SpinUntil(Func<bool>, int) vs SpinUntil(Func<bool>, TimeSpan)), matching on arity alone
                // would bind every call site to the first overload and emit invalid IL. Disambiguate by
                // parameter type in that case.
                if (ParametersMatch(candidate, elementOriginal))
                {
                    return candidate;
                }
            }
        }

        // A single same-arity candidate is unambiguous (and parameter types may legitimately differ, e.g.
        // a parameter of the substituted type maps to the controlled type). Fall back to the first match.
        return arityMatch;
    }

    private bool ParametersMatch(MethodDefinition candidate, MethodReference original)
    {
        for (int i = 0; i < original.Parameters.Count; i++)
        {
            TypeReference originalParameter = original.Parameters[i].ParameterType;
            string expected = (MapType(originalParameter) ?? originalParameter).FullName;
            if (!string.Equals(candidate.Parameters[i].ParameterType.FullName, expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameTypeShape(TypeReference left, TypeReference right) =>
        SameTypeShape(left, right, []);

    private static bool SameTypeShape(
        TypeReference left,
        TypeReference right,
        HashSet<(TypeReference Left, TypeReference Right)> visited)
    {
        if (!visited.Add((left, right)))
        {
            return true;
        }

        if (left is GenericParameter leftParameter && right is GenericParameter rightParameter)
        {
            return leftParameter.Type == rightParameter.Type
                && leftParameter.Position == rightParameter.Position;
        }

        bool leftSpecification = left is TypeSpecification;
        bool rightSpecification = right is TypeSpecification;
        return (left, right) switch
        {
            (ByReferenceType l, ByReferenceType r) => SameTypeShape(l.ElementType, r.ElementType, visited),
            (PointerType l, PointerType r) => SameTypeShape(l.ElementType, r.ElementType, visited),
            (ArrayType l, ArrayType r) =>
                l.Rank == r.Rank
                && l.IsVector == r.IsVector
                && l.Dimensions.Zip(r.Dimensions).All(
                    static dimensions =>
                        dimensions.First.LowerBound == dimensions.Second.LowerBound
                        && dimensions.First.UpperBound == dimensions.Second.UpperBound)
                && SameTypeShape(l.ElementType, r.ElementType, visited),
            (GenericInstanceType l, GenericInstanceType r) =>
                l.ElementType.FullName == r.ElementType.FullName
                && l.GenericArguments.Count == r.GenericArguments.Count
                && l.GenericArguments.Zip(
                    r.GenericArguments,
                    (leftArgument, rightArgument) =>
                        SameTypeShape(leftArgument, rightArgument, visited)).All(static match => match),
            (RequiredModifierType l, RequiredModifierType r) =>
                SameTypeShape(l.ModifierType, r.ModifierType, visited)
                && SameTypeShape(l.ElementType, r.ElementType, visited),
            (OptionalModifierType l, OptionalModifierType r) =>
                SameTypeShape(l.ModifierType, r.ModifierType, visited)
                && SameTypeShape(l.ElementType, r.ElementType, visited),
            (PinnedType l, PinnedType r) => SameTypeShape(l.ElementType, r.ElementType, visited),
            (SentinelType l, SentinelType r) => SameTypeShape(l.ElementType, r.ElementType, visited),
            (FunctionPointerType l, FunctionPointerType r) =>
                l.HasThis == r.HasThis
                && l.ExplicitThis == r.ExplicitThis
                && l.CallingConvention == r.CallingConvention
                && l.Parameters.Count == r.Parameters.Count
                && SameTypeShape(l.ReturnType, r.ReturnType, visited)
                && l.Parameters.Zip(
                    r.Parameters,
                    (leftParameter, rightParameter) =>
                        SameTypeShape(leftParameter.ParameterType, rightParameter.ParameterType, visited))
                    .All(static match => match),
            _ => !leftSpecification && !rightSpecification && left.FullName == right.FullName,
        };
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

    private static MethodReference Rebuild(
        MethodReference original,
        TypeReference declaringType,
        TypeReference? mappedReturn,
        TypeReference[]? mappedParameters)
    {
        var rebuilt = new MethodReference(
            original.Name,
            mappedReturn ?? original.ReturnType,
            declaringType)
        {
            HasThis = original.HasThis,
            ExplicitThis = original.ExplicitThis,
            CallingConvention = original.CallingConvention,
        };

        var genericParameters = new Dictionary<GenericParameter, GenericParameter>();
        foreach (GenericParameter parameter in original.GenericParameters)
        {
            var replacement = new GenericParameter(parameter.Name, rebuilt);
            rebuilt.GenericParameters.Add(replacement);
            genericParameters.Add(parameter, replacement);
        }

        rebuilt.ReturnType = RebindMethodGenericParameters(rebuilt.ReturnType, genericParameters);
        for (int i = 0; i < original.Parameters.Count; i++)
        {
            ParameterDefinition parameter = original.Parameters[i];
            rebuilt.Parameters.Add(new ParameterDefinition(
                parameter.Name,
                parameter.Attributes,
                RebindMethodGenericParameters(
                    mappedParameters?[i] ?? parameter.ParameterType,
                    genericParameters)));
        }

        return rebuilt;
    }

    private static TypeReference RebindMethodGenericParameters(
        TypeReference type,
        IReadOnlyDictionary<GenericParameter, GenericParameter> genericParameters) =>
        type switch
        {
            GenericParameter parameter when genericParameters.TryGetValue(parameter, out GenericParameter? replacement) =>
                replacement,
            GenericInstanceType generic => RebindGenericInstance(generic, genericParameters),
            ByReferenceType byReference => new ByReferenceType(
                RebindMethodGenericParameters(byReference.ElementType, genericParameters)),
            ArrayType array when array.IsVector => new ArrayType(
                RebindMethodGenericParameters(array.ElementType, genericParameters)),
            ArrayType array => new ArrayType(
                RebindMethodGenericParameters(array.ElementType, genericParameters),
                array.Rank),
            PointerType pointer => new PointerType(
                RebindMethodGenericParameters(pointer.ElementType, genericParameters)),
            RequiredModifierType modifier => new RequiredModifierType(
                RebindMethodGenericParameters(modifier.ModifierType, genericParameters),
                RebindMethodGenericParameters(modifier.ElementType, genericParameters)),
            OptionalModifierType modifier => new OptionalModifierType(
                RebindMethodGenericParameters(modifier.ModifierType, genericParameters),
                RebindMethodGenericParameters(modifier.ElementType, genericParameters)),
            PinnedType pinned => new PinnedType(
                RebindMethodGenericParameters(pinned.ElementType, genericParameters)),
            SentinelType sentinel => new SentinelType(
                RebindMethodGenericParameters(sentinel.ElementType, genericParameters)),
            _ => type,
        };

    private static GenericInstanceType RebindGenericInstance(
        GenericInstanceType generic,
        IReadOnlyDictionary<GenericParameter, GenericParameter> genericParameters)
    {
        var result = new GenericInstanceType(
            RebindMethodGenericParameters(generic.ElementType, genericParameters));
        foreach (TypeReference argument in generic.GenericArguments)
        {
            result.GenericArguments.Add(RebindMethodGenericParameters(argument, genericParameters));
        }

        return result;
    }

    private TypeReference[]? MapParameters(Mono.Collections.Generic.Collection<ParameterDefinition> parameters)
    {
        TypeReference[]? mapped = null;
        for (int i = 0; i < parameters.Count; i++)
        {
            TypeReference? result = MapType(parameters[i].ParameterType);
            if (result is not null)
            {
                mapped ??= new TypeReference[parameters.Count];
                mapped[i] = result;
            }
        }

        return mapped;
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

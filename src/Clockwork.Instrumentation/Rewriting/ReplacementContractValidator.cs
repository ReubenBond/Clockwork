using Clockwork.Instrumentation.Rules;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>Validates replacement method stack contracts before call-site IL is edited.</summary>
internal static class ReplacementContractValidator
{
    public static bool TryValidate(
        RewriteOperationKind operation,
        MethodReference original,
        MethodReference replacement,
        Func<TypeReference, TypeReference, bool> additionalTypeCompatibility,
        out string? error)
    {
        if (replacement.HasThis)
        {
            error = $"Replacement '{replacement.FullName}' must be static.";
            return false;
        }

        return operation switch
        {
            RewriteOperationKind.RedirectCall => ValidateRedirect(
                original, replacement, isConstructor: false, additionalTypeCompatibility, out error),
            RewriteOperationKind.RedirectNewObj => ValidateRedirect(
                original, replacement, isConstructor: true, additionalTypeCompatibility, out error),
            RewriteOperationKind.WrapAfterCall => ValidateWrapper(
                original, replacement, additionalTypeCompatibility, out error),
            RewriteOperationKind.InjectRejection => ValidateRejection(replacement, out error),
            _ => Success(out error),
        };
    }

    private static bool ValidateRedirect(
        MethodReference original,
        MethodReference replacement,
        bool isConstructor,
        Func<TypeReference, TypeReference, bool> additionalTypeCompatibility,
        out string? error)
    {
        int receiverCount = !isConstructor && original.HasThis ? 1 : 0;
        int expectedCount = original.Parameters.Count + receiverCount;
        if (replacement.Parameters.Count != expectedCount)
        {
            error =
                $"Replacement '{replacement.FullName}' consumes {replacement.Parameters.Count} stack arguments, " +
                $"but target '{original.FullName}' requires {expectedCount} including its receiver.";
            return false;
        }

        int replacementIndex = 0;
        TypeReference receiverType = original.DeclaringType.IsValueType
            ? new ByReferenceType(original.DeclaringType)
            : original.DeclaringType;
        if (receiverCount == 1
            && !SameType(
                receiverType,
                original,
                replacement.Parameters[replacementIndex++].ParameterType,
                replacement,
                additionalTypeCompatibility))
        {
            error = $"Replacement '{replacement.FullName}' has an incompatible receiver parameter for '{original.FullName}'.";
            return false;
        }

        for (int i = 0; i < original.Parameters.Count; i++)
        {
            if (!SameType(
                original.Parameters[i].ParameterType,
                original,
                replacement.Parameters[replacementIndex + i].ParameterType,
                replacement,
                additionalTypeCompatibility))
            {
                error =
                    $"Replacement '{replacement.FullName}' parameter {replacementIndex + i} is incompatible with " +
                    $"target parameter {i} of '{original.FullName}'.";
                return false;
            }
        }

        TypeReference expectedReturn = isConstructor ? original.DeclaringType : original.ReturnType;
        if (!SameType(
            expectedReturn,
            original,
            replacement.ReturnType,
            replacement,
            additionalTypeCompatibility))
        {
            error =
                $"Replacement '{replacement.FullName}' returns '{Shape(replacement.ReturnType, replacement)}', " +
                $"but target '{original.FullName}' requires '{Shape(expectedReturn, original)}'.";
            return false;
        }

        return Success(out error);
    }

    private static bool ValidateWrapper(
        MethodReference original,
        MethodReference replacement,
        Func<TypeReference, TypeReference, bool> additionalTypeCompatibility,
        out string? error)
    {
        if (original.ReturnType.MetadataType == MetadataType.Void)
        {
            error = $"Post-call rule cannot wrap void target '{original.FullName}'.";
            return false;
        }

        if (replacement.Parameters.Count != 1
            || !SameType(
                original.ReturnType,
                original,
                replacement.Parameters[0].ParameterType,
                replacement,
                additionalTypeCompatibility)
            || !SameType(
                original.ReturnType,
                original,
                replacement.ReturnType,
                replacement,
                additionalTypeCompatibility))
        {
            error =
                $"Post-call replacement '{replacement.FullName}' must be static and consume and return exactly " +
                $"'{Shape(original.ReturnType, original)}'.";
            return false;
        }

        return Success(out error);
    }

    private static bool ValidateRejection(MethodReference replacement, out string? error)
    {
        if (replacement.Parameters.Count != 1
            || replacement.Parameters[0].ParameterType.FullName != "System.String"
            || replacement.ReturnType.MetadataType != MetadataType.Void)
        {
            error =
                $"Rejection replacement '{replacement.FullName}' must be a static void method taking exactly one System.String.";
            return false;
        }

        return Success(out error);
    }

    private static bool SameType(
        TypeReference left,
        MethodReference leftOwner,
        TypeReference right,
        MethodReference rightOwner,
        Func<TypeReference, TypeReference, bool> additionalTypeCompatibility) =>
        new TypeShapeComparer(leftOwner, rightOwner).Same(left, right)
        || additionalTypeCompatibility(left, right);

    private static string Shape(TypeReference type, MethodReference owner) =>
        Shape(type, owner, allowSubstitution: true);

    private static string Shape(
        TypeReference type,
        MethodReference owner,
        bool allowSubstitution)
    {
        if (type is GenericParameter parameter)
        {
            if (allowSubstitution
                && TryGetGenericArgument(parameter, owner, out TypeReference? argument)
                && !ReferenceEquals(parameter, argument))
            {
                return Shape(argument!, owner, allowSubstitution: false);
            }

            return (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position;
        }

        return type switch
        {
            ByReferenceType byReference => Shape(byReference.ElementType, owner, allowSubstitution) + "&",
            PointerType pointer => Shape(pointer.ElementType, owner, allowSubstitution) + "*",
            ArrayType array => Shape(array.ElementType, owner, allowSubstitution) + ShapeArrayDimensions(array),
            GenericInstanceType generic =>
                Shape(generic.ElementType, owner, allowSubstitution) + "<" +
                string.Join(",", generic.GenericArguments.Select(
                    argument => Shape(argument, owner, allowSubstitution))) + ">",
            RequiredModifierType modifier =>
                Shape(modifier.ElementType, owner, allowSubstitution) + " modreq(" +
                Shape(modifier.ModifierType, owner, allowSubstitution) + ")",
            OptionalModifierType modifier =>
                Shape(modifier.ElementType, owner, allowSubstitution) + " modopt(" +
                Shape(modifier.ModifierType, owner, allowSubstitution) + ")",
            PinnedType pinned => Shape(pinned.ElementType, owner, allowSubstitution) + " pinned",
            SentinelType sentinel => Shape(sentinel.ElementType, owner, allowSubstitution) + " sentinel",
            FunctionPointerType functionPointer => Shape(functionPointer, owner, allowSubstitution),
            _ => type.FullName,
        };
    }

    private static string Shape(
        FunctionPointerType functionPointer,
        MethodReference owner,
        bool allowSubstitution) =>
        $"method[{functionPointer.CallingConvention};this={functionPointer.HasThis};explicit={functionPointer.ExplicitThis}] " +
        Shape(functionPointer.ReturnType, owner, allowSubstitution) + " *(" +
        string.Join(",", functionPointer.Parameters.Select(
            parameter => Shape(parameter.ParameterType, owner, allowSubstitution))) + ")";

    private static string ShapeArrayDimensions(ArrayType array) =>
        array.IsVector
            ? "[]"
            : "[" + string.Join(",", array.Dimensions.Select(
                static dimension =>
                    (dimension.LowerBound?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                    + "..."
                    + (dimension.UpperBound?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)))
                + "]";

    public static TypeReference InflateType(TypeReference type, MethodReference owner) =>
        InflateType(type, owner, allowSubstitution: true);

    private static TypeReference InflateType(
        TypeReference type,
        MethodReference owner,
        bool allowSubstitution)
    {
        if (type is GenericParameter parameter)
        {
            if (allowSubstitution
                && TryGetGenericArgument(parameter, owner, out TypeReference? argument)
                && !ReferenceEquals(parameter, argument))
            {
                return InflateType(argument!, owner, allowSubstitution: false);
            }

            return type;
        }

        if (type is GenericInstanceType generic)
        {
            var inflated = new GenericInstanceType(generic.ElementType);
            foreach (TypeReference argument in generic.GenericArguments)
            {
                inflated.GenericArguments.Add(InflateType(argument, owner, allowSubstitution));
            }

            return inflated;
        }

        return type switch
        {
            ByReferenceType byReference =>
                new ByReferenceType(InflateType(byReference.ElementType, owner, allowSubstitution)),
            PointerType pointer =>
                new PointerType(InflateType(pointer.ElementType, owner, allowSubstitution)),
            ArrayType array => InflateArray(array, owner, allowSubstitution),
            RequiredModifierType modifier => new RequiredModifierType(
                InflateType(modifier.ModifierType, owner, allowSubstitution),
                InflateType(modifier.ElementType, owner, allowSubstitution)),
            OptionalModifierType modifier => new OptionalModifierType(
                InflateType(modifier.ModifierType, owner, allowSubstitution),
                InflateType(modifier.ElementType, owner, allowSubstitution)),
            PinnedType pinned =>
                new PinnedType(InflateType(pinned.ElementType, owner, allowSubstitution)),
            SentinelType sentinel =>
                new SentinelType(InflateType(sentinel.ElementType, owner, allowSubstitution)),
            FunctionPointerType functionPointer =>
                InflateFunctionPointer(functionPointer, owner, allowSubstitution),
            _ => type,
        };
    }

    private static ArrayType InflateArray(
        ArrayType array,
        MethodReference owner,
        bool allowSubstitution)
    {
        TypeReference element = InflateType(array.ElementType, owner, allowSubstitution);
        var inflated = array.IsVector ? new ArrayType(element) : new ArrayType(element, array.Rank);
        for (int i = 0; i < array.Dimensions.Count; i++)
        {
            inflated.Dimensions[i] = new ArrayDimension(
                array.Dimensions[i].LowerBound,
                array.Dimensions[i].UpperBound);
        }

        return inflated;
    }

    private static FunctionPointerType InflateFunctionPointer(
        FunctionPointerType functionPointer,
        MethodReference owner,
        bool allowSubstitution)
    {
        var inflated = new FunctionPointerType
        {
            HasThis = functionPointer.HasThis,
            ExplicitThis = functionPointer.ExplicitThis,
            CallingConvention = functionPointer.CallingConvention,
            ReturnType = InflateType(functionPointer.ReturnType, owner, allowSubstitution),
        };
        foreach (ParameterDefinition parameter in functionPointer.Parameters)
        {
            inflated.Parameters.Add(new ParameterDefinition(
                InflateType(parameter.ParameterType, owner, allowSubstitution)));
        }

        return inflated;
    }

    private static bool TryGetGenericArgument(
        GenericParameter parameter,
        MethodReference owner,
        out TypeReference? argument)
    {
        if (parameter.Type == GenericParameterType.Method
            && owner is GenericInstanceMethod genericMethod
            && IsOwnedBy(parameter, genericMethod.ElementMethod)
            && parameter.Position < genericMethod.GenericArguments.Count)
        {
            argument = genericMethod.GenericArguments[parameter.Position];
            return true;
        }

        if (parameter.Type == GenericParameterType.Type
            && owner.DeclaringType is GenericInstanceType genericType
            && IsOwnedBy(parameter, genericType.ElementType)
            && parameter.Position < genericType.GenericArguments.Count)
        {
            argument = genericType.GenericArguments[parameter.Position];
            return true;
        }

        argument = null;
        return false;
    }

    private static bool IsOwnedBy(GenericParameter parameter, IGenericParameterProvider owner)
    {
        if (ReferenceEquals(parameter.Owner, owner))
        {
            return true;
        }

        return parameter.Owner is MemberReference parameterOwner
            && owner is MemberReference expectedOwner
            && parameterOwner.MetadataToken.RID != 0
            && parameterOwner.MetadataToken == expectedOwner.MetadataToken
            && ReferenceEquals(parameterOwner.Module, expectedOwner.Module);
    }

    private sealed class TypeShapeComparer(
        MethodReference leftOwner,
        MethodReference rightOwner)
    {
        private readonly HashSet<(
            TypeReference Left,
            bool SubstituteLeft,
            TypeReference Right,
            bool SubstituteRight)> _visited = [];

        public bool Same(TypeReference left, TypeReference right) =>
            Same(left, substituteLeft: true, right, substituteRight: true);

        private bool Same(
            TypeReference left,
            bool substituteLeft,
            TypeReference right,
            bool substituteRight)
        {
            if (substituteLeft
                && TrySubstitute(left, leftOwner, out TypeReference? substitutedLeft))
            {
                left = substitutedLeft!;
                substituteLeft = false;
            }

            if (substituteRight
                && TrySubstitute(right, rightOwner, out TypeReference? substitutedRight))
            {
                right = substitutedRight!;
                substituteRight = false;
            }

            if (!_visited.Add((left, substituteLeft, right, substituteRight)))
            {
                return true;
            }

            if (left is GenericParameter leftParameter && right is GenericParameter rightParameter)
            {
                return leftParameter.Type == rightParameter.Type
                    && leftParameter.Position == rightParameter.Position;
            }

            return (left, right) switch
            {
                (ByReferenceType l, ByReferenceType r) =>
                    Same(l.ElementType, substituteLeft, r.ElementType, substituteRight),
                (PointerType l, PointerType r) =>
                    Same(l.ElementType, substituteLeft, r.ElementType, substituteRight),
                (ArrayType l, ArrayType r) =>
                    SameArray(l, substituteLeft, r, substituteRight),
                (GenericInstanceType l, GenericInstanceType r) =>
                    Same(l.ElementType, substituteLeft, r.ElementType, substituteRight)
                    && l.GenericArguments.Count == r.GenericArguments.Count
                    && l.GenericArguments.Zip(
                        r.GenericArguments,
                        (leftArgument, rightArgument) =>
                            Same(leftArgument, substituteLeft, rightArgument, substituteRight))
                        .All(static match => match),
                (RequiredModifierType l, RequiredModifierType r) =>
                    Same(l.ModifierType, substituteLeft, r.ModifierType, substituteRight)
                    && Same(l.ElementType, substituteLeft, r.ElementType, substituteRight),
                (OptionalModifierType l, OptionalModifierType r) =>
                    Same(l.ModifierType, substituteLeft, r.ModifierType, substituteRight)
                    && Same(l.ElementType, substituteLeft, r.ElementType, substituteRight),
                (PinnedType l, PinnedType r) =>
                    Same(l.ElementType, substituteLeft, r.ElementType, substituteRight),
                (SentinelType l, SentinelType r) =>
                    Same(l.ElementType, substituteLeft, r.ElementType, substituteRight),
                (FunctionPointerType l, FunctionPointerType r) =>
                    SameFunctionPointer(l, substituteLeft, r, substituteRight),
                _ => left is not TypeSpecification
                    && right is not TypeSpecification
                    && string.Equals(left.FullName, right.FullName, StringComparison.Ordinal),
            };
        }

        private static bool TrySubstitute(
            TypeReference type,
            MethodReference owner,
            out TypeReference? substituted)
        {
            if (type is GenericParameter parameter
                && TryGetGenericArgument(parameter, owner, out TypeReference? argument)
                && !ReferenceEquals(type, argument))
            {
                substituted = argument;
                return true;
            }

            substituted = null;
            return false;
        }

        private bool SameArray(
            ArrayType left,
            bool substituteLeft,
            ArrayType right,
            bool substituteRight)
        {
            if (left.Rank != right.Rank
                || left.IsVector != right.IsVector
                || left.Dimensions.Count != right.Dimensions.Count
                || !Same(left.ElementType, substituteLeft, right.ElementType, substituteRight))
            {
                return false;
            }

            return left.Dimensions.Zip(right.Dimensions).All(
                static dimensions =>
                    dimensions.First.LowerBound == dimensions.Second.LowerBound
                    && dimensions.First.UpperBound == dimensions.Second.UpperBound);
        }

        private bool SameFunctionPointer(
            FunctionPointerType left,
            bool substituteLeft,
            FunctionPointerType right,
            bool substituteRight) =>
            left.HasThis == right.HasThis
            && left.ExplicitThis == right.ExplicitThis
            && left.CallingConvention == right.CallingConvention
            && left.Parameters.Count == right.Parameters.Count
            && Same(left.ReturnType, substituteLeft, right.ReturnType, substituteRight)
            && left.Parameters.Zip(
                right.Parameters,
                (l, r) => Same(l.ParameterType, substituteLeft, r.ParameterType, substituteRight))
                .All(static match => match);
    }

    private static bool Success(out string? error)
    {
        error = null;
        return true;
    }
}

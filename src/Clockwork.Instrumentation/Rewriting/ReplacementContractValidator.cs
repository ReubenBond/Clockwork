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
        Shape(type, owner, []);

    private static string Shape(
        TypeReference type,
        MethodReference owner,
        HashSet<GenericParameter> expandingParameters)
    {
        if (type is GenericParameter parameter)
        {
            if (TryGetGenericArgument(parameter, owner, out TypeReference? argument)
                && !ReferenceEquals(parameter, argument)
                && expandingParameters.Add(parameter))
            {
                string shape = Shape(argument!, owner, expandingParameters);
                expandingParameters.Remove(parameter);
                return shape;
            }

            return (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position;
        }

        return type switch
        {
            ByReferenceType byReference => Shape(byReference.ElementType, owner, expandingParameters) + "&",
            PointerType pointer => Shape(pointer.ElementType, owner, expandingParameters) + "*",
            ArrayType array => Shape(array.ElementType, owner, expandingParameters) + ShapeArrayDimensions(array),
            GenericInstanceType generic =>
                Shape(generic.ElementType, owner, expandingParameters) + "<" +
                string.Join(",", generic.GenericArguments.Select(
                    argument => Shape(argument, owner, expandingParameters))) + ">",
            RequiredModifierType modifier =>
                Shape(modifier.ElementType, owner, expandingParameters) + " modreq(" +
                Shape(modifier.ModifierType, owner, expandingParameters) + ")",
            OptionalModifierType modifier =>
                Shape(modifier.ElementType, owner, expandingParameters) + " modopt(" +
                Shape(modifier.ModifierType, owner, expandingParameters) + ")",
            PinnedType pinned => Shape(pinned.ElementType, owner, expandingParameters) + " pinned",
            SentinelType sentinel => Shape(sentinel.ElementType, owner, expandingParameters) + " sentinel",
            FunctionPointerType functionPointer => Shape(functionPointer, owner, expandingParameters),
            _ => type.FullName,
        };
    }

    private static string Shape(
        FunctionPointerType functionPointer,
        MethodReference owner,
        HashSet<GenericParameter> expandingParameters) =>
        $"method[{functionPointer.CallingConvention};this={functionPointer.HasThis};explicit={functionPointer.ExplicitThis}] " +
        Shape(functionPointer.ReturnType, owner, expandingParameters) + " *(" +
        string.Join(",", functionPointer.Parameters.Select(
            parameter => Shape(parameter.ParameterType, owner, expandingParameters))) + ")";

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
        InflateType(type, owner, []);

    private static TypeReference InflateType(
        TypeReference type,
        MethodReference owner,
        HashSet<GenericParameter> expandingParameters)
    {
        if (type is GenericParameter parameter)
        {
            if (TryGetGenericArgument(parameter, owner, out TypeReference? argument)
                && !ReferenceEquals(parameter, argument)
                && expandingParameters.Add(parameter))
            {
                TypeReference inflated = InflateType(argument!, owner, expandingParameters);
                expandingParameters.Remove(parameter);
                return inflated;
            }

            return type;
        }

        if (type is GenericInstanceType generic)
        {
            var inflated = new GenericInstanceType(generic.ElementType);
            foreach (TypeReference argument in generic.GenericArguments)
            {
                inflated.GenericArguments.Add(InflateType(argument, owner, expandingParameters));
            }

            return inflated;
        }

        return type switch
        {
            ByReferenceType byReference =>
                new ByReferenceType(InflateType(byReference.ElementType, owner, expandingParameters)),
            PointerType pointer =>
                new PointerType(InflateType(pointer.ElementType, owner, expandingParameters)),
            ArrayType array => InflateArray(array, owner, expandingParameters),
            RequiredModifierType modifier => new RequiredModifierType(
                InflateType(modifier.ModifierType, owner, expandingParameters),
                InflateType(modifier.ElementType, owner, expandingParameters)),
            OptionalModifierType modifier => new OptionalModifierType(
                InflateType(modifier.ModifierType, owner, expandingParameters),
                InflateType(modifier.ElementType, owner, expandingParameters)),
            PinnedType pinned =>
                new PinnedType(InflateType(pinned.ElementType, owner, expandingParameters)),
            SentinelType sentinel =>
                new SentinelType(InflateType(sentinel.ElementType, owner, expandingParameters)),
            FunctionPointerType functionPointer =>
                InflateFunctionPointer(functionPointer, owner, expandingParameters),
            _ => type,
        };
    }

    private static ArrayType InflateArray(
        ArrayType array,
        MethodReference owner,
        HashSet<GenericParameter> expandingParameters)
    {
        TypeReference element = InflateType(array.ElementType, owner, expandingParameters);
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
        HashSet<GenericParameter> expandingParameters)
    {
        var inflated = new FunctionPointerType
        {
            HasThis = functionPointer.HasThis,
            ExplicitThis = functionPointer.ExplicitThis,
            CallingConvention = functionPointer.CallingConvention,
            ReturnType = InflateType(functionPointer.ReturnType, owner, expandingParameters),
        };
        foreach (ParameterDefinition parameter in functionPointer.Parameters)
        {
            inflated.Parameters.Add(new ParameterDefinition(
                InflateType(parameter.ParameterType, owner, expandingParameters)));
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
        private readonly HashSet<(TypeReference Left, TypeReference Right)> _visited = [];

        public bool Same(TypeReference left, TypeReference right)
        {
            left = Substitute(left, leftOwner);
            right = Substitute(right, rightOwner);
            if (!_visited.Add((left, right)))
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
                (ByReferenceType l, ByReferenceType r) => Same(l.ElementType, r.ElementType),
                (PointerType l, PointerType r) => Same(l.ElementType, r.ElementType),
                (ArrayType l, ArrayType r) => SameArray(l, r),
                (GenericInstanceType l, GenericInstanceType r) =>
                    Same(l.ElementType, r.ElementType)
                    && l.GenericArguments.Count == r.GenericArguments.Count
                    && l.GenericArguments.Zip(r.GenericArguments, Same).All(static match => match),
                (RequiredModifierType l, RequiredModifierType r) =>
                    Same(l.ModifierType, r.ModifierType) && Same(l.ElementType, r.ElementType),
                (OptionalModifierType l, OptionalModifierType r) =>
                    Same(l.ModifierType, r.ModifierType) && Same(l.ElementType, r.ElementType),
                (PinnedType l, PinnedType r) => Same(l.ElementType, r.ElementType),
                (SentinelType l, SentinelType r) => Same(l.ElementType, r.ElementType),
                (FunctionPointerType l, FunctionPointerType r) => SameFunctionPointer(l, r),
                _ => left is not TypeSpecification
                    && right is not TypeSpecification
                    && string.Equals(left.FullName, right.FullName, StringComparison.Ordinal),
            };
        }

        private static TypeReference Substitute(TypeReference type, MethodReference owner) =>
            type is GenericParameter parameter
                && TryGetGenericArgument(parameter, owner, out TypeReference? argument)
                && !ReferenceEquals(type, argument)
                    ? argument!
                    : type;

        private bool SameArray(ArrayType left, ArrayType right)
        {
            if (left.Rank != right.Rank
                || left.IsVector != right.IsVector
                || left.Dimensions.Count != right.Dimensions.Count
                || !Same(left.ElementType, right.ElementType))
            {
                return false;
            }

            return left.Dimensions.Zip(right.Dimensions).All(
                static dimensions =>
                    dimensions.First.LowerBound == dimensions.Second.LowerBound
                    && dimensions.First.UpperBound == dimensions.Second.UpperBound);
        }

        private bool SameFunctionPointer(FunctionPointerType left, FunctionPointerType right) =>
            left.HasThis == right.HasThis
            && left.ExplicitThis == right.ExplicitThis
            && left.CallingConvention == right.CallingConvention
            && left.Parameters.Count == right.Parameters.Count
            && Same(left.ReturnType, right.ReturnType)
            && left.Parameters.Zip(
                right.Parameters,
                (l, r) => Same(l.ParameterType, r.ParameterType)).All(static match => match);
    }

    private static bool Success(out string? error)
    {
        error = null;
        return true;
    }
}

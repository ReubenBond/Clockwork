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
        if (receiverCount == 1
            && !SameType(
                original.DeclaringType,
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
        string.Equals(Shape(left, leftOwner), Shape(right, rightOwner), StringComparison.Ordinal)
        || additionalTypeCompatibility(left, right);

    private static string Shape(TypeReference type, MethodReference owner)
    {
        if (type is GenericParameter parameter)
        {
            if (parameter.Type == GenericParameterType.Method
                && owner is GenericInstanceMethod genericMethod
                && parameter.Position < genericMethod.GenericArguments.Count)
            {
                return Shape(genericMethod.GenericArguments[parameter.Position], owner);
            }

            if (parameter.Type == GenericParameterType.Type
                && owner.DeclaringType is GenericInstanceType genericType
                && parameter.Position < genericType.GenericArguments.Count)
            {
                return Shape(genericType.GenericArguments[parameter.Position], owner);
            }

            return (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position;
        }

        return type switch
        {
            ByReferenceType byReference => Shape(byReference.ElementType, owner) + "&",
            PointerType pointer => Shape(pointer.ElementType, owner) + "*",
            ArrayType array => Shape(array.ElementType, owner) + "[" + new string(',', array.Rank - 1) + "]",
            GenericInstanceType generic =>
                generic.ElementType.FullName + "<" +
                string.Join(",", generic.GenericArguments.Select(argument => Shape(argument, owner))) + ">",
            RequiredModifierType modifier =>
                Shape(modifier.ElementType, owner) + " modreq(" + modifier.ModifierType.FullName + ")",
            OptionalModifierType modifier =>
                Shape(modifier.ElementType, owner) + " modopt(" + modifier.ModifierType.FullName + ")",
            _ => type.FullName,
        };
    }

    private static bool Success(out string? error)
    {
        error = null;
        return true;
    }
}

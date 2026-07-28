using System.Runtime.CompilerServices;
using System.Text;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>Compares and inflates Cecil type graphs without assuming that they are acyclic.</summary>
internal static class TypeReferenceStructure
{
    public static bool AreEquivalent(
        TypeReference left,
        MethodReference? leftOwner,
        TypeReference right,
        MethodReference? rightOwner) =>
        AreEquivalent(
            left,
            leftOwner,
            right,
            rightOwner,
            new HashSet<TypePair>(TypePairComparer.Instance));

    public static string Shape(TypeReference type, MethodReference owner) =>
        Shape(type, owner, new Dictionary<TypeReference, int>(ReferenceEqualityComparer.Instance));

    public static TypeReference Inflate(TypeReference type, MethodReference owner) =>
        Inflate(
            type,
            candidate => ResolveGenericParameter(candidate, owner),
            new Dictionary<TypeReference, TypeReference>(ReferenceEqualityComparer.Instance));

    public static TypeReference Inflate(TypeReference type, FieldReference owner) =>
        Inflate(
            type,
            candidate => ResolveGenericParameter(candidate, owner),
            new Dictionary<TypeReference, TypeReference>(ReferenceEqualityComparer.Instance));

    private static bool AreEquivalent(
        TypeReference left,
        MethodReference? leftOwner,
        TypeReference right,
        MethodReference? rightOwner,
        HashSet<TypePair> visited)
    {
        if (ReferenceEquals(left, right) && ReferenceEquals(leftOwner, rightOwner))
        {
            return true;
        }

        if (!visited.Add(new TypePair(left, right)))
        {
            return true;
        }

        TypeReference resolvedLeft = ResolveGenericParameter(left, leftOwner);
        TypeReference resolvedRight = ResolveGenericParameter(right, rightOwner);
        if (!ReferenceEquals(resolvedLeft, left) || !ReferenceEquals(resolvedRight, right))
        {
            return AreEquivalent(resolvedLeft, leftOwner, resolvedRight, rightOwner, visited);
        }

        if (left is GenericParameter || right is GenericParameter)
        {
            return left is GenericParameter leftGeneric
                && right is GenericParameter rightGeneric
                && leftGeneric.Type == rightGeneric.Type
                && leftGeneric.Position == rightGeneric.Position;
        }

        return (left, right) switch
        {
            (ByReferenceType l, ByReferenceType r) =>
                AreEquivalent(l.ElementType, leftOwner, r.ElementType, rightOwner, visited),
            (PointerType l, PointerType r) =>
                AreEquivalent(l.ElementType, leftOwner, r.ElementType, rightOwner, visited),
            (ArrayType l, ArrayType r) =>
                ArraysAreEquivalent(l, leftOwner, r, rightOwner, visited),
            (GenericInstanceType l, GenericInstanceType r) =>
                GenericInstancesAreEquivalent(l, leftOwner, r, rightOwner, visited),
            (RequiredModifierType l, RequiredModifierType r) =>
                AreEquivalent(l.ModifierType, leftOwner, r.ModifierType, rightOwner, visited)
                && AreEquivalent(l.ElementType, leftOwner, r.ElementType, rightOwner, visited),
            (OptionalModifierType l, OptionalModifierType r) =>
                AreEquivalent(l.ModifierType, leftOwner, r.ModifierType, rightOwner, visited)
                && AreEquivalent(l.ElementType, leftOwner, r.ElementType, rightOwner, visited),
            (PinnedType l, PinnedType r) =>
                AreEquivalent(l.ElementType, leftOwner, r.ElementType, rightOwner, visited),
            (SentinelType l, SentinelType r) =>
                AreEquivalent(l.ElementType, leftOwner, r.ElementType, rightOwner, visited),
            (FunctionPointerType l, FunctionPointerType r) =>
                FunctionPointersAreEquivalent(l, leftOwner, r, rightOwner, visited),
            (TypeSpecification, _) or (_, TypeSpecification) => false,
            _ => string.Equals(left.FullName, right.FullName, StringComparison.Ordinal),
        };
    }

    private static bool ArraysAreEquivalent(
        ArrayType left,
        MethodReference? leftOwner,
        ArrayType right,
        MethodReference? rightOwner,
        HashSet<TypePair> visited)
    {
        if (left.Rank != right.Rank
            || left.IsVector != right.IsVector
            || left.Dimensions.Count != right.Dimensions.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Dimensions.Count; i++)
        {
            ArrayDimension leftDimension = left.Dimensions[i];
            ArrayDimension rightDimension = right.Dimensions[i];
            if (leftDimension.LowerBound != rightDimension.LowerBound
                || leftDimension.UpperBound != rightDimension.UpperBound)
            {
                return false;
            }
        }

        return AreEquivalent(left.ElementType, leftOwner, right.ElementType, rightOwner, visited);
    }

    private static bool GenericInstancesAreEquivalent(
        GenericInstanceType left,
        MethodReference? leftOwner,
        GenericInstanceType right,
        MethodReference? rightOwner,
        HashSet<TypePair> visited)
    {
        if (left.GenericArguments.Count != right.GenericArguments.Count
            || !AreEquivalent(left.ElementType, leftOwner, right.ElementType, rightOwner, visited))
        {
            return false;
        }

        for (int i = 0; i < left.GenericArguments.Count; i++)
        {
            if (!AreEquivalent(
                left.GenericArguments[i],
                leftOwner,
                right.GenericArguments[i],
                rightOwner,
                visited))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FunctionPointersAreEquivalent(
        FunctionPointerType left,
        MethodReference? leftOwner,
        FunctionPointerType right,
        MethodReference? rightOwner,
        HashSet<TypePair> visited)
    {
        if (left.HasThis != right.HasThis
            || left.ExplicitThis != right.ExplicitThis
            || left.CallingConvention != right.CallingConvention
            || left.GenericParameters.Count != right.GenericParameters.Count
            || left.Parameters.Count != right.Parameters.Count
            || !AreEquivalent(left.ReturnType, leftOwner, right.ReturnType, rightOwner, visited))
        {
            return false;
        }

        for (int i = 0; i < left.Parameters.Count; i++)
        {
            if (!AreEquivalent(
                left.Parameters[i].ParameterType,
                leftOwner,
                right.Parameters[i].ParameterType,
                rightOwner,
                visited))
            {
                return false;
            }
        }

        return true;
    }

    private static string Shape(
        TypeReference type,
        MethodReference owner,
        Dictionary<TypeReference, int> active)
    {
        if (active.TryGetValue(type, out int recursionIndex))
        {
            return "#" + recursionIndex;
        }

        active.Add(type, active.Count);
        try
        {
            TypeReference resolved = ResolveGenericParameter(type, owner);
            if (!ReferenceEquals(resolved, type))
            {
                return Shape(resolved, owner, active);
            }

            if (type is GenericParameter parameter)
            {
                return GenericParameterShape(parameter);
            }

            return type switch
            {
                ByReferenceType byReference => Shape(byReference.ElementType, owner, active) + "&",
                PointerType pointer => Shape(pointer.ElementType, owner, active) + "*",
                ArrayType array => ArrayShape(array, owner, active),
                GenericInstanceType generic => GenericInstanceShape(generic, owner, active),
                RequiredModifierType modifier =>
                    Shape(modifier.ElementType, owner, active)
                    + " modreq(" + Shape(modifier.ModifierType, owner, active) + ")",
                OptionalModifierType modifier =>
                    Shape(modifier.ElementType, owner, active)
                    + " modopt(" + Shape(modifier.ModifierType, owner, active) + ")",
                PinnedType pinned => Shape(pinned.ElementType, owner, active) + " pinned",
                SentinelType sentinel => Shape(sentinel.ElementType, owner, active) + " sentinel",
                FunctionPointerType functionPointer => FunctionPointerShape(functionPointer, owner, active),
                _ => type.FullName,
            };
        }
        finally
        {
            active.Remove(type);
        }
    }

    private static string ArrayShape(
        ArrayType array,
        MethodReference owner,
        Dictionary<TypeReference, int> active)
    {
        if (array.IsVector)
        {
            return Shape(array.ElementType, owner, active) + "[]";
        }

        var builder = new StringBuilder(Shape(array.ElementType, owner, active)).Append('[');
        for (int i = 0; i < array.Dimensions.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            ArrayDimension dimension = array.Dimensions[i];
            if (dimension.IsSized)
            {
                builder.Append(dimension.LowerBound).Append("...").Append(dimension.UpperBound);
            }
        }

        return builder.Append(']').ToString();
    }

    private static string GenericInstanceShape(
        GenericInstanceType generic,
        MethodReference owner,
        Dictionary<TypeReference, int> active)
    {
        var builder = new StringBuilder(generic.ElementType.FullName).Append('<');
        for (int i = 0; i < generic.GenericArguments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Shape(generic.GenericArguments[i], owner, active));
        }

        return builder.Append('>').ToString();
    }

    private static string FunctionPointerShape(
        FunctionPointerType functionPointer,
        MethodReference owner,
        Dictionary<TypeReference, int> active)
    {
        var builder = new StringBuilder("method ")
            .Append(Shape(functionPointer.ReturnType, owner, active))
            .Append(" *(");
        for (int i = 0; i < functionPointer.Parameters.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Shape(functionPointer.Parameters[i].ParameterType, owner, active));
        }

        return builder.Append(')').ToString();
    }

    private static TypeReference Inflate(
        TypeReference type,
        Func<TypeReference, TypeReference> resolve,
        Dictionary<TypeReference, TypeReference> memo)
    {
        if (memo.TryGetValue(type, out TypeReference? cached))
        {
            return cached;
        }

        TypeReference resolved = resolve(type);
        if (!ReferenceEquals(resolved, type))
        {
            TypeReference inflated = Inflate(resolved, resolve, memo);
            memo[type] = inflated;
            return inflated;
        }

        TypeReference result;
        switch (type)
        {
            case GenericInstanceType generic:
                {
                    var instance = new GenericInstanceType(Inflate(generic.ElementType, resolve, memo));
                    memo[type] = instance;
                    foreach (TypeReference argument in generic.GenericArguments)
                    {
                        instance.GenericArguments.Add(Inflate(argument, resolve, memo));
                    }

                    return instance;
                }

            case FunctionPointerType functionPointer:
                {
                    var inflated = new FunctionPointerType
                    {
                        HasThis = functionPointer.HasThis,
                        ExplicitThis = functionPointer.ExplicitThis,
                        CallingConvention = functionPointer.CallingConvention,
                    };
                    memo[type] = inflated;
                    inflated.ReturnType = Inflate(functionPointer.ReturnType, resolve, memo);
                    foreach (ParameterDefinition parameter in functionPointer.Parameters)
                    {
                        inflated.Parameters.Add(new ParameterDefinition(
                            parameter.Name,
                            parameter.Attributes,
                            Inflate(parameter.ParameterType, resolve, memo)));
                    }

                    return inflated;
                }

            case ByReferenceType byReference:
                result = new ByReferenceType(Inflate(byReference.ElementType, resolve, memo));
                break;
            case PointerType pointer:
                result = new PointerType(Inflate(pointer.ElementType, resolve, memo));
                break;
            case ArrayType array:
                {
                    TypeReference element = Inflate(array.ElementType, resolve, memo);
                    var inflated = array.IsVector ? new ArrayType(element) : new ArrayType(element, array.Rank);
                    for (int i = 0; i < array.Dimensions.Count; i++)
                    {
                        ArrayDimension dimension = inflated.Dimensions[i];
                        dimension.LowerBound = array.Dimensions[i].LowerBound;
                        dimension.UpperBound = array.Dimensions[i].UpperBound;
                        inflated.Dimensions[i] = dimension;
                    }

                    result = inflated;
                    break;
                }

            case RequiredModifierType modifier:
                result = new RequiredModifierType(
                    Inflate(modifier.ModifierType, resolve, memo),
                    Inflate(modifier.ElementType, resolve, memo));
                break;
            case OptionalModifierType modifier:
                result = new OptionalModifierType(
                    Inflate(modifier.ModifierType, resolve, memo),
                    Inflate(modifier.ElementType, resolve, memo));
                break;
            case PinnedType pinned:
                result = new PinnedType(Inflate(pinned.ElementType, resolve, memo));
                break;
            case SentinelType sentinel:
                result = new SentinelType(Inflate(sentinel.ElementType, resolve, memo));
                break;
            default:
                result = type;
                break;
        }

        memo[type] = result;
        return result;
    }

    private static TypeReference ResolveGenericParameter(TypeReference type, MethodReference? owner)
    {
        if (type is not GenericParameter parameter || owner is null)
        {
            return type;
        }

        if (parameter.Type == GenericParameterType.Method
            && owner is GenericInstanceMethod genericMethod
            && parameter.Position < genericMethod.GenericArguments.Count
            && HasSameOwner(parameter.Owner, genericMethod.ElementMethod))
        {
            return genericMethod.GenericArguments[parameter.Position];
        }

        if (parameter.Type == GenericParameterType.Type
            && owner.DeclaringType is GenericInstanceType genericType
            && parameter.Position < genericType.GenericArguments.Count
            && HasSameOwner(parameter.Owner, genericType.ElementType))
        {
            return genericType.GenericArguments[parameter.Position];
        }

        return type;
    }

    private static TypeReference ResolveGenericParameter(TypeReference type, FieldReference owner)
    {
        if (type is GenericParameter parameter
            && parameter.Type == GenericParameterType.Type
            && owner.DeclaringType is GenericInstanceType genericType
            && parameter.Position < genericType.GenericArguments.Count
            && HasSameOwner(parameter.Owner, genericType.ElementType))
        {
            return genericType.GenericArguments[parameter.Position];
        }

        return type;
    }

    private static bool HasSameOwner(IGenericParameterProvider parameterOwner, IGenericParameterProvider candidate)
    {
        if (ReferenceEquals(parameterOwner, candidate))
        {
            return true;
        }

        return (parameterOwner, candidate) switch
        {
            (TypeReference left, TypeReference right) =>
                string.Equals(left.FullName, right.FullName, StringComparison.Ordinal),
            (MethodReference left, MethodReference right) =>
                string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                && string.Equals(left.DeclaringType.FullName, right.DeclaringType.FullName, StringComparison.Ordinal)
                && left.GenericParameters.Count == right.GenericParameters.Count,
            _ => false,
        };
    }

    private static string GenericParameterShape(GenericParameter parameter) =>
        (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position;

    private readonly record struct TypePair(TypeReference Left, TypeReference Right);

    private sealed class TypePairComparer : IEqualityComparer<TypePair>
    {
        public static TypePairComparer Instance { get; } = new();

        public bool Equals(TypePair x, TypePair y) =>
            ReferenceEquals(x.Left, y.Left) && ReferenceEquals(x.Right, y.Right);

        public int GetHashCode(TypePair obj) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Left), RuntimeHelpers.GetHashCode(obj.Right));
    }
}

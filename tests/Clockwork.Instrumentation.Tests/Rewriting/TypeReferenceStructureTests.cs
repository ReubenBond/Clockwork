using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Rewriting;

public sealed class TypeReferenceStructureTests
{
    [Fact]
    public void ContractValidationTerminatesForSelfReferentialGenericGraphs()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("recursive", ModuleKind.Dll);
        TypeReference open = Type(module, "Recursive`2");
        GenericInstanceType targetType = RecursiveInstance(open, module.TypeSystem.Int32);
        GenericInstanceType replacementType = RecursiveInstance(open, module.TypeSystem.Int32);
        MethodReference target = StaticMethod(module, "Target", targetType);
        MethodReference replacement = StaticMethod(module, "Replacement", replacementType);

        bool valid = ReplacementContractValidator.TryValidate(
            RewriteOperationKind.RedirectCall,
            target,
            replacement,
            static (_, _) => false,
            out string? error);

        Assert.True(valid, error);
    }

    [Fact]
    public void IncompatibleSelfReferentialGenericGraphsFailDeterministically()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("recursive", ModuleKind.Dll);
        TypeReference open = Type(module, "Recursive`2");
        GenericInstanceType targetType = RecursiveInstance(open, module.TypeSystem.Int32);
        GenericInstanceType replacementType = RecursiveInstance(open, module.TypeSystem.String);
        MethodReference target = StaticMethod(module, "Target", targetType);
        MethodReference replacement = StaticMethod(module, "Replacement", replacementType);

        bool valid = ReplacementContractValidator.TryValidate(
            RewriteOperationKind.RedirectCall,
            target,
            replacement,
            static (_, _) => false,
            out string? error);

        Assert.False(valid);
        Assert.Equal(
            "Replacement 'Fx.Recursive`2<#0,System.String> Fx.Contract::Replacement()' returns " +
            "'Fx.Recursive`2<#0,System.String>', but target 'Fx.Recursive`2<#0,System.Int32> " +
            "Fx.Contract::Target()' requires 'Fx.Recursive`2<#0,System.Int32>'.",
            error);
    }

    [Fact]
    public void StructuralComparisonHandlesCyclicSpecificationsAndFunctionPointers()
    {
        using ModuleDefinition module = ModuleDefinition.CreateModule("recursive", ModuleKind.Dll);
        FunctionPointerType left = FunctionPointer(module, arrayRank: 2);
        FunctionPointerType right = FunctionPointer(module, arrayRank: 2);
        FunctionPointerType different = FunctionPointer(module, arrayRank: 1);

        Assert.True(TypeReferenceStructure.AreEquivalent(left, null, right, null));
        Assert.False(TypeReferenceStructure.AreEquivalent(left, null, different, null));
    }

    private static FunctionPointerType FunctionPointer(ModuleDefinition module, int arrayRank)
    {
        TypeReference modifier = Type(module, "Modifier");
        TypeReference open = Type(module, "Recursive`2");
        GenericInstanceType recursive = RecursiveInstance(open, module.TypeSystem.Int32);
        var functionPointer = new FunctionPointerType
        {
            CallingConvention = MethodCallingConvention.C,
            ReturnType = new PointerType(recursive),
        };
        functionPointer.Parameters.Add(new ParameterDefinition(
            new ByReferenceType(new ArrayType(recursive, arrayRank))));
        functionPointer.Parameters.Add(new ParameterDefinition(
            new RequiredModifierType(modifier, recursive)));
        functionPointer.Parameters.Add(new ParameterDefinition(
            new OptionalModifierType(modifier, new PinnedType(new SentinelType(recursive)))));
        return functionPointer;
    }

    private static GenericInstanceType RecursiveInstance(TypeReference open, TypeReference leaf)
    {
        var instance = new GenericInstanceType(open);
        instance.GenericArguments.Add(instance);
        instance.GenericArguments.Add(leaf);
        return instance;
    }

    private static MethodReference StaticMethod(ModuleDefinition module, string name, TypeReference returnType) =>
        new(name, returnType, Type(module, "Contract"));

    private static TypeReference Type(ModuleDefinition module, string name) =>
        new("Fx", name, module, module.TypeSystem.CoreLibrary);
}

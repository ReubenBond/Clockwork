using System.Collections.Immutable;
using System.Reflection;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Infrastructure;

/// <summary>
/// The reference assemblies of the shared framework this project compiles against, read once per test run, with the
/// helpers that name their members the way a call site and a rule target do. Cecil reads metadata lazily and is not
/// thread-safe, so every test class that reads <see cref="Shared"/> belongs to <see cref="ReferencePackTests"/>.
/// </summary>
internal sealed class ReferencePack
{
    private static readonly Lazy<ReferencePack> Instance = new(() => new ReferencePack(FindDirectory()));

    private ReferencePack(string directory)
    {
        Directory = directory;
        Resolver = new DefaultAssemblyResolver();
        Resolver.AddSearchDirectory(directory);

        var modules = ImmutableArray.CreateBuilder<ModuleDefinition>();
        var types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        foreach (string path in System.IO.Directory.GetFiles(directory, "*.dll").Order(StringComparer.Ordinal))
        {
            ModuleDefinition module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = Resolver, InMemory = true });
            modules.Add(module);
            foreach (TypeDefinition type in module.GetTypes().Where(IsVisible))
            {
                types.TryAdd(type.FullName, type);
            }
        }

        Modules = modules.ToImmutable();
        Types = types;
    }

    /// <summary>Gets the reference pack of the shared framework this project compiles against.</summary>
    public static ReferencePack Shared => Instance.Value;

    /// <summary>Gets the directory of the reference assemblies.</summary>
    public string Directory { get; }

    /// <summary>Gets the resolver that resolves references between the reference assemblies.</summary>
    public DefaultAssemblyResolver Resolver { get; }

    /// <summary>Gets every reference assembly's module, ordered by file name.</summary>
    public ImmutableArray<ModuleDefinition> Modules { get; }

    /// <summary>Gets the public and protected types, keyed by Cecil full name.</summary>
    public IReadOnlyDictionary<string, TypeDefinition> Types { get; }

    /// <summary>Gets the file names of the reference assemblies, in ordinal order.</summary>
    public IEnumerable<string> AssemblyFileNames => Modules.Select(module => Path.GetFileName(module.FileName));

    /// <summary>Gets the public and protected methods and fields <paramref name="type"/> declares.</summary>
    public static IEnumerable<IMemberDefinition> VisibleMembers(TypeDefinition type) =>
        type.Methods.Where(IsVisible).Cast<IMemberDefinition>().Concat(type.Fields.Where(IsVisible));

    /// <summary>
    /// Names a member as a call site does: <c>Name(parameters)</c> for a method, with <c>``N</c> after the name of a
    /// method with N generic parameters, and <c>Name</c> for a field.
    /// </summary>
    public static string MemberKey(IMemberDefinition member) => member switch
    {
        MethodDefinition method =>
            method.Name
            + (method.HasGenericParameters ? "``" + method.GenericParameters.Count : string.Empty)
            + "(" + string.Join(",", method.Parameters.Select(parameter => CallSiteName(parameter.ParameterType))) + ")",
        _ => member.Name,
    };

    /// <summary>
    /// Returns the full name a call site's member reference gives <paramref name="type"/>, which is what rule targets
    /// match: generic parameters are positional (<c>!0</c> for the declaring type's, <c>!!0</c> for the method's).
    /// </summary>
    public static string CallSiteName(TypeReference type) => CallSiteType(type).FullName;

    /// <summary>Returns a reference to <paramref name="method"/> shaped as a call site names it.</summary>
    public static MethodReference CallSite(MethodDefinition method)
    {
        var reference = new MethodReference(method.Name, method.ReturnType, method.DeclaringType) { HasThis = method.HasThis };
        foreach (ParameterDefinition parameter in method.Parameters)
        {
            reference.Parameters.Add(new ParameterDefinition(CallSiteType(parameter.ParameterType)));
        }

        return reference;
    }

    public static bool IsVisible(TypeDefinition type) =>
        type.IsNested
            ? (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamilyOrAssembly) && IsVisible(type.DeclaringType)
            : type.IsPublic;

    public static bool IsVisible(MethodDefinition method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    public static bool IsVisible(FieldDefinition field) => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static TypeReference CallSiteType(TypeReference type)
    {
        switch (type)
        {
            case GenericParameter parameter:
                return new GenericParameter((parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position, parameter.Owner);
            case GenericInstanceType generic:
                var instance = new GenericInstanceType(generic.ElementType);
                foreach (TypeReference argument in generic.GenericArguments)
                {
                    instance.GenericArguments.Add(CallSiteType(argument));
                }

                return instance;
            case ByReferenceType byReference:
                return new ByReferenceType(CallSiteType(byReference.ElementType));
            case PointerType pointer:
                return new PointerType(CallSiteType(pointer.ElementType));
            case ArrayType { IsVector: true } array:
                return new ArrayType(CallSiteType(array.ElementType));
            case RequiredModifierType modifier:
                return new RequiredModifierType(modifier.ModifierType, CallSiteType(modifier.ElementType));
            case OptionalModifierType modifier:
                return new OptionalModifierType(modifier.ModifierType, CallSiteType(modifier.ElementType));
            default:
                return type;
        }
    }

    private static string FindDirectory()
    {
        string? directory = typeof(ReferencePack).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "ReferencePackDirectory")?.Value;
        Assert.False(string.IsNullOrEmpty(directory), "The test assembly does not record its reference pack directory.");
        Assert.True(File.Exists(Path.Combine(directory, "System.Runtime.dll")), $"No reference pack at '{directory}'.");
        return directory;
    }
}

/// <summary>Runs the test classes that read <see cref="ReferencePack.Shared"/> one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class ReferencePackTests
{
    /// <summary>The collection name.</summary>
    public const string Name = "Reference pack";
}

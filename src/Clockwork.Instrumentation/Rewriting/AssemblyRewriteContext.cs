// Portions of this file (assembly loading with a search-directory resolver, symbol-availability
// detection, mixed-mode detection, portable-PDB write configuration, and the rewrite-signature
// custom-attribute read/apply pattern) are adapted from Microsoft Coyote's
// Source/Test/Rewriting/AssemblyInfo.cs, licensed under the MIT License:
//
//   Copyright (c) Microsoft Corporation.
//   Licensed under the MIT License.
//
// See THIRD-PARTY-NOTICES.md for the adaptation record. Clockwork-specific changes: the
// rewrite-signature attribute carries an engine version, rule-set id, rule-set version, and a
// content hash (rather than Coyote's version + configuration signature); symbol handling
// distinguishes portable vs. embedded vs. unsupported forms and reports absence explicitly; and
// resolution failures surface as structured RewriteDiagnostic values instead of log lines.

using Clockwork.Instrumentation.Attributes;
using Clockwork.Instrumentation.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>
/// Loads a single assembly for rewriting, exposes its Mono.Cecil <see cref="AssemblyDefinition"/>,
/// detects and preserves its debug-symbol form, reads and applies the idempotence signature marker
/// (<see cref="ClockworkRewriteSignatureAttribute"/>), and writes the rewritten result. Instances
/// own an assembly resolver and the underlying <see cref="AssemblyDefinition"/> and must be disposed.
/// </summary>
internal sealed class AssemblyRewriteContext : IDisposable
{
    private static readonly string SignatureAttributeNamespace =
        typeof(ClockworkRewriteSignatureAttribute).Namespace ?? string.Empty;

    private static readonly string SignatureAttributeName = nameof(ClockworkRewriteSignatureAttribute);

    private static readonly string DoNotRewriteAttributeFullName = typeof(DoNotRewriteAttribute).FullName!;

    private readonly DefaultAssemblyResolver _resolver;
    private readonly List<RewriteDiagnostic> _loadDiagnostics = [];
    private bool _disposed;

    private AssemblyRewriteContext(
        string filePath,
        AssemblyDefinition definition,
        DefaultAssemblyResolver resolver,
        SymbolKind symbolKind)
    {
        FilePath = filePath;
        Definition = definition;
        _resolver = resolver;
        Symbols = symbolKind;
    }

    /// <summary>Gets the path the assembly was loaded from.</summary>
    public string FilePath { get; }

    /// <summary>Gets the underlying Mono.Cecil assembly definition.</summary>
    public AssemblyDefinition Definition { get; }

    /// <summary>Gets the assembly's main module.</summary>
    public ModuleDefinition MainModule => Definition.MainModule;

    /// <summary>Gets the simple name of the assembly.</summary>
    public string Name => Definition.Name.Name;

    /// <summary>Gets the detected debug-symbol form.</summary>
    public SymbolKind Symbols { get; }

    /// <summary>Gets the diagnostics produced while loading (e.g. symbol absence).</summary>
    public IReadOnlyList<RewriteDiagnostic> LoadDiagnostics => _loadDiagnostics;

    /// <summary>
    /// Loads the assembly at <paramref name="filePath"/>, resolving references from the assembly's
    /// own directory plus any <paramref name="searchDirectories"/>.
    /// </summary>
    /// <param name="filePath">The path to the assembly to load.</param>
    /// <param name="searchDirectories">Additional directories to search when resolving references.</param>
    /// <param name="readSymbols">Whether to attempt to read debug symbols.</param>
    /// <param name="onResolveFailure">Optional callback invoked when a reference cannot be resolved.</param>
    public static AssemblyRewriteContext Load(
        string filePath,
        IEnumerable<string>? searchDirectories = null,
        bool readSymbols = true,
        Action<AssemblyNameReference>? onResolveFailure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Assembly to rewrite was not found: '{filePath}'.", filePath);
        }

        var resolver = new DefaultAssemblyResolver();
        string? assemblyDir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            resolver.AddSearchDirectory(assemblyDir);
        }

        if (searchDirectories is not null)
        {
            foreach (string dir in searchDirectories)
            {
                if (!string.IsNullOrEmpty(dir))
                {
                    resolver.AddSearchDirectory(dir);
                }
            }
        }

        if (onResolveFailure is not null)
        {
            resolver.ResolveFailure += (_, reference) =>
            {
                onResolveFailure(reference);
                return null;
            };
        }

        string pdbPath = Path.ChangeExtension(filePath, "pdb");
        bool separatePdbExists = File.Exists(pdbPath);
        var diagnostics = new List<RewriteDiagnostic>();

        AssemblyDefinition definition;
        var symbolKind = SymbolKind.None;
        if (readSymbols)
        {
            try
            {
                definition = AssemblyDefinition.ReadAssembly(filePath, new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadSymbols = true,
                    InMemory = true,
                });

                string readerName = definition.MainModule.SymbolReader?.GetType().Name ?? string.Empty;
                if (readerName.Contains("Portable", StringComparison.Ordinal))
                {
                    symbolKind = separatePdbExists ? SymbolKind.Portable : SymbolKind.Embedded;
                }
                else if (definition.MainModule.SymbolReader is not null)
                {
                    symbolKind = SymbolKind.Unsupported;
                    diagnostics.Add(RewriteDiagnostic.Warning(
                        RewriteDiagnosticIds.UnsupportedSymbolForm,
                        $"Assembly '{Path.GetFileName(filePath)}' uses an unsupported symbol form ('{readerName}'); " +
                        "the rewritten output will not carry debug symbols."));
                }
            }
            catch (Exception ex) when (ex is SymbolsNotFoundException or SymbolsNotMatchingException or BadImageFormatException)
            {
                definition = AssemblyDefinition.ReadAssembly(filePath, new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadSymbols = false,
                    InMemory = true,
                });
                diagnostics.Add(RewriteDiagnostic.Info(
                    RewriteDiagnosticIds.SymbolsAbsent,
                    $"No readable debug symbols were found for '{Path.GetFileName(filePath)}'; " +
                    "the rewritten output will not carry symbols and source mapping will be unavailable."));
            }
        }
        else
        {
            definition = AssemblyDefinition.ReadAssembly(filePath, new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadSymbols = false,
                InMemory = true,
            });
        }

        var context = new AssemblyRewriteContext(filePath, definition, resolver, symbolKind);
        context._loadDiagnostics.AddRange(diagnostics);
        return context;
    }

    /// <summary>
    /// Returns <see langword="true"/> if this assembly only contains IL (is not a mixed-mode
    /// assembly). Mono.Cecil cannot round-trip mixed-mode assemblies.
    /// </summary>
    public bool IsPureIL() => Definition.Modules.All(m => (m.Attributes & ModuleAttributes.ILOnly) != 0);

    /// <summary>
    /// Reads the idempotence signature marker previously applied by the engine, if any.
    /// </summary>
    public bool TryGetRewriteSignature(out ClockworkRewriteSignatureValues values)
    {
        CustomAttribute? attribute = FindSignatureAttribute();
        if (attribute is not null && attribute.ConstructorArguments.Count == 4)
        {
            values = new ClockworkRewriteSignatureValues(
                attribute.ConstructorArguments[0].Value as string ?? string.Empty,
                attribute.ConstructorArguments[1].Value as string ?? string.Empty,
                attribute.ConstructorArguments[2].Value as string ?? string.Empty,
                attribute.ConstructorArguments[3].Value as string ?? string.Empty);
            return true;
        }

        values = default;
        return false;
    }

    /// <summary>
    /// Applies (or replaces) the idempotence signature marker recording the engine and rule-set
    /// identity used for this rewrite.
    /// </summary>
    public void ApplyRewriteSignature(ClockworkRewriteSignatureValues values)
    {
        ModuleDefinition module = Definition.MainModule;
        TypeReference stringType = module.TypeSystem.String;
        CustomAttributeArgument[] args =
        [
            new(stringType, values.EngineVersion),
            new(stringType, values.RuleSetId),
            new(stringType, values.RuleSetVersion),
            new(stringType, values.Signature),
        ];

        CustomAttribute? existing = FindSignatureAttribute();
        if (existing is not null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                existing.ConstructorArguments[i] = args[i];
            }

            return;
        }

        MethodReference ctor = module.ImportReference(
            typeof(ClockworkRewriteSignatureAttribute).GetConstructor(
                [typeof(string), typeof(string), typeof(string), typeof(string)])!);
        var attribute = new CustomAttribute(ctor);
        foreach (CustomAttributeArgument arg in args)
        {
            attribute.ConstructorArguments.Add(arg);
        }

        Definition.CustomAttributes.Add(attribute);
    }

    /// <summary>
    /// Returns <see langword="true"/> if the specified type is excluded from rewriting via
    /// <see cref="DoNotRewriteAttribute"/>, and outputs the recorded reason.
    /// </summary>
    public static bool IsExcludedByAttribute(TypeDefinition type, out string reason)
    {
        foreach (CustomAttribute attribute in type.CustomAttributes)
        {
            if (attribute.AttributeType.FullName == DoNotRewriteAttributeFullName)
            {
                reason = attribute.ConstructorArguments.Count > 0
                    ? attribute.ConstructorArguments[0].Value as string ?? string.Empty
                    : string.Empty;
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }

    /// <summary>
    /// Writes the (possibly rewritten) assembly to <paramref name="outputPath"/>, preserving the
    /// detected debug-symbol form where the engine supports it.
    /// </summary>
    public void Write(string outputPath) => Write(outputPath, strongNameKeyBlob: null);

    /// <summary>
    /// Writes the (possibly rewritten) assembly to <paramref name="outputPath"/>, preserving the
    /// detected debug-symbol form and, when <paramref name="strongNameKeyBlob"/> is supplied,
    /// (re-)signing the output with that CryptoAPI key blob.
    /// </summary>
    /// <param name="outputPath">The destination path.</param>
    /// <param name="strongNameKeyBlob">
    /// The CryptoAPI strong-name key blob to sign the output with, or <see langword="null"/> to write
    /// the output unsigned. Mono.Cecil does not preserve an existing strong-name signature across a
    /// write, so a signed assembly must be re-signed here to keep its strong name.
    /// </param>
    public void Write(string outputPath, byte[]? strongNameKeyBlob)
    {
        var parameters = new WriterParameters();
        switch (Symbols)
        {
            case SymbolKind.Portable:
                parameters.WriteSymbols = true;
                parameters.SymbolWriterProvider = new PortablePdbWriterProvider();
                break;
            case SymbolKind.Embedded:
                parameters.WriteSymbols = true;
                parameters.SymbolWriterProvider = new EmbeddedPortablePdbWriterProvider();
                break;
            default:
                parameters.WriteSymbols = false;
                break;
        }

        if (strongNameKeyBlob is { Length: > 0 })
        {
            parameters.StrongNameKeyBlob = strongNameKeyBlob;
        }

        Definition.Write(outputPath, parameters);
    }

    private CustomAttribute? FindSignatureAttribute() =>
        Definition.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType.Namespace == SignatureAttributeNamespace &&
            a.AttributeType.Name == SignatureAttributeName);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Definition.Dispose();
        _resolver.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// The four string values stored in a <see cref="ClockworkRewriteSignatureAttribute"/>.
/// </summary>
/// <param name="EngineVersion">The engine version that performed the rewrite.</param>
/// <param name="RuleSetId">The identity of the applied rule set.</param>
/// <param name="RuleSetVersion">The version of the applied rule set.</param>
/// <param name="Signature">The stable content hash of the applied rule set and engine version.</param>
internal readonly record struct ClockworkRewriteSignatureValues(
    string EngineVersion,
    string RuleSetId,
    string RuleSetVersion,
    string Signature);

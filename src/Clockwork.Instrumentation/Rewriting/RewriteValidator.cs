using Clockwork.Instrumentation.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>
/// Post-rewrite validation. Reads the written assembly back with Mono.Cecil (proving it is
/// structurally loadable) and checks per-method that every branch target and exception-handler
/// boundary points at an instruction that still exists in the method body. Any failure is reported as
/// a deterministic <see cref="RewriteDiagnosticIds.ValidationFailed"/> error rather than throwing.
/// </summary>
internal static class RewriteValidator
{
    public static IReadOnlyList<RewriteDiagnostic> Validate(string assemblyPath, IEnumerable<string> searchDirectories)
    {
        var diagnostics = new List<RewriteDiagnostic>();

        var resolver = new DefaultAssemblyResolver();
        string? directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (!string.IsNullOrEmpty(directory))
        {
            resolver.AddSearchDirectory(directory);
        }

        foreach (string dir in searchDirectories)
        {
            if (!string.IsNullOrEmpty(dir))
            {
                resolver.AddSearchDirectory(dir);
            }
        }

        try
        {
            using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
            {
                AssemblyResolver = resolver,
                ReadSymbols = false,
                InMemory = true,
            });

            foreach (ModuleDefinition module in definition.Modules)
            {
                foreach (TypeDefinition type in module.GetTypes())
                {
                    foreach (MethodDefinition method in type.Methods)
                    {
                        if (method.HasBody)
                        {
                            ValidateBody(method, diagnostics);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or Mono.Cecil.Cil.SymbolsNotMatchingException)
        {
            diagnostics.Add(RewriteDiagnostic.Error(
                RewriteDiagnosticIds.ValidationFailed,
                $"Read-back of rewritten assembly '{Path.GetFileName(assemblyPath)}' failed: {ex.Message}"));
        }

        return diagnostics;
    }

    private static void ValidateBody(MethodDefinition method, List<RewriteDiagnostic> diagnostics)
    {
        MethodBody body = method.Body;
        var instructions = new HashSet<Instruction>(body.Instructions);
        string name = CecilNames.FullyQualifiedMethodName(method);

        foreach (Instruction instruction in body.Instructions)
        {
            if (instruction.Operand is Instruction target && !instructions.Contains(target))
            {
                diagnostics.Add(RewriteDiagnostic.Error(
                    RewriteDiagnosticIds.ValidationFailed,
                    "A branch targets an instruction that is not part of the method body after rewriting.",
                    name,
                    instruction.Offset));
            }
            else if (instruction.Operand is Instruction[] targets)
            {
                foreach (Instruction switchTarget in targets)
                {
                    if (!instructions.Contains(switchTarget))
                    {
                        diagnostics.Add(RewriteDiagnostic.Error(
                            RewriteDiagnosticIds.ValidationFailed,
                            "A switch branch targets an instruction that is not part of the method body after rewriting.",
                            name,
                            instruction.Offset));
                    }
                }
            }
        }

        if (!body.HasExceptionHandlers)
        {
            return;
        }

        foreach (ExceptionHandler handler in body.ExceptionHandlers)
        {
            CheckBoundary(handler.TryStart, instructions, name, "try-start", diagnostics);
            CheckBoundary(handler.TryEnd, instructions, name, "try-end", diagnostics);
            CheckBoundary(handler.HandlerStart, instructions, name, "handler-start", diagnostics);
            CheckBoundary(handler.HandlerEnd, instructions, name, "handler-end", diagnostics);
            CheckBoundary(handler.FilterStart, instructions, name, "filter-start", diagnostics);
        }
    }

    private static void CheckBoundary(
        Instruction? boundary,
        HashSet<Instruction> instructions,
        string method,
        string label,
        List<RewriteDiagnostic> diagnostics)
    {
        if (boundary is not null && !instructions.Contains(boundary))
        {
            diagnostics.Add(RewriteDiagnostic.Error(
                RewriteDiagnosticIds.ValidationFailed,
                $"An exception-handler {label} boundary points at an instruction that is not part of the method body after rewriting.",
                method,
                boundary.Offset));
        }
    }
}

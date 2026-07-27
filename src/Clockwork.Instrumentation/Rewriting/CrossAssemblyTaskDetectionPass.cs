using System.Globalization;
using Clockwork.Instrumentation.Diagnostics;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>
/// Read-only rewriting pass that enforces cross-assembly task control (Phase 6B). It flags every call site
/// that invokes a method in an <em>uncontrolled</em> assembly - one that is neither the assembly being
/// rewritten, nor part of the BCL (<c>System.*</c> / <c>mscorlib</c> / <c>netstandard</c>), nor the
/// Clockwork runtime shim - and returns a <see cref="System.Threading.Tasks.Task"/>,
/// <see cref="System.Threading.Tasks.ValueTask"/>, their generic forms, or any other custom awaitable (a
/// type exposing a parameterless <c>GetAwaiter</c>). Such a task's continuations are produced by code the
/// rewriter never saw and can therefore escape the deterministic scheduler; the pass surfaces each with a
/// precise <see cref="RewriteDiagnosticIds.UncontrolledTaskReturn"/> warning naming the callee and the
/// call-site method/IL offset (and source line when symbols are present) so the escape is never silently
/// accepted. The pass runs last, after controlled BCL calls have already been redirected onto the shim, so
/// controlled surfaces are not re-flagged. HttpClient-specific control is deferred to Phase 10, so
/// <c>System.*</c> callees are intentionally not flagged.
/// </summary>
internal sealed class CrossAssemblyTaskDetectionPass : RewritePass
{
    private const string TaskNamespace = "System.Threading.Tasks";
    private const string ShimAssemblyName = "Clockwork.Runtime";

    private readonly HashSet<Instruction> _reported = [];

    public CrossAssemblyTaskDetectionPass(RewriteSession session)
        : base(session)
    {
    }

    protected override Instruction VisitInstruction(Instruction instruction)
    {
        if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj) ||
            instruction.Operand is not MethodReference method ||
            !_reported.Add(instruction))
        {
            return instruction;
        }

        if (!IsUncontrolledExternalCallee(method) || !ReturnsAwaitable(method, out string awaitableKind))
        {
            return instruction;
        }

        RewriteSession.TryGetSequencePoint(Method!, instruction, out string? file, out int line);
        string location = file is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" ({file}:{line})");

        string message = string.Create(
            CultureInfo.InvariantCulture,
            $"Call to '{method.FullName}' returns an uncontrolled {awaitableKind} from assembly '{AssemblyNameOf(method.DeclaringType)}'{location}; its continuations are not routed through the deterministic scheduler. Rewrite that assembly, await the result in rewritten code, or wrap it explicitly so the continuation cannot escape.");

        Session.AddDiagnostic(RewriteDiagnostic.Warning(
            RewriteDiagnosticIds.UncontrolledTaskReturn,
            message,
            CecilNames.FullyQualifiedMethodName(Method!),
            instruction.Offset));

        return instruction;
    }

    private bool IsUncontrolledExternalCallee(MethodReference method)
    {
        string assembly = AssemblyNameOf(method.DeclaringType);

        // The assembly currently being rewritten is controlled by definition.
        if (string.Equals(assembly, Session.TargetModule.Assembly?.Name?.Name, StringComparison.Ordinal))
        {
            return false;
        }

        // The Clockwork runtime shim (holds the controlled Task/Thread/ThreadPool surfaces) is controlled.
        if (string.Equals(assembly, ShimAssemblyName, StringComparison.Ordinal))
        {
            return false;
        }

        // The BCL is either controlled through explicit rules (already redirected before this pass) or is a
        // benign framework primitive; HttpClient-style control of System.* is deferred to Phase 10.
        return !IsFrameworkAssembly(assembly);
    }

    private static bool IsFrameworkAssembly(string assembly) =>
        assembly.Length == 0 ||
        string.Equals(assembly, "mscorlib", StringComparison.Ordinal) ||
        string.Equals(assembly, "netstandard", StringComparison.Ordinal) ||
        string.Equals(assembly, "System", StringComparison.Ordinal) ||
        assembly.StartsWith("System.", StringComparison.Ordinal);

    private static bool ReturnsAwaitable(MethodReference method, out string kind)
    {
        TypeReference returnType = method.ReturnType;

        if (returnType.Namespace == TaskNamespace)
        {
            switch (returnType.Name)
            {
                case "Task":
                case "Task`1":
                    kind = "Task";
                    return true;
                case "ValueTask":
                case "ValueTask`1":
                    kind = "ValueTask";
                    return true;
            }
        }

        if (HasParameterlessGetAwaiter(returnType))
        {
            kind = "custom awaitable";
            return true;
        }

        kind = string.Empty;
        return false;
    }

    private static bool HasParameterlessGetAwaiter(TypeReference returnType)
    {
        if (returnType.IsGenericParameter || returnType.IsByReference || returnType.IsPointer)
        {
            return false;
        }

        TypeDefinition? definition;
        try
        {
            definition = returnType.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return false;
        }

        while (definition is not null)
        {
            foreach (MethodDefinition candidate in definition.Methods)
            {
                if (candidate.Name == "GetAwaiter" && candidate.Parameters.Count == 0 && !candidate.IsStatic)
                {
                    return true;
                }
            }

            try
            {
                definition = definition.BaseType?.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return false;
            }
        }

        return false;
    }

    private static string AssemblyNameOf(TypeReference type) =>
        type.Scope switch
        {
            AssemblyNameReference assemblyName => assemblyName.Name,
            ModuleDefinition module => module.Assembly?.Name?.Name ?? string.Empty,
            _ => type.Module?.Assembly?.Name?.Name ?? string.Empty,
        };
}

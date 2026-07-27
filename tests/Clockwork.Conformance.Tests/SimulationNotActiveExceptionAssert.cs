using System.Reflection;
using Clockwork.Runtime.Shims;

namespace Clockwork.Conformance.Tests;

internal static class SimulationNotActiveExceptionAssert
{
    public static SimulationNotActiveException Throws(MethodInfo method, params object?[] args)
    {
        var invocation = Assert.Throws<TargetInvocationException>(
            () => method.Invoke(null, args.Length == 0 ? null : args));
        var exception = Assert.IsType<SimulationNotActiveException>(invocation.InnerException);

        Assert.False(string.IsNullOrWhiteSpace(exception.ApiName));
        Assert.Equal(SimulationNotActiveException.DiagnosticMessage, exception.Message);
        return exception;
    }
}

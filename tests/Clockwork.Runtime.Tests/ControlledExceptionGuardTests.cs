using System.IO;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Tests;

/// <summary>
/// Tests for the exception-handler hardening guard (Phase 6B slice 7). The instrumentation's
/// exception-hardening pass injects a call to
/// <see cref="ControlledExceptionGuard.ThrowIfControlSignal(object)"/> at the start of every broad user
/// <c>catch</c> block and exception filter. The guard must re-throw the scheduler's internal control-flow
/// signal (so it keeps unwinding past a broad user handler) while letting every ordinary exception pass
/// straight through untouched.
/// </summary>
public sealed class ControlledExceptionGuardTests
{
    [Fact]
    public void RethrowsTheInternalControlSignal()
    {
        var signal = new ControlledOperationAbortSignal(new ControlledOperationId(42));

        // The guard must re-surface the exact same signal instance, preserving its identity/stack.
        var rethrown = Assert.Throws<ControlledOperationAbortSignal>(
            () => ControlledExceptionGuard.ThrowIfControlSignal(signal));

        Assert.Same(signal, rethrown);
        Assert.Equal(new ControlledOperationId(42), rethrown.OperationId);
    }

    [Fact]
    public void LetsOrdinaryExceptionsPassThrough()
    {
        // A normal application exception a broad user catch is meant to handle must NOT be re-thrown by
        // the guard - it returns so the user handler runs exactly as written.
        ControlledExceptionGuard.ThrowIfControlSignal(new InvalidOperationException("boom"));
        ControlledExceptionGuard.ThrowIfControlSignal(new IOException("io"));
        ControlledExceptionGuard.ThrowIfControlSignal(new TimeoutException("base"));
    }

    [Fact]
    public void IgnoresNonExceptionAndNullOperands()
    {
        // The injected IL dups whatever is on the handler's evaluation stack; the guard must tolerate a
        // null or non-Exception operand without throwing.
        ControlledExceptionGuard.ThrowIfControlSignal(null);
        ControlledExceptionGuard.ThrowIfControlSignal("not an exception");
        ControlledExceptionGuard.ThrowIfControlSignal(42);
    }
}

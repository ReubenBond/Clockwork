namespace Clockwork.Runtime.Tests;

public sealed class ControlledApiExceptionTests
{
    [Fact]
    public void PreservesStructuredFailureDetails()
    {
        var exception = new ControlledApiException(
            ControlledApiCategory.ThreadPool,
            "System.Threading.ThreadPool.UnsafeQueueNativeOverlapped",
            "native I/O completion is not deterministic.");

        Assert.Equal(ControlledApiCategory.ThreadPool, exception.Category);
        Assert.Equal("System.Threading.ThreadPool.UnsafeQueueNativeOverlapped", exception.ApiName);
        Assert.Equal("native I/O completion is not deterministic.", exception.Reason);
        Assert.Contains("thread-pool", exception.Message, StringComparison.Ordinal);
    }
}

using Clockwork.Runtime;

namespace Clockwork.Runtime.Tests;

public sealed class SupportedRuntimesMarkerTests
{
    private static readonly Version Net11 = new(11, 0);
    private static readonly Version Net12 = new(12, 0);

    [Theory]
    [InlineData("11.0", "11.0", "[11.0,11.0]")]
    [InlineData("11.0", null, "[11.0,]")]
    [InlineData(null, "12.0", "[,12.0]")]
    [InlineData(null, null, "[,]")]
    public void FormatRoundTrips(string? minimum, string? maximum, string expected)
    {
        Version? low = minimum is null ? null : Version.Parse(minimum);
        Version? high = maximum is null ? null : Version.Parse(maximum);

        string marker = SupportedRuntimesMarker.Format(low, high);

        Assert.Equal(expected, marker);
        Assert.True(SupportedRuntimesMarker.TryParse(marker, out Version? parsedLow, out Version? parsedHigh));
        Assert.Equal(low, parsedLow);
        Assert.Equal(high, parsedHigh);
    }

    [Theory]
    [InlineData("")]
    [InlineData("11.0")]
    [InlineData("[11.0]")]
    [InlineData("[11.0,11.0,12.0]")]
    [InlineData("[11.0.1,]")]
    [InlineData("[12.0,11.0]")]
    [InlineData("[eleven,]")]
    public void TryParseRejectsMalformedValues(string value) =>
        Assert.False(SupportedRuntimesMarker.TryParse(value, out _, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("[11.0,11.0]")]
    [InlineData("[10.0,]")]
    [InlineData("[,11.0]")]
    [InlineData("[,]")]
    public void RuntimeInRangePasses(string? marker) =>
        SupportedRuntimesMarker.Verify("Fx.App", marker, Net11);

    [Theory]
    [InlineData("[11.0,11.0]", "11.0")]
    [InlineData("[10.0,11.0]", "10.0 to 11.0")]
    [InlineData("[,11.0]", "11.0 or earlier")]
    [InlineData("[13.0,]", "13.0 or later")]
    public void RuntimeOutsideRangeFails(string marker, string supported)
    {
        UnsupportedRuntimeException exception = Assert.Throws<UnsupportedRuntimeException>(
            () => SupportedRuntimesMarker.Verify("Fx.App", marker, Net12));

        Assert.Equal("Fx.App", exception.AssemblyName);
        Assert.Equal(supported, exception.SupportedRuntimes);
        Assert.Equal(Net12, exception.Runtime);
        Assert.Contains($"for .NET {supported}, but this process runs .NET 12.0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableMarkerFails()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SupportedRuntimesMarker.Verify("Fx.App", "11.0", Net11));

        Assert.Contains("unreadable value '11.0'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatRejectsPatchVersions() =>
        Assert.Throws<ArgumentException>(() => SupportedRuntimesMarker.Format(new Version(11, 0, 1), null));
}

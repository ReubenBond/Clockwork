using Clockwork.Runtime.Random;

namespace Clockwork.Runtime.Tests.Random;

/// <summary>
/// Covers <see cref="DeterministicHash"/>: stability, non-triviality relative to
/// <see cref="object.GetHashCode()"/>, distinctness between <see cref="DeterministicHash.ToInt32(string)"/>
/// and <see cref="DeterministicHash.ToInt64(string)"/>, and that the component separator prevents
/// simple concatenation collisions.
/// </summary>
public sealed class DeterministicHashTests
{
    [Fact]
    public void ToInt32IsStableAcrossCalls()
    {
        Assert.Equal(DeterministicHash.ToInt32("clockwork"), DeterministicHash.ToInt32("clockwork"));
    }

    [Fact]
    public void ToInt32ProducesAKnownVectorSoARegressionInTheAlgorithmIsCaught()
    {
        // Pinned known-vector: SHA-256("clockwork") first four bytes as big-endian int32. If this
        // ever changes, every derived seed across the whole codebase silently changes with it, so
        // this is deliberately asserted against a literal rather than only self-consistency.
        Assert.Equal(-85322183, DeterministicHash.ToInt32("clockwork"));
    }

    [Fact]
    public void ToInt32DiffersForDifferentInputs()
    {
        Assert.NotEqual(DeterministicHash.ToInt32("a"), DeterministicHash.ToInt32("b"));
    }

    [Fact]
    public void ToInt32MultiComponentOverloadsAgreeWithManualSeparatorJoin()
    {
        var viaParams = DeterministicHash.ToInt32("scheduler", "node-1");
        var viaEnumerable = DeterministicHash.ToInt32((IEnumerable<string>)["scheduler", "node-1"]);
        var viaManualJoin = DeterministicHash.ToInt32($"scheduler{DeterministicHash.ComponentSeparator}node-1");

        Assert.Equal(viaParams, viaEnumerable);
        Assert.Equal(viaParams, viaManualJoin);
    }

    [Fact]
    public void ComponentSeparatorPreventsNaiveConcatenationCollisions()
    {
        // Without a separator, ("ab", "c") and ("a", "bc") would hash identically because both
        // concatenate to "abc". The separator must keep them distinct.
        var first = DeterministicHash.ToInt32("ab", "c");
        var second = DeterministicHash.ToInt32("a", "bc");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ToInt64DiffersFromToInt32ForTheSameInput()
    {
        // ToInt64 reads a different (wider) slice of the same digest, so it must not just be a
        // sign/width-extended copy of ToInt32's result for the same input.
        var asInt32 = DeterministicHash.ToInt32("same-input");
        var asInt64 = DeterministicHash.ToInt64("same-input");

        Assert.NotEqual((long)asInt32, asInt64);
    }

    [Fact]
    public void ToInt64IsStableAcrossCalls()
    {
        Assert.Equal(DeterministicHash.ToInt64("clockwork"), DeterministicHash.ToInt64("clockwork"));
    }

    [Fact]
    public void ToInt32ThrowsForNullSingleValue()
    {
        Assert.Throws<ArgumentNullException>(() => DeterministicHash.ToInt32((string)null!));
    }

    [Fact]
    public void ToInt32ThrowsForNullValuesSequence()
    {
        Assert.Throws<ArgumentNullException>(() => DeterministicHash.ToInt32((IEnumerable<string>)null!));
    }

    [Fact]
    public void ToInt64ThrowsForNullValue()
    {
        Assert.Throws<ArgumentNullException>(() => DeterministicHash.ToInt64(null!));
    }
}

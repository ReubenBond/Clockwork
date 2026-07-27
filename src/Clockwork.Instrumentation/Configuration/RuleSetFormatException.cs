namespace Clockwork.Instrumentation.Configuration;

/// <summary>
/// Thrown when a serialized rule-set or instrumentation-configuration document is malformed:
/// invalid JSON, a missing required field, an unknown enum value, or a value that fails strict
/// schema/type/signature validation. The message identifies the offending element so authoring
/// mistakes are diagnosable without a debugger.
/// </summary>
public sealed class RuleSetFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RuleSetFormatException"/> class.</summary>
    /// <param name="message">A description of the schema violation.</param>
    public RuleSetFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RuleSetFormatException"/> class.</summary>
    /// <param name="message">A description of the schema violation.</param>
    /// <param name="innerException">The underlying parse error, if any.</param>
    public RuleSetFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     Signals a command-line usage error whose message is written to standard error before the
///     process exits with a non-zero code.
/// </summary>
/// <remarks>
///     A dedicated exception type lets <c>Main</c> distinguish a user's mistake — which deserves a
///     clean one-line message and a non-zero exit — from an unexpected fault, which deserves a full
///     diagnostic. The carried message is always safe to show a user; it never contains a host path
///     or other detail a user did not already supply.
/// </remarks>
public sealed class CommandLineException : Exception
{
    /// <summary>
    ///     Creates a usage error carrying a user-facing message.
    /// </summary>
    /// <param name="message">The actionable message explaining what was wrong and how to fix it.</param>
    public CommandLineException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Creates a usage error carrying a user-facing message and an underlying cause.
    /// </summary>
    /// <param name="message">The actionable message explaining what was wrong and how to fix it.</param>
    /// <param name="innerException">The exception that caused this usage error.</param>
    public CommandLineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Creates a usage error with no message. Provided to satisfy the standard exception
    ///     constructor set; the sample always uses the message-carrying constructor.
    /// </summary>
    public CommandLineException()
    {
    }
}

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     The tool naming convention and its validation.
/// </summary>
/// <remarks>
///     <para>
///     A tool name is the only handle a model has on a tool, so it has to be unambiguous,
///     stable and unique across every library an application combines. The convention is
///     <c>{family}_{verb}</c>: a family prefix, a single underscore, and the remainder. The
///     family prefix is what keeps one library's tools from colliding with another's.
///     </para>
///     <para>
///     <b>Throwing is correct here.</b> A malformed tool name is a construction-time
///     programming error discovered by the developer who wrote it, never a runtime decision
///     reported to a model. The non-throwing discipline the rest of this library observes
///     applies to <em>runtime policy denials</em> — see <see cref="ToolResult.Denied"/> — and
///     must not be confused with argument validation.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public static class ToolName
{
    /// <summary>
    ///     The greatest number of characters a tool name may contain.
    /// </summary>
    /// <remarks>
    ///     Not an arbitrary round number: the major model providers constrain function names to
    ///     <c>^[a-zA-Z0-9_-]{1,64}$</c>, so a name this library accepts is guaranteed to be a
    ///     name a provider accepts. The hyphen is excluded from the accepted character set
    ///     because mixing separators makes the family prefix ambiguous.
    /// </remarks>
    public const int MaxLength = 64;

    /// <summary>
    ///     The bare tool names published by the Agent Framework's file access provider.
    /// </summary>
    /// <remarks>
    ///     An application that combines this library with the Agent Framework would otherwise
    ///     offer the model two tools with the same name, and which one is invoked is undefined.
    ///     Rejecting them by name is strictly redundant against the family-prefix rule today —
    ///     none of them contains an underscore — but it is stated separately because the point
    ///     is the <em>explanation</em>, and because a later relaxation of the prefix rule must
    ///     not silently reopen the collision.
    /// </remarks>
    private static readonly string[] ReservedNames = ["read", "write", "delete"];

    /// <summary>
    ///     Composes a tool name from a family and a verb.
    /// </summary>
    /// <remarks>
    ///     Composition and validation are deliberately the same code path: the composed name is
    ///     passed through <see cref="Validate"/> before it is returned, so the two can never
    ///     diverge into a name this method produces but that method refuses.
    /// </remarks>
    /// <param name="family">
    ///     The family prefix grouping related tools, for example <c>text_file</c>. Must be
    ///     non-null and non-empty.
    /// </param>
    /// <param name="verb">
    ///     The action the tool performs, for example <c>read</c>. Must be non-null and
    ///     non-empty.
    /// </param>
    /// <returns>The composed tool name, <c>{family}_{verb}</c>.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="family"/> or <paramref name="verb"/> is
    ///     <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="family"/> or <paramref name="verb"/> is empty, or when
    ///     the composed name does not satisfy the naming convention.
    /// </exception>
    public static string Create(string family, string verb)
    {
        // Both halves are required: composing from a missing part would silently produce a
        // leading or trailing underscore, which the convention forbids anyway.
        ArgumentException.ThrowIfNullOrEmpty(family);
        ArgumentException.ThrowIfNullOrEmpty(verb);

        // Compose, then validate the composed result rather than the parts, so that every name
        // this library produces has passed exactly the check every name it accepts must pass.
        var name = family + "_" + verb;
        Validate(name);
        return name;
    }

    /// <summary>
    ///     Validates a tool name against the naming convention.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The rules are applied in a fixed order, because the order determines which
    ///     explanation a developer sees. The reserved-name check comes first so that its
    ///     explanation — a collision with the Agent Framework's file access provider — is not
    ///     displaced by the generic family-prefix message.
    ///     </para>
    ///     <para>
    ///     Consequences worth stating, because they follow from the rules rather than being
    ///     written anywhere as rules of their own: names are entirely lowercase, because model
    ///     providers treat names case-sensitively and a mixed-case name is a coin-flip on how a
    ///     prompt refers to it; a leading underscore and a leading digit are both rejected; and
    ///     the underscore rules together guarantee a non-empty family prefix and a non-empty
    ///     remainder.
    ///     </para>
    /// </remarks>
    /// <param name="name">The tool name to validate. Must be non-null and non-empty.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="name"/> is empty, is reserved by the Agent Framework
    ///     file access provider, exceeds <see cref="MaxLength"/>, does not begin with a
    ///     lowercase letter, ends with an underscore, contains a character outside the accepted
    ///     set, contains consecutive underscores, or carries no family prefix.
    /// </exception>
    public static void Validate(string name)
    {
        // Rule 1: a missing name is a programming error in the tool author's code.
        ArgumentException.ThrowIfNullOrEmpty(name);

        // Rule 2: the reserved bare names are refused first, so the developer is told about the
        // Agent Framework collision rather than being told merely that an underscore is absent.
        if (Array.IndexOf(ReservedNames, name) >= 0)
        {
            throw new ArgumentException(
                "The tool name is reserved by the Agent Framework file access provider, and an "
                + "application combining both libraries would offer the model two tools with "
                + "the same name. Use a family-prefixed name such as 'text_file_read'.",
                nameof(name));
        }

        // Rule 3: a name longer than the providers accept would be rejected at the far end of
        // the call, where the failure is opaque; refusing it here makes it obvious.
        if (name.Length > MaxLength)
        {
            throw new ArgumentException(
                "The tool name must contain at most 64 characters.",
                nameof(name));
        }

        // Rule 4: the first character fixes the case convention and rules out a leading
        // underscore and a leading digit in one check.
        if (name[0] is < 'a' or > 'z')
        {
            throw new ArgumentException(
                "The tool name must begin with a lowercase letter.",
                nameof(name));
        }

        // Rule 5: a trailing underscore would name an empty verb.
        if (name[^1] == '_')
        {
            throw new ArgumentException(
                "The tool name must not end with an underscore.",
                nameof(name));
        }

        // Rule 6: restrict the character set. Uppercase is excluded everywhere, not only in the
        // first position, so a name is unambiguous however a prompt refers to it.
        if (name.Any(character => character is (< 'a' or > 'z') and (< '0' or > '9') and not '_'))
        {
            throw new ArgumentException(
                "The tool name must contain only lowercase letters, digits and underscores.",
                nameof(name));
        }

        // Rule 7: consecutive underscores would make the family prefix ambiguous.
        if (name.Contains("__", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The tool name must not contain consecutive underscores.",
                nameof(name));
        }

        // Rule 8: the family prefix is the whole point of the convention — it is what keeps one
        // library's tools from colliding with another's.
        if (!name.Contains('_', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The tool name must carry a family prefix separated from the remainder by an "
                + "underscore, for example 'text_file_read'.",
                nameof(name));
        }
    }
}

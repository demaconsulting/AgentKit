namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolName"/> class.
/// </summary>
/// <remarks>
///     Every failing scenario asserts an exception rather than a returned result, because a
///     malformed tool name is a construction-time programming error discovered by the developer
///     who wrote it. The non-throwing discipline this library observes elsewhere applies to
///     runtime policy refusals, not to argument validation.
/// </remarks>
public class ToolNameTests
{
    /// <summary>
    ///     Proves that a family and a verb compose into an underscore-separated name.
    /// </summary>
    [Fact]
    public void ToolName_Create_FamilyAndVerb_ProducesUnderscoreSeparatedName()
    {
        // Act: compose from the simplest possible family and verb
        var name = ToolName.Create("text", "read");

        // Assert: the family, a single underscore, then the verb
        Assert.Equal("text_read", name);
    }

    /// <summary>
    ///     Proves that a family which itself contains an underscore composes correctly.
    /// </summary>
    /// <remarks>
    ///     Families are commonly multi-word, and the convention's family prefix is the leading
    ///     portion of the name rather than the single token before the first underscore.
    /// </remarks>
    [Fact]
    public void ToolName_Create_MultiWordFamily_ProducesUnderscoreSeparatedName()
    {
        // Act: compose from a multi-word family
        var name = ToolName.Create("text_file", "read");

        // Assert: the internal underscore of the family is preserved
        Assert.Equal("text_file_read", name);
    }

    /// <summary>
    ///     Proves that composing without a family is rejected.
    /// </summary>
    [Fact]
    public void ToolName_Create_EmptyFamily_ThrowsArgumentException()
    {
        // Act & Assert: an empty family would compose to a leading underscore
        Assert.Throws<ArgumentException>(() => ToolName.Create(string.Empty, "read"));
    }

    /// <summary>
    ///     Proves that composing without a verb is rejected.
    /// </summary>
    [Fact]
    public void ToolName_Create_EmptyVerb_ThrowsArgumentException()
    {
        // Act & Assert: an empty verb would compose to a trailing underscore
        Assert.Throws<ArgumentException>(() => ToolName.Create("text_file", string.Empty));
    }

    /// <summary>
    ///     Proves that a conforming name is accepted.
    /// </summary>
    [Fact]
    public void ToolName_Validate_ValidName_DoesNotThrow()
    {
        // Act: validate a name that satisfies every rule
        var exception = Record.Exception(() => ToolName.Validate("text_file_read2"));

        // Assert: lowercase letters, digits and single underscores are all permitted
        Assert.Null(exception);
    }

    /// <summary>
    ///     Proves that an uppercase character anywhere in a name is rejected.
    /// </summary>
    /// <remarks>
    ///     Model providers treat tool names case-sensitively, so a mixed-case name is a
    ///     coin-flip on how a prompt refers to it.
    /// </remarks>
    [Fact]
    public void ToolName_Validate_UppercaseCharacter_ThrowsArgumentException()
    {
        // Act & Assert: uppercase is excluded everywhere, not only in the first position
        Assert.Throws<ArgumentException>(() => ToolName.Validate("text_File_read"));
    }

    /// <summary>
    ///     Proves that a character outside the accepted set is rejected.
    /// </summary>
    /// <remarks>
    ///     The hyphen is excluded deliberately even though providers accept it: mixing
    ///     separators would make the family prefix ambiguous.
    /// </remarks>
    [Fact]
    public void ToolName_Validate_UnsupportedCharacter_ThrowsArgumentException()
    {
        // Act & Assert: hyphens are not a second separator
        Assert.Throws<ArgumentException>(() => ToolName.Validate("text-file-read"));
    }

    /// <summary>
    ///     Proves that a name beginning with a digit is rejected.
    /// </summary>
    [Fact]
    public void ToolName_Validate_LeadingDigit_ThrowsArgumentException()
    {
        // Act & Assert: the first character must be a lowercase letter
        Assert.Throws<ArgumentException>(() => ToolName.Validate("1text_read"));
    }

    /// <summary>
    ///     Proves that a name longer than the providers accept is rejected.
    /// </summary>
    /// <remarks>
    ///     Refusing it here makes the mistake obvious to the developer; left to the provider it
    ///     would surface as an opaque failure at the far end of a call.
    /// </remarks>
    [Fact]
    public void ToolName_Validate_NameExceedingMaximumLength_ThrowsArgumentException()
    {
        // Arrange: a conforming name one character beyond the ceiling
        var name = "text_" + new string('a', ToolName.MaxLength - 4);

        // Act & Assert: the length ceiling is enforced
        Assert.Equal(ToolName.MaxLength + 1, name.Length);
        Assert.Throws<ArgumentException>(() => ToolName.Validate(name));
    }

    /// <summary>
    ///     Proves that a name with no underscore is rejected.
    /// </summary>
    /// <remarks>
    ///     The family prefix is what keeps one library's tools from colliding with another's,
    ///     so a name without one is refused however well-formed it otherwise is.
    /// </remarks>
    [Fact]
    public void ToolName_Validate_NameWithoutUnderscore_ThrowsArgumentException()
    {
        // Act & Assert: a bare name carries no family
        Assert.Throws<ArgumentException>(() => ToolName.Validate("readfile"));
    }

    /// <summary>
    ///     Proves that a name beginning with an underscore is rejected.
    /// </summary>
    [Fact]
    public void ToolName_Validate_LeadingUnderscore_ThrowsArgumentException()
    {
        // Act & Assert: a leading underscore would name an empty family
        Assert.Throws<ArgumentException>(() => ToolName.Validate("_text_read"));
    }

    /// <summary>
    ///     Proves that a name ending with an underscore is rejected.
    /// </summary>
    [Fact]
    public void ToolName_Validate_TrailingUnderscore_ThrowsArgumentException()
    {
        // Act & Assert: a trailing underscore would name an empty verb
        Assert.Throws<ArgumentException>(() => ToolName.Validate("text_read_"));
    }

    /// <summary>
    ///     Proves that consecutive underscores are rejected.
    /// </summary>
    [Fact]
    public void ToolName_Validate_ConsecutiveUnderscores_ThrowsArgumentException()
    {
        // Act & Assert: a doubled separator makes the family prefix ambiguous
        Assert.Throws<ArgumentException>(() => ToolName.Validate("text__read"));
    }

    /// <summary>
    ///     Proves that the bare file access names published by the Agent Framework are refused.
    /// </summary>
    /// <remarks>
    ///     The Agent Framework's file access provider registers <c>read</c>, <c>write</c> and
    ///     <c>delete</c> as bare tool names. An application combining both libraries would
    ///     otherwise present the model with two tools of the same name, and which one is
    ///     invoked is undefined. The assertion checks the explanation, not merely the exception
    ///     type, so that the rule cannot be quietly folded into the generic family-prefix rule.
    /// </remarks>
    /// <param name="name">The reserved bare name under test.</param>
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("delete")]
    public void ToolName_Validate_BareAgentFrameworkName_IsRejected(string name)
    {
        // Act: validate a name the Agent Framework already publishes
        var exception = Assert.Throws<ArgumentException>(() => ToolName.Validate(name));

        // Assert: the developer is told about the collision, not merely about an underscore
        Assert.Contains("reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Agent Framework", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a missing name is rejected.
    /// </summary>
    [Fact]
    public void ToolName_Validate_NullName_ThrowsArgumentNullException()
    {
        // Act & Assert: a null name is a defect in the tool author's code
        Assert.Throws<ArgumentNullException>(() => ToolName.Validate(null!));
    }

    /// <summary>
    ///     Proves that an empty name is rejected.
    /// </summary>
    [Fact]
    public void ToolName_Validate_EmptyName_ThrowsArgumentException()
    {
        // Act & Assert: an empty name gives the model nothing to invoke
        Assert.Throws<ArgumentException>(() => ToolName.Validate(string.Empty));
    }
}

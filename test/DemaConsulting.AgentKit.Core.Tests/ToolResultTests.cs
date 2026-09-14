using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolResult"/> class.
/// </summary>
/// <remarks>
///     The captioned scenarios assert the concrete runtime shape — a two-element list with the
///     caption first and the content second — rather than merely that two parts exist. That
///     exact shape is the one proven to be flattened into JSON when a tool is built without the
///     result-delivery guard, so pinning it here is what makes the guard's own tests meaningful.
/// </remarks>
public class ToolResultTests
{
    /// <summary>
    ///     Proves that text is returned as the supplied string itself.
    /// </summary>
    [Fact]
    public void ToolResult_Text_Content_ReturnsSuppliedString()
    {
        // Arrange: ordinary text a tool would hand back after a read
        const string content = "the contents of a file";

        // Act: construct a text result
        var result = ToolResult.Text(content);

        // Assert: the plain string arrives unwrapped, ready for any runtime to present
        Assert.Equal(content, Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves that a missing text is rejected as a programming error.
    /// </summary>
    [Fact]
    public void ToolResult_Text_NullText_ThrowsArgumentNullException()
    {
        // Act & Assert: a null text is a defect in the tool, not a refusal for the model
        Assert.Throws<ArgumentNullException>(() => ToolResult.Text(null!));
    }

    /// <summary>
    ///     Proves that a structured result is returned as the supplied value itself.
    /// </summary>
    /// <remarks>
    ///     The constructor does not serialize; the guarded factory does that on the way to the
    ///     runtime. Keeping the two separate means a tool author can assert on the value their
    ///     tool produced rather than on its JSON form.
    /// </remarks>
    [Fact]
    public void ToolResult_Structured_Value_ReturnsTheSuppliedValue()
    {
        // Arrange: data a tool would hand back that is neither text nor content
        var value = new Dictionary<string, int> { ["matches"] = 2 };

        // Act: construct a structured result
        var result = ToolResult.Structured(value);

        // Assert: the value itself, unchanged
        Assert.Same(value, result);
    }

    /// <summary>
    ///     Proves that a missing structured value is rejected as a programming error.
    /// </summary>
    [Fact]
    public void ToolResult_Structured_NullValue_ThrowsArgumentNullException()
    {
        // Act & Assert: a tool with nothing to say returns text or a refusal, never null data
        Assert.Throws<ArgumentNullException>(() => ToolResult.Structured(null!));
    }

    /// <summary>
    ///     Proves that binary content without a caption is returned as content carrying its media
    ///     type.
    /// </summary>
    [Fact]
    public void ToolResult_Binary_NoCaption_ReturnsDataContentWithMediaType()
    {
        // Arrange: a small payload and the media type describing it
        var data = new byte[] { 1, 2, 3 };
        const string mediaType = "application/pdf";

        // Act: construct a binary result with no caption
        var result = ToolResult.Binary(data, mediaType);

        // Assert: a single content part, carrying the media type the provider needs
        var content = Assert.IsType<DataContent>(result);
        Assert.Equal(mediaType, content.MediaType);
    }

    /// <summary>
    ///     Proves that a captioned binary result places the caption ahead of the content.
    /// </summary>
    /// <remarks>
    ///     The order is the contract: the model reads the parts in sequence and must be told
    ///     what the attachment is before it reaches the attachment.
    /// </remarks>
    [Fact]
    public void ToolResult_Binary_WithCaption_ReturnsCaptionThenContent()
    {
        // Arrange: a payload together with a caption describing it
        var data = new byte[] { 1, 2, 3 };
        const string caption = "The quarterly report.";

        // Act: construct a captioned binary result
        var result = ToolResult.Binary(data, "application/pdf", caption);

        // Assert: exactly two parts, caption first and content second
        var parts = Assert.IsType<List<AIContent>>(result);
        Assert.Equal(2, parts.Count);
        Assert.Equal(caption, Assert.IsType<TextContent>(parts[0]).Text);
        Assert.IsType<DataContent>(parts[1]);
    }

    /// <summary>
    ///     Proves that binary content without a media type is rejected.
    /// </summary>
    [Fact]
    public void ToolResult_Binary_EmptyMediaType_ThrowsArgumentException()
    {
        // Arrange: a payload with nothing describing what it is
        var data = new byte[] { 1, 2, 3 };

        // Act & Assert: a provider cannot interpret content with no media type
        Assert.Throws<ArgumentException>(() => ToolResult.Binary(data, string.Empty));
    }

    /// <summary>
    ///     Proves that a captioned image places the caption ahead of the image.
    /// </summary>
    [Fact]
    public void ToolResult_Image_WithCaption_ReturnsCaptionThenImageContent()
    {
        // Arrange: image bytes together with a caption describing them
        var data = new byte[] { 1, 2, 3 };
        const string caption = "A screenshot of the failing dialog.";

        // Act: construct a captioned image result
        var result = ToolResult.Image(data, "image/png", caption);

        // Assert: the caption-plus-image shape, with the image carrying its media type
        var parts = Assert.IsType<List<AIContent>>(result);
        Assert.Equal(2, parts.Count);
        Assert.Equal(caption, Assert.IsType<TextContent>(parts[0]).Text);
        Assert.Equal("image/png", Assert.IsType<DataContent>(parts[1]).MediaType);
    }

    /// <summary>
    ///     Proves that a media type which does not denote an image is rejected.
    /// </summary>
    /// <remarks>
    ///     A caption attached to bytes that are not an image would tell the model it is looking
    ///     at a picture when it is not, and the model has no way to discover otherwise.
    /// </remarks>
    [Fact]
    public void ToolResult_Image_NonImageMediaType_ThrowsArgumentException()
    {
        // Arrange: bytes that are not an image
        var data = new byte[] { 1, 2, 3 };

        // Act & Assert: describing them as an image is refused at construction
        Assert.Throws<ArgumentException>(() => ToolResult.Image(data, "application/pdf"));
    }

    /// <summary>
    ///     Proves that every defined reason produces a returned refusal rather than an
    ///     exception.
    /// </summary>
    /// <remarks>
    ///     Enumerating every member means a reason added later without thought is caught here
    ///     rather than at a model's tool call.
    /// </remarks>
    /// <param name="reason">The refusal reason under test.</param>
    [Theory]
    [InlineData(DenialReason.PathNotPermitted)]
    [InlineData(DenialReason.ResourceTooLarge)]
    [InlineData(DenialReason.UnsupportedMediaType)]
    [InlineData(DenialReason.TargetNotFound)]
    [InlineData(DenialReason.HostCapabilityUnavailable)]
    [InlineData(DenialReason.InvalidRequest)]
    public void ToolResult_Denied_AnyReason_ProducesResultNotException(DenialReason reason)
    {
        // Act: refuse for this reason
        var result = ToolResult.Denied(reason, "the operation was refused");

        // Assert: a returned string, so the agent's turn continues
        Assert.NotEmpty(Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves that a refusal names the reason it was refused for.
    /// </summary>
    [Fact]
    public void ToolResult_Denied_Result_NamesTheReason()
    {
        // Arrange: a refusal the model should be able to reason about
        const string message = "the requested path is outside the permitted read location";

        // Act: refuse because the path was not permitted
        var result = ToolResult.Denied(DenialReason.PathNotPermitted, message);

        // Assert: both the reason and the explanation reach the model
        var text = Assert.IsType<string>(result);
        Assert.Contains(nameof(DenialReason.PathNotPermitted), text, StringComparison.Ordinal);
        Assert.Contains(message, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a refusal carrying a classification redirect names the reader for that
    ///     kind of content.
    /// </summary>
    /// <remarks>
    ///     The redirect is the one exception to the rule that a denial states a fact and never
    ///     prescribes a remedy, and it is an exception only because the naming is part of the
    ///     fact: the file is an image, and <c>image_read</c> is what reads images.
    /// </remarks>
    [Fact]
    public void ToolResult_Denied_WithRedirect_NamesTheRedirectTool()
    {
        // Arrange: the reader for the kind of content this refusal has classified the file as
        const string redirect = "image_read";

        // Act: refuse and classify
        var result = ToolResult.Denied(
            DenialReason.UnsupportedMediaType,
            "this tool reads text only",
            redirect);

        // Assert: the redirect tool is named in the text the model receives
        Assert.Contains(redirect, Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a redirect to a name no tool could legally carry is rejected.
    /// </summary>
    /// <remarks>
    ///     Left unchecked it would direct the model at a tool that cannot exist, which the
    ///     model has no way to recover from.
    /// </remarks>
    [Fact]
    public void ToolResult_Denied_InvalidRedirectToolName_ThrowsArgumentException()
    {
        // Act & Assert: an unprefixed redirect name is refused at construction
        Assert.Throws<ArgumentException>(
            () => ToolResult.Denied(DenialReason.InvalidRequest, "no", "readfile"));
    }

    /// <summary>
    ///     Proves that an undefined reason is rejected.
    /// </summary>
    /// <remarks>
    ///     There is no zero member, so <c>default(DenialReason)</c> is not a valid reason: a
    ///     refusal with an unspecified reason is a bug rather than something to show a model.
    /// </remarks>
    [Fact]
    public void ToolResult_Denied_UndefinedReason_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert: the default value of the enumeration is not a reason
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ToolResult.Denied(default, "the operation was refused"));
    }

    /// <summary>
    ///     Proves that a refusal with no explanation is rejected.
    /// </summary>
    [Fact]
    public void ToolResult_Denied_EmptyMessage_ThrowsArgumentException()
    {
        // Act & Assert: a reason with no explanation leaves the model nothing to act on
        Assert.Throws<ArgumentException>(
            () => ToolResult.Denied(DenialReason.TargetNotFound, string.Empty));
    }

    /// <summary>
    ///     Proves that a refusal consists only of its reason, the caller's explanation and the
    ///     library's fixed fragments.
    /// </summary>
    /// <remarks>
    ///     The refusal text is handed to a model and the resulting transcript leaves this
    ///     process. This is the <see cref="ToolResult"/>-level counterpart of the path policy's
    ///     disclosure scenario: the library must contribute no host detail of its own, so the
    ///     only way host layout can appear is if a caller puts it in the message.
    /// </remarks>
    [Fact]
    public void ToolResult_Denied_Message_ContainsOnlySuppliedTextAndReason()
    {
        // Arrange: an explanation that itself carries no host detail
        const string message = "the requested path is outside the permitted read location";

        // Act: refuse with no redirect, so only the fixed fragments are added
        var result = ToolResult.Denied(DenialReason.PathNotPermitted, message);

        // Assert: exactly the fixed composition, and no directory separator anywhere in it
        var text = Assert.IsType<string>(result);
        Assert.Equal("Denied (PathNotPermitted): " + message, text);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar.ToString(),
            text,
            StringComparison.Ordinal);
    }
}

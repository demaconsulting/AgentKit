using System.Text;
using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileReadTool"/> class.
/// </summary>
/// <remarks>
///     Every scenario invokes the constructed tool exactly as a runtime would — through
///     <see cref="AIFunction.InvokeAsync"/> with named arguments — and uses the paths a model
///     actually sends. The output is the paged, line-numbered form, so scenarios assert the
///     <c>path lines A-B of N</c> header and the numbered body rather than bare content.
/// </remarks>
public class TextFileReadToolTests
{
    /// <summary>
    ///     A five-line fixture with no trailing newline, so line counts are unambiguous.
    /// </summary>
    private const string FiveLines = "line one\nline two\nline three\nline four\nline five";

    /// <summary>
    ///     A real PNG file header: signature, a NUL-bearing chunk, and a distinctive ASCII marker.
    /// </summary>
    private static readonly byte[] PngHeaderBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D,
        0x70, 0x69, 0x78, 0x65, 0x6C, 0x2D, 0x6D, 0x61, 0x72, 0x6B, 0x65, 0x72
    ];

    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileReadTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("text_file_read", TextFileReadTool.ToolName);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", TextFileReadTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileReadTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TextFileReadTool.Create(null!));
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a non-empty description.
    /// </summary>
    [Fact]
    public void TextFileReadTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tool = TextFileReadTool.Create(policy);

        Assert.Equal(TextFileReadTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    /// <summary>
    ///     Proves the result reaches the caller as plain text rather than serialized JSON.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_PermittedFile_ResultIsPlainTextNotJsonElement()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "content");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "note.txt" });

        Assert.IsType<string>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves a whole-file read carries the range header and the numbered content.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_PermittedFile_ReturnsNumberedContentWithHeader()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", FiveLines);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "note.txt" });

        var text = Assert.IsType<string>(result);
        Assert.StartsWith("note.txt lines 1-5 of 5", text, StringComparison.Ordinal);
        Assert.Contains("\n1| line one", text, StringComparison.Ordinal);
        Assert.Contains("\n5| line five", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an empty file reads as an honest empty window rather than a refusal.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_EmptyFile_ReturnsZeroLineHeaderNotDenial()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "empty.txt", string.Empty);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "empty.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Equal("empty.txt lines 0-0 of 0", text);
    }

    /// <summary>
    ///     Proves a ranged read returns only the requested window, with a header naming it and the
    ///     file's true total.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_RangedWindow_ReturnsOnlyThoseLines()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", FiveLines);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 2, ["lineCount"] = 2 });

        var text = Assert.IsType<string>(result);
        Assert.StartsWith("note.txt lines 2-3 of 5", text, StringComparison.Ordinal);
        Assert.Contains("\n2| line two", text, StringComparison.Ordinal);
        Assert.Contains("\n3| line three", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line one", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line four", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a start line past the end of the file is an honest empty window naming the true
    ///     total, not a refusal.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_StartLinePastEndOfFile_ReturnsEmptyWindowNamingTotal()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", FiveLines);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 10 });

        var text = Assert.IsType<string>(result);
        Assert.Contains("of 5", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line one", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Denied", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a lineCount that runs past the end of the file is clamped to the true total.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_LineCountPastEndOfFile_ClampsToTheTotal()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", FiveLines);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 4, ["lineCount"] = 100 });

        var text = Assert.IsType<string>(result);
        Assert.StartsWith("note.txt lines 4-5 of 5", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an invalid paging argument is a refusal, not an exception.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_InvalidStartLine_ReturnsDenialWithoutThrowing()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", FiveLines);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 0 });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a bare relative name is read from the working directory and the header echoes it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_BareFileName_ReturnsTheFileContents()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "only line");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "note.txt" });

        var text = Assert.IsType<string>(result);
        Assert.StartsWith("note.txt lines 1-1 of 1", text, StringComparison.Ordinal);
        Assert.Contains("1| only line", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing path argument is a refusal, not a framework error.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_MissingPathArgument_ReturnsDenialWithoutThrowing()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tool = TextFileReadTool.Create(policy);

        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a path outside the read grant is refused, disclosing the permitted location.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_PathOutsideTheReadRoot_ReturnsDenial()
    {
        using var fixture = new TempDirectoryFixture();
        var outsideFile = TempDirectoryFixture.WriteFile(fixture.Outside, "secret.txt", "leaked-body-token");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = outsideFile });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("leaked-body-token", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing file is refused with a plain fact that names no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_MissingFile_ReturnsDenialNamingNoTool()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "absent.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("file_list", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a directory is refused with a plain fact that names no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_DirectoryPath_ReturnsDenialNamingNoTool()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = fixture.Root });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("file_list", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a single line longer than the read ceiling is refused with recourse rather than
    ///     silently truncated, since even a one-line window must fit the read ceiling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_LineLargerThanTheReadCeiling_ReturnsDenialNamingRecourse()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "big.txt", new string('a', 128));
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxReadBytes: 16)));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "big.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("16-byte", text, StringComparison.Ordinal);
        Assert.Contains("startLine", text, StringComparison.Ordinal);
        Assert.Contains("lineCount", text, StringComparison.Ordinal);
        Assert.DoesNotContain("aaaa", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a window beyond the result ceiling is refused rather than truncated.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_TextBeyondTheResultCeiling_ReturnsDenialRatherThanTruncated()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", new string('a', 400));
        var tool = TextFileReadTool.Create(
            RootedPolicy(fixture.Root, new ToolLimits(maxReadBytes: 4096, maxResultCharacters: 32)));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "note.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an unranged read of a file larger than the read ceiling is refused with recourse:
    ///     the refusal names the file's total line count and directs the model to page with
    ///     startLine and lineCount, rather than leaving it no way forward.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_UnrangedLargeFile_ReturnsDenialNamingRecourseAndTotal()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "big.txt", LargeFixture(200));
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxReadBytes: 64)));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "big.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("64-byte", text, StringComparison.Ordinal);
        Assert.Contains("200 lines", text, StringComparison.Ordinal);
        Assert.Contains("startLine", text, StringComparison.Ordinal);
        Assert.Contains("lineCount", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line 42", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a ranged read of a file larger than the read ceiling returns exactly the requested
    ///     mid-file window rather than a denial. This is the regression test for the windowing
    ///     defect: the file is far larger than the read ceiling, yet paging to a single line still
    ///     works because the ceiling bounds the window, not the file.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_RangedWindowInLargeFile_ReturnsThatWindowNotADenial()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "big.txt", LargeFixture(200));
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxReadBytes: 64)));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "big.txt", ["startLine"] = 120, ["lineCount"] = 1 });

        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain("Denied", text, StringComparison.Ordinal);
        Assert.StartsWith("big.txt lines 120-120 of 200", text, StringComparison.Ordinal);
        Assert.Contains("120| line 120", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line 119", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line 121", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a ranged read near the end of a file larger than the read ceiling returns the tail
    ///     window, clamped to the true total, rather than a denial.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_RangedWindowNearEndOfLargeFile_ReturnsTailWindow()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "big.txt", LargeFixture(200));
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxReadBytes: 64)));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "big.txt", ["startLine"] = 198, ["lineCount"] = 10 });

        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain("Denied", text, StringComparison.Ordinal);
        Assert.StartsWith("big.txt lines 198-200 of 200", text, StringComparison.Ordinal);
        Assert.Contains("198| line 198", text, StringComparison.Ordinal);
        Assert.Contains("200| line 200", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a binary image is refused as unsupported media and redirected to the image tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_BinaryImageFile_ReturnsDenialRedirectingToImageRead()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "picture.png", PngHeaderBytes);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "picture.png" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains(ImageReadTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("pixel-marker", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a non-image binary file is refused without a redirect.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_BinaryNonImageFile_ReturnsDenialWithoutRedirect()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "data.bin", [0x01, 0x00, 0x02, 0x00]);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "data.bin" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a byte-order-marked UTF-16 file still reads as text, despite its NUL bytes.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_Utf16BomTextFile_ReturnsTheFileContents()
    {
        using var fixture = new TempDirectoryFixture();
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("hello"))
            .ToArray();
        TempDirectoryFixture.WriteBytes(fixture.Root, "utf16.txt", bytes);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "utf16.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("hello", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Denied", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds a multi-line fixture whose bytes exceed a small read ceiling while each individual
    ///     line stays tiny, so a windowed read of one line fits the ceiling but the whole file does
    ///     not.
    /// </summary>
    /// <param name="lineCount">The number of lines to generate.</param>
    /// <returns>The fixture text, each line reading <c>line N</c>, with no trailing newline.</returns>
    private static string LargeFixture(int lineCount)
    {
        var builder = new StringBuilder();
        for (var number = 1; number <= lineCount; number++)
        {
            if (number > 1)
            {
                builder.Append('\n');
            }

            builder.Append("line ").Append(number);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Composes a policy over one read-write location, optionally with tighter limits.
    /// </summary>
    /// <param name="root">The permitted location.</param>
    /// <param name="limits">The tool limits, or null for the defaults.</param>
    /// <returns>The policy.</returns>
    private static PathPolicy RootedPolicy(string root, ToolLimits? limits = null)
    {
        return limits is null
            ? new PathPolicy(root, [PathRule.ReadWrite(root)])
            : new PathPolicy(root, [PathRule.ReadWrite(root)], limits);
    }

    /// <summary>
    ///     Invokes a tool with the supplied arguments, exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="arguments">The arguments to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(AIFunction tool, AIFunctionArguments arguments)
    {
        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }
}

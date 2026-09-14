using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileReplaceTool"/> class.
/// </summary>
public class TextFileReplaceToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileReplaceTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("text_file_replace", TextFileReplaceTool.ToolName);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", TextFileReplaceTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileReplaceTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TextFileReplaceTool.Create(null!));
    }

    /// <summary>
    ///     Proves a unique match is replaced and the confirmation names the line span the new text
    ///     now occupies as well as the line-count change.
    /// </summary>
    /// <remarks>
    ///     The span is the point of the confirmation: an edit renumbers every line below it, and
    ///     evaluation found that a confirmation without the span drove models to re-read the whole
    ///     file purely to re-derive line numbers.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReplaceTool_Replace_UniqueMatch_ReplacesItAndReportsSpanAndDelta()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "alpha\nbeta\ngamma\n");
        var tool = TextFileReplaceTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["oldText"] = "beta", ["newText"] = "BETA" });

        var text = Assert.IsType<string>(result);
        Assert.Equal(
            "Replaced 1 occurrence. The new text occupies line 2. "
            + "The file changed from 3 to 3 lines (+0).",
            text);
        Assert.Equal("alpha\nBETA\ngamma\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a multi-line replacement names the whole span the new text occupies, in the
    ///     updated file's own numbering.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReplaceTool_Replace_MultiLineNewText_ReportsTheWholeSpan()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "alpha\nbeta\ngamma\n");
        var tool = TextFileReplaceTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments
            {
                ["path"] = "note.txt",
                ["oldText"] = "beta",
                ["newText"] = "one\ntwo\nthree"
            });

        var text = Assert.IsType<string>(result);
        Assert.Equal(
            "Replaced 1 occurrence. The new text occupies lines 2-4. "
            + "The file changed from 3 to 5 lines (+2).",
            text);
    }

    /// <summary>
    ///     Proves a deletion — an empty replacement — names the position the removed text was at
    ///     rather than claiming a span the new text does not occupy.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReplaceTool_Replace_EmptyNewText_ReportsTheRemovalPosition()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "alpha\nbeta\ngamma\n");
        var tool = TextFileReplaceTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments
            {
                ["path"] = "note.txt",
                ["oldText"] = "beta\n",
                ["newText"] = string.Empty
            });

        var text = Assert.IsType<string>(result);
        Assert.Equal(
            "Replaced 1 occurrence. The replaced text was removed at line 2. "
            + "The file changed from 3 to 2 lines (-1).",
            text);
        Assert.Equal("alpha\ngamma\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a not-found match is refused, and the refusal says the text was not found.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReplaceTool_Replace_NotFound_ReturnsDenialSayingNotFound()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "alpha\nbeta\n");
        var tool = TextFileReplaceTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["oldText"] = "absent", ["newText"] = "x" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("not found", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Proves an ambiguous match is refused, naming the count and asking for more surrounding
    ///     lines, and the file is left unchanged.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReplaceTool_Replace_AppearsMultipleTimes_ReturnsDenialNamingCountAndAsksForContext()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "x\nx\nx\n");
        var tool = TextFileReplaceTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["oldText"] = "x", ["newText"] = "y" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("appears 3 times", text, StringComparison.Ordinal);
        Assert.Contains("surrounding lines", text, StringComparison.Ordinal);
        // The ambiguous edit changed nothing.
        Assert.Equal("x\nx\nx\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves an empty replacement deletes the matched text.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReplaceTool_Replace_EmptyNewText_DeletesTheMatchedText()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "keep\nremove me\nkeep\n");
        var tool = TextFileReplaceTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments
            {
                ["path"] = "note.txt",
                ["oldText"] = "remove me\n",
                ["newText"] = string.Empty
            });

        Assert.IsType<string>(result);
        Assert.Equal("keep\nkeep\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves an insertion is expressed by including surrounding text in both old and new text.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReplaceTool_Replace_SurroundingContext_InsertsBetweenLines()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "first\nthird\n");
        var tool = TextFileReplaceTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments
            {
                ["path"] = "note.txt",
                ["oldText"] = "first\nthird",
                ["newText"] = "first\nsecond\nthird"
            });

        Assert.IsType<string>(result);
        Assert.Equal("first\nsecond\nthird\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a missing file is refused with a plain fact that names no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReplaceTool_Replace_MissingFile_ReturnsDenialNamingNoTool()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = TextFileReplaceTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "absent.txt", ["oldText"] = "a", ["newText"] = "b" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFileCreateTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an edit in a read-only location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReplaceTool_Replace_ReadOnlyLocation_ReturnsDenial()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "content");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = TextFileReplaceTool.Create(policy);

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["oldText"] = "content", ["newText"] = "x" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Composes a policy over one read-write location.
    /// </summary>
    /// <param name="root">The permitted location.</param>
    /// <returns>The policy.</returns>
    private static PathPolicy RootedPolicy(string root)
    {
        return new PathPolicy(root, [PathRule.ReadWrite(root)]);
    }

    /// <summary>
    ///     Reads a file's text with the test's cancellation token.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The file's text.</returns>
    private static async Task<string> ReadAsync(string path)
    {
        return await System.IO.File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
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

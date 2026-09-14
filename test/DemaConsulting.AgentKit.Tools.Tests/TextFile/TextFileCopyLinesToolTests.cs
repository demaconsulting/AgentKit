using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileCopyLinesTool"/> class.
/// </summary>
public class TextFileCopyLinesToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileCopyLinesTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("text_file_copy_lines", TextFileCopyLinesTool.ToolName);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", TextFileCopyLinesTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy or buffer is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileCopyLinesTool_Create_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TextFileCopyLinesTool.Create(null!, new TextFileLineBuffers()));
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        Assert.Throws<ArgumentNullException>(() => TextFileCopyLinesTool.Create(policy, null!));
    }

    /// <summary>
    ///     Proves a range of lines is captured, the confirmation names the count, the first and last
    ///     captured line and that the source is unchanged, and the source file is left byte-identical.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCopyLinesTool_Copy_Range_CapturesReportsAndLeavesSourceByteIdentical()
    {
        using var fixture = new TempDirectoryFixture();
        const string original = "one\ntwo\nthree\nfour\n";
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", original);
        var tool = TextFileCopyLinesTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 2, ["endLine"] = 3 });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Copied 2 lines (2-3)", text, StringComparison.Ordinal);
        Assert.Contains("note.txt is unchanged", text, StringComparison.Ordinal);
        Assert.Contains("First: two", text, StringComparison.Ordinal);
        Assert.Contains("Last: three", text, StringComparison.Ordinal);

        // The source must be byte-identical, not merely the same line count.
        Assert.Equal(original, await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a range covering the whole file captures every line and leaves the source
    ///     byte-identical.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCopyLinesTool_Copy_WholeFile_CapturesAll()
    {
        using var fixture = new TempDirectoryFixture();
        const string original = "alpha\nbeta\ngamma\n";
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", original);
        var buffers = new TextFileLineBuffers();
        var copy = TextFileCopyLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var paste = TextFilePasteLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var destination = TempDirectoryFixture.WriteFile(fixture.Root, "dest.txt", string.Empty);

        var result = await InvokeAsync(
            copy,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 1, ["endLine"] = 3 });
        await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "dest.txt", ["atLine"] = 1 });

        Assert.Contains("Copied 3 lines (1-3)", Assert.IsType<string>(result), StringComparison.Ordinal);
        Assert.Equal(original, await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
        Assert.Equal(original, await ReadAsync(destination));
    }

    /// <summary>
    ///     Proves a copy followed by a paste reproduces the copied block byte-for-byte in the
    ///     destination while leaving the source unchanged.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCopyLinesTool_Copy_ThenPasteAtLine_ReproducesContentExactly()
    {
        using var fixture = new TempDirectoryFixture();
        const string original = "one\ntwo\nthree\nfour\n";
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", original);
        var buffers = new TextFileLineBuffers();
        var copy = TextFileCopyLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var paste = TextFilePasteLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var destination = TempDirectoryFixture.WriteFile(fixture.Root, "dest.txt", "head\n");

        await InvokeAsync(
            copy,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 2, ["endLine"] = 3 });
        await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "dest.txt", ["atLine"] = 1 });

        Assert.Equal("two\nthree\nhead\n", await ReadAsync(destination));
        Assert.Equal(original, await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a paste does not consume the slot, so a single copy can be pasted more than once.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCopyLinesTool_Copy_ThenPasteTwice_ProducesTwoCopies()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\nthree\n");
        var buffers = new TextFileLineBuffers();
        var copy = TextFileCopyLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var paste = TextFilePasteLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var destination = TempDirectoryFixture.WriteFile(fixture.Root, "dest.txt", "tail\n");

        await InvokeAsync(
            copy,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 1, ["endLine"] = 2 });
        await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "dest.txt", ["atLine"] = 1 });
        await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "dest.txt", ["atLine"] = 1 });

        Assert.Equal("one\ntwo\none\ntwo\ntail\n", await ReadAsync(destination));
    }

    /// <summary>
    ///     Proves a later capture into the same slot replaces its contents, so a paste yields the
    ///     newest capture.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCopyLinesTool_Copy_RecaptureSameSlot_ReplacesContent()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\nthree\n");
        var buffers = new TextFileLineBuffers();
        var copy = TextFileCopyLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var paste = TextFilePasteLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var destination = TempDirectoryFixture.WriteFile(fixture.Root, "dest.txt", string.Empty);

        await InvokeAsync(
            copy,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 1, ["endLine"] = 1 });
        await InvokeAsync(
            copy,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 3, ["endLine"] = 3 });
        await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "dest.txt", ["atLine"] = 1 });

        Assert.Equal("three\n", await ReadAsync(destination));
    }

    /// <summary>
    ///     Proves a cut into the slot a copy filled replaces the copied text on the next paste, while
    ///     the copy's source stays byte-identical and the cut still mutates its own source.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCopyLinesTool_Copy_ThenCutSameSlot_CutReplacesTheCopiedSlot()
    {
        using var fixture = new TempDirectoryFixture();
        const string copySource = "a1\na2\na3\n";
        TempDirectoryFixture.WriteFile(fixture.Root, "copy-source.txt", copySource);
        TempDirectoryFixture.WriteFile(fixture.Root, "cut-source.txt", "b1\nb2\n");
        var buffers = new TextFileLineBuffers();
        var copy = TextFileCopyLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var cut = TextFileCutLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var paste = TextFilePasteLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var destination = TempDirectoryFixture.WriteFile(fixture.Root, "dest.txt", string.Empty);

        await InvokeAsync(
            copy,
            new AIFunctionArguments { ["path"] = "copy-source.txt", ["startLine"] = 1, ["endLine"] = 2 });
        await InvokeAsync(
            cut,
            new AIFunctionArguments { ["path"] = "cut-source.txt", ["startLine"] = 1, ["endLine"] = 1 });
        await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "dest.txt", ["atLine"] = 1 });

        Assert.Equal("b1\n", await ReadAsync(destination));
        Assert.Equal(copySource, await ReadAsync(Path.Combine(fixture.Root, "copy-source.txt")));
        Assert.Equal("b2\n", await ReadAsync(Path.Combine(fixture.Root, "cut-source.txt")));
    }

    /// <summary>
    ///     Proves an out-of-range copy is refused and captures nothing, leaving the source
    ///     byte-identical.
    /// </summary>
    /// <param name="startLine">The 1-based first line of the refused range.</param>
    /// <param name="endLine">The 1-based last line of the refused range.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(5, 6)]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    public async Task TextFileCopyLinesTool_Copy_OutOfRange_ReturnsDenialAndLeavesFileUnchanged(
        int startLine,
        int endLine)
    {
        using var fixture = new TempDirectoryFixture();
        const string original = "one\ntwo\n";
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", original);
        var buffers = new TextFileLineBuffers();
        var copy = TextFileCopyLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        var paste = TextFilePasteLinesTool.Create(RootedPolicy(fixture.Root), buffers);
        TempDirectoryFixture.WriteFile(fixture.Root, "dest.txt", string.Empty);

        var result = await InvokeAsync(
            copy,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = startLine, ["endLine"] = endLine });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Equal(original, await ReadAsync(Path.Combine(fixture.Root, "note.txt")));

        // Nothing was captured, so the default slot is empty and a paste is refused.
        var pasteResult = await InvokeAsync(
            paste, new AIFunctionArguments { ["path"] = "dest.txt", ["atLine"] = 1 });
        Assert.Contains("Denied (TargetNotFound)", Assert.IsType<string>(pasteResult), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a copy from a read-only location succeeds — the deliberate inverse of the cut
    ///     tool's read-only denial, because a copy consults the read decision alone.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCopyLinesTool_Copy_ReadOnlyLocation_Succeeds()
    {
        using var fixture = new TempDirectoryFixture();
        const string original = "one\ntwo\n";
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", original);
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = TextFileCopyLinesTool.Create(policy, new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 1, ["endLine"] = 1 });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Copied 1 lines (1-1)", text, StringComparison.Ordinal);
        Assert.Contains("note.txt is unchanged", text, StringComparison.Ordinal);
        Assert.Equal(original, await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a copy from a path outside every grant is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCopyLinesTool_Copy_NonPermittedPath_ReturnsDenial()
    {
        using var fixture = new TempDirectoryFixture();
        var outside = TempDirectoryFixture.WriteFile(fixture.Outside, "secret.txt", "one\ntwo\n");
        var tool = TextFileCopyLinesTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = outside, ["startLine"] = 1, ["endLine"] = 1 });

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

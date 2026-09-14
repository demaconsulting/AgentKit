using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileCutLinesTool"/> class.
/// </summary>
public class TextFileCutLinesToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileCutLinesTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("text_file_cut_lines", TextFileCutLinesTool.ToolName);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", TextFileCutLinesTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy or buffer is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileCutLinesTool_Create_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TextFileCutLinesTool.Create(null!, new TextFileLineBuffers()));
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        Assert.Throws<ArgumentNullException>(() => TextFileCutLinesTool.Create(policy, null!));
    }

    /// <summary>
    ///     Proves a range of lines is removed and the confirmation names the count, the first and
    ///     last captured line, the file's new total, and which line now sits where the removal
    ///     began.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCutLinesTool_Cut_Range_RemovesLinesAndReportsCountBoundsAndNewTotal()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\nthree\nfour\n");
        var tool = TextFileCutLinesTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 2, ["endLine"] = 3 });

        // A removal shifts every line below it, so the confirmation states the new total and the
        // line that now sits at the cut's start rather than leaving a re-read as the only way to
        // learn the new numbering.
        var text = Assert.IsType<string>(result);
        Assert.Equal(
            "Cut 2 lines (2-3) into buffer 'default'. The file now has 2 lines. "
            + "First: two Last: three Line 2 is now: four",
            text);
        Assert.Equal("one\nfour\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a cut that reaches the end of the file says so, since no line then sits where the
    ///     removal began.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCutLinesTool_Cut_ThroughEndOfFile_ReportsTheCutReachedTheEnd()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\nthree\n");
        var tool = TextFileCutLinesTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 2, ["endLine"] = 3 });

        var text = Assert.IsType<string>(result);
        Assert.Equal(
            "Cut 2 lines (2-3) into buffer 'default'. The file now has 1 lines and the cut reached "
            + "the end of the file. First: two Last: three",
            text);
    }

    /// <summary>
    ///     Proves an out-of-range cut is refused and changes nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCutLinesTool_Cut_OutOfRange_ReturnsDenialAndLeavesFileUnchanged()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\n");
        var tool = TextFileCutLinesTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 5, ["endLine"] = 6 });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Equal("one\ntwo\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a cut in a read-only location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCutLinesTool_Cut_ReadOnlyLocation_ReturnsDenial()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\n");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = TextFileCutLinesTool.Create(policy, new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 1, ["endLine"] = 1 });

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

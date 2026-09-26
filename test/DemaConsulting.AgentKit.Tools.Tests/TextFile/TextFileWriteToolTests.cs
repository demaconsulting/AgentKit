using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileWriteTool"/> class.
/// </summary>
/// <remarks>
///     The load-bearing scenarios are the ones that pin the tool's safety properties rather than its
///     happy path: that the previous content is captured verbatim before it is destroyed, that the
///     capture lands somewhere other than the model's working clipboard, and that the write decision
///     alone governs the write.
/// </remarks>
public class TextFileWriteToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileWriteTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("text_file_write", TextFileWriteTool.ToolName);
        Assert.StartsWith(
            TextFilePack.FamilyPrefix + "_", TextFileWriteTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a description that names the
    ///     siblings a model should reach for instead.
    /// </summary>
    /// <remarks>
    ///     The description is where the family teaches, because a refusal never prescribes a remedy.
    ///     An agent reaching for this tool to make a small edit clobbers the file, so the description
    ///     naming the create and replace tools is load-bearing rather than decorative.
    /// </remarks>
    [Fact]
    public void TextFileWriteTool_Create_ConstructedTool_CarriesTheToolNameAndADescriptionNamingItsSiblings()
    {
        using var fixture = new TempDirectoryFixture();

        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        Assert.Equal(TextFileWriteTool.ToolName, tool.Name);
        Assert.NotNull(tool.Description);
        Assert.Contains(TextFileCreateTool.ToolName, tool.Description, StringComparison.Ordinal);
        Assert.Contains(TextFileReplaceTool.ToolName, tool.Description, StringComparison.Ordinal);
        Assert.Contains(
            TextFileLineBuffers.OverwrittenSlot, tool.Description, StringComparison.Ordinal);
        // The honest limit is stated rather than implied away.
        Assert.Contains("most recent overwrite", tool.Description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy or buffer is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileWriteTool_Create_NullArguments_ThrowArgumentNullException()
    {
        using var fixture = new TempDirectoryFixture();

        Assert.Throws<ArgumentNullException>(
            () => TextFileWriteTool.Create(null!, new TextFileLineBuffers()));
        Assert.Throws<ArgumentNullException>(
            () => TextFileWriteTool.Create(RootedPolicy(fixture.Root), null!));
    }

    /// <summary>
    ///     Proves an absent file is brought into existence with the given content, and the
    ///     confirmation says it was created.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_AbsentFile_CreatesItAndReportsItWasCreated()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "new.txt", ["content"] = "alpha\nbeta\ngamma\n" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Wrote 17 characters in 3 lines.", text, StringComparison.Ordinal);
        Assert.Contains("did not exist and was created", text, StringComparison.Ordinal);
        Assert.Equal("alpha\nbeta\ngamma\n", await ReadAsync(Path.Combine(fixture.Root, "new.txt")));
    }

    /// <summary>
    ///     Proves an existing file's content is replaced wholesale, and the confirmation says it was
    ///     replaced rather than created.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_ExistingFile_ReplacesTheContentAndReportsItWasReplaced()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "old\nlines\nhere\n");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "note.txt", ["content"] = "new\n" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Wrote 4 characters in 1 lines.", text, StringComparison.Ordinal);
        Assert.Contains("previous content (15 characters) was captured", text, StringComparison.Ordinal);
        Assert.Equal("new\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves empty content empties the file rather than being refused, and is reported as zero
    ///     lines — the line model every other tool in the family addresses.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_EmptyContent_EmptiesTheFileAndReportsZeroLines()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "something\n");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "note.txt", ["content"] = string.Empty });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Wrote 0 characters in 0 lines.", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves the file's previous content reaches the recovery buffer byte for byte, trailing
    ///     newline and carriage returns included, before it is destroyed.
    /// </summary>
    /// <remarks>
    ///     Load-bearing: this is the whole of the capture-before-overwrite promise. Deleting the
    ///     capture call, or splitting and rejoining the text on the way in, fails here.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_ExistingFile_CapturesThePreviousContentVerbatim()
    {
        using var fixture = new TempDirectoryFixture();
        const string original = "alpha\r\nbeta\n\ngamma\n";
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", original);
        var buffers = new TextFileLineBuffers();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), buffers);

        await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "note.txt", ["content"] = "replacement" });

        Assert.True(buffers.TryPaste(TextFileLineBuffers.OverwrittenSlot, out var captured));
        Assert.Equal(original, captured);
    }

    /// <summary>
    ///     Proves a write never lands on the model's working clipboard: a fragment already staged in
    ///     the default slot survives an overwrite untouched.
    /// </summary>
    /// <remarks>
    ///     Load-bearing for the anti-clobber decision. Swapping the capture slot to
    ///     <see cref="TextFileLineBuffers.DefaultSlot"/> fails here.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_ExistingFile_LeavesTheDefaultBufferUntouched()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "displaced\n");
        var buffers = new TextFileLineBuffers();
        buffers.Capture(TextFileLineBuffers.DefaultSlot, "in-flight fragment\n");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), buffers);

        await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "note.txt", ["content"] = "replacement" });

        Assert.True(buffers.TryPaste(TextFileLineBuffers.DefaultSlot, out var staged));
        Assert.Equal("in-flight fragment\n", staged);
        Assert.True(buffers.TryPaste(TextFileLineBuffers.OverwrittenSlot, out var displaced));
        Assert.Equal("displaced\n", displaced);
    }

    /// <summary>
    ///     Proves an absent file leaves the recovery buffer empty and the confirmation says so, since
    ///     there was nothing to lose.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_AbsentFile_CapturesNothingAndSaysSo()
    {
        using var fixture = new TempDirectoryFixture();
        var buffers = new TextFileLineBuffers();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), buffers);

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "new.txt", ["content"] = "content" });

        Assert.Contains(
            "nothing was captured", Assert.IsType<string>(result), StringComparison.Ordinal);
        Assert.False(buffers.TryPaste(TextFileLineBuffers.OverwrittenSlot, out _));
        Assert.Empty(buffers.PopulatedSlots());
    }

    /// <summary>
    ///     Proves an existing but empty file captures nothing, so an empty slot cannot displace a
    ///     genuine earlier capture or report a recovery that would paste nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_PreviouslyEmptyFile_CapturesNothingAndSaysSo()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "empty.txt", string.Empty);
        var buffers = new TextFileLineBuffers();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), buffers);

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "empty.txt", ["content"] = "content" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("already empty, so nothing was captured", text, StringComparison.Ordinal);
        Assert.False(buffers.TryPaste(TextFileLineBuffers.OverwrittenSlot, out _));
    }

    /// <summary>
    ///     Proves a second overwrite replaces the first capture, pinning the honest limit that only
    ///     the most recent overwrite is recoverable rather than letting it be read as an unbounded
    ///     undo history.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_SecondOverwrite_ReplacesTheCapturedContent()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "first.txt", "first content\n");
        TempDirectoryFixture.WriteFile(fixture.Root, "second.txt", "second content\n");
        var buffers = new TextFileLineBuffers();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), buffers);

        await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "first.txt", ["content"] = "x" });
        await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "second.txt", ["content"] = "y" });

        Assert.True(buffers.TryPaste(TextFileLineBuffers.OverwrittenSlot, out var captured));
        Assert.Equal("second content\n", captured);
    }

    /// <summary>
    ///     Proves a write into a read-only location is refused and changes nothing, so write access
    ///     is never inferred from read access.
    /// </summary>
    /// <remarks>
    ///     Load-bearing for the write-grant check: substituting a read resolution for the write
    ///     resolution fails here.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_ReadOnlyLocation_ReturnsDenialAndLeavesTheFileUnchanged()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "note.txt", "original");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = TextFileWriteTool.Create(policy, new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "note.txt", ["content"] = "replacement" });

        Assert.Contains(
            "Denied (PathNotPermitted)", Assert.IsType<string>(result), StringComparison.Ordinal);
        Assert.Equal("original", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a path outside the permitted location is refused and nothing is written there.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_PathOutsideThePolicy_ReturnsDenialAndWritesNothing()
    {
        using var fixture = new TempDirectoryFixture();
        var outside = TempDirectoryFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = outside, ["content"] = "replacement" });

        Assert.Contains(
            "Denied (PathNotPermitted)", Assert.IsType<string>(result), StringComparison.Ordinal);
        Assert.Equal("secret", await ReadAsync(outside));
    }

    /// <summary>
    ///     Proves a missing parent directory is refused rather than materialized.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_MissingParentDirectory_ReturnsDenialAndCreatesNoDirectory()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "absent-dir/new.txt", ["content"] = "x" });

        Assert.Contains(
            "Denied (TargetNotFound)", Assert.IsType<string>(result), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "absent-dir")));
    }

    /// <summary>
    ///     Proves a path naming a directory is refused rather than treated as a file.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_DirectoryPath_ReturnsDenial()
    {
        using var fixture = new TempDirectoryFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "folder"));
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "folder", ["content"] = "x" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("is a directory, not a file", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file holding binary content is refused and left byte-identical, because content
    ///     that cannot be captured for recovery must not be destroyed.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_BinaryFile_ReturnsDenialAndLeavesItUnchanged()
    {
        using var fixture = new TempDirectoryFixture();
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0x03];
        var path = TempDirectoryFixture.WriteBytes(fixture.Root, "blob.bin", bytes);
        var buffers = new TextFileLineBuffers();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), buffers);

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "blob.bin", ["content"] = "text" });

        Assert.Contains(
            "Denied (UnsupportedMediaType)",
            Assert.IsType<string>(result),
            StringComparison.Ordinal);
        Assert.Equal(
            bytes,
            await System.IO.File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(buffers.PopulatedSlots());
    }

    /// <summary>
    ///     Proves the binary refusal states the fact and prescribes no remedy: it opens no redirect
    ///     sentence and names no sibling tool.
    /// </summary>
    /// <remarks>
    ///     The house rule the create tool's discipline test pins: a denial states a fact, and the
    ///     description is what names the siblings.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_Denial_PrescribesNoRemedy()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "blob.bin", [0x00, 0x01, 0x02, 0x03]);
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "blob.bin", ["content"] = "text" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("holds binary content, not text", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Use the '", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFileCreateTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFileReplaceTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFilePasteLinesTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an absent path or content argument is refused rather than throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_MissingPathOrContent_ReturnsDenialWithoutThrowing()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var missingPath = await InvokeAsync(
            tool, new AIFunctionArguments { ["content"] = "x" });
        var missingContent = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "new.txt" });

        Assert.Contains(
            "Denied (InvalidRequest)",
            Assert.IsType<string>(missingPath),
            StringComparison.Ordinal);
        Assert.Contains(
            "Denied (InvalidRequest)",
            Assert.IsType<string>(missingContent),
            StringComparison.Ordinal);
        Assert.False(System.IO.File.Exists(Path.Combine(fixture.Root, "new.txt")));
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

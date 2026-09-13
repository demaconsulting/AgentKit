using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFilePasteLinesTool"/> class, including the exact cut/paste
///     round-trip.
/// </summary>
public class TextFilePasteLinesToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFilePasteLinesTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("text_file_paste_lines", TextFilePasteLinesTool.ToolName);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", TextFilePasteLinesTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy or buffer is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFilePasteLinesTool_Create_NullArguments_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TextFilePasteLinesTool.Create(null!, new TextFileLineBuffers()));
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        Assert.Throws<ArgumentNullException>(() => TextFilePasteLinesTool.Create(policy, null!));
    }

    /// <summary>
    ///     Proves a cut immediately followed by a paste at the same line reproduces the original
    ///     file byte for byte — the exact round-trip.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("one\ntwo\nthree\nfour\n", 2, 3)]
    [InlineData("one\ntwo\nthree", 2, 3)]
    [InlineData("only\n", 1, 1)]
    [InlineData("a\nb\nc\nd\ne", 1, 2)]
    public async Task TextFilePasteLines_CutThenPasteAtSameLine_ReproducesTheFileExactly(
        string content,
        int startLine,
        int endLine)
    {
        // Arrange: cut and paste sharing one buffer, exactly as the pack composes them
        using var fixture = new ReparsePointFixture();
        var path = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", content);
        var buffers = new TextFileLineBuffers();
        var policy = RootedPolicy(fixture.Root);
        var cut = TextFileCutLinesTool.Create(policy, buffers);
        var paste = TextFilePasteLinesTool.Create(policy, buffers);

        // Act: cut the range, then paste it back at the same start line
        await InvokeAsync(
            cut,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = startLine, ["endLine"] = endLine });
        await InvokeAsync(
            paste,
            new AIFunctionArguments { ["path"] = "note.txt", ["atLine"] = startLine });

        // Assert: the file is byte-for-byte identical to what it started as
        Assert.Equal(content, await ReadAsync(path));
    }

    /// <summary>
    ///     Proves an omitted atLine appends the captured text to the end of the file, and the
    ///     confirmation names the span the appended text occupies and the file's new total.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePasteLines_OmittedAtLine_AppendsToEndAndReportsSpanAndNewTotal()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\nthree\n");
        var buffers = new TextFileLineBuffers();
        var policy = RootedPolicy(fixture.Root);
        var cut = TextFileCutLinesTool.Create(policy, buffers);
        var paste = TextFilePasteLinesTool.Create(policy, buffers);

        // Cut the first line, then paste it (append) at the end
        await InvokeAsync(
            cut,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 1, ["endLine"] = 1 });
        var result = await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "note.txt" });

        Assert.Equal(
            "Pasted 1 lines from buffer 'default' at the end of the file. "
            + "The pasted text occupies line 3, and the file now has 3 lines.",
            Assert.IsType<string>(result));
        Assert.Equal("two\nthree\none\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves a paste at a named line reports the whole span the inserted text now occupies, in
    ///     the updated file's own numbering, together with the file's new total.
    /// </summary>
    /// <remarks>
    ///     An insertion shifts every line below it, so the span and the new total are what let a
    ///     model issue its next line-addressed request without re-reading the file to re-derive the
    ///     numbering.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePasteLines_AtLine_ReportsTheInsertedSpanAndNewTotal()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\nthree\nfour\nfive\n");
        var buffers = new TextFileLineBuffers();
        var policy = RootedPolicy(fixture.Root);
        var cut = TextFileCutLinesTool.Create(policy, buffers);
        var paste = TextFilePasteLinesTool.Create(policy, buffers);

        // Cut two lines from the top, then paste them back below what remains of the file
        await InvokeAsync(
            cut,
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 1, ["endLine"] = 2 });
        var result = await InvokeAsync(
            paste,
            new AIFunctionArguments { ["path"] = "note.txt", ["atLine"] = 2 });

        Assert.Equal(
            "Pasted 2 lines from buffer 'default' at line 2. "
            + "The pasted text occupies lines 2-3, and the file now has 5 lines.",
            Assert.IsType<string>(result));
        Assert.Equal("three\none\ntwo\nfour\nfive\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves pasting from an empty buffer is a refusal naming the buffer, not a silent no-op.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePasteLines_EmptyBuffer_ReturnsDenialNamingTheBuffer()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content\n");
        var paste = TextFilePasteLinesTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "note.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.Contains("default", text, StringComparison.Ordinal);
        // The file is unchanged.
        Assert.Equal("content\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Proves pasting into a file that does not exist is refused as <c>TargetNotFound</c> with a
    ///     plain fact that names no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePasteLines_MissingFile_RefusesNamingNoTool()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "source.txt", "captured\n");
        var buffers = new TextFileLineBuffers();
        var policy = RootedPolicy(fixture.Root);
        var copy = TextFileCopyLinesTool.Create(policy, buffers);
        var paste = TextFilePasteLinesTool.Create(policy, buffers);

        // Capture something so the refusal is about the missing file, not an empty buffer.
        await InvokeAsync(
            copy,
            new AIFunctionArguments { ["path"] = "source.txt", ["startLine"] = 1, ["endLine"] = 1 });

        var result = await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "missing.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.Contains("The requested file does not exist.", text, StringComparison.Ordinal);
        // The denial states the fact and prescribes no tool to run.
        Assert.DoesNotContain("text_file_create", text, StringComparison.Ordinal);
        Assert.DoesNotContain("text_file_read", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that when no slot holds captured text the empty-default-slot refusal states the
    ///     bare fact, naming no capture tool to run.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePasteLines_EmptyDefaultSlot_NoOtherSlotPopulated_StatesTheFactNamingNoTool()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content\n");
        var paste = TextFilePasteLinesTool.Create(RootedPolicy(fixture.Root), new TextFileLineBuffers());

        var result = await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "note.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.Contains("The buffer 'default' is empty.", text, StringComparison.Ordinal);
        // No slot holds content, so nothing is named; and no capture tool is prescribed.
        Assert.DoesNotContain("These slots hold captured lines", text, StringComparison.Ordinal);
        Assert.DoesNotContain("text_file_copy_lines", text, StringComparison.Ordinal);
        Assert.DoesNotContain("text_file_cut_lines", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
        Assert.Equal("content\n", await ReadAsync(Path.Combine(fixture.Root, "note.txt")));
    }

    /// <summary>
    ///     Regression test for the live defect: pasting from the empty default slot while another
    ///     slot holds captured text names that populated slot as a statement of fact, so a model that
    ///     captured into a named slot and then omitted the name learns which name to pass — instead
    ///     of re-reading the source into its context. The refusal states this fact and names no tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePasteLines_EmptySlot_AnotherSlotPopulated_NamesThatSlot()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "large.txt", "keep\nblock\n");
        ReparsePointFixture.WriteFile(fixture.Root, "extract.txt", "head\n");
        var buffers = new TextFileLineBuffers();
        var policy = RootedPolicy(fixture.Root);
        var copy = TextFileCopyLinesTool.Create(policy, buffers);
        var paste = TextFilePasteLinesTool.Create(policy, buffers);

        // The model captured into a named slot with copy, exactly as the live transcript did.
        await InvokeAsync(
            copy,
            new AIFunctionArguments
            {
                ["path"] = "large.txt",
                ["startLine"] = 2,
                ["endLine"] = 2,
                ["name"] = "extract_buffer"
            });

        // Then it pasted without the name, so the default slot is empty.
        var result = await InvokeAsync(paste, new AIFunctionArguments { ["path"] = "extract.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.Contains("The buffer 'default' is empty.", text, StringComparison.Ordinal);
        Assert.Contains("These slots hold captured lines: 'extract_buffer'.", text, StringComparison.Ordinal);
        // Naming the populated slot is a fact; the refusal prescribes no tool to run.
        Assert.DoesNotContain("Pass one of them as name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("text_file_copy_lines", text, StringComparison.Ordinal);
        Assert.DoesNotContain("text_file_cut_lines", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
        // The target is untouched: the refusal is a fact, not a mutation.
        Assert.Equal("head\n", await ReadAsync(Path.Combine(fixture.Root, "extract.txt")));
    }

    /// <summary>
    ///     Proves a named buffer relocates a cut fragment to a different file.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePasteLines_NamedBuffer_RelocatesToAnotherFile()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "source.txt", "keep\nmove me\n");
        ReparsePointFixture.WriteFile(fixture.Root, "target.txt", "head\n");
        var buffers = new TextFileLineBuffers();
        var policy = RootedPolicy(fixture.Root);
        var cut = TextFileCutLinesTool.Create(policy, buffers);
        var paste = TextFilePasteLinesTool.Create(policy, buffers);

        await InvokeAsync(
            cut,
            new AIFunctionArguments
            {
                ["path"] = "source.txt",
                ["startLine"] = 2,
                ["endLine"] = 2,
                ["name"] = "fragment"
            });
        await InvokeAsync(
            paste,
            new AIFunctionArguments { ["path"] = "target.txt", ["atLine"] = 1, ["name"] = "fragment" });

        Assert.Equal("keep\n", await ReadAsync(Path.Combine(fixture.Root, "source.txt")));
        Assert.Equal("move me\nhead\n", await ReadAsync(Path.Combine(fixture.Root, "target.txt")));
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

using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolOutputSpill"/> class.
/// </summary>
/// <remarks>
///     Tool delegates are declared <c>Task&lt;object&gt;</c> deliberately, matching every real
///     guarded tool; see the remarks on <see cref="GuardedToolFactoryTests"/>.
/// </remarks>
public class ToolOutputSpillTests
{
    /// <summary>
    ///     The result ceiling most scenarios compose against, small enough to exceed cheaply.
    /// </summary>
    private const int Ceiling = 1_000;

    /// <summary>
    ///     Proves a result within the ceiling is returned exactly as the tool produced it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_ResultWithinCeiling_IsReturnedUnchanged()
    {
        // Arrange: a tool returning text shorter than the ceiling
        var tools = ToolOutputSpill.Apply([TextTool("probe_small", "short answer")], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_small");

        // Assert: untouched
        Assert.Equal("short answer", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves an oversized text result is replaced by a preview naming its reference, its size,
    ///     and the reader tools, bounded by the ceiling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedText_ReturnsBoundedPreviewNamingReferenceAndReaders()
    {
        // Arrange: a tool returning far more than the ceiling
        var big = Lines(500);
        var tools = ToolOutputSpill.Apply([TextTool("probe_big", big)], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "probe_big"));

        // Assert: a successful result naming the reference, the source, the size and both readers,
        // within the ceiling
        Assert.StartsWith("[Output output-1 from probe_big: " + big.Length + " characters in 500 lines", preview, StringComparison.Ordinal);
        Assert.Contains(ToolOutputReadTool.ToolName, preview, StringComparison.Ordinal);
        Assert.Contains(ToolOutputSearchTool.ToolName, preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Denied", preview, StringComparison.Ordinal);
        Assert.True(preview.Length <= Ceiling, $"The preview is {preview.Length} characters.");
    }

    /// <summary>
    ///     Proves the preview shows numbered lines from the head and the tail of the output, with the
    ///     omitted range marked between them.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedText_PreviewShowsNumberedHeadAndTail()
    {
        // Arrange: a 500-line output
        var tools = ToolOutputSpill.Apply([TextTool("probe_big", Lines(500))], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "probe_big"));

        // Assert: the first and last lines, numbered and padded like text_file_read, and a marker
        Assert.Contains("\n  1| line 1\n", preview, StringComparison.Ordinal);
        Assert.EndsWith("\n500| line 500", preview, StringComparison.Ordinal);
        Assert.Contains("omitted ...]", preview, StringComparison.Ordinal);
        Assert.Contains("Showing lines 1-", preview, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves structured data is retained indented, so it can be paged line by line.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedStructuredResult_IsRetainedIndented()
    {
        // Arrange: a tool returning a large structured result, which reaches the runtime as JSON
        var rows = Enumerable.Range(1, 200).Select(n => new { row = n, name = "item " + n }).ToList();
        var tool = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Structured(rows))),
            "probe_rows",
            "Returns many rows.");
        var tools = ToolOutputSpill.Apply([tool], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it and read the start of what was retained
        await InvokeAsync(tools, "probe_rows");
        var page = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 1,
            ["lineCount"] = 6,
        }));

        // Assert: one value per line, not one compact line
        Assert.StartsWith("output-1 lines 1-6 of ", page, StringComparison.Ordinal);
        Assert.Contains("\"row\": 1", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a sequence of text parts is retained as text.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedTextContentList_IsRetained()
    {
        // Arrange: a tool returning two large text parts
        var tool = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult<object>(
                new List<AIContent> { new TextContent(Lines(100)), new TextContent(Lines(100)) })),
            "probe_parts",
            "Returns text parts.");
        var tools = ToolOutputSpill.Apply([tool], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_parts");

        // Assert: a preview over 200 lines
        Assert.Contains("in 200 lines", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves binary content is never retained, even beside a caption that alone exceeds the
    ///     ceiling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_BinaryContent_PassesThroughUnchanged()
    {
        // Arrange: a bare image and a captioned image whose caption exceeds the ceiling
        var bytes = new byte[Ceiling * 4];
        var bare = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Image(bytes, "image/png"))),
            "probe_image",
            "Returns an image.");
        var captioned = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Image(bytes, "image/png", Lines(500)))),
            "probe_captioned",
            "Returns a captioned image.");
        var tools = ToolOutputSpill.Apply([bare, captioned], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke both
        var bareResult = await InvokeAsync(tools, "probe_image");
        var captionedResult = await InvokeAsync(tools, "probe_captioned");

        // Assert: the content arrives as the tool produced it
        Assert.IsType<DataContent>(bareResult);
        var parts = Assert.IsAssignableFrom<IList<AIContent>>(captionedResult);
        Assert.IsType<DataContent>(parts[1]);
    }

    /// <summary>
    ///     Proves a tool that returns nothing still returns nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_NullResult_PassesThrough()
    {
        // Arrange: a tool returning nothing
        var tool = GuardedToolFactory.Create(
            (Func<Task<object?>>)(() => Task.FromResult<object?>(null)),
            "probe_empty",
            "Returns nothing.");
        var tools = ToolOutputSpill.Apply([tool], new ToolLimits(maxResultCharacters: 0));

        // Act / Assert: an absence, not a reference
        Assert.Null(await InvokeAsync(tools, "probe_empty"));
    }

    /// <summary>
    ///     Proves a tool that declared its output oversized is retained, whatever the measured size.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedMarker_IsRetained()
    {
        // Arrange: a tool declaring a modest output oversized, as a tool with a tighter ceiling would
        var tools = ToolOutputSpill.Apply(
            [MarkerTool("probe_marker", "alpha\nbeta", "The listing exceeds the limit.")],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = Assert.IsType<string>(await InvokeAsync(tools, "probe_marker"));

        // Assert: a reference, not the refusal
        Assert.StartsWith("[Output output-1 from probe_marker", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a declared-oversized output too large even to retain is refused with the tool's own
    ///     refusal, exactly as without retention.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_MarkerOverRetentionCeiling_ReturnsTheToolsRefusal()
    {
        // Arrange: a per-output ceiling below the output's size
        const string message = "The listing exceeds the 1000-character result limit.";
        var tools = ToolOutputSpill.Apply(
            [MarkerTool("probe_marker", Lines(500), message)],
            new ToolLimits(maxResultCharacters: Ceiling),
            new ToolOutputOptions(maxOutputCharacters: 2_000, maxRetainedCharacters: 2_000));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_marker");

        // Assert: byte-for-byte the refusal the tool always gave
        Assert.Equal(ToolResult.Denied(DenialReason.ResourceTooLarge, message), result);
    }

    /// <summary>
    ///     Proves any other output too large to retain is refused naming the retention ceiling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_PlainOutputOverRetentionCeiling_IsRefusedNamingTheCeiling()
    {
        // Arrange: a per-output ceiling below the output's size
        var tools = ToolOutputSpill.Apply(
            [TextTool("probe_big", Lines(500))],
            new ToolLimits(maxResultCharacters: Ceiling),
            new ToolOutputOptions(maxOutputCharacters: 2_000, maxRetainedCharacters: 2_000));

        // Act: invoke it
        var result = Assert.IsType<string>(await InvokeAsync(tools, "probe_big"));

        // Assert: a refusal naming the tool and the retention ceiling, never a truncation
        Assert.StartsWith("Denied (ResourceTooLarge): The output of 'probe_big'", result, StringComparison.Ordinal);
        Assert.Contains("2000-character retention limit", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the preview is clamped so it and its header fit a small ceiling, and that only the
    ///     header is delivered when no line fits.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_SmallCeiling_PreviewIsClampedAndHeaderAlwaysDelivered()
    {
        // Arrange: one composition whose ceiling leaves room for a few lines, one that leaves none
        var roomy = ToolOutputSpill.Apply([TextTool("probe_big", Lines(500))], new ToolLimits(maxResultCharacters: 500));
        var tight = ToolOutputSpill.Apply([TextTool("probe_big", Lines(500))], new ToolLimits(maxResultCharacters: 10));

        // Act: invoke both
        var clamped = Assert.IsType<string>(await InvokeAsync(roomy, "probe_big"));
        var headerOnly = Assert.IsType<string>(await InvokeAsync(tight, "probe_big"));

        // Assert: the first fits its ceiling; the second is the reference alone
        Assert.True(clamped.Length <= 500, $"The preview is {clamped.Length} characters.");
        Assert.Contains("No lines fit in the preview.", headerOnly, StringComparison.Ordinal);
        Assert.EndsWith("]", headerOnly, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the header says so when long lines were wrapped for paging.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_WrappedOutput_HeaderNotesTheWrapping()
    {
        // Arrange: one very long line
        var tools = ToolOutputSpill.Apply(
            [TextTool("probe_long", new string('x', 10_000))],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "probe_long"));

        // Assert: the header names the wrapping
        Assert.Contains("were wrapped onto continuation lines", preview, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the readers are appended after the supplied tools, in a fixed order, and the
    ///     supplied tools keep their metadata.
    /// </summary>
    [Fact]
    public void ToolOutputSpill_Apply_Tools_AreFollowedByTheReaders()
    {
        // Arrange: two tools
        var first = TextTool("probe_first", "a");
        var second = TextTool("probe_second", "b");

        // Act: compose retention over them
        var tools = ToolOutputSpill.Apply([first, second], ToolLimits.Default);

        // Assert: the supplied tools in order, then the two readers, with metadata unchanged
        Assert.Equal(
            ["probe_first", "probe_second", ToolOutputReadTool.ToolName, ToolOutputSearchTool.ToolName],
            tools.Select(tool => tool.Name));
        Assert.Equal(first.Description, tools[0].Description);
        Assert.Equal(first.JsonSchema.GetRawText(), tools[0].JsonSchema.GetRawText());
    }

    /// <summary>
    ///     Proves a reader's own result is never retained: a page over the ceiling is refused, not
    ///     turned into another reference.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_ReaderResults_AreNeverRetained()
    {
        // Arrange: a retained output
        var tools = ToolOutputSpill.Apply([TextTool("probe_big", Lines(500))], new ToolLimits(maxResultCharacters: Ceiling));
        await InvokeAsync(tools, "probe_big");

        // Act: ask for a page far larger than the ceiling
        var page = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 1,
            ["lineCount"] = 500,
        }));

        // Assert: refused with the ceiling, and no second reference was issued
        Assert.StartsWith("Denied (ResourceTooLarge)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("output-2", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a tool not built by the guarded factory is wrapped too, so a hand-built tool's
    ///     oversized output is retained.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_ToolNotBuiltByTheFactory_IsRetainedToo()
    {
        // Arrange: a function built directly by the underlying factory
        var big = Lines(500);
        var raw = AIFunctionFactory.Create(() => big, "probe_raw");
        var tools = ToolOutputSpill.Apply([raw], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_raw");

        // Assert: a reference, however the factory shaped the text
        Assert.StartsWith("[Output output-1 from probe_raw", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves each application allocates its own store, so one composition cannot read another's
    ///     outputs, and the tools supplied are not altered.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_TwoApplications_DoNotShareReferencesOrAlterTheSuppliedTools()
    {
        // Arrange: one declared-oversized tool composed twice
        const string message = "The listing exceeds the limit.";
        var tool = MarkerTool("probe_marker", Lines(10), message);
        var first = ToolOutputSpill.Apply([tool], ToolLimits.Default);
        var second = ToolOutputSpill.Apply([tool], ToolLimits.Default);

        // Act: retain an output through the first, read it through the second, and invoke the
        // original tool directly
        await InvokeAsync(first, "probe_marker");
        var fromSecond = Assert.IsType<string>(await InvokeAsync(second, ToolOutputReadTool.ToolName, new() { ["id"] = "output-1" }));
        var original = await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        // Assert: the second store never issued the reference, and the original still refuses
        Assert.Contains("never retained", fromSecond, StringComparison.Ordinal);
        Assert.Equal(ToolResult.Denied(DenialReason.ResourceTooLarge, message), original);
    }

    /// <summary>
    ///     Proves a list retention cannot be composed over is rejected.
    /// </summary>
    [Fact]
    public void ToolOutputSpill_Apply_InvalidLists_Throw()
    {
        // Arrange: tools for the invalid cases
        var tool = TextTool("probe_one", "a");
        var reserved = TextTool("tool_output_extra", "a");

        // Act & Assert: absent arguments, a null entry, a duplicate name and a reserved name
        Assert.Throws<ArgumentNullException>(() => ToolOutputSpill.Apply(null!, ToolLimits.Default));
        Assert.Throws<ArgumentNullException>(() => ToolOutputSpill.Apply([tool], null!));
        Assert.Throws<ArgumentException>(() => ToolOutputSpill.Apply([tool, null!], ToolLimits.Default));
        Assert.Throws<ArgumentException>(() => ToolOutputSpill.Apply([tool, tool], ToolLimits.Default));
        Assert.Throws<ArgumentException>(() => ToolOutputSpill.Apply([reserved], ToolLimits.Default));
    }

    /// <summary>
    ///     Builds a guarded tool returning fixed text.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="text">The text the tool returns.</param>
    /// <returns>The tool.</returns>
    internal static AIFunction TextTool(string name, string text) => GuardedToolFactory.Create(
        (Func<Task<object>>)(() => Task.FromResult(ToolResult.Text(text))),
        name,
        "Returns fixed text.");

    /// <summary>
    ///     Builds a guarded tool declaring its output oversized.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="text">The full output.</param>
    /// <param name="message">The refusal message given without retention.</param>
    /// <returns>The tool.</returns>
    internal static AIFunction MarkerTool(string name, string text, string message) => GuardedToolFactory.Create(
        (Func<Task<object>>)(() => Task.FromResult(ToolResult.Oversized(text, message))),
        name,
        "Returns an oversized output.");

    /// <summary>
    ///     Builds numbered text of the requested number of lines.
    /// </summary>
    /// <param name="count">The number of lines.</param>
    /// <returns>Lines of the form <c>line N</c>, joined by line feeds.</returns>
    internal static string Lines(int count) =>
        string.Join("\n", Enumerable.Range(1, count).Select(n => "line " + n));

    /// <summary>
    ///     Invokes the named tool from a composed list.
    /// </summary>
    /// <param name="tools">The composed list.</param>
    /// <param name="name">The tool to invoke.</param>
    /// <param name="arguments">The arguments, or null for none.</param>
    /// <returns>The tool's result.</returns>
    internal static async Task<object?> InvokeAsync(
        IReadOnlyList<AIFunction> tools,
        string name,
        AIFunctionArguments? arguments = null)
    {
        var tool = tools.Single(candidate => candidate.Name == name);
        return await tool.InvokeAsync(arguments ?? new AIFunctionArguments(), TestContext.Current.CancellationToken);
    }
}

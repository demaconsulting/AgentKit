using System.Collections;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolOutputReadTool"/> class.
/// </summary>
public class ToolOutputReadToolTests
{
    /// <summary>
    ///     Proves the tool is published under its documented name.
    /// </summary>
    [Fact]
    public void ToolOutputReadTool_Create_PublishesTheDocumentedName()
    {
        // Act: create the tool over an empty store
        var tool = ToolOutputReadTool.Create(new ToolOutputStore(ToolOutputOptions.Default), ToolLimits.Default);

        // Assert: the name the preview tells the model to call
        Assert.Equal("tool_output_read", tool.Name);
        Assert.Equal(ToolOutputReadTool.ToolName, tool.Name);
    }

    /// <summary>
    ///     Proves the tool accepts no path, so no request can name a file.
    /// </summary>
    [Fact]
    public void ToolOutputReadTool_Create_SchemaHasNoPathParameter()
    {
        // Act: create the tool and read its parameter names
        var tool = ToolOutputReadTool.Create(new ToolOutputStore(ToolOutputOptions.Default), ToolLimits.Default);
        var parameters = tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

        // Assert: an identifier and a range, nothing that could address the file system
        Assert.Equal(["id", "startLine", "lineCount"], parameters);
    }

    /// <summary>
    ///     Proves a requested range is returned numbered under a header naming the range and the
    ///     true total.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputReadTool_Read_Range_ReturnsNumberedLinesUnderHeader()
    {
        // Arrange: a retained output of twelve lines
        var (tool, id) = Retained(ToolOutputSpillTests.Lines(12), ToolLimits.Default);

        // Act: read lines 9 to 10
        var page = await ReadAsync(tool, id, 9, 2);

        // Assert: the text_file_read shape, padded to the width of the total
        Assert.Equal(id + " lines 9-10 of 12\n 9| line 9\n10| line 10", page);
    }

    /// <summary>
    ///     Proves an omitted line count returns as many whole lines as fit the ceiling, and the
    ///     header states where the page stopped.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputReadTool_Read_OmittedLineCount_ReturnsWholeLinesWithinCeiling()
    {
        // Arrange: a large output and a modest ceiling
        var limits = new ToolLimits(maxResultCharacters: 400);
        var (tool, id) = Retained(ToolOutputSpillTests.Lines(500), limits);

        // Act: read from line 1 with no count
        var page = await ReadAsync(tool, id, startLine: 1, lineCount: null);

        // Assert: a bounded page starting at line 1 that stops before the end
        Assert.True(page.Length <= 400, $"The page is {page.Length} characters.");
        Assert.StartsWith(id + " lines 1-", page, StringComparison.Ordinal);
        Assert.DoesNotContain("line 500", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a window past the end is an honest empty result naming the true total.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputReadTool_Read_PastTheEnd_ReturnsEmptyWindowNamingTheTotal()
    {
        // Arrange: a three-line output
        var (tool, id) = Retained("a\nb\nc", ToolLimits.Default);

        // Act: read beyond it
        var page = await ReadAsync(tool, id, 10, 5);

        // Assert: no lines, and the total stated
        Assert.Equal(id + " lines 10-9 of 3", page);
    }

    /// <summary>
    ///     Proves an explicit range over the ceiling is refused with the ceiling named, not truncated.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputReadTool_Read_RangeOverCeiling_IsRefusedNamingTheCeiling()
    {
        // Arrange: a large output and a small ceiling
        var (tool, id) = Retained(ToolOutputSpillTests.Lines(500), new ToolLimits(maxResultCharacters: 300));

        // Act: ask for every line
        var page = await ReadAsync(tool, id, 1, 500);

        // Assert: a size refusal naming the range and the ceiling
        Assert.Equal(
            "Denied (ResourceTooLarge): Lines 1-500 of " + id
            + " exceed the 300-character result limit. Request fewer lines.",
            page);
    }

    /// <summary>
    ///     Proves a range far larger than the ceiling is refused after reading no more lines than
    ///     the ceiling admits, rather than the whole requested range.
    /// </summary>
    [Fact]
    public void ToolOutputReadTool_ReadEntry_HugeRangeOverCeiling_ReadsOnlyWhatTheCeilingAdmits()
    {
        // Arrange: a million-line output whose lines count every read, and a small ceiling
        var lines = new ProbeLines(1_000_000, "line");
        var entry = new ToolOutputEntry("output-1", lines, 5_000_000, Wrapped: false);

        // Act: ask for every line there could be
        var page = Assert.IsType<string>(
            ToolOutputReadTool.ReadEntry(entry, new ToolLimits(maxResultCharacters: 1_000), 1, int.MaxValue));

        // Assert: refused, having read lines bounded by the ceiling, not by the output
        Assert.StartsWith("Denied (ResourceTooLarge)", page, StringComparison.Ordinal);
        Assert.True(lines.Reads <= 1_000, $"The refusal read {lines.Reads} lines.");
    }

    /// <summary>
    ///     Proves a page exactly at the ceiling is returned and one character over it is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputReadTool_Read_RangeExactlyAtCeiling_IsReturnedAndOneOverIsRefused()
    {
        // Arrange: the page lines 9 to 10 of a twelve-line output render to, in a fresh store
        // whose first output is output-1
        var expected = "output-1 lines 9-10 of 12\n 9| line 9\n10| line 10";
        var (atTool, atId) = Retained(
            ToolOutputSpillTests.Lines(12),
            new ToolLimits(maxResultCharacters: expected.Length));
        var (overTool, overId) = Retained(
            ToolOutputSpillTests.Lines(12),
            new ToolLimits(maxResultCharacters: expected.Length - 1));

        // Act: read the same range under a ceiling of exactly its size and one less
        var at = await ReadAsync(atTool, atId, 9, 2);
        var over = await ReadAsync(overTool, overId, 9, 2);

        // Assert: the page at its exact size, and a refusal one character below it
        Assert.Equal(expected, at);
        Assert.Equal(
            "Denied (ResourceTooLarge): Lines 9-10 of " + overId + " exceed the "
            + (expected.Length - 1) + "-character result limit. Request fewer lines.",
            over);
    }

    /// <summary>
    ///     Proves malformed paging arguments are refused as invalid requests.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputReadTool_Read_InvalidRange_IsRefused()
    {
        // Arrange: a retained output
        var (tool, id) = Retained("a", ToolLimits.Default);

        // Act: a zero start and a negative count
        var zeroStart = await ReadAsync(tool, id, 0, 1);
        var negativeCount = await ReadAsync(tool, id, 1, -1);

        // Assert: both refused as malformed
        Assert.StartsWith("Denied (InvalidRequest)", zeroStart, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", negativeCount, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves missing, malformed, unknown and evicted identifiers are each refused with their
    ///     own fact.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputReadTool_Read_UnresolvableIds_AreRefused()
    {
        // Arrange: a store holding one output at a time, after two outputs were retained
        var store = new ToolOutputStore(new ToolOutputOptions(maxRetainedOutputs: 1));
        store.Add("first");
        store.Add("second");
        var tool = ToolOutputReadTool.Create(store, ToolLimits.Default);

        // Act: read with each kind of identifier that cannot be resolved
        var missing = await ReadAsync(tool, null, null, null);
        var malformed = await ReadAsync(tool, "../secrets.txt", null, null);
        var unknown = await ReadAsync(tool, "output-7", null, null);
        var evicted = await ReadAsync(tool, "output-1", null, null);

        // Assert: each is a refusal, and the evicted one says so
        Assert.StartsWith("Denied (InvalidRequest)", missing, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", malformed, StringComparison.Ordinal);
        Assert.StartsWith("Denied (TargetNotFound)", unknown, StringComparison.Ordinal);
        Assert.Contains("no longer retained", evicted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Retains an output in a fresh store and creates the tool over it.
    /// </summary>
    /// <param name="text">The output to retain.</param>
    /// <param name="limits">The ceilings bounding each page.</param>
    /// <returns>The tool and the retained output's identifier.</returns>
    private static (AIFunction Tool, string Id) Retained(string text, ToolLimits limits)
    {
        var store = new ToolOutputStore(ToolOutputOptions.Default);
        var entry = store.Add(text)!;
        return (ToolOutputReadTool.Create(store, limits), entry.Id);
    }

    /// <summary>
    ///     Invokes the tool as the runtime would, with only the supplied arguments.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="id">The identifier, or null to omit it.</param>
    /// <param name="startLine">The start line, or null to omit it.</param>
    /// <param name="lineCount">The line count, or null to omit it.</param>
    /// <returns>The text result.</returns>
    private static async Task<string> ReadAsync(AIFunction tool, string? id, int? startLine, int? lineCount)
    {
        var arguments = new AIFunctionArguments();
        if (id is not null)
        {
            arguments["id"] = id;
        }

        if (startLine is not null)
        {
            arguments["startLine"] = startLine;
        }

        if (lineCount is not null)
        {
            arguments["lineCount"] = lineCount;
        }

        var result = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        return Assert.IsType<string>(result);
    }

    /// <summary>
    ///     A retained output's lines, all equal, that count how many times a line is read.
    /// </summary>
    /// <param name="count">The number of lines.</param>
    /// <param name="line">The content of every line.</param>
    private sealed class ProbeLines(int count, string line) : IReadOnlyList<string>
    {
        /// <summary>
        ///     Gets the number of lines read through the indexer.
        /// </summary>
        public int Reads { get; private set; }

        /// <inheritdoc/>
        public int Count => count;

        /// <inheritdoc/>
        public string this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
                Reads++;
                return line;
            }
        }

        /// <inheritdoc/>
        public IEnumerator<string> GetEnumerator()
        {
            for (var index = 0; index < count; index++)
            {
                yield return this[index];
            }
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

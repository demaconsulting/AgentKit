using System.Collections;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolOutputSearchTool"/> class.
/// </summary>
public class ToolOutputSearchToolTests
{
    /// <summary>
    ///     The output most scenarios search: six lines with a repeated word in two cases.
    /// </summary>
    private const string Output = "alpha\nbeta ERROR one\ngamma\ndelta\nepsilon error two\nzeta";

    /// <summary>
    ///     Proves the tool is published under its documented name and accepts no path.
    /// </summary>
    [Fact]
    public void ToolOutputSearchTool_Create_PublishesTheDocumentedNameWithNoPathParameter()
    {
        // Act: create the tool and read its parameter names
        var tool = ToolOutputSearchTool.Create(new ToolOutputStore(ToolOutputOptions.Default), ToolLimits.Default);
        var parameters = tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();

        // Assert: the documented name, and no parameter that could address the file system
        Assert.Equal("tool_output_search", tool.Name);
        Assert.DoesNotContain("path", parameters);
        Assert.Contains("id", parameters);
        Assert.Contains("pattern", parameters);
    }

    /// <summary>
    ///     Proves a literal search reports each matching line by number, case-sensitively by default.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSearchTool_Search_Literal_ReportsMatchingLinesByNumber()
    {
        // Arrange: a retained output
        var (tool, id) = Retained(Output, ToolLimits.Default);

        // Act: search for the upper-case word
        var result = await SearchAsync(tool, new() { ["id"] = id, ["pattern"] = "ERROR" });

        // Assert: only the exact-case line, numbered as tool_output_read numbers it
        Assert.Equal("2:beta ERROR one\n", result);
    }

    /// <summary>
    ///     Proves a regular-expression search, ignoring case, finds every matching line.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSearchTool_Search_RegexIgnoringCase_FindsEveryMatch()
    {
        // Arrange: a retained output
        var (tool, id) = Retained(Output, ToolLimits.Default);

        // Act: search with an expression and case-insensitivity
        var result = await SearchAsync(tool, new()
        {
            ["id"] = id,
            ["pattern"] = "error (one|two)",
            ["literal"] = false,
            ["ignoreCase"] = true,
        });

        // Assert: both lines
        Assert.Equal("2:beta ERROR one\n5:epsilon error two\n", result);
    }

    /// <summary>
    ///     Proves context lines are shown grep-style and separate runs are divided.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSearchTool_Search_ContextLines_AreShownGrepStyle()
    {
        // Arrange: a ten-line output with matches far apart
        var (tool, id) = Retained(ToolOutputSpillTests.Lines(10), ToolLimits.Default);

        // Act: search for two separated lines with one line of context
        var result = await SearchAsync(tool, new()
        {
            ["id"] = id,
            ["pattern"] = "^line (2|8)$",
            ["literal"] = false,
            ["contextLines"] = 1,
        });

        // Assert: matches with ':', context with '-', and a separator between the runs
        Assert.Equal("1-line 1\n2:line 2\n3-line 3\n--\n7-line 7\n8:line 8\n9-line 9\n", result);
    }

    /// <summary>
    ///     Proves the match cap is honored, and a search finding nothing says so.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSearchTool_Search_MaxMatchesAndNoMatches_AreHonored()
    {
        // Arrange: a retained output
        var (tool, id) = Retained(Output, ToolLimits.Default);

        // Act: cap at one match, and search for something absent
        var capped = await SearchAsync(tool, new() { ["id"] = id, ["pattern"] = "a", ["maxMatches"] = 1 });
        var none = await SearchAsync(tool, new() { ["id"] = id, ["pattern"] = "omega" });

        // Assert: the first match only, and a plain statement of no matches
        Assert.Equal("1:alpha\n", capped);
        Assert.Equal("No matches.", none);
    }

    /// <summary>
    ///     Proves an invalid expression and a missing pattern are refused as invalid requests.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSearchTool_Search_InvalidRequests_AreRefused()
    {
        // Arrange: a retained output
        var (tool, id) = Retained(Output, ToolLimits.Default);

        // Act: an unbalanced expression, a missing pattern, and negative context
        var invalid = await SearchAsync(tool, new() { ["id"] = id, ["pattern"] = "(", ["literal"] = false });
        var missing = await SearchAsync(tool, new() { ["id"] = id });
        var negative = await SearchAsync(tool, new() { ["id"] = id, ["pattern"] = "a", ["contextLines"] = -1 });

        // Assert: each refused as malformed
        Assert.StartsWith("Denied (InvalidRequest)", invalid, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", missing, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", negative, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a pathological expression is refused when it times out rather than stalling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSearchTool_Search_PathologicalRegex_IsRefusedOnTimeout()
    {
        // Arrange: a line that makes a nested quantifier backtrack catastrophically
        var (tool, id) = Retained(new string('a', 40) + "b", ToolLimits.Default);

        // Act: search with the pathological expression
        var result = await SearchAsync(tool, new() { ["id"] = id, ["pattern"] = "(a+)+$", ["literal"] = false });

        // Assert: refused, naming the per-line timeout and the output
        Assert.Equal(
            "Denied (InvalidRequest): The search pattern took longer than 1 second to evaluate against one line of "
            + id + ".",
            result);
    }

    /// <summary>
    ///     Proves a result over the ceiling is refused naming the ceiling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSearchTool_Search_ResultOverCeiling_IsRefused()
    {
        // Arrange: many matching lines and a small ceiling
        var (tool, id) = Retained(ToolOutputSpillTests.Lines(500), new ToolLimits(maxResultCharacters: 200));

        // Act: match every line
        var result = await SearchAsync(tool, new() { ["id"] = id, ["pattern"] = "line" });

        // Assert: a size refusal naming the ceiling
        Assert.StartsWith("Denied (ResourceTooLarge)", result, StringComparison.Ordinal);
        Assert.Contains("200-character result limit", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a result far larger than the ceiling is refused after reading no more lines than
    ///     the ceiling admits, rather than searching the whole output first.
    /// </summary>
    [Fact]
    public void ToolOutputSearchTool_SearchEntry_ResultOverCeiling_ReadsOnlyWhatTheCeilingAdmits()
    {
        // Arrange: a million matching lines that count every read, and a small ceiling
        var lines = new ProbeLines(1_000_000, "line");
        var entry = new ToolOutputEntry("output-1", lines, 5_000_000, Wrapped: false);

        // Act: match every line
        var result = Assert.IsType<string>(ToolOutputSearchTool.SearchEntry(
            entry,
            new ToolLimits(maxResultCharacters: 1_000),
            "line",
            contextLines: 0,
            ignoreCase: false,
            literal: true,
            maxMatches: null,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken));

        // Assert: the unchanged refusal, having read lines bounded by the ceiling, not by the output
        Assert.Equal("Denied (ResourceTooLarge): The search result exceeds the 1000-character result limit.", result);
        Assert.True(lines.Reads <= 1_000, $"The refusal read {lines.Reads} lines.");
    }

    /// <summary>
    ///     Proves a result exactly at the ceiling is returned and one character over it is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSearchTool_Search_ResultExactlyAtCeiling_IsReturnedAndOneOverIsRefused()
    {
        // Arrange: a listing with context and a separator, retained under a ceiling of exactly
        // its size and under one a character less
        const string expected = "1-line 1\n2:line 2\n3-line 3\n--\n7-line 7\n8:line 8\n9-line 9\n";
        var (atTool, atId) = Retained(
            ToolOutputSpillTests.Lines(10),
            new ToolLimits(maxResultCharacters: expected.Length));
        var (overTool, overId) = Retained(
            ToolOutputSpillTests.Lines(10),
            new ToolLimits(maxResultCharacters: expected.Length - 1));

        // Act: search both for two separated lines with one line of context
        var at = await SearchAsync(atTool, new()
        {
            ["id"] = atId,
            ["pattern"] = "^line (2|8)$",
            ["literal"] = false,
            ["contextLines"] = 1,
        });
        var over = await SearchAsync(overTool, new()
        {
            ["id"] = overId,
            ["pattern"] = "^line (2|8)$",
            ["literal"] = false,
            ["contextLines"] = 1,
        });

        // Assert: the listing at its exact size, and a refusal one character below it
        Assert.Equal(expected, at);
        Assert.Equal(
            "Denied (ResourceTooLarge): The search result exceeds the " + (expected.Length - 1)
            + "-character result limit.",
            over);
    }

    /// <summary>
    ///     Proves a search whose total time budget is spent is refused on timeout between lines,
    ///     even when no single line is slow to evaluate.
    /// </summary>
    [Fact]
    public void ToolOutputSearchTool_SearchEntry_SpentSearchBudget_IsRefusedOnTimeout()
    {
        // Arrange: a million lines that count every read, none matching
        var lines = new ProbeLines(1_000_000, "line");
        var entry = new ToolOutputEntry("output-1", lines, 5_000_000, Wrapped: false);

        // Act: search with a budget already spent
        var result = Assert.IsType<string>(ToolOutputSearchTool.SearchEntry(
            entry,
            ToolLimits.Default,
            "absent",
            contextLines: 0,
            ignoreCase: false,
            literal: true,
            maxMatches: null,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken));

        // Assert: the whole-search timeout refusal, worded for a literal search too, after at
        // most one line
        Assert.Equal("Denied (InvalidRequest): The search took longer than 0 second to run against output-1.", result);
        Assert.True(lines.Reads <= 1, $"The refusal read {lines.Reads} lines.");
    }

    /// <summary>
    ///     Proves an identifier the store never issued is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSearchTool_Search_UnknownId_IsRefused()
    {
        // Arrange: a store holding one output
        var (tool, _) = Retained(Output, ToolLimits.Default);

        // Act: search an identifier never issued
        var result = await SearchAsync(tool, new() { ["id"] = "output-42", ["pattern"] = "a" });

        // Assert: not found
        Assert.StartsWith("Denied (TargetNotFound)", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Retains an output in a fresh store and creates the tool over it.
    /// </summary>
    /// <param name="text">The output to retain.</param>
    /// <param name="limits">The ceilings bounding each result.</param>
    /// <returns>The tool and the retained output's identifier.</returns>
    private static (AIFunction Tool, string Id) Retained(string text, ToolLimits limits)
    {
        var store = new ToolOutputStore(ToolOutputOptions.Default);
        var entry = store.Add(text)!;
        return (ToolOutputSearchTool.Create(store, limits), entry.Id);
    }

    /// <summary>
    ///     Invokes the tool as the runtime would.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The text result.</returns>
    private static async Task<string> SearchAsync(AIFunction tool, AIFunctionArguments arguments)
    {
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

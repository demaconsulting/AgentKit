using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolOutputRetention"/> class and its snapshot types.
/// </summary>
/// <remarks>
///     The store, the attachment and the reader tools are real: the behavior under verification is
///     that one handle owns one store across every tool it wraps, and that the store's content and
///     counter survive an export and a restore, which only the real collaborators can show.
/// </remarks>
public class ToolOutputRetentionTests
{
    /// <summary>
    ///     The smallest result ceiling retention accepts, used so modest outputs are retained.
    /// </summary>
    private static readonly ToolLimits Limits = new(maxResultCharacters: ToolOutputSpill.MinimumResultCharacters);

    /// <summary>
    ///     Proves tools wrapped by separate calls share one store, so a reference issued by either is
    ///     read through the one pair of readers.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputRetention_Wrap_ToolsWrappedSeparately_ShareOneStore()
    {
        // Arrange: one retention, and two tools wrapped at different times
        var retention = new ToolOutputRetention(Limits);
        var first = retention.Wrap(LargeTool("report_first", "first"));
        var second = retention.Wrap([LargeTool("report_second", "second")])[0];

        // Act: each tool retains an output, and the readers read the second reference
        var firstPreview = await InvokeAsync(first, []);
        var secondPreview = await InvokeAsync(second, []);
        var page = await InvokeAsync(retention.ReaderTools[0], new() { ["id"] = "output-2", ["startLine"] = 1, ["lineCount"] = 1 });

        // Assert: the references are numbered in one sequence and the second is readable
        Assert.StartsWith("[Output output-1 from report_first", firstPreview, StringComparison.Ordinal);
        Assert.StartsWith("[Output output-2 from report_second", secondPreview, StringComparison.Ordinal);
        Assert.Equal("output-2 lines 1-1 of 400\n  1| second line 1", page);
    }

    /// <summary>
    ///     Proves the readers are the two tool_output tools, in order, and the same instances on
    ///     every read.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_ReaderTools_AreTheTwoReadersAndStable()
    {
        // Arrange / Act: a retention's readers, read twice
        var retention = new ToolOutputRetention(Limits);
        var readers = retention.ReaderTools;

        // Assert: read then search, the same instances each time
        Assert.Equal([ToolOutputReadTool.ToolName, ToolOutputSearchTool.ToolName], readers.Select(tool => tool.Name));
        Assert.Same(readers, retention.ReaderTools);
        Assert.Same(Limits, retention.Limits);
        Assert.Same(ToolOutputOptions.Default, retention.Options);
    }

    /// <summary>
    ///     Proves the readers are published as a read-only collection over an owned array, so a
    ///     consumer cannot replace a reader.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_ReaderTools_IsReadOnlyCollection()
    {
        // Arrange / Act: a retention's readers
        var retention = new ToolOutputRetention(Limits);
        var readers = retention.ReaderTools;

        // Assert: a read-only wrapper that refuses replacement, still the two readers in order
        Assert.IsType<ReadOnlyCollection<AIFunction>>(readers);
        Assert.Throws<NotSupportedException>(() => ((IList<AIFunction>)readers)[0] = readers[1]);
        Assert.Equal([ToolOutputReadTool.ToolName, ToolOutputSearchTool.ToolName], readers.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a result ceiling a reference could not fit in is rejected at construction, and a
    ///     missing one is a programming error.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_Constructor_CeilingBelowMinimum_ThrowsArgumentException()
    {
        // Act / Assert: a ceiling one below the minimum, and no ceiling at all
        var exception = Assert.Throws<ArgumentException>(
            () => new ToolOutputRetention(new ToolLimits(maxResultCharacters: ToolOutputSpill.MinimumResultCharacters - 1)));
        Assert.Equal("limits", exception.ParamName);
        Assert.Throws<ArgumentNullException>(() => new ToolOutputRetention(null!));
    }

    /// <summary>
    ///     Proves a tool named under the readers' reserved prefix is refused.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_Wrap_ReservedPrefix_Throws()
    {
        // Arrange: a retention and one of its own readers
        var retention = new ToolOutputRetention(Limits);

        // Act / Assert: wrapping a reader, singly or in a list, is refused
        Assert.Throws<ArgumentException>(() => retention.Wrap(retention.ReaderTools[0]));
        Assert.Throws<ArgumentException>(() => retention.Wrap([retention.ReaderTools[1]]));
    }

    /// <summary>
    ///     Proves a tool whose name no preview header is sized for is refused.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_Wrap_OverlongName_Throws()
    {
        // Arrange: a hand-built function with a name one past the limit
        var retention = new ToolOutputRetention(Limits);
        var tool = AIFunctionFactory.Create(() => "x", new string('a', ToolName.MaxLength + 1), "Too long.");

        // Act / Assert: refused, naming the parameter
        Assert.Equal("tool", Assert.Throws<ArgumentException>(() => retention.Wrap(tool)).ParamName);
    }

    /// <summary>
    ///     Proves two tools of one name in one call are refused, since a model selects by name.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_Wrap_DuplicateNamesInOneCall_Throws()
    {
        // Arrange: two tools sharing a name
        var retention = new ToolOutputRetention(Limits);
        var a = LargeTool("report_rows", "a");
        var b = LargeTool("report_rows", "b");

        // Act / Assert: refused, naming the list
        Assert.Equal("tools", Assert.Throws<ArgumentException>(() => retention.Wrap([a, b])).ParamName);
    }

    /// <summary>
    ///     Proves a missing tool, a missing list and a null entry are programming errors.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_Wrap_Null_Throws()
    {
        // Arrange: a retention
        var retention = new ToolOutputRetention(Limits);

        // Act / Assert: each missing argument is refused
        Assert.Throws<ArgumentNullException>(() => retention.Wrap((AIFunction)null!));
        Assert.Throws<ArgumentNullException>(() => retention.Wrap((IEnumerable<AIFunction>)null!));
        Assert.Throws<ArgumentException>(() => retention.Wrap([null!]));
    }

    /// <summary>
    ///     Proves a restored retention answers an exported reference with the same lines.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputRetention_ExportRestore_RestoredReaderReturnsSameLines()
    {
        // Arrange: a retention that retained one output
        var retention = new ToolOutputRetention(Limits);
        await InvokeAsync(retention.Wrap(LargeTool("report_rows", "row")), []);
        var arguments = new AIFunctionArguments { ["id"] = "output-1", ["startLine"] = 200, ["lineCount"] = 2 };
        var before = await InvokeAsync(retention.ReaderTools[0], arguments);

        // Act: export, restore, and read the same window
        var restored = ToolOutputRetention.Restore(retention.Export(), Limits);
        var after = await InvokeAsync(restored.ReaderTools[0], arguments);

        // Assert: the same page
        Assert.Equal("output-1 lines 200-201 of 400\n200| row line 200\n201| row line 201", after);
        Assert.Equal(before, after);
    }

    /// <summary>
    ///     Proves the wrapped-lines flag survives a restore, so a restored page still notes it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputRetention_ExportRestore_WrappedFlagSurvives()
    {
        // Arrange: a retention holding one output whose single line was wrapped
        var retention = new ToolOutputRetention(Limits);
        var minified = AIFunctionFactory.Create(() => new string('z', 5_000), "report_minified", "One long line.");
        await InvokeAsync(retention.Wrap(minified), []);

        // Act: export and restore
        var snapshot = retention.Export();
        var restored = ToolOutputRetention.Restore(snapshot, Limits).Export();

        // Assert: the flag was exported and carried through
        Assert.True(Assert.Single(snapshot.Outputs).Wrapped);
        Assert.True(Assert.Single(restored.Outputs).Wrapped);
    }

    /// <summary>
    ///     Proves a restored retention numbers new outputs after the last reference ever issued, even
    ///     one that was evicted before the export.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputRetention_Restore_NewOutputsNumberAfterLastIssued()
    {
        // Arrange: a retention keeping one output, which retained three (two evicted)
        var options = new ToolOutputOptions(maxRetainedOutputs: 1);
        var retention = new ToolOutputRetention(Limits, options);
        var tool = retention.Wrap(LargeTool("report_rows", "row"));
        await InvokeAsync(tool, []);
        await InvokeAsync(tool, []);
        await InvokeAsync(tool, []);

        // Act: restore and retain another output
        var restored = ToolOutputRetention.Restore(retention.Export(), Limits, options);
        var preview = await InvokeAsync(restored.Wrap(LargeTool("report_rows", "row")), []);

        // Assert: output-4, never a re-issued output-1 or output-2
        Assert.StartsWith("[Output output-4 ", preview, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that when only the counter is persisted, an earlier reference is answered as no
    ///     longer retained rather than never retained.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputRetention_Restore_CountersOnlySnapshot_EarlierReferenceAnsweredNoLongerRetained()
    {
        // Arrange: a snapshot carrying the counter of a retention that issued two references
        var snapshot = new ToolOutputRetentionSnapshot(2, []);

        // Act: restore it and read an earlier reference
        var restored = ToolOutputRetention.Restore(snapshot, Limits);
        var refusal = await InvokeAsync(restored.ReaderTools[0], new() { ["id"] = "output-2" });

        // Assert: the honest answer, that it was retained once and is not now
        Assert.StartsWith("Denied (TargetNotFound)", refusal, StringComparison.Ordinal);
        Assert.Contains("no longer retained", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the restoring ceilings apply: smaller ceilings evict the oldest outputs first.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputRetention_Restore_SmallerCeilings_EvictsOldestFirst()
    {
        // Arrange: a retention holding three outputs
        var retention = new ToolOutputRetention(Limits);
        var tool = retention.Wrap(LargeTool("report_rows", "row"));
        await InvokeAsync(tool, []);
        await InvokeAsync(tool, []);
        await InvokeAsync(tool, []);

        // Act: restore under a ceiling of two outputs
        var restored = ToolOutputRetention.Restore(
            retention.Export(), Limits, new ToolOutputOptions(maxRetainedOutputs: 2)).Export();

        // Assert: the oldest was dropped and the counter kept
        Assert.Equal(["output-2", "output-3"], restored.Outputs.Select(output => output.Id));
        Assert.Equal(3, restored.LastIssued);
    }

    /// <summary>
    ///     Proves a malformed snapshot is rejected rather than partly restored.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_Restore_MalformedSnapshot_Throws()
    {
        // Arrange: snapshots each broken in one way
        RetainedToolOutput Output(string id, params string[] lines) => new(id, lines, false);
        var malformed = new[]
        {
            new ToolOutputRetentionSnapshot(-1, []),
            new ToolOutputRetentionSnapshot(1, [Output("notes.txt", "x")]),
            new ToolOutputRetentionSnapshot(1, [Output("output-1", "x"), Output("output-1", "y")]),
            new ToolOutputRetentionSnapshot(1, [Output("output-1")]),
            new ToolOutputRetentionSnapshot(1, [Output("output-1", "a\nb")]),
            new ToolOutputRetentionSnapshot(1, [Output("output-1", new string('x', 2_001))]),
            new ToolOutputRetentionSnapshot(1, [null!])
        };

        // Act / Assert: each is rejected, and a missing snapshot is a programming error
        Assert.All(malformed, snapshot => Assert.Throws<ArgumentException>(() => ToolOutputRetention.Restore(snapshot, Limits)));
        Assert.Throws<ArgumentNullException>(() => ToolOutputRetention.Restore(null!, Limits));
    }

    /// <summary>
    ///     Proves a snapshot holding a reference above its own counter — one the retention never
    ///     issued — is rejected rather than installed.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_Restore_ReferenceAboveCounter_Throws()
    {
        // Arrange: snapshots whose outputs carry a number their counter says was never issued
        var aboveCounter = new[]
        {
            new ToolOutputRetentionSnapshot(1, [new RetainedToolOutput("output-2", ["x"], false)]),
            new ToolOutputRetentionSnapshot(0, [new RetainedToolOutput("output-1", ["x"], false)])
        };

        // Act / Assert: each is rejected, naming the snapshot
        Assert.All(
            aboveCounter,
            snapshot => Assert.Equal(
                "snapshot",
                Assert.Throws<ArgumentException>(() => ToolOutputRetention.Restore(snapshot, Limits)).ParamName));
    }

    /// <summary>
    ///     Proves a snapshot whose counter leaves too little room to keep numbering is rejected, so a
    ///     restored retention can never wrap its counter into a reference no reader resolves.
    /// </summary>
    [Fact]
    public void ToolOutputRetention_Restore_CounterWithoutHeadroom_Throws()
    {
        // Arrange: counters at and just below the end of the range, with and without an output at the
        // counter, and one just above the restorable bound
        var withoutHeadroom = new[]
        {
            new ToolOutputRetentionSnapshot(long.MaxValue, []),
            new ToolOutputRetentionSnapshot(long.MaxValue - 1, []),
            new ToolOutputRetentionSnapshot(
                long.MaxValue,
                [new RetainedToolOutput("output-9223372036854775807", ["x"], false)]),
            new ToolOutputRetentionSnapshot(
                long.MaxValue - 1,
                [new RetainedToolOutput("output-9223372036854775806", ["x"], false)]),
            new ToolOutputRetentionSnapshot(ToolOutputStore.MaxRestorableCounter + 1, [])
        };

        // Act: restore a snapshot exactly at the bound
        var atBound = ToolOutputRetention.Restore(
            new ToolOutputRetentionSnapshot(ToolOutputStore.MaxRestorableCounter, []),
            Limits);

        // Assert: each without headroom is rejected naming the snapshot; the bound itself is accepted
        Assert.All(
            withoutHeadroom,
            snapshot => Assert.Equal(
                "snapshot",
                Assert.Throws<ArgumentException>(() => ToolOutputRetention.Restore(snapshot, Limits)).ParamName));
        Assert.Equal(ToolOutputStore.MaxRestorableCounter, atBound.Export().LastIssued);
    }

    /// <summary>
    ///     Proves a snapshot keeps its own copy of the outputs it was constructed with, so a later
    ///     change to the caller's list does not change it.
    /// </summary>
    [Fact]
    public void ToolOutputRetentionSnapshot_Constructor_CallerListChanged_SnapshotUnchanged()
    {
        // Arrange: a snapshot built from a list the caller keeps
        var outputs = new List<RetainedToolOutput> { new("output-1", ["a"], false) };
        var snapshot = new ToolOutputRetentionSnapshot(1, outputs);

        // Act: the caller changes its list
        outputs.Add(new RetainedToolOutput("output-2", ["b"], false));
        outputs.RemoveAt(0);

        // Assert: the snapshot still holds the original output, in a read-only collection
        Assert.Equal("output-1", Assert.Single(snapshot.Outputs).Id);
        Assert.IsType<ReadOnlyCollection<RetainedToolOutput>>(snapshot.Outputs);
    }

    /// <summary>
    ///     Proves a retained output keeps its own copy of the lines it was constructed with, so a
    ///     later change to the caller's list changes neither it nor what a restore reads.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task RetainedToolOutput_Constructor_CallerLinesChanged_OutputUnchanged()
    {
        // Arrange: an output built from a list the caller keeps
        var lines = new List<string> { "a", "b" };
        var output = new RetainedToolOutput("output-1", lines, false);

        // Act: the caller changes its list, then the output is restored and read
        lines[0] = "changed";
        lines.Add("c");
        var restored = ToolOutputRetention.Restore(new ToolOutputRetentionSnapshot(1, [output]), Limits);
        var page = await InvokeAsync(restored.ReaderTools[0], new() { ["id"] = "output-1" });

        // Assert: the original lines, in a read-only collection, and the reader pages them
        Assert.Equal(["a", "b"], output.Lines);
        Assert.IsType<ReadOnlyCollection<string>>(output.Lines);
        Assert.Equal("output-1 lines 1-2 of 2\n1| a\n2| b", page);
    }

    /// <summary>
    ///     Proves a retained output refuses missing lines, whether constructed directly or
    ///     deserialized from JSON that omits them.
    /// </summary>
    [Fact]
    public void RetainedToolOutput_Constructor_NullLines_ThrowsArgumentNullException()
    {
        // Arrange: JSON for a snapshot whose output has no lines
        const string json = "{\"lastIssued\":1,\"outputs\":[{\"id\":\"output-1\",\"wrapped\":false}]}";

        // Act / Assert: both paths refuse, naming the lines
        Assert.Equal(
            "lines",
            Assert.Throws<ArgumentNullException>(() => new RetainedToolOutput("output-1", null!, false)).ParamName);
        Assert.Throws<ArgumentNullException>(() => JsonSerializer.Deserialize(json, ToolOutputRetentionSnapshot.JsonTypeInfo));
    }

    /// <summary>
    ///     Proves a snapshot survives a JSON round trip through its source-generated contract.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputRetention_Snapshot_JsonRoundTrip_IsEquivalent()
    {
        // Arrange: a snapshot of a retention holding one output
        var retention = new ToolOutputRetention(Limits);
        await InvokeAsync(retention.Wrap(LargeTool("report_rows", "row")), []);
        var snapshot = retention.Export();

        // Act: serialize and deserialize with the published contract
        var json = JsonSerializer.Serialize(snapshot, ToolOutputRetentionSnapshot.JsonTypeInfo);
        var copy = JsonSerializer.Deserialize(json, ToolOutputRetentionSnapshot.JsonTypeInfo)!;

        // Assert: camel-case names, and the same counter, reference, lines and flag
        Assert.Contains("\"lastIssued\":1", json, StringComparison.Ordinal);
        Assert.Equal(snapshot.LastIssued, copy.LastIssued);
        var original = Assert.Single(snapshot.Outputs);
        var restored = Assert.Single(copy.Outputs);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Lines, restored.Lines);
        Assert.Equal(original.Wrapped, restored.Wrapped);
    }

    /// <summary>
    ///     Proves two retentions never share references.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputRetention_SeparateInstances_AreIsolated()
    {
        // Arrange: two retentions, only the first of which retains an output
        var first = new ToolOutputRetention(Limits);
        var second = new ToolOutputRetention(Limits);
        await InvokeAsync(first.Wrap(LargeTool("report_rows", "row")), []);

        // Act: read the first's reference through the second's reader
        var refusal = await InvokeAsync(second.ReaderTools[0], new() { ["id"] = "output-1" });

        // Assert: the second never issued it
        Assert.StartsWith("Denied (TargetNotFound)", refusal, StringComparison.Ordinal);
        Assert.Contains("never retained", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds a hand-made tool returning 400 numbered lines, well beyond the minimum ceiling.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <param name="word">The word each line starts with.</param>
    /// <returns>The tool.</returns>
    private static AIFunction LargeTool(string name, string word) => AIFunctionFactory.Create(
        () => string.Join("\n", Enumerable.Range(1, 400).Select(n => word + " line " + n)),
        name,
        "Returns 400 lines.");

    /// <summary>
    ///     Invokes a tool and returns its text result.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="arguments">The arguments to supply.</param>
    /// <returns>The text the tool returned.</returns>
    private static async Task<string> InvokeAsync(AIFunction tool, AIFunctionArguments arguments)
    {
        var result = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        return result switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()!,
            _ => throw new InvalidOperationException("The tool returned " + result?.GetType().Name + ".")
        };
    }
}

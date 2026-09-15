namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="ProviderSessionSeed"/> and <see cref="ProviderTurn"/>: the whole
///     interface between the compaction engine and a provider adapter.
/// </summary>
public class ProviderSessionTests
{
    /// <summary>
    ///     Proves a seed carries instructions, tools and history separately, because providers accept
    ///     them separately — which is also why the first two are accounted for as fixed overhead
    ///     rather than as conversation.
    /// </summary>
    [Fact]
    public void ProviderSessionSeed_Construct_CarriesInstructionsToolsAndHistorySeparately()
    {
        // Arrange / Act: a seed for a rotation that preserved one record and one turn
        var history = new[] { TranscriptEntry.ContextRecord("earlier"), TranscriptEntry.User("latest") };
        var seed = new ProviderSessionSeed("be helpful", [], history);

        // Assert: each part is available in its own right
        Assert.Equal("be helpful", seed.Instructions);
        Assert.Empty(seed.Tools);
        Assert.Equal(2, seed.History.Count);
    }

    /// <summary>
    ///     Proves a null tool list is refused: an adapter discovering it mid-construction would
    ///     report the failure against the provider rather than against the composition.
    /// </summary>
    [Fact]
    public void ProviderSessionSeed_Construct_NullTools_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProviderSessionSeed(null, null!, []));
    }

    /// <summary>
    ///     Proves a null history is refused; a fresh session must be told what it is resuming from,
    ///     even when the answer is nothing.
    /// </summary>
    [Fact]
    public void ProviderSessionSeed_Construct_NullHistory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProviderSessionSeed(null, [], null!));
    }

    /// <summary>
    ///     Proves a null entry inside the history is refused rather than seeded, because the failure
    ///     would otherwise appear at the provider.
    /// </summary>
    [Fact]
    public void ProviderSessionSeed_Construct_NullHistoryEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ProviderSessionSeed(null, [], [null!]));
    }

    /// <summary>
    ///     Proves a turn with no explicit entries records itself as one assistant message, which is
    ///     the correct history for a provider that called no tools and spares a simple adapter from
    ///     restating its own answer.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_NoEntries_RecordsOneAssistantMessage()
    {
        // Arrange / Act: a plain answer
        var turn = new ProviderTurn("the answer");

        // Assert: recorded as a single assistant message carrying that answer
        var entry = Assert.Single(turn.Entries);
        Assert.Equal(TranscriptEntryKind.AssistantMessage, entry.Kind);
        Assert.Equal("the answer", entry.Text);
    }

    /// <summary>
    ///     Proves an adapter that did call tools can record exactly what happened, so the engine's
    ///     transcript matches what the provider holds and a tier boundary can be snapped correctly.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_WithEntries_PreservesThemExactly()
    {
        // Arrange: the entries a tool-using turn produced
        TranscriptEntry[] entries =
        [
            TranscriptEntry.Assistant("let me look"),
            TranscriptEntry.ToolCall("c1", "read"),
            TranscriptEntry.ToolResult("c1", "contents")
        ];

        // Act: record the turn
        var turn = new ProviderTurn("the answer", entries);

        // Assert: the supplied entries are carried through unchanged
        Assert.Same(entries, turn.Entries);
    }

    /// <summary>
    ///     Proves a null answer is refused; a turn with no text could not be returned to the caller.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_NullResponseText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProviderTurn(null!));
    }

    /// <summary>
    ///     Proves a null entry is refused rather than recorded, because it would fail at the next
    ///     rotation instead of at the adapter that produced it.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_NullEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ProviderTurn("the answer", [null!]));
    }
}

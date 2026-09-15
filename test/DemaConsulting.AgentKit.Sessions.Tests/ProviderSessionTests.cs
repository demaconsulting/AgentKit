using Microsoft.Extensions.AI;

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
    ///     Proves a tool-using turn records what led to the answer and then the answer itself. The
    ///     entries are what every consumer records — the engine's transcript and any session keeping
    ///     its own history — so an answer absent from them is an answer absent from the history a
    ///     later turn is seeded from.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_WithEntries_PreservesThemAndAppendsTheAnswer()
    {
        // Arrange: the entries a tool-using turn produced before answering
        TranscriptEntry[] entries =
        [
            TranscriptEntry.Assistant("let me look"),
            TranscriptEntry.ToolCall("c1", "read"),
            TranscriptEntry.ToolResult("c1", "contents")
        ];

        // Act: record the turn
        var turn = new ProviderTurn("the answer", entries);

        // Assert: the supplied entries are carried through unchanged, in order, ahead of the answer
        Assert.Equal(4, turn.Entries.Count);
        Assert.Equal(entries, turn.Entries.Take(3));

        // Assert: and the answer is the last entry
        Assert.Equal(TranscriptEntryKind.AssistantMessage, turn.Entries[^1].Kind);
        Assert.Equal("the answer", turn.Entries[^1].Text);
    }

    /// <summary>
    ///     Proves an adapter that already ended its entries with the answer — one mapping a
    ///     provider's own message list straight across — does not have it recorded twice, which
    ///     would bill the conversation for it twice and show the model saying the same thing twice.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_EntriesAlreadyEndingWithTheAnswer_RecordItOnce()
    {
        // Arrange: entries whose final assistant message is the answer itself
        TranscriptEntry[] entries =
        [
            TranscriptEntry.ToolCall("c1", "read"),
            TranscriptEntry.ToolResult("c1", "contents"),
            TranscriptEntry.Assistant("the answer")
        ];

        // Act: record the turn
        var turn = new ProviderTurn("the answer", entries);

        // Assert: exactly the supplied entries, with the answer appearing once
        Assert.Equal(entries, turn.Entries);
        Assert.Single(turn.Entries, entry =>
            entry.Kind == TranscriptEntryKind.AssistantMessage && entry.Text == "the answer");
    }

    /// <summary>
    ///     Proves an assistant entry that merely precedes the answer is not mistaken for it: only a
    ///     trailing entry carrying the answer text is taken to be the answer already recorded.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_TrailingAssistantEntryWithDifferentText_AppendsTheAnswer()
    {
        // Arrange: a turn whose last entry is an assistant message saying something else
        TranscriptEntry[] entries = [TranscriptEntry.Assistant("let me look")];

        // Act: record the turn
        var turn = new ProviderTurn("the answer", entries);

        // Assert: the answer is appended rather than assumed present
        Assert.Equal(2, turn.Entries.Count);
        Assert.Equal("the answer", turn.Entries[^1].Text);
    }

    /// <summary>
    ///     Proves a seed copies the lists it is given and publishes read-only views of the copies.
    ///     A seed is an immutable snapshot an adapter may hold across a rotation: retaining the
    ///     caller's lists would let it start a session from something other than what was validated.
    /// </summary>
    [Fact]
    public void ProviderSessionSeed_Construct_CopiesTheSuppliedLists()
    {
        // Arrange: a mutable history handed to the seed
        var history = new List<TranscriptEntry> { TranscriptEntry.User("first") };
        var seed = new ProviderSessionSeed(null, [], history);

        // Act: mutate the caller's list afterwards
        history.Clear();

        // Assert: the seed is unaffected, and neither list it publishes can be written through
        Assert.Single(seed.History);
        Assert.Throws<NotSupportedException>(() => ((IList<TranscriptEntry>)seed.History).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<AIFunction>)seed.Tools).Clear());
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

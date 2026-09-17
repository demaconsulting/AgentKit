namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for <see cref="SessionTranscript"/>: the turn-granular, append-only verbatim
///     history the engine keeps out of session.
/// </summary>
public class SessionTranscriptTests
{
    /// <summary>
    ///     Proves a turn groups its message, answer and any tool traffic as one indivisible
    ///     exchange, so a tool call is never separated from its result.
    /// </summary>
    [Fact]
    public void SessionTranscript_AppendTurn_GroupsEntriesAsOneTurn()
    {
        // Arrange / Act: append one exchange carrying a message, a tool call, its result and the
        // answer
        var transcript = SessionTranscript.Empty.AppendTurn([
            TranscriptEntry.User("do it"),
            TranscriptEntry.ToolCall("c1", "call"),
            TranscriptEntry.ToolResult("c1", "result"),
            TranscriptEntry.Assistant("done"),
        ]);

        // Assert: one turn holding all four entries, which the flat view also reports
        Assert.Equal(1, transcript.TurnCount);
        Assert.Equal(4, transcript.Turns[0].Entries.Count);
        Assert.Equal(4, transcript.Entries.Count);
    }

    /// <summary>
    ///     Proves an empty turn, a null entry, or a null sequence is refused.
    /// </summary>
    [Fact]
    public void SessionTranscript_AppendTurn_Malformed_Throws()
    {
        // Act / Assert: each malformed turn is refused before it can enter the transcript
        Assert.Throws<ArgumentNullException>(() => SessionTranscript.Empty.AppendTurn(null!));
        Assert.Throws<ArgumentException>(() => SessionTranscript.Empty.AppendTurn([]));
        Assert.Throws<ArgumentException>(() => SessionTranscript.Empty.AppendTurn([null!]));
    }

    /// <summary>
    ///     Proves the split keeps the newest turns verbatim and returns everything older as whole
    ///     turns, at whole-turn granularity.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtTail_KeepsNewestTurns()
    {
        // Arrange: five turns
        var transcript = SessionTestData.TranscriptOf(5, tokensEach: 40);

        // Act: split, keeping the newest two verbatim
        var (older, retained) = transcript.SplitAtTail(keepTurns: 2);

        // Assert: the newest two are retained
        Assert.Equal(2, retained.TurnCount);

        // Assert: three older turns, returned as whole turns rather than flattened entries, so a
        // boundary can never fall inside one exchange
        Assert.Equal(3, older.Count);
        Assert.All(older, turn => Assert.Equal(2, turn.Entries.Count));
    }

    /// <summary>
    ///     Proves the split retains the whole transcript when it holds no more than the tail.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtTail_NothingOlder_RetainsAll()
    {
        // Arrange: a transcript shorter than the tail asked for
        var transcript = SessionTestData.TranscriptOf(2, tokensEach: 40);

        // Act: split, keeping more turns than exist
        var (older, retained) = transcript.SplitAtTail(keepTurns: 5);

        // Assert: nothing is older, and the whole transcript is retained
        Assert.Empty(older);
        Assert.Equal(2, retained.TurnCount);
    }

    /// <summary>
    ///     Proves the oldest turn can be dropped, which is the last resort under sustained pressure,
    ///     and that dropping from an empty transcript is refused.
    /// </summary>
    [Fact]
    public void SessionTranscript_DropOldestTurn_RemovesTheOldest()
    {
        // Arrange: three turns
        var transcript = SessionTestData.TranscriptOf(3, tokensEach: 40);

        // Act: drop the oldest
        var dropped = transcript.DropOldestTurn();

        // Assert: one fewer turn, and an empty transcript has nothing to drop
        Assert.Equal(2, dropped.TurnCount);
        Assert.Throws<InvalidOperationException>(() => SessionTranscript.Empty.DropOldestTurn());
    }
}

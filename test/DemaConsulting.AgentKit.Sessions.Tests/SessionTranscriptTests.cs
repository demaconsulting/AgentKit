namespace DemaConsulting.AgentKit.Sessions.Tests;

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
        var transcript = SessionTranscript.Empty.AppendTurn([
            TranscriptEntry.User("do it"),
            TranscriptEntry.ToolCall("c1", "call"),
            TranscriptEntry.ToolResult("c1", "result"),
            TranscriptEntry.Assistant("done"),
        ]);

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
        Assert.Throws<ArgumentNullException>(() => SessionTranscript.Empty.AppendTurn(null!));
        Assert.Throws<ArgumentException>(() => SessionTranscript.Empty.AppendTurn([]));
        Assert.Throws<ArgumentException>(() => SessionTranscript.Empty.AppendTurn([null!]));
    }

    /// <summary>
    ///     Proves the split keeps the newest turns verbatim and returns everything older as rendered
    ///     material, at whole-turn granularity.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtTail_KeepsNewestTurns()
    {
        var transcript = SessionTestData.TranscriptOf(5, tokensEach: 40);

        var (older, retained) = transcript.SplitAtTail(keepTurns: 2);

        Assert.Equal(2, retained.TurnCount);
        // Three older turns, each two entries.
        Assert.Equal(6, older.Count);
    }

    /// <summary>
    ///     Proves the split retains the whole transcript when it holds no more than the tail.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtTail_NothingOlder_RetainsAll()
    {
        var transcript = SessionTestData.TranscriptOf(2, tokensEach: 40);

        var (older, retained) = transcript.SplitAtTail(keepTurns: 5);

        Assert.Empty(older);
        Assert.Equal(2, retained.TurnCount);
    }

    /// <summary>
    ///     Proves the oldest turn can be dropped, which is the last resort of the drop-until-it-fits
    ///     rule, and that dropping from an empty transcript is refused.
    /// </summary>
    [Fact]
    public void SessionTranscript_DropOldestTurn_RemovesTheOldest()
    {
        var transcript = SessionTestData.TranscriptOf(3, tokensEach: 40);

        var dropped = transcript.DropOldestTurn();

        Assert.Equal(2, dropped.TurnCount);
        Assert.Throws<InvalidOperationException>(() => SessionTranscript.Empty.DropOldestTurn());
    }

    /// <summary>
    ///     Proves rendering is deterministic and labeled, and refuses a null entry.
    /// </summary>
    [Fact]
    public void SessionTranscript_Render_IsDeterministicAndLabeled()
    {
        var rendered = SessionTranscript.Render([
            TranscriptEntry.User("hello"),
            TranscriptEntry.Assistant("hi"),
        ]);

        Assert.Equal("USER: hello\nASSISTANT: hi", rendered);
        Assert.Throws<ArgumentException>(() => SessionTranscript.Render([null!]));
    }
}

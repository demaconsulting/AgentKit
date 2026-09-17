using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the provider seam: <see cref="ProviderSessionSeed"/> and
///     <see cref="ProviderTurn"/>, the whole interface between the compaction engine and an adapter.
/// </summary>
public class ProviderSessionTests
{
    /// <summary>
    ///     Proves a seed carries its instructions, tools and history, copied so a later mutation of a
    ///     caller's list cannot change what a session is created from.
    /// </summary>
    [Fact]
    public void ProviderSessionSeed_Construct_CopiesToolsAndHistory()
    {
        var tools = new List<AIFunction> { AIFunctionFactory.Create(() => 0, "probe") };
        var history = new List<TranscriptEntry> { TranscriptEntry.User("hi") };

        var seed = new ProviderSessionSeed("instructions", tools, history);
        tools.Clear();
        history.Clear();

        Assert.Equal("instructions", seed.Instructions);
        Assert.Single(seed.Tools);
        Assert.Single(seed.History);
    }

    /// <summary>
    ///     Proves a null tool or history list, or a null entry within one, is refused.
    /// </summary>
    [Fact]
    public void ProviderSessionSeed_Construct_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ProviderSessionSeed(null, null!, []));
        Assert.Throws<ArgumentNullException>(() => new ProviderSessionSeed(null, [], null!));
        Assert.Throws<ArgumentException>(() => new ProviderSessionSeed(null, [null!], []));
        Assert.Throws<ArgumentException>(() => new ProviderSessionSeed(null, [], [null!]));
    }

    /// <summary>
    ///     Proves a turn with no entries records the answer as its only entry.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_NoEntries_RecordsAnswerAlone()
    {
        var turn = new ProviderTurn("the answer");

        Assert.Single(turn.Entries);
        Assert.Equal(TranscriptEntryKind.AssistantMessage, turn.Entries[0].Kind);
        Assert.Equal("the answer", turn.Entries[0].Text);
    }

    /// <summary>
    ///     Proves the answer is appended after supplied entries, and never recorded twice when an
    ///     adapter already ended with it.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_RecordsAnswerExactlyOnce()
    {
        var withTool = new ProviderTurn("done", [
            TranscriptEntry.Assistant("working"),
            TranscriptEntry.ToolCall("c1", "call"),
            TranscriptEntry.ToolResult("c1", "result"),
        ]);
        Assert.Equal(4, withTool.Entries.Count);
        Assert.Equal("done", withTool.Entries[^1].Text);

        var endingWithAnswer = new ProviderTurn("done", [TranscriptEntry.Assistant("done")]);
        Assert.Single(endingWithAnswer.Entries);
    }

    /// <summary>
    ///     Proves a null response text, or a null entry, is refused.
    /// </summary>
    [Fact]
    public void ProviderTurn_Construct_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ProviderTurn(null!));
        Assert.Throws<ArgumentException>(() => new ProviderTurn("answer", [null!]));
    }

    /// <summary>
    ///     Proves a transcript entry pairs a tool call with its result by identifier, and refuses an
    ///     identifier on a plain message.
    /// </summary>
    [Fact]
    public void TranscriptEntry_Construct_ValidatesPairingIdentifier()
    {
        Assert.Equal("c1", TranscriptEntry.ToolCall("c1", "call").ToolCallId);
        Assert.Throws<ArgumentException>(() => new TranscriptEntry(TranscriptEntryKind.ToolCall, "call", null));
        Assert.Throws<ArgumentException>(() => new TranscriptEntry(TranscriptEntryKind.UserMessage, "hi", "c1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscriptEntry((TranscriptEntryKind)99, "x"));
    }

    /// <summary>
    ///     Proves every kind of entry renders as one mechanically labeled line, so consolidation
    ///     material says who said what and a tool result names the call it answers.
    /// </summary>
    /// <remarks>
    ///     The summarizer is a stateless call that receives text, so this rendering is the whole of
    ///     what it learns about the shape of the history. The labels are fixed rather than prose
    ///     precisely so the same history always renders to the same string, which is what lets a
    ///     test assert on it exactly.
    /// </remarks>
    [Fact]
    public void TranscriptEntry_ToTranscriptLine_IsDeterministicAndLabeled()
    {
        // Arrange / Act / Assert: each kind carries its own label, and a pair carries its identifier
        Assert.Equal("USER: hello", TranscriptEntry.User("hello").ToTranscriptLine());
        Assert.Equal("ASSISTANT: hi", TranscriptEntry.Assistant("hi").ToTranscriptLine());
        Assert.Equal("TOOL CALL [c1]: call", TranscriptEntry.ToolCall("c1", "call").ToTranscriptLine());
        Assert.Equal("TOOL RESULT [c1]: result", TranscriptEntry.ToolResult("c1", "result").ToTranscriptLine());
        Assert.Equal("RECORD: earlier", TranscriptEntry.ContextRecord("earlier").ToTranscriptLine());

        // Assert: the rendering is a pure function of the entry, so the same entry renders the same
        // way every time
        var entry = TranscriptEntry.User("hello");
        Assert.Equal(entry.ToTranscriptLine(), entry.ToTranscriptLine());
    }
}

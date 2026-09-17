using DemaConsulting.AgentKit.Core;
using GitHub.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotSessionObserver"/>: the usage reading it holds, the tool
///     traffic and answers it records for one turn, and its detection of the runtime rewriting
///     history behind AgentKit's back.
/// </summary>
/// <remarks>
///     Every event driven through the observer is a real SDK event object, so the matching under
///     test is the matching that runs in production. See <c>FakeCopilotTurnChannel.cs</c>.
/// </remarks>
public class CopilotSessionObserverTests
{
    /// <summary>
    ///     Proves a usage reading is held as the runtime reported it — occupancy, limit and the
    ///     conversation's share — because that reading is the only thing the session engine has to
    ///     decide a rotation on.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_UsageInfo_IsHeldAsTheLatestReading()
    {
        // Arrange: a fresh observer, which has seen nothing
        var observer = new CopilotSessionObserver();

        // Act: the runtime reports its occupancy, its limit and the conversation's share
        observer.OnEvent(CopilotEvents.Usage(currentTokens: 1200, tokenLimit: 8000, conversationTokens: 900));

        // Assert: all three figures are held exactly as they arrived
        var reading = observer.LatestUsage;
        Assert.NotNull(reading);
        Assert.Equal(1200, reading.CurrentTokens);
        Assert.Equal(8000, reading.TokenLimit);
        Assert.Equal(900, reading.ConversationTokens);
    }

    /// <summary>
    ///     Proves a later reading replaces an earlier one. Copilot's usage is cumulative session
    ///     state rather than a per-request figure, so the newest reading is simply the truth.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_SecondUsageInfo_ReplacesTheFirst()
    {
        // Arrange
        var observer = new CopilotSessionObserver();

        // Act: two readings, the second larger as a conversation grows
        observer.OnEvent(CopilotEvents.Usage(currentTokens: 1200, tokenLimit: 8000));
        observer.OnEvent(CopilotEvents.Usage(currentTokens: 3400, tokenLimit: 8000));

        // Assert: the later figure stands
        Assert.Equal(3400, observer.LatestUsage?.CurrentTokens);
    }

    /// <summary>
    ///     Proves a reading with no conversation split is held as such, so the session above can
    ///     decide what to do about it rather than being handed an invented number.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_UsageWithoutSplit_HoldsNoConversationFigure()
    {
        // Arrange
        var observer = new CopilotSessionObserver();

        // Act
        observer.OnEvent(CopilotEvents.Usage(currentTokens: 1200, tokenLimit: 8000));

        // Assert
        Assert.Null(observer.LatestUsage?.ConversationTokens);
    }

    /// <summary>
    ///     Proves the runtime truncating this session's history is seen. AgentKit asks for that to be
    ///     switched off, so observing it is how a request the runtime did not honor becomes
    ///     diagnosable rather than silent.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_TruncationEvent_MarksHistoryRewritten()
    {
        // Arrange
        var observer = new CopilotSessionObserver();
        Assert.False(observer.ProviderRewroteHistory);

        // Act
        observer.OnEvent(CopilotEvents.Truncation());

        // Assert
        Assert.True(observer.ProviderRewroteHistory);
    }

    /// <summary>
    ///     Proves the runtime compacting this session is seen too. Compaction and truncation are
    ///     different runtime behaviors with the same consequence for AgentKit: the transcript the
    ///     engine holds no longer describes the conversation the provider does.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_CompactionEvent_MarksHistoryRewritten()
    {
        // Arrange
        var observer = new CopilotSessionObserver();

        // Act
        observer.OnEvent(CopilotEvents.CompactionStart());

        // Assert
        Assert.True(observer.ProviderRewroteHistory);
    }

    /// <summary>
    ///     Proves an event the adapter has no use for changes nothing. The runtime emits a great deal
    ///     that is not part of a conversation, and recording it would seed a future session with a
    ///     history the model never saw.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_UnrelatedEvent_IsIgnored()
    {
        // Arrange
        var observer = new CopilotSessionObserver();

        // Act: a plain session event carrying nothing this adapter matches on
        observer.OnEvent(new SessionEvent());

        // Assert: no reading, no entries, no rewrite, no error
        Assert.Null(observer.LatestUsage);
        Assert.Empty(observer.DrainEntries());
        Assert.False(observer.ProviderRewroteHistory);
        Assert.Null(observer.LastErrorMessage);
    }

    /// <summary>
    ///     Proves a turn's entries are collected in arrival order and carry the runtime's own call
    ///     identifiers, so a rotation keeps each call with its result.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_DrainEntries_ReturnsArrivalOrder()
    {
        // Arrange: a turn that announces its work, calls a tool, gets a result, then answers
        var observer = new CopilotSessionObserver();
        observer.BeginTurn();

        // Act
        observer.OnEvent(CopilotEvents.Assistant("Looking that up."));
        observer.OnEvent(CopilotEvents.ToolStart("call-1", "doc_read"));
        observer.OnEvent(CopilotEvents.ToolComplete("call-1", "the contents"));
        observer.OnEvent(CopilotEvents.Assistant("It says the contents."));

        // Assert: four entries, in the order the runtime produced them, paired by call identifier
        var entries = observer.DrainEntries();
        Assert.Collection(
            entries,
            entry => Assert.Equal(TranscriptEntryKind.AssistantMessage, entry.Kind),
            entry =>
            {
                Assert.Equal(TranscriptEntryKind.ToolCall, entry.Kind);
                Assert.Equal("call-1", entry.ToolCallId);
                Assert.Contains("doc_read", entry.Text, StringComparison.Ordinal);
            },
            entry =>
            {
                Assert.Equal(TranscriptEntryKind.ToolResult, entry.Kind);
                Assert.Equal("call-1", entry.ToolCallId);
                Assert.Equal("the contents", entry.Text);
            },
            entry => Assert.Equal("It says the contents.", entry.Text));
    }

    /// <summary>
    ///     Proves draining empties the buffer, so a turn's work is recorded exactly once even if a
    ///     caller asks twice.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_DrainEntries_Twice_ReturnsNothingTheSecondTime()
    {
        // Arrange
        var observer = new CopilotSessionObserver();
        observer.BeginTurn();
        observer.OnEvent(CopilotEvents.Assistant("hello"));

        // Act
        var first = observer.DrainEntries();
        var second = observer.DrainEntries();

        // Assert
        Assert.Single(first);
        Assert.Empty(second);
    }

    /// <summary>
    ///     Proves starting a turn discards whatever the previous one left behind. A turn that failed
    ///     mid-flight leaves entries in the buffer, and attributing them to the next turn would
    ///     record work in a turn that did not do it.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_BeginTurn_ClearsThePreviousTurnsEntries()
    {
        // Arrange: a turn that produced work and was never drained
        var observer = new CopilotSessionObserver();
        observer.BeginTurn();
        observer.OnEvent(CopilotEvents.Assistant("abandoned"));

        // Act: the next turn starts
        observer.BeginTurn();
        observer.OnEvent(CopilotEvents.Assistant("this turn"));

        // Assert: only this turn's work is recorded
        var entry = Assert.Single(observer.DrainEntries());
        Assert.Equal("this turn", entry.Text);
    }

    /// <summary>
    ///     Proves a usage reading survives a turn boundary, which is the deliberate difference from
    ///     the ChatClient adapter's per-request recorder: Copilot's figure is cumulative session
    ///     state, and clearing it would make a session that had already reported look like one that
    ///     never had.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_BeginTurn_KeepsTheUsageReading()
    {
        // Arrange: a session that has reported its occupancy
        var observer = new CopilotSessionObserver();
        observer.OnEvent(CopilotEvents.Usage(currentTokens: 1200, tokenLimit: 8000));

        // Act: a new turn begins
        observer.BeginTurn();

        // Assert: the reading still stands
        Assert.Equal(1200, observer.LatestUsage?.CurrentTokens);
    }

    /// <summary>
    ///     Proves a tool that failed is recorded with the runtime's error as its result. The model saw
    ///     the failure and reasoned from it, so a history showing the call succeeding — or showing no
    ///     result at all — would seed a future session with a conversation that did not happen.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_ToolFailure_RecordsTheErrorAsTheResult()
    {
        // Arrange
        var observer = new CopilotSessionObserver();
        observer.BeginTurn();

        // Act
        observer.OnEvent(CopilotEvents.ToolStart("call-9", "doc_read"));
        observer.OnEvent(CopilotEvents.ToolFailure("call-9", "file not found"));

        // Assert: the result entry carries the error text under the call's own identifier
        var entries = observer.DrainEntries();
        Assert.Equal(2, entries.Count);
        Assert.Equal(TranscriptEntryKind.ToolResult, entries[1].Kind);
        Assert.Equal("call-9", entries[1].ToolCallId);
        Assert.Equal("file not found", entries[1].Text);
    }

    /// <summary>
    ///     Proves the work of a nested agent the runtime ran on this session's behalf is not recorded.
    ///     Those messages were never part of this conversation and the model was never shown them as
    ///     such; recording them would seed a future session with a history it does not recognize.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_NestedAgentEvents_AreNotRecorded()
    {
        // Arrange: this conversation's call, then a nested agent's own traffic under it
        var observer = new CopilotSessionObserver();
        observer.BeginTurn();

        // Act
        observer.OnEvent(CopilotEvents.ToolStart("call-1", "agent_run"));
        observer.OnEvent(CopilotEvents.ToolStart("nested-1", "doc_read", parentToolCallId: "call-1"));
        observer.OnEvent(CopilotEvents.ToolComplete("nested-1", "nested result", parentToolCallId: "call-1"));
        observer.OnEvent(CopilotEvents.Assistant("nested answer", parentToolCallId: "call-1"));
        observer.OnEvent(CopilotEvents.ToolComplete("call-1", "the child reported back"));

        // Assert: only this conversation's call and its result, not the child's three events
        var entries = observer.DrainEntries();
        Assert.Equal(2, entries.Count);
        Assert.Equal("call-1", entries[0].ToolCallId);
        Assert.Equal("the child reported back", entries[1].Text);
    }

    /// <summary>
    ///     Proves a tool call with no usable identifier is skipped rather than recorded. A transcript
    ///     entry requires an identifier so a call can be paired with its result, and an unidentified
    ///     call could not be paired.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_ToolCallWithoutAnIdentifier_IsSkipped()
    {
        // Arrange
        var observer = new CopilotSessionObserver();
        observer.BeginTurn();

        // Act
        observer.OnEvent(CopilotEvents.ToolStart(toolCallId: null, "doc_read"));
        observer.OnEvent(CopilotEvents.ToolStart(toolCallId: "   ", "doc_read"));

        // Assert: nothing recorded, and nothing thrown on the runtime's dispatch thread
        Assert.Empty(observer.DrainEntries());
    }

    /// <summary>
    ///     Proves an empty assistant message is not recorded. The runtime emits assistant messages in
    ///     phases, and an empty one is not something the model said.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_EmptyAssistantMessage_IsNotRecorded()
    {
        // Arrange
        var observer = new CopilotSessionObserver();
        observer.BeginTurn();

        // Act
        observer.OnEvent(CopilotEvents.Assistant(string.Empty));

        // Assert
        Assert.Empty(observer.DrainEntries());
    }

    /// <summary>
    ///     Proves a session error is remembered, so a turn that ends without an answer can name the
    ///     cause instead of reporting only that nothing came back.
    /// </summary>
    [Fact]
    public void CopilotSessionObserver_OnEvent_SessionError_IsRemembered()
    {
        // Arrange
        var observer = new CopilotSessionObserver();

        // Act
        observer.OnEvent(CopilotEvents.Error("model unavailable"));

        // Assert
        Assert.Equal("model unavailable", observer.LastErrorMessage);
    }
}

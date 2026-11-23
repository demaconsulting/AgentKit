using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Copilot;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotAgent"/> and <see cref="CopilotAgentSession"/>: opening and
///     resuming the runtime session, releasing it after every run, streaming, attachments,
///     serialization, and the serialization of runs on one conversation.
/// </summary>
/// <remarks>
///     The agent is built over a scripted runtime through <see cref="CopilotAgentFactory.CreateAgent"/>,
///     so the configurations it hands the runtime are the factory's real ones — the documented
///     dependency the agent is built on — and every event it maps is a real SDK event object.
/// </remarks>
public class CopilotAgentTests
{
    /// <summary>
    ///     Proves a conversation's first run creates one runtime session, confined to the agent's
    ///     tools, and records that session's identity on the conversation.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_FirstRun_CreatesAConfinedSessionAndRecordsItsId()
    {
        // Arrange: an agent over a scripted runtime, and a fresh conversation
        var runtime = new FakeCopilotRuntime(_ => [Answered("hello")]);
        var agent = MakeAgent(runtime);
        var session = (CopilotAgentSession)await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        await agent.RunAsync("hi", session, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: one session created, confined, and its identity recorded; nothing resumed
        var config = Assert.Single(runtime.Configs);
        Assert.Equal(["custom:doc_read"], config.Tools.AvailableTools);
        Assert.NotNull(config.Permissions.OnRequest);
        Assert.Empty(runtime.Resumes);
        Assert.Equal("session-1", session.SessionId);
    }

    /// <summary>
    ///     Proves a later run resumes the recorded runtime session rather than creating another, and
    ///     resumes it under the same confinement it was created with.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_LaterRun_ResumesTheRecordedSessionWithTheSameConfinement()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime(_ => [Answered("ok")]);
        var agent = MakeAgent(runtime);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: two runs on one conversation
        await agent.RunAsync("first", session, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("second", session, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: one creation, then one resumption of that same session, identically confined
        var created = Assert.Single(runtime.Configs);
        var (resumedId, resumed) = Assert.Single(runtime.Resumes);
        Assert.Equal("session-1", resumedId);
        Assert.Equal(created.Tools.AvailableTools, resumed.Tools.AvailableTools);
        Assert.Equal(
            created.Tools.Definitions!.Select(tool => tool.Name),
            resumed.Tools.Definitions!.Select(tool => tool.Name));
        Assert.Contains(CopilotAgentFactory.BuiltInGitHubMcpServer, resumed.Mcp.DisabledServers!);
        Assert.NotNull(resumed.Permissions.OnRequest);
        Assert.Equal("second", runtime.Channels[1].Prompts.Single());
    }

    /// <summary>
    ///     Proves the runtime session is released after every successful run, and stays resumable:
    ///     the next run resumes it rather than creating a replacement.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_AfterEveryRun_ReleasesTheSessionAndKeepsItResumable()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime(_ => [Answered("ok")]);
        var agent = MakeAgent(runtime);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        await agent.RunAsync("first", session, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("second", session, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: each run's channel released exactly once, and the conversation still names the
        // session the first run created
        Assert.All(runtime.Channels, channel => Assert.Equal(1, channel.DisposeCount));
        Assert.Equal("session-1", ((CopilotAgentSession)session).SessionId);
    }

    /// <summary>
    ///     Proves a run made without a conversation opens its runtime session through the opener
    ///     whose sessions are deleted on release, because nothing could ever resume it, and still
    ///     releases it.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_WithoutASession_UsesASessionDeletedAfterTheRun()
    {
        // Arrange: separate runtimes behind the keeping and the deleting opener
        var kept = new FakeCopilotRuntime(_ => [Answered("ok")]);
        var deleted = new FakeCopilotRuntime(_ => [Answered("ok")]);
        var agent = CopilotAgentFactory.CreateAgent(
            [AIFunctionFactory.Create(() => "ok", "doc_read")],
            instructions: null,
            onPermissionRequest: null,
            name: null,
            model: null,
            workingDirectory: null,
            kept.Opener,
            kept.Resumer,
            deleted.Opener);

        // Act: a run with no conversation
        await agent.RunAsync("one-off question", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(kept.Configs);
        Assert.Single(deleted.Configs);
        Assert.Equal(1, Assert.Single(deleted.Channels).DisposeCount);
    }

    /// <summary>
    ///     Proves a run that fails still releases its runtime session, and still records it, so the
    ///     conversation is not orphaned.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_OnFailure_ReleasesTheSession()
    {
        // Arrange: a turn the runtime fails
        var runtime = new FakeCopilotRuntime(_ =>
            [new ScriptedTurn([], Failure: new InvalidOperationException("transport lost"))]);
        var agent = MakeAgent(runtime);
        var session = (CopilotAgentSession)await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunAsync("q", session, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("transport lost", failure.Message);
        Assert.Equal(1, Assert.Single(runtime.Channels).DisposeCount);
        Assert.Equal("session-1", session.SessionId);
    }

    /// <summary>
    ///     Proves a run canceled while its turn is in flight releases its runtime session.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_OnCancellation_ReleasesTheSession()
    {
        // Arrange: a turn that runs until it is canceled, and signals when it has started
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn([], During: async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }),
        ]);
        var agent = MakeAgent(runtime);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();

        // Act: cancel once the turn is running
        var run = agent.RunAsync("q", session, cancellationToken: cancellation.Token);
        await started.Task;
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(1, Assert.Single(runtime.Channels).DisposeCount);
    }

    /// <summary>
    ///     Proves the root agent's text deltas are streamed as text as they arrive, and the final
    ///     assistant message does not repeat the text the deltas already carried.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_RunStreaming_TextDeltas_AreStreamedAsTextContent()
    {
        // Arrange: two deltas, then the complete message they belong to
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn(
            [
                CopilotEvents.Delta("Hel", "m1"),
                CopilotEvents.Delta("lo", "m1"),
                CopilotEvents.Assistant("Hello", messageId: "m1"),
            ]),
        ]);
        var agent = MakeAgent(runtime);

        // Act
        var updates = await Collect(agent, "hi");

        // Assert: the text arrives exactly once, as the deltas
        var texts = updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(text => text.Text);
        Assert.Equal(["Hel", "lo"], texts);
        Assert.All(updates, update => Assert.Equal(agent.Id, update.AgentId));
        Assert.Equal("m1", updates[0].MessageId);
    }

    /// <summary>
    ///     Proves a final assistant message whose text never streamed is still delivered as text, so
    ///     a runtime that sent no deltas loses nothing.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_RunStreaming_MessageWithoutDeltas_IsDeliveredAsText()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime(_ => [Answered("whole answer")]);
        var agent = MakeAgent(runtime);

        // Act
        var updates = await Collect(agent, "hi");

        // Assert
        var text = Assert.Single(updates.SelectMany(update => update.Contents).OfType<TextContent>());
        Assert.Equal("whole answer", text.Text);
    }

    /// <summary>
    ///     Proves a tool call and its result are streamed under the identifier the runtime gave the
    ///     call, with the call's arguments and the result's content.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_RunStreaming_ToolCallAndResult_AreStreamedWithTheRuntimesCallId()
    {
        // Arrange
        var start = CopilotEvents.ToolStart("call-7", "doc_read");
        start.Data.Arguments = JsonDocument.Parse("""{"path":"notes.txt"}""").RootElement.Clone();
        var runtime = new FakeCopilotRuntime(_ =>
            [new ScriptedTurn([start, CopilotEvents.ToolComplete("call-7", "the notes")])]);
        var agent = MakeAgent(runtime);

        // Act
        var updates = await Collect(agent, "read the notes");

        // Assert
        var call = Assert.Single(updates.SelectMany(update => update.Contents).OfType<FunctionCallContent>());
        Assert.Equal("call-7", call.CallId);
        Assert.Equal("doc_read", call.Name);
        Assert.Equal("notes.txt", ((JsonElement)call.Arguments!["path"]!).GetString());

        var resultUpdate = Assert.Single(updates, update => update.Contents.OfType<FunctionResultContent>().Any());
        var result = resultUpdate.Contents.OfType<FunctionResultContent>().Single();
        Assert.Equal(ChatRole.Tool, resultUpdate.Role);
        Assert.Equal("call-7", result.CallId);
        Assert.Equal("the notes", result.Result);
    }

    /// <summary>
    ///     Proves a failed tool call streams the runtime's error as its result, or a fixed message
    ///     when the runtime gave none.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_RunStreaming_ToolFailure_StreamsTheErrorAsTheResult()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn(
            [
                CopilotEvents.ToolStart("call-1", "doc_read"),
                CopilotEvents.ToolFailure("call-1", "Denied: outside every permitted location."),
                CopilotEvents.ToolStart("call-2", "doc_read"),
                CopilotEvents.ToolFailure("call-2", null),
            ]),
        ]);
        var agent = MakeAgent(runtime);

        // Act
        var updates = await Collect(agent, "q");

        // Assert
        var results = updates.SelectMany(update => update.Contents).OfType<FunctionResultContent>().ToList();
        Assert.Equal("Denied: outside every permitted location.", results[0].Result);
        Assert.Equal("Tool execution failed", results[1].Result);
    }

    /// <summary>
    ///     Proves events a sub-agent produced are not streamed: they were never part of this
    ///     conversation.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_RunStreaming_NestedAgentEvents_AreNotStreamed()
    {
        // Arrange: a sub-agent's call, result and text around nothing of the root agent's own
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn(
            [
                CopilotEvents.ToolStart("nested-1", "doc_read", agentId: "sub-1"),
                CopilotEvents.ToolComplete("nested-1", "nested result", agentId: "sub-1"),
                CopilotEvents.Delta("nested text", "m9", agentId: "sub-1"),
                CopilotEvents.Assistant("nested answer", agentId: "sub-1"),
                WithAgent(CopilotEvents.Error("a sub-agent's own error"), "sub-1"),
            ]),
        ]);
        var agent = MakeAgent(runtime);

        // Act
        var updates = await Collect(agent, "q");

        // Assert: nothing at all, and the sub-agent's error did not fail the run
        Assert.Empty(updates);
    }

    /// <summary>
    ///     Proves the runtime's per-request usage is streamed as usage content.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_RunStreaming_AssistantUsage_IsStreamedAsUsageContent()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([CopilotEvents.AssistantUsage(120, 30)])]);
        var agent = MakeAgent(runtime);

        // Act
        var updates = await Collect(agent, "q");

        // Assert
        var usage = Assert.Single(updates.SelectMany(update => update.Contents).OfType<UsageContent>()).Details;
        Assert.Equal(120, usage.InputTokenCount);
        Assert.Equal(30, usage.OutputTokenCount);
        Assert.Equal(150, usage.TotalTokenCount);
        Assert.Equal(7, usage.CachedInputTokenCount);
        Assert.Equal(3, usage.AdditionalCounts!["CacheWriteTokens"]);
    }

    /// <summary>
    ///     Proves a session error the runtime reports for the root agent fails the run, naming the
    ///     error, and still releases the session.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_RunStreaming_SessionError_FailsTheRunNamingTheError()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([CopilotEvents.Error("model overloaded")])]);
        var agent = MakeAgent(runtime);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(agent, "q"));

        // Assert
        Assert.Contains("model overloaded", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, Assert.Single(runtime.Channels).DisposeCount);
    }

    /// <summary>
    ///     Proves a session error that quotes a token the runtime's sign-in knows, or one shaped like a
    ///     GitHub token, fails the run with both redacted and no cause attached.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_RunStreaming_SessionErrorQuotingAToken_IsRedacted()
    {
        // Arrange: a sign-in that knows a token of no GitHub shape, and an error quoting it and a gho_ token
        const string secret = "s3cr3t-agent-xyz";
        const string shaped = "gho_AgentSecret123";
        var secrets = new CopilotSignInSecrets();
        secrets.Add(secret);
        var plan = new CopilotSignInPlan { Secrets = secrets };
        var runtime = new FakeCopilotRuntime(_ =>
            [new ScriptedTurn([CopilotEvents.Error($"refused {secret} and {shaped}")])]);
        var agent = CopilotAgentFactory.CreateAgent(
            [AIFunctionFactory.Create(() => "ok", "doc_read")],
            instructions: null,
            onPermissionRequest: null,
            "test-agent",
            model: null,
            workingDirectory: null,
            runtime.Opener,
            runtime.Resumer,
            signIn: plan);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(agent, "q"));

        // Assert
        Assert.Equal("Session error: refused <redacted> and gho_<redacted>", failure.Message);
        Assert.DoesNotContain(secret, failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(shaped, failure.ToString(), StringComparison.Ordinal);
        Assert.Null(failure.InnerException);
    }

    /// <summary>
    ///     Proves the non-streaming run returns exactly what the stream carried, gathered into one
    ///     response.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_ReturnsTheStreamedContentAsOneResponse()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn(
            [
                CopilotEvents.ToolStart("call-1", "doc_read"),
                CopilotEvents.ToolComplete("call-1", "contents"),
                CopilotEvents.Delta("The notes ", "m1"),
                CopilotEvents.Delta("say hello.", "m1"),
                CopilotEvents.Assistant("The notes say hello.", messageId: "m1"),
            ]),
        ]);
        var agent = MakeAgent(runtime);

        // Act
        var response = await agent.RunAsync("q", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("The notes say hello.", response.Text);
        Assert.Contains(response.Messages.SelectMany(message => message.Contents), content => content is FunctionCallContent);
    }

    /// <summary>
    ///     Proves inline data in the input is sent as an in-memory blob attachment carrying the data,
    ///     its media type and a name, beside the text prompt.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_DataContent_IsSentAsABlobAttachment()
    {
        // Arrange: a message carrying text and an unnamed image
        var runtime = new FakeCopilotRuntime(_ => [Answered("a square")]);
        var agent = MakeAgent(runtime);
        byte[] image = [0x89, 0x50, 0x4E, 0x47];
        var input = new ChatMessage(ChatRole.User, [new TextContent("describe this"), new DataContent(image, "image/png")]);

        // Act
        await agent.RunAsync(input, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var sent = Assert.Single(runtime.Channels[0].Messages);
        Assert.Equal("describe this", sent.Prompt);
        var blob = Assert.IsType<AttachmentBlob>(Assert.Single(sent.Attachments!));
        Assert.Equal(Convert.ToBase64String(image), blob.Data);
        Assert.Equal("image/png", blob.MimeType);
        Assert.Equal("attachment-1.png", blob.DisplayName);
    }

    /// <summary>
    ///     Proves the prompt is the text of every input message joined with a newline, and that a run
    ///     with no inline data sends no attachments.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_PromptJoinsTheInputMessagesText()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime(_ => [Answered("ok")]);
        var agent = MakeAgent(runtime);
        ChatMessage[] input = [new(ChatRole.User, "first line"), new(ChatRole.User, "second line")];

        // Act
        await agent.RunAsync(input, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var sent = Assert.Single(runtime.Channels[0].Messages);
        Assert.Equal("first line\nsecond line", sent.Prompt);
        Assert.Null(sent.Attachments);
    }

    /// <summary>
    ///     Proves runs on one conversation are serialized: a second run waits for the first instead of
    ///     opening a second runtime session alongside it, and then resumes the first run's session.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_ConcurrentRunsOnOneSession_AreSerialized()
    {
        // Arrange: a first turn held open until the test releases it
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeCopilotRuntime(index => index == 0
            ? [new ScriptedTurn([], During: async _ => { started.SetResult(); await release.Task; })]
            : [Answered("second")]);
        var agent = MakeAgent(runtime);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: start a second run while the first is still in its turn
        var first = agent.RunAsync("first", session, cancellationToken: TestContext.Current.CancellationToken);
        await started.Task;
        var second = agent.RunAsync("second", session, cancellationToken: TestContext.Current.CancellationToken);
        var channelsWhileFirstRan = runtime.Channels.Count;
        release.SetResult();
        await Task.WhenAll(first, second);

        // Assert: the second run opened nothing while the first held the conversation, then
        // resumed the session the first created
        Assert.Equal(1, channelsWhileFirstRan);
        Assert.Single(runtime.Configs);
        Assert.Equal("session-1", Assert.Single(runtime.Resumes).SessionId);
    }

    /// <summary>
    ///     Proves a serialized conversation round-trips its runtime session identity and its state
    ///     bag, and that an agent given it resumes the same runtime session.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_SerializeSession_RoundTripsTheSessionIdAndStateBag()
    {
        // Arrange: a conversation that has run once and carries state of its own
        var runtime = new FakeCopilotRuntime(_ => [Answered("ok")]);
        var agent = MakeAgent(runtime);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("first", session, cancellationToken: TestContext.Current.CancellationToken);
        session.StateBag.SetValue("note", "kept");

        // Act: serialize, then restore on a second agent over the same runtime
        var serialized = await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var other = MakeAgent(runtime);
        var restored = await other.DeserializeSessionAsync(serialized, cancellationToken: TestContext.Current.CancellationToken);
        await other.RunAsync("second", restored, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the shape, the restored identity and state, and a resumption of the same session
        Assert.Equal("session-1", serialized.GetProperty("sessionId").GetString());
        Assert.Equal(JsonValueKind.Object, serialized.GetProperty("stateBag").ValueKind);
        Assert.Equal("session-1", Assert.IsType<CopilotAgentSession>(restored).SessionId);
        Assert.Equal("kept", restored.StateBag.GetValue<string>("note"));
        Assert.Equal("session-1", Assert.Single(runtime.Resumes).SessionId);
    }

    /// <summary>
    ///     Proves a serialized form that is not a JSON object is refused.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_DeserializeSession_NonObject_Throws()
    {
        // Arrange
        var agent = MakeAgent(new FakeCopilotRuntime());
        var array = JsonDocument.Parse("[]").RootElement.Clone();

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await agent.DeserializeSessionAsync(array, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a serialized form whose state bag is present but not an object is refused, rather
    ///     than restored with its state silently discarded, while a <c>null</c> state bag is still
    ///     accepted as an empty one.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_DeserializeSession_NonObjectStateBag_Throws()
    {
        // Arrange
        var agent = MakeAgent(new FakeCopilotRuntime());
        var corrupted = JsonDocument.Parse("""{ "sessionId": "session-1", "stateBag": "invalid" }""").RootElement.Clone();
        var nullBag = JsonDocument.Parse("""{ "sessionId": "session-1", "stateBag": null }""").RootElement.Clone();

        // Act / Assert: the malformed bag is refused, the null bag is restored
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await agent.DeserializeSessionAsync(corrupted, cancellationToken: TestContext.Current.CancellationToken));
        var restored = await agent.DeserializeSessionAsync(nullBag, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("session-1", Assert.IsType<CopilotAgentSession>(restored).SessionId);
    }

    /// <summary>
    ///     Proves serializing a conversation does not wait for a run in progress on it, and carries
    ///     the identifier the run recorded when it opened its runtime session.
    /// </summary>
    /// <remarks>
    ///     The run holds the conversation's gate for its whole streamed enumeration, so a
    ///     serialization that waited on it would deadlock a host serializing from inside its own
    ///     streaming loop.
    /// </remarks>
    [Fact]
    public async Task CopilotAgent_SerializeSession_DuringARun_ReturnsWithoutWaitingForTheRun()
    {
        // Arrange: a first turn held open until the test releases it
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeCopilotRuntime(_ =>
            [new ScriptedTurn([], During: async _ => { started.SetResult(); await release.Task; })]);
        var agent = MakeAgent(runtime);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        var run = agent.RunAsync("first", session, cancellationToken: TestContext.Current.CancellationToken);
        await started.Task;

        // Act: serialize while the run still holds the conversation
        var serializing = agent
            .SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken)
            .AsTask();
        var completedWhileRunning = serializing.IsCompleted;
        release.SetResult();
        await run;

        // Assert: it did not wait, and it carries the identifier recorded when the session was opened
        Assert.True(completedWhileRunning);
        Assert.Equal("session-1", (await serializing).GetProperty("sessionId").GetString());
    }

    /// <summary>
    ///     Proves a session another kind of agent created cannot be serialized by this one.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_SerializeSession_ForeignSession_Throws()
    {
        // Arrange
        var agent = MakeAgent(new FakeCopilotRuntime());

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await agent.SerializeSessionAsync(new ForeignSession(), cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a run on a session another kind of agent created is refused before the runtime is
    ///     touched.
    /// </summary>
    [Fact]
    public async Task CopilotAgent_Run_ForeignSession_Throws()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime();
        var agent = MakeAgent(runtime);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunAsync("q", new ForeignSession(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(runtime.Configs);
    }

    /// <summary>
    ///     Proves an agent the host names nothing reports the default name and description, and one
    ///     the host names reports that name.
    /// </summary>
    [Fact]
    public void CopilotAgent_Name_DefaultsWhenNoneSupplied()
    {
        // Arrange / Act
        var unnamed = MakeAgent(new FakeCopilotRuntime(), name: null);
        var named = MakeAgent(new FakeCopilotRuntime(), name: "document-assistant");

        // Assert
        Assert.Equal("GitHub Copilot Agent", unnamed.Name);
        Assert.Equal("An AI agent powered by GitHub Copilot", unnamed.Description);
        Assert.Equal("document-assistant", named.Name);
    }

    /// <summary>
    ///     Builds an agent over a scripted runtime, confined to one tool.
    /// </summary>
    /// <param name="runtime">The scripted runtime.</param>
    /// <param name="name">The agent's name.</param>
    /// <returns>The agent.</returns>
    private static CopilotAgent MakeAgent(FakeCopilotRuntime runtime, string? name = "test-agent") =>
        CopilotAgentFactory.CreateAgent(
            [AIFunctionFactory.Create(() => "ok", "doc_read")],
            instructions: null,
            onPermissionRequest: null,
            name,
            model: null,
            workingDirectory: null,
            runtime.Opener,
            runtime.Resumer);

    /// <summary>
    ///     Builds a scripted turn whose only event is a complete assistant message.
    /// </summary>
    /// <param name="text">The answer.</param>
    /// <returns>The scripted turn.</returns>
    private static ScriptedTurn Answered(string text)
    {
        var message = CopilotEvents.Assistant(text);
        return new ScriptedTurn([message], message);
    }

    /// <summary>
    ///     Runs the agent streaming and collects every update.
    /// </summary>
    /// <param name="agent">The agent.</param>
    /// <param name="prompt">The prompt.</param>
    /// <returns>The updates, in order.</returns>
    private static async Task<List<AgentResponseUpdate>> Collect(AIAgent agent, string prompt)
    {
        var updates = new List<AgentResponseUpdate>();
        await foreach (var update in agent.RunStreamingAsync(prompt, cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        return updates;
    }

    /// <summary>
    ///     Attributes an event to a sub-agent.
    /// </summary>
    /// <param name="sessionEvent">The event.</param>
    /// <param name="agentId">The sub-agent instance.</param>
    /// <returns>The same event, attributed.</returns>
    private static SessionEvent WithAgent(SessionEvent sessionEvent, string agentId)
    {
        sessionEvent.AgentId = agentId;
        return sessionEvent;
    }

    /// <summary>
    ///     A session some other kind of agent would have created.
    /// </summary>
    private sealed class ForeignSession : AgentSession
    {
    }
}

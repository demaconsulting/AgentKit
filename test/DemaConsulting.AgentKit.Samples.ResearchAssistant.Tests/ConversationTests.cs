using DemaConsulting.AgentKit.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Unit tests for <see cref="Conversation"/> and <see cref="ConversationPlan"/>: that a plan
///     carrying an AgentKit session runs the conversation on one and reports what each turn made of
///     it, and that a plan without one says so.
/// </summary>
/// <remarks>
///     Exercised against <c>InMemoryProviderSessionFactory</c>, which AgentKit ships precisely so
///     the whole session lifecycle can be run without a live model. That is what lets the sample's
///     conversation shape be a tested thing rather than something only a scheduled live run
///     touches.
/// </remarks>
public class ConversationTests
{
    /// <summary>
    ///     Proves a conversation on a compacting session returns the provider's answer and the
    ///     three facts a turn reports, so an application that looks can act on them.
    /// </summary>
    [Fact]
    public async Task Conversation_AskAsync_CompactingSession_ReturnsTheAnswerAndWhatTheTurnReported()
    {
        // Arrange: a plan carrying a compacting session over the in-memory provider session
        await using var conversation = await Conversation.StartAsync(
            CompactingPlan(out _),
            transcript: null,
            TestContext.Current.CancellationToken);

        // Act
        var turn = await conversation.AskAsync("what changed?", TestContext.Current.CancellationToken);

        // Assert: the answer, and a report of a session comfortably inside its window
        Assert.NotNull(turn);
        Assert.Equal("Acknowledged: what changed?", turn.Text);
        Assert.False(turn.RotationOccurred);
        Assert.False(turn.MaterialDropped);
        Assert.Equal(CompactionLevel.Low, turn.Level);
        Assert.Equal(0, conversation.Session?.RotationCount);
    }

    /// <summary>
    ///     Proves one conversation carries its turns, which is the premise of everything the sample
    ///     demonstrates: a plan made in one turn and consulted in another.
    /// </summary>
    [Fact]
    public async Task Conversation_AskAsync_SeveralTurns_AreCarriedOnOneProviderSession()
    {
        // Arrange
        await using var conversation = await Conversation.StartAsync(
            CompactingPlan(out var factory),
            transcript: null,
            TestContext.Current.CancellationToken);

        // Act: three turns on the one conversation
        await conversation.AskAsync("first", TestContext.Current.CancellationToken);
        await conversation.AskAsync("second", TestContext.Current.CancellationToken);
        await conversation.AskAsync("third", TestContext.Current.CancellationToken);

        // Assert: one provider session served all three, because nothing forced a rotation
        Assert.Single(factory.Sessions);
        Assert.Equal(0, conversation.Session?.RotationCount);
    }

    /// <summary>
    ///     Proves a conversation with no AgentKit session reports nothing about compaction, so the
    ///     turn loop prints no session lines for a run that has no session.
    /// </summary>
    [Fact]
    public async Task Conversation_StartAsync_ProviderManagedPlan_ExposesNoSession()
    {
        // Arrange: a plan with no compacting session, as a Copilot run produces
        using var client = new ScriptedChatClient(
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "hello"), InputTokens: 10));
        var plan = new ConversationPlan(new ChatClientAgent(client), Compacting: null);

        // Act
        await using var conversation = await Conversation.StartAsync(
            plan,
            transcript: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(conversation.Session);
    }

    /// <summary>
    ///     Proves the startup banner names the compacting session and the window it is accounted
    ///     against, which is the number the whole arrangement turns on.
    /// </summary>
    [Fact]
    public void ConversationPlan_Describe_CompactingPlan_NamesTheSessionAndItsWindow()
    {
        // Arrange
        var plan = CompactingPlan(out _);

        // Act
        var described = plan.Describe();

        // Assert
        Assert.Contains("compacting session", described, StringComparison.Ordinal);
        Assert.Contains(
            InMemoryProviderSessionFactory.DefaultWindowTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            described,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a provider with no AgentKit session says so in the banner rather than letting a
    ///     reader assume the conversation compacts when it does not.
    /// </summary>
    [Fact]
    public void ConversationPlan_Describe_ProviderManagedPlan_SaysNothingCompacts()
    {
        // Arrange
        using var client = new ScriptedChatClient(
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "hello"), InputTokens: 10));
        var plan = new ConversationPlan(new ChatClientAgent(client), Compacting: null);

        // Act
        var described = plan.Describe();

        // Assert
        Assert.Contains("provider-managed", described, StringComparison.Ordinal);
        Assert.Contains("nothing compacts", described, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds a conversation plan carrying a compacting session over the in-memory provider
    ///     session.
    /// </summary>
    /// <param name="factory">Receives the factory, so a test can count the sessions a run created.</param>
    /// <returns>The plan.</returns>
    private static ConversationPlan CompactingPlan(out InMemoryProviderSessionFactory factory)
    {
        factory = new InMemoryProviderSessionFactory();

        var options = new AgentSessionOptions(
            new StubSummarizer(),
            instructions: "You are a research assistant.");

        var window = new ContextWindow(
            InMemoryProviderSessionFactory.DefaultWindowTokens,
            ContextWindowSource.Stated);

        // The agent is unused on a compacting plan — it is what a delegated child would be started
        // from — so a scripted client that is never asked anything stands in for it. It is not
        // disposed here because the plan outlives this method; the client holds nothing.
        var client = new ScriptedChatClient(
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "hello"), InputTokens: 10));

        return new ConversationPlan(
            new ChatClientAgent(client),
            new CompactingSessionPlan(options, factory, window));
    }
    /// <summary>
    ///     A summarizer that consolidates deterministically, so a rotation in a test is a pure
    ///     function of its input.
    /// </summary>
    private sealed class StubSummarizer : ISummarizer
    {
        /// <inheritdoc/>
        public Task<string> ConsolidateAsync(
            ConsolidationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Task.FromResult($"record of tier {request.TierIndex}");
        }
    }
}

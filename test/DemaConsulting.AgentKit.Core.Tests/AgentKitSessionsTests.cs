using System.Text.RegularExpressions;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     End-to-end tests for AgentKit Sessions: a long compacting conversation exercised through the
///     public surface with the in-memory provider.
/// </summary>
public partial class AgentKitSessionsTests
{
    /// <summary>
    ///     Builds a message occupying approximately the requested number of tokens.
    /// </summary>
    /// <param name="tokens">The tokens the message should occupy.</param>
    /// <returns>A message string.</returns>
    private static string Msg(int tokens) => new('m', tokens * SessionTestData.CharactersPerToken);

    /// <summary>
    ///     Proves a conversation that runs well past a small window keeps answering, rotating
    ///     repeatedly as it goes.
    /// </summary>
    [Fact]
    public async Task AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering()
    {
        var factory = new InMemoryProviderSessionFactory(SessionTestData.SizedResponder(15), windowTokens: 400);
        var options = new AgentSessionOptions(new FakeSummarizer(0.2), verbatimTurns: 4);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        for (var turn = 0; turn < 40; turn++)
        {
            var response = await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
            Assert.NotNull(response.Text);
        }

        Assert.True(session.RotationCount > 0);
    }

    /// <summary>
    ///     Proves detail from an early turn is still carried in the context after many rotations,
    ///     which is the whole promise of tiered retention.
    /// </summary>
    [Fact]
    public async Task AgentKitSessions_AfterManyRotations_EarlyDetailIsStillCarriedInContext()
    {
        var summarizer = new FakeSummarizer(MarkerPreserving);
        var factory = new InMemoryProviderSessionFactory(SessionTestData.SizedResponder(30), windowTokens: 1000);
        var options = new AgentSessionOptions(summarizer, verbatimTurns: 5);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        await session.SendAsync("Remember MARKER0 which is important.", TestContext.Current.CancellationToken);
        for (var turn = 0; turn < 30; turn++)
        {
            await session.SendAsync(Msg(30), TestContext.Current.CancellationToken);
        }

        Assert.True(session.RotationCount > 0);
        var live = factory.Sessions[^1];
        Assert.Contains(live.History, entry => entry.Text.Contains("MARKER0", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the session reads the window from the provider session rather than from anything
    ///     configured alongside it, by running the same conversation against two providers that
    ///     differ only in the window they report.
    /// </summary>
    /// <remarks>
    ///     The window is a fact about the provider, so the adapter is the only thing that knows it.
    ///     A session carrying its own copy would have two sources for one fact and a rule for
    ///     choosing between them; the narrow window here must therefore compact where the wide one
    ///     does not, with nothing but the provider distinguishing them.
    /// </remarks>
    [Fact]
    public async Task AgentKitSessions_WindowComesFromTheProvider_NarrowCompactsWhereWideDoesNot()
    {
        var narrow = await RunAsync(windowTokens: 300);
        var wide = await RunAsync(windowTokens: 100_000);

        Assert.True(narrow > 0, "A narrow provider window must provoke compaction.");
        Assert.Equal(0, wide);

        static async Task<int> RunAsync(int windowTokens)
        {
            var factory = new InMemoryProviderSessionFactory(
                SessionTestData.SizedResponder(15), windowTokens: windowTokens);
            var options = new AgentSessionOptions(
                new FakeSummarizer(0.2), verbatimTurns: 3);
            await using var session = await CompactingAgentSession.CreateAsync(
                options, factory, TestContext.Current.CancellationToken);

            for (var turn = 0; turn < 20; turn++)
            {
                await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
            }

            return session.RotationCount;
        }
    }

    /// <summary>
    ///     A summarizer that preserves any marker in its material and pads to a modest size, so a
    ///     test can follow a specific fact through consolidation.
    /// </summary>
    /// <param name="request">The consolidation request.</param>
    /// <returns>A record carrying any markers found, padded to a modest size.</returns>
    private static string MarkerPreserving(ConsolidationRequest request)
    {
        var markers = MarkerPattern().Matches(request.Material)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal);
        var kept = string.Join(" ", markers);

        return $"[T{request.TierIndex}] {kept} {new string('.', 80)}".Trim();
    }

    /// <summary>
    ///     Gets the compiled pattern matching a marker token.
    /// </summary>
    /// <returns>The marker pattern.</returns>
    [GeneratedRegex("MARKER[0-9]+")]
    private static partial Regex MarkerPattern();
}

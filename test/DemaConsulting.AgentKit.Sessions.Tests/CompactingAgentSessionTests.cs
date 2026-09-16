namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="CompactingAgentSession"/>: the sequencing that turns options, a
///     layout, a rotation engine and a provider factory into a session that outlives its window.
/// </summary>
public class CompactingAgentSessionTests
{
    /// <summary>
    ///     Proves a new session starts one provider session seeded with no history, carrying the
    ///     instructions the application configured.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_SeedsOneEmptyProviderSession()
    {
        // Arrange: a session over the in-memory provider
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), "be helpful", providerWindowTokens: 1000, compaction: SessionTestData.SmallPolicy);

        // Act: create the session
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Assert: exactly one provider session, seeded empty, carrying the instructions
        var provider = Assert.Single(factory.Sessions);
        Assert.Empty(provider.Seed.History);
        Assert.Equal("be helpful", provider.Seed.Instructions);
        Assert.Equal(0, session.RotationCount);
    }

    /// <summary>
    ///     Proves an ordinary turn is answered without compaction and is recorded in the engine's own
    ///     transcript, which is where a later consolidation will read it from.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_BelowThreshold_AnswersWithoutRotating()
    {
        // Arrange: a session with plenty of room
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: take one turn
        var response = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: answered, nothing rotated, and both halves of the turn are in the transcript
        Assert.Contains("hello", response.Text, StringComparison.Ordinal);
        Assert.False(response.RotationOccurred);
        Assert.Equal(0, session.RotationCount);
        Assert.Equal(2, session.Layout.Transcript.Entries.Count);
        Assert.Single(factory.Sessions);
    }

    /// <summary>
    ///     Proves the central behavior: crossing the threshold consolidates older history, creates a
    ///     fresh provider session seeded from the preserved content, and only then disposes the one
    ///     it replaced. Rotation rather than in-place editing is what makes the behavior identical on
    ///     a provider that re-sends history and one that holds it server-side.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_AboveThreshold_RotatesIntoAFreshSeededSession()
    {
        // Arrange: a converging window and the small policy, with turns small enough that an entry
        // still fits tier zero - so the rotation has something to retain verbatim as well as
        // something to consolidate
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 70 * TokenEstimator.CharactersPerToken)),
            windowTokens: SessionTestData.ConvergentWindowTokens);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: SessionTestData.ConvergentWindowTokens, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 70 * TokenEstimator.CharactersPerToken);

        // Act: three turns, the third of which crosses the 420-token threshold
        await session.SendAsync(message, TestContext.Current.CancellationToken);
        await session.SendAsync(message, TestContext.Current.CancellationToken);
        var response = await session.SendAsync(message, TestContext.Current.CancellationToken);

        // Assert: the session rotated exactly once and reported it
        Assert.True(response.RotationOccurred);
        Assert.Equal(1, session.RotationCount);
        Assert.True(session.ConsolidationCount > 0);

        // Assert: the previous provider session was disposed and a replacement created
        Assert.Equal(2, factory.Sessions.Count);
        Assert.True(factory.Sessions[0].IsDisposed);
        Assert.False(factory.Sessions[1].IsDisposed);

        // Assert: the replacement was seeded with a consolidated record ahead of verbatim history
        var seeded = factory.Sessions[1].Seed.History;
        Assert.Equal(TranscriptEntryKind.ContextRecord, seeded[0].Kind);
        Assert.Contains(seeded, entry => entry.Kind != TranscriptEntryKind.ContextRecord);

        // Assert: the replacement carries the same capability, because rotation replaces history
        // rather than capability
        Assert.Equal(options.Tools, factory.Sessions[1].Seed.Tools);
    }

    /// <summary>
    ///     Proves a turn the provider never accepted leaves no trace. A provider may honor
    ///     cancellation or fail before taking the turn; a message recorded ahead of that would be a
    ///     turn no provider ever saw, which a later rotation would nonetheless consolidate and seed
    ///     into the replacement session.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderRejectsTheTurn_RecordsNoGhostEntry()
    {
        // Arrange: a live session and a token that was canceled before the turn was taken
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act: the provider refuses the turn
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.SendAsync("never seen by any provider", canceled.Token));

        // Assert: neither the engine's transcript nor the provider's history holds the message
        Assert.Empty(session.Layout.Transcript.Entries);
        Assert.Empty(factory.Sessions[0].History);
    }

    /// <summary>
    ///     Proves the engine prefers a provider's own account of the window, because those figures
    ///     count framing this library never sees.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReportsUsage_PrefersTheProviderFigures()
    {
        // Arrange: a provider that reports
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: take a turn
        var response = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: the figure is marked as the provider's own
        Assert.Equal(ContextUsageOrigin.Provider, response.Usage.Origin);
    }

    /// <summary>
    ///     Proves the engine falls back to its own estimate for the provider family that reports
    ///     nothing, so both provider shapes reach the same rotation decision by the same arithmetic.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReportsNothing_UsesItsOwnEstimate()
    {
        // Arrange: a provider that reveals nothing about its window
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000, reportsUsage: false);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: take a turn
        var response = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: the figure is marked as estimated, and reflects the engine's own transcript
        Assert.Equal(ContextUsageOrigin.Estimated, response.Usage.Origin);
        Assert.Equal(session.Layout.TotalEstimatedTokens, response.Usage.UsedTokens);
        Assert.Equal(100_000, response.Usage.WindowTokens);
    }

    /// <summary>
    ///     Proves the rotation threshold is derived from the window the usage figure was actually
    ///     measured against. The package's whole promise is that it rotates before the provider's
    ///     own compactor fires; a threshold taken from a configured window that disagrees with the
    ///     one the provider reports voids that guarantee exactly when it matters.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReportsASmallerWindow_RotatesAgainstTheReportedOne()
    {
        // Arrange: a host that configured 4,000 tokens against a provider reporting 600. The
        // configured threshold is 2,800 conversation tokens; the reported one is 420.
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 70 * TokenEstimator.CharactersPerToken)),
            windowTokens: SessionTestData.ConvergentWindowTokens);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 70 * TokenEstimator.CharactersPerToken);

        // Act: turns of 148 tokens each, so the conversation passes the reported threshold of 420
        // on the third turn while remaining nowhere near the configured one
        var first = await session.SendAsync(message, TestContext.Current.CancellationToken);
        var second = await session.SendAsync(message, TestContext.Current.CancellationToken);
        var third = await session.SendAsync(message, TestContext.Current.CancellationToken);

        // Assert: the provider's own window is what the session is measured against
        Assert.Equal(ContextUsageOrigin.Provider, third.Usage.Origin);
        Assert.Equal(SessionTestData.ConvergentWindowTokens, third.Usage.WindowTokens);
        Assert.Equal(4000, options.ProviderWindowTokens);
        Assert.Equal(2800, options.RotationThresholdTokens);

        // Assert: the earlier turns stayed below the reported threshold and the third crossed it
        Assert.False(first.RotationOccurred);
        Assert.False(second.RotationOccurred);
        Assert.True(third.RotationOccurred);
        Assert.Equal(1, session.RotationCount);
    }

    /// <summary>
    ///     Proves the reporting path performs no subtraction of this library's estimate. A provider
    ///     that reports its own conversation count is measured entirely in its own tokens, so a
    ///     deliberately wrong <see cref="AgentSessionOptions.FixedOverheadTokens"/> — here 2,589
    ///     tokens, the figure the compaction spike estimated for eleven tool declarations — cannot
    ///     move the point at which the session rotates.
    /// </summary>
    /// <remarks>
    ///     This is the regression test for mixing currencies. The defect subtracted the estimated
    ///     fixed overhead from a provider-measured usage and from a provider-reported window, so the
    ///     rotation point moved with an estimate of material the provider had already counted for
    ///     itself. Measured against the arithmetic as it stood, the same provider rotated on turn
    ///     seven with no instructions configured and on turn eight with instructions estimated at
    ///     2,589 tokens — the provider having reported identical figures in both runs.
    ///     <para>
    ///     No existing test could have caught it. Every rotation test either configures no
    ///     instructions and no tools, making the estimated overhead zero, or runs against
    ///     <see cref="InMemoryProviderSession"/>, which measures its own reported overhead with the
    ///     very same <see cref="TokenEstimator"/> — so the two currencies are identical by
    ///     construction and the subtraction cancels exactly. The fake here is deliberately not the
    ///     in-memory session for that reason.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReportsConversation_RotatesRegardlessOfTheEstimate()
    {
        // Act: the same reporting provider, run once with no estimated overhead at all and once
        // with an estimate of thousands of tokens
        var withoutOverhead = await RotationTurnAsync(instructionTokens: 0);
        var withWrongOverhead = await RotationTurnAsync(instructionTokens: 2589);

        // Assert: the provider reported a 10,000-token window carrying 500 tokens of its own
        // overhead, so the threshold is 70 percent of 9,500 - that is 6,650 conversation tokens,
        // crossed by the seventh turn of 1,000
        Assert.Equal(7, withoutOverhead);

        // Assert: and the estimate, right or wrong, did not enter into it
        Assert.Equal(withoutOverhead, withWrongOverhead);
    }

    /// <summary>
    ///     Runs a session against a provider that reports its own conversation split and returns the
    ///     turn on which it first rotated.
    /// </summary>
    /// <remarks>
    ///     The instructions are sized to an exact estimated token count, which is the only input
    ///     that varies between runs: the provider reports the same figures either way, so a rotation
    ///     turn that moves with this parameter is a rotation decision contaminated by an estimate.
    /// </remarks>
    /// <param name="instructionTokens">The estimated tokens the configured instructions occupy.</param>
    /// <returns>The one-based turn on which the session rotated.</returns>
    private static async Task<int> RotationTurnAsync(int instructionTokens)
    {
        var factory = new SplitReportingProviderSessionFactory(
            windowTokens: 10_000, overheadTokens: 500, conversationTokensPerTurn: 1000);
        var options = new AgentSessionOptions(
            new FakeSummarizer(),
            instructions: new string('i', instructionTokens * TokenEstimator.CharactersPerToken),
            compaction: SessionTestData.SmallPolicy);
        Assert.Equal(instructionTokens, options.FixedOverheadTokens);

        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 50 * TokenEstimator.CharactersPerToken);

        for (var turn = 1; turn <= 20; turn++)
        {
            var response = await session.SendAsync(message, TestContext.Current.CancellationToken);
            if (response.RotationOccurred)
            {
                return turn;
            }
        }

        Assert.Fail("The session never rotated, so there is no rotation point to compare.");
        return 0;
    }

    /// <summary>
    ///     Proves a crossing the provider reported produces a real consolidation and a replacement
    ///     session even where this library's own estimate sees tier zero barely touched.
    /// </summary>
    /// <remarks>
    ///     <b>This is the regression test for the second half of the currency defect.</b> The
    ///     trigger was already measured in the provider's tokens, and the split
    ///     <see cref="RotationEngine"/> performs is measured in
    ///     <see cref="TranscriptEntry.EstimatedTokens"/> — this library's character ratio. Where the
    ///     two disagree the trigger fired and the split did nothing: a provider reporting 500
    ///     conversation tokens in a 600-token window crosses a threshold of 420 for a turn whose
    ///     local estimate is a few dozen tokens against a tier-zero budget of 100, so the engine
    ///     found no overflow, returned the layout unchanged, created no replacement, and reported
    ///     the turn as an ordinary one — leaving the provider to run on into its own compactor,
    ///     which is the one outcome this package exists to prevent. The provider knows
    ///     <em>whether</em> the context is too large; the estimator only decides <em>what</em> to
    ///     consolidate, and is not entitled to veto the decision it was never asked to make.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReportsACrossingTheEstimateCannotSee_ConsolidatesAnyway()
    {
        // Arrange: a provider counting 500 conversation tokens per turn in a 600-token window, so
        // the first turn crosses the 420-token threshold, against a 20-token message whose whole
        // turn this library estimates at well under tier zero's 100-token budget
        var factory = new SplitReportingProviderSessionFactory(
            windowTokens: SessionTestData.ConvergentWindowTokens,
            overheadTokens: 0,
            conversationTokensPerTurn: 500);
        var summarizer = new FakeSummarizer();
        var options = new AgentSessionOptions(
            summarizer,
            providerWindowTokens: SessionTestData.ConvergentWindowTokens,
            compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 20 * TokenEstimator.CharactersPerToken);

        // Act: one turn, which the provider reports as a crossing and the estimate does not
        var response = await session.SendAsync(message, TestContext.Current.CancellationToken);

        // Assert: the crossing was the provider's own, in the provider's own tokens
        Assert.Equal(ContextUsageOrigin.Provider, response.Usage.Origin);

        // Assert: the reported crossing produced a real consolidation, not a reported non-rotation
        Assert.True(response.RotationOccurred);
        Assert.Equal(1, session.RotationCount);
        Assert.True(session.ConsolidationCount > 0);

        // Assert: the material consolidated was the whole verbatim history and it fitted tier zero
        // comfortably - which is the disagreement this scenario exists for. An estimated split
        // looking at this transcript finds nothing to age out, and a rotation abandoned on that
        // basis would have left the provider's own count exactly where it was.
        var request = Assert.Single(summarizer.Requests);
        Assert.True(
            TokenEstimator.EstimateTokens(request.Material) < options.Compaction.TierBudgetTokens[0],
            "The consolidated material overflowed tier zero, so the split would have found work to "
            + "do whatever currency the trigger was measured in.");
        Assert.Empty(session.Layout.Transcript.Entries);

        // Assert: and a replacement provider session seeded from the consolidated record, with the
        // session it superseded released
        Assert.Equal(2, factory.Sessions.Count);
        Assert.True(factory.Sessions[0].IsDisposed);
        Assert.False(factory.Sessions[1].IsDisposed);
        Assert.False(session.Layout.CoarseTiers[0].IsEmpty);
    }

    /// <summary>
    ///     Proves a blank message is refused: a blank turn spends context to say nothing, and is a
    ///     defect in the calling application rather than something to forward to a provider.
    /// </summary>
    /// <param name="message">The rejected message.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompactingAgentSession_SendAsync_BlankMessage_Throws(string? message)
    {
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => session.SendAsync(message!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves disposal releases the live provider session, which for some providers is
    ///     server-side state that keeps being billed for until it is released.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_DisposeAsync_ReleasesTheLiveProviderSession()
    {
        // Arrange: a session that has taken a turn
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Act: dispose it, twice, which must be permitted
        await session.DisposeAsync();
        await session.DisposeAsync();

        // Assert: the provider session was released, and the session refuses further turns
        Assert.True(factory.Sessions[0].IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SendAsync("again", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a rotated session is coherent even when the superseded provider session fails to
    ///     dispose. The rotation has already succeeded by that point — the context was consolidated
    ///     and the replacement created — so a disposal failure is not allowed to surface as a
    ///     rotation failure, and must not leave this session pointing at the replacement while its
    ///     layout and counters still describe the session it replaced.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ReplacedProviderFailsToDispose_StaysCoherent()
    {
        // Arrange: the same arithmetic as the rotation scenario - a converging window, the small
        // policy, and turns of roughly 220 tokens - against a provider that throws when disposed
        var factory = new ThrowingDisposeProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 110 * TokenEstimator.CharactersPerToken)));
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: SessionTestData.ConvergentWindowTokens, compaction: SessionTestData.SmallPolicy);
        var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 110 * TokenEstimator.CharactersPerToken);

        // Act: two turns, the second of which rotates and so disposes the first session
        await session.SendAsync(message, TestContext.Current.CancellationToken);
        var response = await session.SendAsync(message, TestContext.Current.CancellationToken);

        // Assert: the disposal was attempted and failed, and the rotation was still reported as the
        // success it was
        Assert.True(factory.Sessions[0].DisposeAttempted);
        Assert.True(response.RotationOccurred);
        Assert.Equal(1, session.RotationCount);
        Assert.True(session.ConsolidationCount > 0);

        // Assert: the session describes the replacement, not the session it replaced - the layout
        // was consolidated into tier one and the transcript was reduced to the retained suffix
        Assert.Equal(2, factory.Sessions.Count);
        Assert.False(session.Layout.CoarseTiers[0].IsEmpty);
        Assert.True(session.Layout.ConversationTokens <= session.Layout.MaximumBoundTokens);

        // Assert: and it still works, appending the next turn to the replacement's transcript
        var entriesBefore = session.Layout.Transcript.Entries.Count;
        await session.SendAsync("after the failed disposal", TestContext.Current.CancellationToken);
        Assert.True(session.Layout.Transcript.Entries.Count > entriesBefore);

        // Assert: explicit disposal is a different matter and does report the failure - a caller
        // that asked for the session to be released is entitled to learn that it was not
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
    }

    /// <summary>
    ///     Proves the answer of a tool-using turn survives a rotation into the seed of the
    ///     replacement session. A turn that called tools produces call and result entries that say
    ///     what was looked at but not what was concluded; if the conclusion is not recorded too, the
    ///     agent's own output is missing from the history every later turn is seeded from — and an
    ///     agent that uses tools on nearly every turn would lose nearly all of it.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ToolUsingTurnRotates_SeedsTheAnswerIntoTheReplacement()
    {
        // Arrange: a summarizer that keeps everything it is given, so what survives is decided by
        // the tier arrangement rather than by a model's discretion
        var summarizer = new FakeSummarizer(request =>
            string.IsNullOrEmpty(request.PreviousRecord)
                ? request.Material
                : request.PreviousRecord + "\n" + request.Material);

        // Arrange: a provider that calls a tool on every turn and states its conclusion only in the
        // answer, which is exactly how a real tool-using adapter reports a turn
        var factory = new InMemoryProviderSessionFactory(
            message => new ProviderTurn(
                $"Concluded: {message}",
                [TranscriptEntry.ToolCall("c1", "read the file"), TranscriptEntry.ToolResult("c1", "contents")]),
            windowTokens: SessionTestData.ConvergentWindowTokens);
        var options = new AgentSessionOptions(
            summarizer, providerWindowTokens: SessionTestData.ConvergentWindowTokens, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: one distinctive tool-using turn, then enough routine turns to force a rotation
        await session.SendAsync("where does the deployment key live", TestContext.Current.CancellationToken);
        for (var turn = 0; turn < 10; turn++)
        {
            await session.SendAsync(
                $"routine step {turn} " + new string('p', 30 * TokenEstimator.CharactersPerToken),
                TestContext.Current.CancellationToken);
        }

        // Assert: the session rotated, so the newest provider session was seeded rather than grown
        Assert.True(session.RotationCount >= 1, $"Expected a rotation, saw {session.RotationCount}.");
        Assert.True(factory.Sessions.Count >= 2);

        // Assert: the conclusion the agent reached on that first turn is in what the replacement was
        // seeded with, not just the tool traffic that led to it
        var seeded = string.Join("\n", factory.Sessions[^1].Seed.History.Select(entry => entry.Text));
        Assert.Contains("Concluded: where does the deployment key live", seeded, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a failed release can be retried. Disposal that marked itself done before the
    ///     provider had actually been released would turn a transient provider failure into a
    ///     permanent leak: every later call would return at the disposed check while the provider
    ///     still held server-side state.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_DisposeAsync_FirstReleaseFails_RetriesAndReleases()
    {
        // Arrange: a provider whose first disposal fails and whose second succeeds
        var factory = new ThrowingDisposeProviderSessionFactory(
            _ => new ProviderTurn("noted"),
            disposeFailures: 1);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Act: the first disposal propagates the failure, as a caller that asked for a release is
        // entitled to learn that it did not happen
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
        Assert.False(factory.Sessions[0].IsDisposed);

        // Act: retry
        await session.DisposeAsync();

        // Assert: the provider really was released on the retry, and a third call is a no-op
        Assert.True(factory.Sessions[0].IsDisposed);
        Assert.Equal(2, factory.Sessions[0].DisposeAttempts);
        await session.DisposeAsync();
        Assert.Equal(2, factory.Sessions[0].DisposeAttempts);

        // Assert: the session refused turns from the first disposal onwards, failed release or not
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.SendAsync("again", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a provider reporting a window this session could not converge in is refused at
    ///     creation, and that the session it was created for releases the provider on the way out.
    ///     Allowing it would derive a rotation threshold from the reported window that a rotated
    ///     context could not land below, so every turn would cross the threshold, rotate, and cross
    ///     it again — a thrash loop spending a summarizer call and a provider session per turn
    ///     without ever settling, and raising no saturation signal while doing it.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_ProviderWindowBelowTheBound_ReleasesAndThrows()
    {
        // Arrange: the small policy needs 446 tokens to converge - a rotated context of 311, being
        // 230 of tier budgets plus 81 of seeded record framing, landing below 70 percent of the
        // window - against a provider reporting 100
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);

        // Act
        var error = await Assert.ThrowsAsync<AgentSessionCreationException>(() =>
            CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken));

        // Assert: the failure names the reported window, what a rotated context occupies, and what
        // the policy actually requires, so a host can see which figure to change
        Assert.Contains("100", error.Message, StringComparison.Ordinal);
        Assert.Contains("230", error.Message, StringComparison.Ordinal);
        Assert.Contains("446", error.Message, StringComparison.Ordinal);

        // Assert: the session was abandoned mid-life, so the provider it had already created was
        // released rather than left holding a conversation the caller has no handle to - and
        // because the release succeeded the failure carries nothing for the caller to clean up
        Assert.True(Assert.Single(factory.Sessions).IsDisposed);
        Assert.Null(error.RetainedProviderSession);
    }

    /// <summary>
    ///     Proves a window that holds a rotated context but leaves it at or above the rotation
    ///     threshold is refused, which is the case the guard used to admit.
    /// </summary>
    /// <remarks>
    ///     <b>This is the reported-window twin of the configured-window case, and the band between
    ///     the two conditions is where the defect lived.</b> A provider reporting 400 tokens holds
    ///     the small policy's 311-token rotated context comfortably, so the old guard — which asked
    ///     only whether the window held the bound — passed it. The rotation threshold at 400 tokens
    ///     is 280, which a 311-token rotated context sits above, so the session rotated on nearly
    ///     every turn forever. Nine tests in this suite ran at exactly this window.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_ProviderWindowHoldsTheBoundButCannotConverge_ReleasesAndThrows()
    {
        // Arrange: a reported window larger than the 311-token rotated context but smaller than the
        // 446 tokens convergence requires
        var factory = new InMemoryProviderSessionFactory(windowTokens: 400);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);

        // Act / Assert: refused, even though a rotated context would have fitted
        var error = await Assert.ThrowsAsync<AgentSessionCreationException>(() =>
            CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken));

        Assert.Contains("446", error.Message, StringComparison.Ordinal);
        Assert.True(Assert.Single(factory.Sessions).IsDisposed);
        Assert.Null(error.RetainedProviderSession);
    }

    /// <summary>
    ///     Proves the same refusal applies to a provider that only begins reporting its window after
    ///     a turn. A check made only at creation would miss every provider that reveals nothing
    ///     until it has answered something, which is precisely the shape that motivated the optional
    ///     usage-reporting interface.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReportsAWindowBelowTheBound_ReleasesAndThrows()
    {
        // Arrange: a provider silent until it has answered, then reporting 100 tokens against the
        // small policy's 311-token bound
        var factory = new LateReportingProviderSessionFactory(reportedWindowTokens: 100);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);
        var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: the first turn is the first moment the window is knowable
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SendAsync("hello", TestContext.Current.CancellationToken));

        // Assert: the live provider was released and the session refuses further turns
        Assert.True(factory.Sessions[0].IsDisposed);
        Assert.Equal(1, factory.Sessions[0].DisposeAttempts);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.SendAsync("again", TestContext.Current.CancellationToken));

        // Assert: disposing the abandoned session again is permitted and releases nothing further
        await session.DisposeAsync();
        Assert.Equal(1, factory.Sessions[0].DisposeAttempts);
    }

    /// <summary>
    ///     Proves a rotation that consolidated nothing does not replace the provider session, does
    ///     not count as a rotation, and is not reported as one.
    /// </summary>
    /// <remarks>
    ///     <b>"A re-seed costs nothing" is true of the engine and false of this class.</b> When the
    ///     transcript already fits tier zero the engine returns the layout unchanged, which for a
    ///     pure function over a layout is genuinely free. Acting on it here is not: it creates a
    ///     replacement provider session, disposes the live one, increments the rotation count and
    ///     tells the caller a rotation happened — all to arrive at the context the session already
    ///     had. Measured before this was fixed, a policy of <c>[100, 1]</c> in a 128-token window
    ///     did that on 16 of 20 turns, creating 17 provider sessions and reporting no saturation at
    ///     any point.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_RotationConsolidatesNothing_IsNotReportedAsARotation()
    {
        // Arrange: a layout whose coarse tiers are already full enough that the conversation crosses
        // the threshold while the verbatim transcript still fits tier zero, so a rotation would find
        // nothing to age out
        var summarizer = FakeSummarizer.Filling();
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn("ok"),
            windowTokens: SessionTestData.ConvergentWindowTokens,
            reportsUsage: false);
        var options = new AgentSessionOptions(
            summarizer,
            providerWindowTokens: SessionTestData.ConvergentWindowTokens,
            compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: run a long conversation of small turns
        var reportedRotations = 0;
        for (var turn = 0; turn < 30; turn++)
        {
            var response = await session.SendAsync(
                $"{turn}-" + new string('m', 20 * TokenEstimator.CharactersPerToken),
                TestContext.Current.CancellationToken);
            if (response.RotationOccurred)
            {
                reportedRotations++;
            }
        }

        // Assert: every rotation reported is a rotation that actually happened, every rotation that
        // happened consolidated something, and each one created exactly one provider session
        Assert.Equal(session.RotationCount, reportedRotations);
        Assert.Equal(session.RotationCount + 1, factory.Sessions.Count);
        Assert.True(
            session.ConsolidationCount >= session.RotationCount,
            $"{session.RotationCount} rotations performed only {session.ConsolidationCount} "
            + "consolidations, so at least one replaced a provider session for nothing.");
    }

    /// <summary>
    ///     Proves a rotation that consolidated nothing is a no-op at the engine boundary too: the
    ///     layout is returned unchanged and the consolidation count is zero, which is the signal
    ///     this class acts on.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_TranscriptFitsTierZero_KeepsTheLiveProviderSession()
    {
        // Arrange: a transcript that fits tier zero, rotated directly through the engine
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(3, 20));

        // Act: rotate on a crossing this library measured itself, which is the only trigger whose
        // currency agrees with the split below
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: nothing was consolidated and nothing changed, so there is no new context to seed a
        // replacement provider session from
        Assert.Equal(0, outcome.ConsolidationCount);
        Assert.Same(layout, outcome.Layout);
        Assert.Empty(summarizer.Requests);
    }

    /// <summary>
    ///     Proves a release that fails while a creation is being refused is reported honestly, and
    ///     that the provider session it could not release is handed to the caller rather than
    ///     discarded with the instance.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The message was untrue in exactly the case it reports.</b> The catch around the
    ///     release deliberately leaves the release flag false so a later call can retry, and the
    ///     text nonetheless said the session "has been released". Messages in this package state
    ///     facts, so this one states the release's actual outcome.
    ///     </para>
    ///     <para>
    ///     <b>And the retryable state was unreachable.</b> Saying the release needed retrying left
    ///     the caller nothing to retry it with: <c>CreateAsync</c> returns no handle on this path,
    ///     so the only <c>CompactingAgentSession</c> holding the provider session was discarded
    ///     while the provider still held a conversation. The failure therefore carries the provider
    ///     session itself, so disposing it is a retry a caller can actually perform.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_ReleaseFailsWhileRefusingTheWindow_DoesNotClaimRelease()
    {
        // Arrange: a provider reporting a window the small policy cannot converge in, whose release
        // then fails - the one case where the claim and the outcome came apart
        var factory = new ScriptedWindowProviderSessionFactory([100], disposeThrows: true);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);

        // Act
        var error = await Assert.ThrowsAsync<AgentSessionCreationException>(() =>
            CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken));

        // Assert: the configuration defect is still the failure reported, rather than the adapter's
        // disposal failure that would hide it
        Assert.Contains("446", error.Message, StringComparison.Ordinal);

        // Assert: the release really did fail, so nothing was released
        var session = Assert.Single(factory.Sessions);
        Assert.Equal(1, session.DisposeAttempts);
        Assert.False(session.IsDisposed);

        // Assert: and the message says the release was attempted rather than claiming it happened
        Assert.DoesNotContain("has been released", error.Message, StringComparison.Ordinal);
        Assert.Contains("attempted", error.Message, StringComparison.Ordinal);

        // Assert: the handle is not discarded - the caller receives the very session the provider
        // still holds, and disposing it is a retry that actually reaches the provider
        Assert.Same(session, error.RetainedProviderSession);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await error.RetainedProviderSession!.DisposeAsync());
        Assert.Equal(2, session.DisposeAttempts);
    }

    /// <summary>
    ///     Proves a replacement provider session reporting a window this session could not converge
    ///     in is refused during the rotation that adopted it, rather than after the next turn has
    ///     already been accepted against it.
    /// </summary>
    /// <remarks>
    ///     <b>A factory is free to return a session unlike the one it replaced.</b> Nothing requires
    ///     a provider to report the same window twice — a routed deployment, a changed model, or a
    ///     tier downgrade between rotations all produce a smaller one — and the rotation read the
    ///     replacement's usage without ever asking whether the session could converge in it. The
    ///     turn was then answered normally and the unusable replacement surfaced only on the
    ///     following turn, by which point the caller had been told the rotation succeeded and a
    ///     message had already been sent to a session that cannot settle.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ReplacementReportsAWindowBelowTheBound_ReleasesAndThrows()
    {
        // Arrange: a first session reporting a window that converges comfortably, and a replacement
        // reporting one hopelessly below the 446 tokens the small policy requires. The scripted
        // conversation figure crosses the first session's 2,800-token threshold in a single turn.
        var factory = new ScriptedWindowProviderSessionFactory(
            [4000, 100], conversationTokensPerTurn: 3000);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);
        var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: a message long enough to overflow tier zero, so the rotation really consolidates and
        // really does adopt a replacement
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SendAsync(new string('m', 600), TestContext.Current.CancellationToken));

        // Assert: the replacement was created, adopted, and then found unusable
        Assert.Equal(2, factory.Sessions.Count);
        Assert.Contains("100", error.Message, StringComparison.Ordinal);
        Assert.Contains("446", error.Message, StringComparison.Ordinal);

        // Assert: both provider sessions were released - the superseded one by the rotation, the
        // replacement by the refusal - so nothing is left held by a session the caller cannot reach
        Assert.True(factory.Sessions[0].IsDisposed);
        Assert.True(factory.Sessions[1].IsDisposed);

        // Assert: the session was abandoned, so no further turn is accepted against it
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.SendAsync("again", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a provider reporting totals alone, over a real overhead it never breaks out, is
    ///     refused when the window it reports cannot in fact converge — the case the zero-overhead
    ///     default made invisible.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The default is safe for the trigger and unsafe for this check, which is how it was
    ///     missed.</b> <c>ContextUsage.FromProvider</c> with no conversation split treats the whole
    ///     of the usage as conversation, so <c>OverheadTokens</c> is zero. For the rotation trigger
    ///     that is the safe direction: an inflated conversation crosses a threshold taken from the
    ///     whole window early. For the convergence check it is the unsafe one, because it makes the
    ///     window look larger than it is while the figure a rotated context reports still carries
    ///     the fold.
    ///     </para>
    ///     <para>
    ///     The arithmetic, exactly: the small policy's rotated context bounds at 311 tokens, a
    ///     500-token window gives a threshold of 350, and 311 is below it — so the old guard
    ///     accepted the session. The provider charges 100 tokens it never broke out, so the context a
    ///     rotation actually produces is reported at 411 against that same 350, and the session
    ///     rotates on every turn from then on, raising no saturation signal because each individual
    ///     consolidation reduces perfectly well. Neither shipped adapter reaches it — Copilot reports
    ///     the split and the <c>IChatClient</c> path is estimated — but the factory method is public
    ///     and documented to accept totals alone.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_ProviderReportsTotalsOverUnreportedOverhead_ReleasesAndThrows()
    {
        // Arrange: a provider reporting totals only, over 100 tokens of overhead it never breaks
        // out, in a window that looks convergent only while that overhead is credited as nothing
        var factory = new UnsplitReportingProviderSessionFactory(
            windowTokens: 500, unreportedOverheadTokens: 100, conversationTokensPerTurn: 0);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);

        // Act: refused before a single turn is spent against it
        var error = await Assert.ThrowsAsync<AgentSessionCreationException>(() =>
            CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken));

        // Assert: the overhead the provider folded in was measured against the empty conversation
        // the session starts from, and is named in the refusal
        Assert.Contains("100", error.Message, StringComparison.Ordinal);

        // Assert: and the requirement quoted is the one that credits it - 589 tokens, not the 446
        // the same policy needs when the provider breaks its overhead out
        Assert.Contains("589", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("446", error.Message, StringComparison.Ordinal);

        // Assert: the provider session was released rather than left held by a session the caller
        // never receives
        Assert.True(Assert.Single(factory.Sessions).IsDisposed);
    }

    /// <summary>
    ///     Proves the same provider shape is accepted, and settles, in a window large enough to
    ///     converge once its unreported overhead is credited — so the guard refuses the
    ///     configurations that thrash rather than the reporting shape itself.
    /// </summary>
    /// <remarks>
    ///     An adapter that genuinely cannot split its counts is supported rather than turned away:
    ///     it keeps reporting totals alone, the session measures the fold for it against the empty
    ///     conversation, and everything downstream stays in the provider's own tokens. Without this
    ///     scenario the refusal above would be satisfied by a guard that simply rejected every
    ///     unsplit provider.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReportsTotalsInAConvergentWindow_RotatesAndSettles()
    {
        // Arrange: the same unreported 100 tokens of overhead, in a 900-token window - above the 589
        // that crediting it requires - with each turn adding 200 reported conversation tokens
        var factory = new UnsplitReportingProviderSessionFactory(
            windowTokens: 900, unreportedOverheadTokens: 100, conversationTokensPerTurn: 200);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Assert: the session was accepted, and the provider's own figures are what it reads
        Assert.Equal(ContextUsageOrigin.Provider, session.Usage.Origin);

        // Act: four turns, which crosses the 630-token threshold and rotates
        for (var turn = 0; turn < 4; turn++)
        {
            await session.SendAsync($"message {turn}", TestContext.Current.CancellationToken);
        }

        // Assert: it rotated rather than running on, and did not rotate on every turn
        Assert.True(session.RotationCount >= 1);
        Assert.True(session.RotationCount < 4);
    }

    /// <summary>
    ///     Proves a missing configuration or provider factory is refused where the host wrote it,
    ///     rather than at the first conversation.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_NullArguments_Throw()
    {
        var options = new AgentSessionOptions(new FakeSummarizer());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CompactingAgentSession.CreateAsync(null!, new InMemoryProviderSessionFactory(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CompactingAgentSession.CreateAsync(options, null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a provider session that returns no turn at all is refused by name rather than
    ///     dereferenced.
    /// </summary>
    /// <remarks>
    ///     <b>The annotation is a promise, not an enforcement.</b> An adapter compiled without
    ///     nullable analysis, or one whose own transport returned nothing, can still hand back a
    ///     null turn. Dereferenced, it produced an opaque <c>NullReferenceException</c> from the
    ///     middle of a turn the provider may already have accepted, while the engine's transcript
    ///     still said nothing had happened. The factory, the summarizer and the in-memory responder
    ///     all convert a null result into an explicit refusal; this path now does the same.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReturnsNullTurn_Throws()
    {
        // Arrange: an adapter that answers with nothing at all
        var factory = new ThrowingDisposeProviderSessionFactory(_ => null!, disposeFailures: 0);
        var options = new AgentSessionOptions(new FakeSummarizer());
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SendAsync("anything", TestContext.Current.CancellationToken));

        // Assert: named rather than opaque
        Assert.Contains("null", error.Message, StringComparison.Ordinal);

        // Assert: and nothing was recorded for a turn no provider produced
        Assert.Empty(session.Layout.Transcript.Entries);
    }

    /// <summary>
    ///     Proves a totals-only session replaced by one that reports its conversation split hands
    ///     the fold over with the provider, so a convergent replacement is accepted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The fold belongs to the provider session it was measured from.</b> Measured once
    ///     against the first provider session and reused, it went on being credited to replacements
    ///     it was never measured from. Here the first session folds 100 tokens into its conversation
    ///     figure, so the session credits it 100 and requires 589 tokens of effective window. The
    ///     replacement reports its overhead separately: it folds nothing, so it requires only the
    ///     446 tokens the policy needs, and its 500-token effective window satisfies that.
    ///     </para>
    ///     <para>
    ///     Against the stale fold this rotation threw, abandoning a session whose replacement was
    ///     perfectly usable, over an overhead that replacement does not charge.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_TotalsOnlyReplacedBySplitReporting_TakesTheReplacementsFold()
    {
        // Arrange: a totals-only first session folding 100 tokens, in a 900-token window that
        // converges once those 100 are credited, then a split-reporting replacement whose 600-token
        // window leaves 500 once its own reported overhead is paid for
        var factory = new ShapeScriptedProviderSessionFactory(
        [
            new ProviderReportingShape(900, OverheadTokens: 100, ReportsSplit: false, ConversationTokensPerTurn: 600),
            new ProviderReportingShape(600, OverheadTokens: 100, ReportsSplit: true, ConversationTokensPerTurn: 0)
        ]);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: one turn large enough to overflow tier zero, crossing the 630-token threshold
        var response = await session.SendAsync(
            new string('m', 600), TestContext.Current.CancellationToken);

        // Assert: the rotation carried through rather than being abandoned over a fold the
        // replacement does not charge
        Assert.True(response.RotationOccurred);
        Assert.Equal(2, factory.Sessions.Count);
        Assert.Equal(1, session.RotationCount);

        // Assert: and the session is live against the replacement, not abandoned
        Assert.Equal(ContextUsageOrigin.Provider, session.Usage.Origin);
    }

    /// <summary>
    ///     Proves a split-reporting session replaced by a totals-only one measures the replacement's
    ///     own fold, so a replacement that could never settle is refused during the rotation that
    ///     adopted it.
    /// </summary>
    /// <remarks>
    ///     <b>This is the same defect in the other direction.</b> The first session reports its
    ///     split, so the fold measured at creation is zero — and reused, it credited zero to a
    ///     replacement folding 400 tokens into a 500-token window. The rotated context the
    ///     replacement holds is then reported at 400 above the small policy's own 311, against a
    ///     rotation threshold of 350, so the session rotates on every turn thereafter and raises no
    ///     saturation signal while doing it, because each consolidation reduces perfectly normally.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_SplitReportingReplacedByTotalsOnly_RefusesTheReplacement()
    {
        // Arrange: a split-reporting first session in a comfortable window, then a totals-only
        // replacement folding 400 tokens into a 500-token window
        var factory = new ShapeScriptedProviderSessionFactory(
        [
            new ProviderReportingShape(4000, OverheadTokens: 0, ReportsSplit: true, ConversationTokensPerTurn: 3000),
            new ProviderReportingShape(500, OverheadTokens: 400, ReportsSplit: false, ConversationTokensPerTurn: 0)
        ]);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);
        var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: one turn large enough to overflow tier zero, crossing the 2,800-token threshold
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SendAsync(new string('m', 600), TestContext.Current.CancellationToken));

        // Assert: the replacement's own fold was measured and named, rather than the zero the
        // session it replaced was measured at
        Assert.Equal(2, factory.Sessions.Count);
        Assert.Contains("400", error.Message, StringComparison.Ordinal);

        // Assert: both provider sessions were released, and the session refuses further turns
        Assert.True(factory.Sessions[0].IsDisposed);
        Assert.True(factory.Sessions[1].IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.SendAsync("again", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a provider session whose usage cannot be read is released rather than left holding
    ///     a conversation nothing can name, and that the adapter's own failure is what the caller
    ///     receives.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The window this closes is between creation and adoption.</b> The factory hands back a
    ///     provider session and this session reads its usage before anything owns it. That read is
    ///     adapter code, and it is designed to be able to throw: <c>ContextUsage</c> refuses a
    ///     conversation larger than the total occupied rather than clamping it, precisely so an
    ///     adapter's arithmetic defect surfaces where the adapter wrote it. The instance was then
    ///     discarded, the exception carried no handle, and the provider-side session was left with
    ///     no reference to it anywhere in the process — for a provider holding history server-side,
    ///     a remote session that is never discarded.
    ///     </para>
    ///     <para>
    ///     Because the release succeeded there is nothing for a caller to clean up, so the adapter's
    ///     failure travels unchanged rather than wrapped: a wrapper would only move the failure away
    ///     from the code that produced it, which is the one thing the refusal exists to avoid.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_ProviderUsageThrows_ReleasesTheProviderSession()
    {
        // Arrange: an adapter reporting a conversation larger than its own total, which ContextUsage
        // refuses
        var factory = new DefectiveUsageProviderSessionFactory(windowTokens: 4000);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);

        // Act: the adapter's own refusal is what the caller receives, unwrapped
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken));

        // Assert: the provider session created a moment earlier was released rather than leaked
        var provider = Assert.Single(factory.Sessions);
        Assert.Equal(1, provider.DisposeAttempts);
        Assert.True(provider.IsDisposed);
    }

    /// <summary>
    ///     Proves that when a provider session cannot be adopted <em>and</em> cannot then be
    ///     released, the failure carries the provider session itself, because this is the one state
    ///     in which something is still held and no other handle to it exists.
    /// </summary>
    /// <remarks>
    ///     The creation path is the only one where the caller receives no session, so it is the only
    ///     one where a retryable release has nothing to retry it with. Wrapping happens here and
    ///     only here: the adapter's failure is kept as the inner exception, so nothing about why the
    ///     creation failed is lost, and the retained session makes the retry a caller can actually
    ///     perform reachable rather than merely described.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_ProviderUsageThrowsAndReleaseFails_CarriesTheProviderSession()
    {
        // Arrange: the same defective adapter, whose provider-side release also fails
        var factory = new DefectiveUsageProviderSessionFactory(windowTokens: 4000, disposeThrows: true);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);

        // Act
        var error = await Assert.ThrowsAsync<AgentSessionCreationException>(() =>
            CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken));

        // Assert: why the creation failed is preserved rather than replaced
        Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);

        // Assert: the message says the provider still holds the session and names what to dispose
        Assert.Contains("still holds it", error.Message, StringComparison.Ordinal);
        Assert.Contains(
            nameof(AgentSessionCreationException.RetainedProviderSession),
            error.Message,
            StringComparison.Ordinal);

        // Assert: the handle is the very session the provider holds, and disposing it is a retry
        // that actually reaches the provider
        var provider = Assert.Single(factory.Sessions);
        Assert.Same(provider, error.RetainedProviderSession);
        Assert.Equal(1, provider.DisposeAttempts);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await error.RetainedProviderSession!.DisposeAsync());
        Assert.Equal(2, provider.DisposeAttempts);
    }

    /// <summary>
    ///     Proves a replacement whose usage cannot be read is released by the rotation that created
    ///     it, leaving the session coherent against the provider it already had.
    /// </summary>
    /// <remarks>
    ///     The rotation's sibling window to the one at creation, and the one that cannot be reported
    ///     by carrying a handle: the caller holds a working session, and the session it holds is not
    ///     the one that failed. So the replacement is released and the failure travels on unchanged.
    ///     The rotation is not carried out, which is what leaves the conversation on the provider
    ///     that still has it.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ReplacementUsageThrows_ReleasesTheReplacement()
    {
        // Arrange: a sound first session whose scripted conversation crosses the 2,800-token
        // threshold in one turn, and a replacement whose usage cannot be read at all
        var factory = new DefectiveUsageProviderSessionFactory(
            windowTokens: 4000, conversationTokensPerTurn: 3000, defectiveFromIndex: 1);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 4000, compaction: SessionTestData.SmallPolicy);
        var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: a message long enough to overflow tier zero, so the rotation really does create a
        // replacement
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.SendAsync(new string('m', 600), TestContext.Current.CancellationToken));

        // Assert: the replacement was created and then released rather than orphaned
        Assert.Equal(2, factory.Sessions.Count);
        Assert.True(factory.Sessions[1].IsDisposed);

        // Assert: the rotation was not carried out, so the conversation stays on the provider that
        // holds it and that session is still the live one
        Assert.Equal(0, session.RotationCount);
        Assert.False(factory.Sessions[0].IsDisposed);

        // Assert: and disposing the session still releases exactly that provider session
        await session.DisposeAsync();
        Assert.True(factory.Sessions[0].IsDisposed);
        Assert.Equal(1, factory.Sessions[1].DisposeAttempts);
    }

    /// <summary>
    ///     Proves the natural way to construct the creation failure without a retained session
    ///     compiles, which is the whole of the defect this test exists for.
    /// </summary>
    /// <remarks>
    ///     <b>A compile-time assertion, deliberately.</b> The type declared both
    ///     <c>(string, Exception)</c> and <c>(string, IAsyncDisposable?)</c>, and neither parameter
    ///     type converts to the other, so <c>new AgentSessionCreationException(message, null)</c> —
    ///     which is how an application says "nothing is retained" — was ambiguous and did not
    ///     compile. This is new public API in a package whose adapters have not shipped, so the
    ///     ambiguity was cheap to remove now and a breaking change later. The retained session is
    ///     carried by an initializer instead, which composes with every constructor rather than
    ///     competing with one; the lines below would not build if that were undone.
    /// </remarks>
    [Fact]
    public void AgentSessionCreationException_Construct_WithoutARetainedSession_IsUnambiguous()
    {
        // Act: the natural call, with no inner exception and nothing retained
        var plain = new AgentSessionCreationException("Creation failed.", null);

        // Assert
        Assert.Null(plain.InnerException);
        Assert.Null(plain.RetainedProviderSession);

        // Act: and the two axes compose rather than exclude one another
        var retained = new InMemoryProviderSession(
            new ProviderSessionSeed(null, [], []),
            message => new ProviderTurn($"Acknowledged: {message}"),
            windowTokens: 4000);
        var carrying = new AgentSessionCreationException("Creation failed.", new InvalidOperationException())
        {
            RetainedProviderSession = retained,
        };

        // Assert
        Assert.IsType<InvalidOperationException>(carrying.InnerException);
        Assert.Same(retained, carrying.RetainedProviderSession);
    }
}

/// <summary>
///     What one provider session in a scripted conversation reports about itself.
/// </summary>
/// <remarks>
///     The two reporting shapes <see cref="ContextUsage.FromProvider(int, int, int?)"/> accepts,
///     expressed as data so one conversation can change shape between rotations. No other fake here
///     can do that: each produces sessions of a single shape, which is exactly why a fold measured
///     against the first session of a conversation could go on being credited to every replacement
///     without any test noticing.
/// </remarks>
/// <param name="WindowTokens">The window the session reports.</param>
/// <param name="OverheadTokens">
///     The fixed overhead the session charges on every reading. Reported outside the conversation
///     when <paramref name="ReportsSplit"/> is <see langword="true"/>, and folded silently into the
///     conversation figure otherwise.
/// </param>
/// <param name="ReportsSplit">Whether the session reports its conversation separately from its total.</param>
/// <param name="ConversationTokensPerTurn">The conversation tokens each answered turn adds.</param>
internal sealed record ProviderReportingShape(
    int WindowTokens,
    int OverheadTokens,
    bool ReportsSplit,
    int ConversationTokensPerTurn);

/// <summary>
///     A provider session reporting whichever shape the script gave it, and counting the history it
///     was seeded with as a real adapter would.
/// </summary>
/// <remarks>
///     Counting the seed matters: a session created by a rotation holds a rotated context, so a fake
///     that ignored it would make every replacement look as though it held nothing and would hide
///     the very measurement under test.
/// </remarks>
/// <param name="seed">What the session was started from.</param>
/// <param name="shape">What it reports about itself.</param>
internal sealed class ShapeScriptedProviderSession(ProviderSessionSeed seed, ProviderReportingShape shape)
    : IProviderSession, IContextUsageReporter
{
    /// <summary>
    ///     This library's estimate of the history the session was seeded with, which a provider
    ///     counting the same entries would arrive at.
    /// </summary>
    private readonly int _seededTokens = seed.History.Sum(entry => entry.EstimatedTokens);

    /// <summary>
    ///     The conversation tokens the answered turns have added.
    /// </summary>
    private int _turnTokens;

    /// <summary>
    ///     Gets a value indicating whether this session was released.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public ContextUsage? CurrentUsage
    {
        get
        {
            var conversation = _seededTokens + _turnTokens;

            // Split: the overhead is reported outside the conversation, so nothing is folded.
            // Totals only: the overhead is inside the single occupied figure and nowhere else, so
            // the engine sees an OverheadTokens of zero and a conversation silently carrying it.
            return shape.ReportsSplit
                ? ContextUsage.FromProvider(
                    shape.OverheadTokens + conversation, shape.WindowTokens, conversation)
                : ContextUsage.FromProvider(shape.OverheadTokens + conversation, shape.WindowTokens);
        }
    }

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _turnTokens += shape.ConversationTokensPerTurn;
        return Task.FromResult(new ProviderTurn($"Acknowledged: {message}"));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return default;
    }
}

/// <summary>
///     Creates <see cref="ShapeScriptedProviderSession"/> instances, giving each the next shape in a
///     script, and remembers every one it made.
/// </summary>
/// <remarks>
///     Once the script is exhausted every further session takes the last shape in it, so a scenario
///     states only the sessions it cares about.
/// </remarks>
/// <param name="shapes">What each successive session reports. Must not be empty.</param>
internal sealed class ShapeScriptedProviderSessionFactory(IReadOnlyList<ProviderReportingShape> shapes)
    : IProviderSessionFactory
{
    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public List<ShapeScriptedProviderSession> Sessions { get; } = [];

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var shape = shapes[Math.Min(Sessions.Count, shapes.Count - 1)];
        var session = new ShapeScriptedProviderSession(seed, shape);
        Sessions.Add(session);
        return Task.FromResult<IProviderSession>(session);
    }
}

/// <summary>
///     A provider session that answers normally but fails when it is disposed.
/// </summary>
/// <remarks>
///     Models the adapter this engine cannot control: one whose provider-side release fails, on a
///     network error or against a session the service has already reclaimed. The shipped in-memory
///     session cannot express that, because its disposal cannot fail.
/// </remarks>
/// <param name="seed">What the session was started from.</param>
/// <param name="responder">Produces the turn for a given message.</param>
/// <param name="disposeFailures">
///     How many disposal attempts fail before one succeeds. The default never succeeds, which is
///     the shape a rotation scenario needs; a finite count models the transient failure a retry
///     recovers from.
/// </param>
internal sealed class ThrowingDisposeProviderSession(
    ProviderSessionSeed seed,
    Func<string, ProviderTurn> responder,
    int disposeFailures = int.MaxValue) : IProviderSession
{
    /// <summary>
    ///     The disposal attempts still to fail before one succeeds.
    /// </summary>
    private int _remainingFailures = disposeFailures;

    /// <summary>
    ///     Gets what the session was started from.
    /// </summary>
    public ProviderSessionSeed Seed { get; } = seed;

    /// <summary>
    ///     Gets how many times disposal was attempted on this session.
    /// </summary>
    /// <remarks>
    ///     Counted rather than flagged: a retry is only a retry if the release was actually
    ///     attempted again, and a release already completed must not be attempted a third time.
    /// </remarks>
    public int DisposeAttempts { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether disposal was attempted on this session.
    /// </summary>
    /// <remarks>
    ///     Recorded rather than inferred: a rotation that swallows the disposal failure must still
    ///     be shown to have tried to release the session it replaced.
    /// </remarks>
    public bool DisposeAttempted => DisposeAttempts > 0;

    /// <summary>
    ///     Gets a value indicating whether this session was actually released.
    /// </summary>
    /// <remarks>
    ///     The distinction that matters to a retry: an attempt that threw left the session held,
    ///     and only a completed disposal releases it.
    /// </remarks>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(responder(message));
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Records the attempt and then fails while any failures remain, which is the whole point of
    ///     this fake.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        DisposeAttempts++;

        if (_remainingFailures > 0)
        {
            _remainingFailures--;
            throw new InvalidOperationException("The provider session could not be released.");
        }

        IsDisposed = true;
        return default;
    }
}

/// <summary>
///     Creates <see cref="ThrowingDisposeProviderSession"/> instances and remembers every one it
///     made.
/// </summary>
/// <remarks>
///     Remembering them is what lets a test assert both halves of a rotation whose disposal failed:
///     that a replacement was created, and that the session it replaced was asked to release itself.
/// </remarks>
/// <param name="responder">Produces the turn for a given message.</param>
/// <param name="disposeFailures">How many disposal attempts on each session fail before one succeeds.</param>
internal sealed class ThrowingDisposeProviderSessionFactory(
    Func<string, ProviderTurn> responder,
    int disposeFailures = int.MaxValue)
    : IProviderSessionFactory
{
    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public List<ThrowingDisposeProviderSession> Sessions { get; } = [];

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new ThrowingDisposeProviderSession(seed, responder, disposeFailures);
        Sessions.Add(session);
        return Task.FromResult<IProviderSession>(session);
    }
}

/// <summary>
///     A provider session that reports nothing until it has answered a turn, and then reports a
///     fixed window.
/// </summary>
/// <remarks>
///     Models the adapter that learns its own limits only from a response. The shipped in-memory
///     session cannot express that: it either reports from the outset or never reports at all, so
///     the moment a window first becomes knowable is unreachable through it.
/// </remarks>
/// <param name="reportedWindowTokens">The window reported once a turn has been answered.</param>
internal sealed class LateReportingProviderSession(int reportedWindowTokens)
    : IProviderSession, IContextUsageReporter
{
    /// <summary>
    ///     The tokens reported as occupied, grown by each turn.
    /// </summary>
    private int _usedTokens;

    /// <summary>
    ///     Whether a turn has been answered, and so whether a window can be reported.
    /// </summary>
    private bool _answered;

    /// <summary>
    ///     Gets how many times disposal was attempted on this session.
    /// </summary>
    /// <remarks>
    ///     Counted rather than flagged, so a test can show that abandoning the session released the
    ///     provider exactly once and that a later explicit disposal does not release it again.
    /// </remarks>
    public int DisposeAttempts { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this session was released.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public ContextUsage? CurrentUsage =>
        _answered ? ContextUsage.FromProvider(_usedTokens, reportedWindowTokens) : null;

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _answered = true;
        _usedTokens += TokenEstimator.EstimateTokens(message);
        return Task.FromResult(new ProviderTurn($"Acknowledged: {message}"));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        DisposeAttempts++;
        IsDisposed = true;
        return default;
    }
}

/// <summary>
///     Creates <see cref="LateReportingProviderSession"/> instances and remembers every one it made.
/// </summary>
/// <param name="reportedWindowTokens">The window each created session reports once it has answered.</param>
internal sealed class LateReportingProviderSessionFactory(int reportedWindowTokens) : IProviderSessionFactory
{
    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public List<LateReportingProviderSession> Sessions { get; } = [];

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new LateReportingProviderSession(reportedWindowTokens);
        Sessions.Add(session);
        return Task.FromResult<IProviderSession>(session);
    }
}

/// <summary>
///     A provider session that reports its own conversation count alongside its totals, as a
///     provider distinguishing the conversation from its framing does.
/// </summary>
/// <remarks>
///     Models the adapter this package's reporting path is built for. The shipped in-memory session
///     cannot stand in for it: that session measures its own overhead with the same
///     <see cref="TokenEstimator"/> the engine would have used, so the two currencies coincide and a
///     test written against it cannot tell a measurement from an estimate. The figures here are
///     scripted, owe nothing to any estimator, and are identical whatever the host configured.
/// </remarks>
/// <param name="windowTokens">The window the session reports.</param>
/// <param name="overheadTokens">The tokens the session reports outside the conversation.</param>
/// <param name="conversationTokensPerTurn">The conversation tokens each answered turn adds.</param>
internal sealed class SplitReportingProviderSession(
    int windowTokens,
    int overheadTokens,
    int conversationTokensPerTurn) : IProviderSession, IContextUsageReporter
{
    /// <summary>
    ///     The conversation tokens reported so far, grown by each answered turn.
    /// </summary>
    private int _conversationTokens;

    /// <summary>
    ///     Gets a value indicating whether this session was released.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public ContextUsage? CurrentUsage => ContextUsage.FromProvider(
        overheadTokens + _conversationTokens, windowTokens, _conversationTokens);

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _conversationTokens += conversationTokensPerTurn;
        return Task.FromResult(new ProviderTurn($"Acknowledged: {message}"));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return default;
    }
}

/// <summary>
///     Creates <see cref="SplitReportingProviderSession"/> instances and remembers every one it
///     made.
/// </summary>
/// <param name="windowTokens">The window each created session reports.</param>
/// <param name="overheadTokens">The tokens each created session reports outside the conversation.</param>
/// <param name="conversationTokensPerTurn">The conversation tokens each answered turn adds.</param>
internal sealed class SplitReportingProviderSessionFactory(
    int windowTokens,
    int overheadTokens,
    int conversationTokensPerTurn) : IProviderSessionFactory
{
    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public List<SplitReportingProviderSession> Sessions { get; } = [];

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new SplitReportingProviderSession(
            windowTokens, overheadTokens, conversationTokensPerTurn);
        Sessions.Add(session);
        return Task.FromResult<IProviderSession>(session);
    }
}

/// <summary>
///     A provider session that reports totals and a window but never a conversation split, over a
///     real fixed overhead it charges for and never breaks out.
/// </summary>
/// <remarks>
///     Models the third adapter shape: one whose provider hands it a single occupied-token figure
///     and a limit, with no account of what within them is conversation.
///     <see cref="ContextUsage.FromProvider"/> is public and documents that shape as supported, and
///     no other fake here produces it — <see cref="SplitReportingProviderSession"/> reports the
///     split, <see cref="ScriptedWindowProviderSession"/> reports a conversation equal to its total
///     and so carries no hidden overhead at all, and
///     <see cref="InMemoryProviderSession"/> reports the split it measures with the engine's own
///     estimator. The overhead here is genuinely charged and genuinely invisible in the split, which
///     is the whole condition under test.
/// </remarks>
/// <param name="windowTokens">The window the session reports.</param>
/// <param name="unreportedOverheadTokens">
///     The fixed overhead the session charges for on every reading, folded into its total and never
///     reported separately.
/// </param>
/// <param name="conversationTokensPerTurn">The conversation tokens each answered turn adds.</param>
internal sealed class UnsplitReportingProviderSession(
    int windowTokens,
    int unreportedOverheadTokens,
    int conversationTokensPerTurn) : IProviderSession, IContextUsageReporter
{
    /// <summary>
    ///     The conversation tokens charged so far, grown by each answered turn.
    /// </summary>
    private int _conversationTokens;

    /// <summary>
    ///     Gets a value indicating whether this session was released.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     Totals only: the overhead is inside <c>usedTokens</c> and nowhere else, so the engine
    ///     sees an <c>OverheadTokens</c> of zero and a conversation figure that silently carries it.
    /// </remarks>
    public ContextUsage? CurrentUsage =>
        ContextUsage.FromProvider(unreportedOverheadTokens + _conversationTokens, windowTokens);

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _conversationTokens += conversationTokensPerTurn;
        return Task.FromResult(new ProviderTurn($"Acknowledged: {message}"));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return default;
    }
}

/// <summary>
///     Creates <see cref="UnsplitReportingProviderSession"/> instances and remembers every one it
///     made.
/// </summary>
/// <remarks>
///     Every session it makes reports the same window and the same unreported overhead, because the
///     overhead is the instructions and tool declarations a rotation carries forward unchanged: a
///     replacement charges exactly what the session it replaced charged.
/// </remarks>
/// <param name="windowTokens">The window each created session reports.</param>
/// <param name="unreportedOverheadTokens">The overhead each created session folds into its total.</param>
/// <param name="conversationTokensPerTurn">The conversation tokens each answered turn adds.</param>
internal sealed class UnsplitReportingProviderSessionFactory(
    int windowTokens,
    int unreportedOverheadTokens,
    int conversationTokensPerTurn) : IProviderSessionFactory
{
    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public List<UnsplitReportingProviderSession> Sessions { get; } = [];

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new UnsplitReportingProviderSession(
            windowTokens, unreportedOverheadTokens, conversationTokensPerTurn);
        Sessions.Add(session);
        return Task.FromResult<IProviderSession>(session);
    }
}

/// <summary>
///     A provider session reporting a window supplied by the script that created it, whose release
///     may be made to fail.
/// </summary>
/// <remarks>
///     Models the two shapes no other fake here can produce together: a provider whose reported
///     window differs from one session of a conversation to the next, and one whose provider-side
///     release fails while the engine is abandoning the session over that very window. The
///     conversation figure is scripted rather than measured, so a scenario can cross a rotation
///     threshold in a single turn without building a transcript of thousands of tokens.
/// </remarks>
/// <param name="windowTokens">The window this session reports.</param>
/// <param name="conversationTokensPerTurn">The conversation tokens each answered turn adds.</param>
/// <param name="disposeThrows">Whether the provider-side release fails.</param>
internal sealed class ScriptedWindowProviderSession(
    int windowTokens,
    int conversationTokensPerTurn,
    bool disposeThrows) : IProviderSession, IContextUsageReporter
{
    /// <summary>
    ///     The conversation tokens reported so far, grown by each answered turn.
    /// </summary>
    private int _conversationTokens;

    /// <summary>
    ///     Gets the window this session reports.
    /// </summary>
    public int WindowTokens { get; } = windowTokens;

    /// <summary>
    ///     Gets how many times disposal was attempted on this session.
    /// </summary>
    /// <remarks>
    ///     Counted separately from whether it succeeded, because a release that threw was still
    ///     attempted - and the distinction between the two is exactly what the refusal message has
    ///     to be honest about.
    /// </remarks>
    public int DisposeAttempts { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this session was actually released.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public ContextUsage? CurrentUsage =>
        ContextUsage.FromProvider(_conversationTokens, WindowTokens, _conversationTokens);

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _conversationTokens += conversationTokensPerTurn;
        return Task.FromResult(new ProviderTurn($"Acknowledged: {message}"));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        DisposeAttempts++;

        if (disposeThrows)
        {
            throw new InvalidOperationException("The provider session could not be released.");
        }

        IsDisposed = true;
        return default;
    }
}

/// <summary>
///     Creates <see cref="ScriptedWindowProviderSession"/> instances, giving each the next window in
///     a script, and remembers every one it made.
/// </summary>
/// <remarks>
///     The script is what lets a scenario state that a rotation's replacement reports a different
///     window from the session it replaced. Once the script is exhausted every further session
///     reports the last window in it, so a scenario states only the windows it cares about.
/// </remarks>
/// <param name="windowTokens">The window each successive session reports. Must not be empty.</param>
/// <param name="conversationTokensPerTurn">The conversation tokens each answered turn adds.</param>
/// <param name="disposeThrows">Whether the provider-side release of each session fails.</param>
internal sealed class ScriptedWindowProviderSessionFactory(
    IReadOnlyList<int> windowTokens,
    int conversationTokensPerTurn = 0,
    bool disposeThrows = false) : IProviderSessionFactory
{
    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public List<ScriptedWindowProviderSession> Sessions { get; } = [];

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var window = windowTokens[Math.Min(Sessions.Count, windowTokens.Count - 1)];
        var session = new ScriptedWindowProviderSession(window, conversationTokensPerTurn, disposeThrows);
        Sessions.Add(session);
        return Task.FromResult<IProviderSession>(session);
    }
}

/// <summary>
///     A provider session whose usage reading is arithmetically impossible, and so throws where the
///     adapter wrote it.
/// </summary>
/// <remarks>
///     <para>
///     <b>Models the defect the usage shape is designed to surface rather than hide.</b>
///     <see cref="ContextUsage"/> refuses a conversation count larger than the total occupied
///     instead of clamping it, so an adapter that splits its own figures wrongly throws out of
///     <see cref="IContextUsageReporter.CurrentUsage"/>. No other fake here can do that: every one
///     of them reports a figure that is at worst unhelpful, and a reading that fails is the only way
///     to reach the moment a provider session exists and nothing owns it.
///     </para>
///     <para>
///     The defect is switchable so one conversation can hold a sound first session and a defective
///     replacement, which is the rotation half of the same window.
///     </para>
/// </remarks>
/// <param name="windowTokens">The window this session reports.</param>
/// <param name="conversationTokensPerTurn">The conversation tokens each answered turn adds.</param>
/// <param name="usageThrows">Whether reading this session's usage fails.</param>
/// <param name="disposeThrows">Whether the provider-side release fails.</param>
internal sealed class DefectiveUsageProviderSession(
    int windowTokens,
    int conversationTokensPerTurn,
    bool usageThrows,
    bool disposeThrows) : IProviderSession, IContextUsageReporter
{
    /// <summary>
    ///     The conversation tokens reported so far, grown by each answered turn.
    /// </summary>
    private int _conversationTokens;

    /// <summary>
    ///     Gets how many times disposal was attempted on this session.
    /// </summary>
    /// <remarks>
    ///     Counted rather than flagged, because a release that threw was still attempted and a
    ///     caller handed the session back must be shown to reach the provider when it retries.
    /// </remarks>
    public int DisposeAttempts { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this session was actually released.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     The defective reading reports one more conversation token than it reports as occupied in
    ///     total, which is the one split no accounting can produce and which
    ///     <see cref="ContextUsage"/> therefore refuses.
    /// </remarks>
    public ContextUsage? CurrentUsage => usageThrows
        ? ContextUsage.FromProvider(_conversationTokens, windowTokens, _conversationTokens + 1)
        : ContextUsage.FromProvider(_conversationTokens, windowTokens, _conversationTokens);

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _conversationTokens += conversationTokensPerTurn;
        return Task.FromResult(new ProviderTurn($"Acknowledged: {message}"));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        DisposeAttempts++;

        if (disposeThrows)
        {
            throw new InvalidOperationException("The provider session could not be released.");
        }

        IsDisposed = true;
        return default;
    }
}

/// <summary>
///     Creates <see cref="DefectiveUsageProviderSession"/> instances, turning the defect on from a
///     chosen point in the conversation, and remembers every one it made.
/// </summary>
/// <remarks>
///     Remembering them is the whole point: the property under test is that a provider session
///     created and then found unusable was released, and that can only be asserted against the
///     session itself.
/// </remarks>
/// <param name="windowTokens">The window each session reports.</param>
/// <param name="conversationTokensPerTurn">The conversation tokens each answered turn adds.</param>
/// <param name="defectiveFromIndex">
///     The first session in the conversation whose usage reading fails. Zero makes the very first
///     session defective, which is the creation case; one leaves creation sound and makes every
///     replacement defective, which is the rotation case.
/// </param>
/// <param name="disposeThrows">Whether the provider-side release of each session fails.</param>
internal sealed class DefectiveUsageProviderSessionFactory(
    int windowTokens,
    int conversationTokensPerTurn = 0,
    int defectiveFromIndex = 0,
    bool disposeThrows = false) : IProviderSessionFactory
{
    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public List<DefectiveUsageProviderSession> Sessions { get; } = [];

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new DefectiveUsageProviderSession(
            windowTokens,
            conversationTokensPerTurn,
            Sessions.Count >= defectiveFromIndex,
            disposeThrows);
        Sessions.Add(session);
        return Task.FromResult<IProviderSession>(session);
    }
}

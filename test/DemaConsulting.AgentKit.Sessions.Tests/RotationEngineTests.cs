namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="RotationEngine"/>: the deterministic aging of a context by one
///     rotation, exercised entirely against a fake summarizer so that every assertion is about the
///     engine rather than about a model.
/// </summary>
/// <remarks>
///     Every test here supplies a <see cref="FakeSummarizer"/>, which makes the engine a pure
///     function of the layout it was handed. That is what allows the cascade ordering, the ratchet,
///     the boundary snap and the saturation reports to be asserted exactly rather than
///     approximately.
/// </remarks>
public class RotationEngineTests
{
    /// <summary>
    ///     Proves a rotation whose recent history already fits is a pure re-seed when the crossing
    ///     was measured in this library's own tokens: nothing ages, no summarizer call is made, and
    ///     the layout is handed back as it was.
    /// </summary>
    /// <remarks>
    ///     The trigger's currency is what entitles the engine to this answer. The split is measured
    ///     in estimated tokens, so an estimated crossing and this split agree about the same
    ///     transcript; a provider-reported crossing does not, and is handled by
    ///     <see cref="RotationEngine_RotateAsync_ProviderReportedTrigger_ConsolidatesEvenWithoutEstimatedOverflow"/>.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_RotateAsync_NothingOverflows_ConsolidatesNothing()
    {
        // Arrange: sixty tokens of history against a hundred-token verbatim tier
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(3, 20));

        // Act: rotate on a crossing this library measured itself
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: the same layout, and the summarizer was never asked anything
        Assert.Same(layout, outcome.Layout);
        Assert.Equal(0, outcome.ConsolidationCount);
        Assert.Equal(0, summarizer.CallCount);
        Assert.False(outcome.IsSaturated);
    }

    /// <summary>
    ///     Proves a provider-reported crossing forces a real consolidation even where the estimated
    ///     split finds tier zero has room, so the estimate cannot veto a decision only the provider
    ///     was in a position to make.
    /// </summary>
    /// <remarks>
    ///     <b>The two roles are genuinely different.</b> A provider knows <em>whether</em> the
    ///     context is too large, because it counts its own tokens; the estimate is all there is for
    ///     deciding <em>what</em> to consolidate, because no provider can be asked to measure a
    ///     candidate split. Reading the estimated split as an answer to the first question is what
    ///     let a reported crossing consolidate nothing, seed no replacement, and leave the provider
    ///     running into its own compactor.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_RotateAsync_ProviderReportedTrigger_ConsolidatesEvenWithoutEstimatedOverflow()
    {
        // Arrange: sixty tokens of history against a hundred-token verbatim tier, which is exactly
        // the layout the estimated trigger above is entitled to leave alone
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(3, 20));

        // Act: rotate on a crossing the provider reported in its own tokens
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, ContextUsageOrigin.Provider, TestContext.Current.CancellationToken);

        // Assert: a real consolidation happened, so a caller has new context to seed a replacement
        // provider session from
        Assert.Equal(1, outcome.ConsolidationCount);
        Assert.NotSame(layout, outcome.Layout);
        Assert.False(outcome.Layout.CoarseTiers[0].IsEmpty);

        // Assert: the whole verbatim history went into it. Consolidating a smaller portion would
        // not be a reduction at all - a turn appends at least a message and an answer, so shaving
        // an entry or two per rotation never overtakes what the provider is counting.
        var request = Assert.Single(summarizer.Requests);
        foreach (var entry in layout.Transcript.Entries)
        {
            Assert.Contains(entry.Text, request.Material, StringComparison.Ordinal);
        }

        Assert.Empty(outcome.Layout.Transcript.Entries);
    }

    /// <summary>
    ///     Proves a provider-reported crossing with nothing recorded at all still consolidates
    ///     nothing, so the forced path terminates instead of manufacturing an empty consolidation.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_ProviderReportedTriggerWithEmptyTranscript_ConsolidatesNothing()
    {
        // Arrange: a layout holding no verbatim history whatsoever
        var summarizer = new FakeSummarizer();
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 0, 0);

        // Act
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, ContextUsageOrigin.Provider, TestContext.Current.CancellationToken);

        // Assert: no summarizer call, and the zero count that tells a caller not to replace a
        // provider session for nothing
        Assert.Same(layout, outcome.Layout);
        Assert.Equal(0, outcome.ConsolidationCount);
        Assert.Equal(0, summarizer.CallCount);
    }

    /// <summary>
    ///     Proves a forced consolidation keeps every tool call with its result, because the whole
    ///     history travels into one consolidation rather than being cut at an estimated boundary.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_ProviderReportedTriggerWithToolPairs_SeedsNoOrphanedResult()
    {
        // Arrange: three interleaved runs of parallel calls, 39 tokens in all, well within tier
        // zero's hundred-token budget so the estimated split would retain every one of them
        var summarizer = new FakeSummarizer();
        var transcript = SessionTranscript.Empty;
        for (var run = 0; run < 3; run++)
        {
            transcript = transcript
                .Append(SessionTestData.ToolCallOfTokens(5, $"a{run}"))
                .Append(SessionTestData.ToolCallOfTokens(5, $"b{run}"))
                .Append(SessionTestData.ToolResultOfTokens(5, $"a{run}"))
                .Append(SessionTestData.ToolResultOfTokens(5, $"b{run}"));
        }

        // Act: rotate on a provider-reported crossing
        var outcome = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.SmallPolicy, transcript),
            summarizer,
            ContextUsageOrigin.Provider,
            TestContext.Current.CancellationToken);

        // Assert: nothing verbatim survived, so no result can have been left without its call
        Assert.Empty(outcome.Layout.Transcript.Entries);
        Assert.DoesNotContain(
            outcome.Layout.BuildSeed(),
            entry => entry.Kind == TranscriptEntryKind.ToolResult);
    }

    /// <summary>
    ///     Proves an undefined trigger origin is refused. The origin states which currency crossed
    ///     the threshold, and that is exactly what decides whether an estimated split may abandon
    ///     the rotation, so a cast integer cannot be guessed at here.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_UndefinedTriggerOrigin_Throws()
    {
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(3, 20));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RotationEngine.RotateAsync(
            layout,
            new FakeSummarizer(),
            (ContextUsageOrigin)42,
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves overflowing history is folded into tier one and the newest turns stay verbatim —
    ///     and that only the tier that actually overflowed was consolidated, which is where the
    ///     tiered scheme's cost advantage over a flat rolling summary comes from.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_Overflow_FoldsIntoTierOneOnly()
    {
        // Arrange: two hundred tokens of history against a hundred-token verbatim tier
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: one consolidation, into tier one, as a first recording
        Assert.Equal(1, outcome.ConsolidationCount);
        Assert.Equal([(1, true)], summarizer.Shape);

        // Assert: tier one now holds a record, the coarser tiers were left alone
        Assert.False(outcome.Layout.CoarseTiers[0].IsEmpty);
        Assert.True(outcome.Layout.CoarseTiers[1].IsEmpty);
        Assert.True(outcome.Layout.CoarseTiers[2].IsEmpty);

        // Assert: the newest turns survived verbatim, within tier zero's budget
        Assert.Equal(5, outcome.Layout.Transcript.Entries.Count);
        Assert.True(outcome.Layout.Transcript.EstimatedTokens <= 100);
    }

    /// <summary>
    ///     Proves the arrangement is bounded by construction: after a rotation the whole context sits
    ///     within the fixed overhead plus the sum of the tier budgets.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_AfterRotation_ContextIsWithinItsConstructionBound()
    {
        // Arrange: far more history than the bound allows, and a summarizer that reduces sharply
        var summarizer = new FakeSummarizer(0.05);
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(40, 20));

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: back inside the bound the configuration promised
        Assert.False(layout.IsWithinBound);
        Assert.True(outcome.Layout.IsWithinBound);
    }

    /// <summary>
    ///     Proves consolidation is a ratchet: the second rotation is handed tier one's existing
    ///     record as an input rather than starting from the new material alone. Without this the
    ///     arrangement degenerates into re-summarizing a summary, which is what makes a flat rolling
    ///     summary forget everything beyond a handful of rotations.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_SecondRotation_CarriesThePreviousRecordForward()
    {
        // Arrange: rotate once so tier one holds a record
        var summarizer = new FakeSummarizer();
        var first = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20)),
            summarizer,
            ContextUsageOrigin.Estimated,
            TestContext.Current.CancellationToken);
        var tierOneRecord = first.Layout.CoarseTiers[0].Content;

        // Act: add more history and rotate again
        var grown = first.Layout.WithTranscript(first.Layout.Transcript.Append(
            SessionTestData.TranscriptOf(10, 20).Entries));
        await RotationEngine.RotateAsync(
            grown, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: the second consolidation received the first one's record as an input
        var second = summarizer.Requests[1];
        Assert.Equal(1, second.TierIndex);
        Assert.False(second.IsDegradation);
        Assert.Equal(tierOneRecord, second.PreviousRecord);
    }

    /// <summary>
    ///     Proves a tier that cannot hold both its old record and the new material cascades: the
    ///     <em>older</em> record degrades into the next coarser tier and the tier is then re-recorded
    ///     holding the new material alone. That ordering is what keeps the hierarchy monotonic in
    ///     age — coarser always means older.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_TierOneOverflows_DegradesTheOlderRecordIntoTierTwo()
    {
        // Arrange: a summarizer whose combined record overflows tier one but whose first recording
        // into any tier comfortably fits
        var summarizer = new FakeSummarizer(request =>
            new string('y', request.IsDegradation ? 80 : 260));
        var first = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20)),
            summarizer,
            ContextUsageOrigin.Estimated,
            TestContext.Current.CancellationToken);

        // Act: grow the history and rotate again, so tier one must hold old and new together
        var grown = first.Layout.WithTranscript(first.Layout.Transcript.Append(
            SessionTestData.TranscriptOf(10, 20).Entries));
        var second = await RotationEngine.RotateAsync(
            grown, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: merge at tier one, degrade the older record into tier two, re-record tier one
        Assert.Equal(3, second.ConsolidationCount);
        Assert.Equal([(1, true), (1, false), (2, true), (1, true)], summarizer.Shape);

        // Assert: tier two now holds the degraded older record, and both tiers fit their budgets
        Assert.False(second.Layout.CoarseTiers[1].IsEmpty);
        Assert.True(second.Layout.CoarseTiers[0].IsWithinBudget);
        Assert.True(second.Layout.CoarseTiers[1].IsWithinBudget);
    }

    /// <summary>
    ///     Proves a tool call and its result are never separated by a rotation, including when the
    ///     boundary falls inside an interleaved run of parallel calls. A boundary leaving a result
    ///     whose call was consolidated away produces history some providers reject outright and no
    ///     model can interpret.
    /// </summary>
    /// <remarks>
    ///     <b>The sequence is interleaved, and that is the whole point of the test.</b> Written with
    ///     sequential pairs — call, result, call, result — and asserting only that the first
    ///     surviving entry is not a result, this test passes against the defective
    ///     first-entry-only check as readily as against the correct one: with sequential pairs the
    ///     two implementations snap to the identical boundary, so it could never fail for the reason
    ///     it is named for. An interleaved run — call c1, call c2, result c1, result c2 — is what
    ///     separates them, because a boundary landing on c2's call passes a first-entry check (a
    ///     call is not a result) while leaving c1's result inside the retained window with its own
    ///     call in the overflow. Parallel tool calls are ordinary agent traffic. The assertion is
    ///     correspondingly over the whole retained window rather than its first entry.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_RotateAsync_BoundaryInsideToolPair_NeverSeedsAnOrphanedResult()
    {
        // Arrange: six interleaved runs of parallel calls, each entry exactly 13 tokens. Tier zero's
        // budget of 100 retains seven of them, which puts the boundary on index 17 - the second call
        // of a run. That is the discriminating position: the first retained entry is a call, so a
        // first-entry-only check sees nothing wrong, while that run's first result stays retained
        // with its own call in the overflow.
        var summarizer = new FakeSummarizer();
        var transcript = SessionTranscript.Empty;
        for (var run = 0; run < 6; run++)
        {
            transcript = transcript
                .Append(SessionTestData.ToolCallOfTokens(13, $"a{run}"))
                .Append(SessionTestData.ToolCallOfTokens(13, $"b{run}"))
                .Append(SessionTestData.ToolResultOfTokens(13, $"a{run}"))
                .Append(SessionTestData.ToolResultOfTokens(13, $"b{run}"));
        }

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.SmallPolicy, transcript),
            summarizer,
            ContextUsageOrigin.Estimated,
            TestContext.Current.CancellationToken);

        // Assert: something survived verbatim, and every retained result has its own call retained
        // ahead of it. Checking the whole window is what a first-entry assertion cannot do.
        var retained = outcome.Layout.Transcript.Entries;
        Assert.NotEmpty(retained);

        var callsRetained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in retained)
        {
            if (entry.Kind == TranscriptEntryKind.ToolCall)
            {
                callsRetained.Add(entry.ToolCallId!);
            }
            else if (entry.Kind == TranscriptEntryKind.ToolResult)
            {
                Assert.True(
                    callsRetained.Contains(entry.ToolCallId!),
                    $"Retained result '{entry.ToolCallId}' has no retained call; the rotation "
                    + "orphaned it. Retained kinds were: "
                    + string.Join(", ", retained.Select(e => $"{e.Kind}:{e.ToolCallId}")));
            }
        }

        // Assert: the boundary really did land inside a run, so the interleaving was exercised
        // rather than incidentally avoided. Without this the test could silently degrade back into
        // the sequential case it was rewritten to escape.
        Assert.NotEmpty(outcome.Layout.Transcript.Entries);
        Assert.True(
            retained.Count < transcript.Entries.Count,
            "Nothing overflowed, so no boundary was exercised at all.");
    }

    /// <summary>
    ///     Proves saturation is detected rather than silently repeated: a consolidation that returns
    ///     output nearly as large as its input means the material holds no redundancy left, and
    ///     rotating again would spend summarizer tokens for nothing.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_ConsolidationDoesNotReduce_ReportsNoRedundancy()
    {
        // Arrange: a summarizer that returns its material unchanged
        var summarizer = new FakeSummarizer(request => request.Material);
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: the failure to reduce is surfaced, with the figures that show it
        Assert.True(outcome.IsSaturated);
        var signal = outcome.Saturations.First(s => s.Reason == SaturationReason.NoRedundancy);
        Assert.Equal(1, signal.TierIndex);
        Assert.True(signal.OutputTokens >= signal.InputTokens * CompactionPolicy.DefaultSaturationRatio);
    }

    /// <summary>
    ///     Proves a record that will not fit and has nowhere coarser to go is reported rather than
    ///     quietly left over budget, so an application can see that the bound is no longer being met.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_CoarsestTierCannotFit_ReportsTierOverBudget()
    {
        // Arrange: a two-tier policy with a small coarse tier, and a summarizer that cannot reduce
        var summarizer = new FakeSummarizer(request => request.Material);
        var layout = SessionTestData.LayoutOf(
            new CompactionPolicy([100, 30]),
            SessionTestData.TranscriptOf(10, 20));

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: the over-budget condition is reported for the tier that could not hold it
        Assert.Contains(outcome.Saturations, s => s.Reason == SaturationReason.TierOverBudget);
    }

    /// <summary>
    ///     Proves the redundancy test is applied to the re-recording a cascade performs, not only
    ///     to the merge that overflowed. A re-recording that returns nearly as much as it was given
    ///     has saturated whether or not it happened to fit the tier, and a rotation that lost that
    ///     signal told the caller it had succeeded normally.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_CascadeReRecordingDoesNotReduce_ReportsNoRedundancy()
    {
        // Arrange: a summarizer scripted call by call, so the cascade path lands on a re-recording
        // that fits tier one's sixty-token budget but returns seventeen tokens for eighteen given
        var calls = 0;
        var summarizer = new FakeSummarizer(_ =>
        {
            calls++;
            return calls switch
            {
                // The first rotation's tier-one record: sixty tokens, exactly its budget
                1 => new string('p', 60 * TokenEstimator.CharactersPerToken),

                // The second rotation's merge: sixty-five tokens, over budget so it must cascade,
                // but well under the saturation ratio of its seventy-eight tokens of input
                2 => new string('m', 65 * TokenEstimator.CharactersPerToken),

                // The older record degrading into tier two: comfortably within that budget
                3 => new string('d', 20 * TokenEstimator.CharactersPerToken),

                // Tier one re-recorded alone: within budget, but it reduced eighteen tokens to
                // seventeen, so there was no redundancy left to remove
                _ => new string('a', 17 * TokenEstimator.CharactersPerToken),
            };
        });

        var first = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(6, 20)),
            summarizer,
            ContextUsageOrigin.Estimated,
            TestContext.Current.CancellationToken);

        // Act: grow the history by one entry and rotate again, so tier one must cascade
        var grown = first.Layout.WithTranscript(
            first.Layout.Transcript.Append(SessionTestData.UserOfTokens(20, "g")));
        var second = await RotationEngine.RotateAsync(
            grown, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: the second rotation really did cascade - merge, degrade, re-record
        Assert.Equal([(1, false), (2, true), (1, true)], summarizer.Shape.Skip(1).ToArray());

        // Assert: the re-recording's lack of redundancy was reported rather than lost
        var signal = Assert.Single(second.Saturations);
        Assert.Equal(SaturationReason.NoRedundancy, signal.Reason);
        Assert.Equal(1, signal.TierIndex);
        Assert.True(second.IsSaturated);
    }

    /// <summary>
    ///     Proves an outcome refuses a null saturation signal, following the same rule
    ///     <see cref="AgentSessionResponse"/> applies. An outcome holding one would report
    ///     <see cref="RotationOutcome.IsSaturated"/> true while the consumer that went to read the
    ///     signal could not.
    /// </summary>
    [Fact]
    public void RotationOutcome_Construct_NullSaturationEntry_Throws()
    {
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 0, 0);

        Assert.Throws<ArgumentException>(() => new RotationOutcome(layout, [null!], 0));
    }

    /// <summary>
    ///     Proves the engine is deterministic: the same layout and the same summarizer behavior
    ///     produce byte-identical tiers and verbatim history. This is the property that makes every
    ///     other assertion in this file meaningful.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_SameInputs_ProduceIdenticalOutput()
    {
        // Arrange: one layout, two independent summarizers behaving identically
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));

        // Act: rotate the same layout twice
        var first = await RotationEngine.RotateAsync(
            layout, new FakeSummarizer(), ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);
        var second = await RotationEngine.RotateAsync(
            layout, new FakeSummarizer(), ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: identical tiers, identical surviving history, identical cost
        Assert.Equal(
            first.Layout.CoarseTiers.Select(tier => tier.Content),
            second.Layout.CoarseTiers.Select(tier => tier.Content));
        Assert.Equal(
            first.Layout.Transcript.Entries.Select(entry => entry.Text),
            second.Layout.Transcript.Entries.Select(entry => entry.Text));
        Assert.Equal(first.ConsolidationCount, second.ConsolidationCount);
    }

    /// <summary>
    ///     Proves a missing layout is refused as a programming error.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_NullLayout_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RotationEngine.RotateAsync(
                null!,
                new FakeSummarizer(),
                ContextUsageOrigin.Estimated,
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a missing summarizer is refused: compaction cannot happen without one, and a
    ///     session that silently never compacted would fail much later and much less clearly.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_NullSummarizer_Throws()
    {
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(3, 20));

        await Assert.ThrowsAsync<ArgumentNullException>(() => RotationEngine.RotateAsync(
            layout, null!, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a summarizer returning null is refused rather than stored: a null record would
    ///     surface as a missing tier at the next rotation, far from the implementation that caused it.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_SummarizerReturnsNull_Throws()
    {
        var summarizer = new FakeSummarizer(_ => null);
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RotationEngine.RotateAsync(
                layout, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a rotation can be canceled, so a host shutting down is not held open by a
    ///     summarizer round trip.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_Canceled_Throws()
    {
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RotationEngine.RotateAsync(layout, summarizer, ContextUsageOrigin.Estimated, source.Token));
    }

    /// <summary>
    ///     Proves a rotation that has nothing to do still honors an already-canceled token. The
    ///     no-work path returns before any consolidation, so a check placed only between
    ///     consolidations would hand a caller a successful rotation result for a rotation it had
    ///     already canceled, whenever the transcript happened to fit.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_CanceledWithNothingToRotate_Throws()
    {
        // Arrange: a transcript of 60 tokens against a tier-zero budget of 100, so nothing
        // overflows and the rotation is a pure re-seed, and a token canceled before the call
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(3, 20));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        // Act / Assert: refused rather than reported as a successful rotation
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RotationEngine.RotateAsync(layout, summarizer, ContextUsageOrigin.Estimated, source.Token));
    }

    /// <summary>
    ///     Proves a summarizer returning whitespace does not poison the session. Three neighboring
    ///     validators used to disagree about whether such a record was empty, and the disagreement
    ///     was permanent once the record was written.
    /// </summary>
    /// <remarks>
    ///     <b>A whitespace answer is contract-conformant:</b> <c>ISummarizer</c> forbids only
    ///     <see langword="null"/>. It was nonetheless non-empty to <c>ContextTier.IsEmpty</c>, which
    ///     seeded it with a full label and per-entry framing to say nothing, and non-empty to the
    ///     rotation engine's cascade test, which handed it to a <c>ConsolidationRequest</c> that
    ///     refuses blank material — throwing an <c>ArgumentException</c> out of <c>RotateAsync</c>,
    ///     which documents no such exception, and out of <c>SendAsync</c>. The record was permanent
    ///     state by then, so every later rotation failed the same way. All three now read blank as
    ///     empty.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_RotateAsync_SummarizerReturnsWhitespace_TreatsTheRecordAsEmpty()
    {
        // Arrange: whitespace on the first consolidation, then an answer far too large for its tier
        // so the next rotation must try to cascade the whitespace record into a coarser tier
        var calls = 0;
        var summarizer = new FakeSummarizer(request =>
        {
            calls++;
            return calls == 1
                ? "   "
                : new string('y', 3 * request.BudgetTokens * TokenEstimator.CharactersPerToken);
        });

        // Act: the first rotation records the whitespace
        var first = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20)),
            summarizer,
            ContextUsageOrigin.Estimated,
            TestContext.Current.CancellationToken);

        // Assert: the whitespace record is empty, so it costs no framing and is not seeded at all
        Assert.True(first.Layout.CoarseTiers[0].IsEmpty);
        Assert.DoesNotContain(
            first.Layout.BuildSeed(),
            entry => entry.Kind == TranscriptEntryKind.ContextRecord);

        // Act: a second rotation, which is where the disagreement used to surface
        var grown = first.Layout.WithTranscript(
            first.Layout.Transcript.Append(SessionTestData.TranscriptOf(10, 20).Entries));
        var second = await RotationEngine.RotateAsync(
            grown, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: it completed rather than throwing, and the whitespace was never offered as
        // material to consolidate
        Assert.True(second.ConsolidationCount > 0);
        Assert.DoesNotContain(summarizer.Requests, request => string.IsNullOrWhiteSpace(request.Material));

        // Assert: a consolidation carrying only a whitespace previous record is a degradation, not
        // an extension - there is no detail in whitespace to ratchet forward
        Assert.All(
            summarizer.Requests.Where(request => string.IsNullOrWhiteSpace(request.PreviousRecord)),
            request => Assert.True(request.IsDegradation));
    }

    /// <summary>
    ///     Proves a blank summarizer answer is normalized where it is received, so what the engine
    ///     sizes, cascades on and stores is an empty record rather than the whitespace it was handed.
    /// </summary>
    /// <remarks>
    ///     <b>The consumers agreeing about whitespace was not enough, because the value was still
    ///     whitespace.</b> Reading blank as empty in <c>ContextTier.IsEmpty</c>,
    ///     <c>ConsolidationRequest.IsDegradation</c> and the cascade test settled what those three
    ///     call it, and left the record itself intact — so the engine went on measuring it. A
    ///     whitespace answer larger than its tier's budget was therefore stored as over-budget
    ///     content and reported as <c>TierOverBudget</c> saturation, while <c>BuildSeed</c> skipped
    ///     it entirely: the session was told its context had saturated on material no provider would
    ///     ever be sent. Normalizing at the boundary removes the disagreement by removing the value,
    ///     so every consumer downstream shares one definition of empty by construction.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_RotateAsync_SummarizerReturnsWhitespace_StoresAnEmptyRecordAndReportsNoSaturation()
    {
        // Arrange: a contract-conformant answer of pure whitespace, three times tier one's
        // sixty-token budget, which is the shape that used to be measured as over-budget content
        var summarizer = new FakeSummarizer(request =>
            new string(' ', 3 * request.BudgetTokens * TokenEstimator.CharactersPerToken));
        var layout = SessionTestData.LayoutOf(
            SessionTestData.SmallPolicy,
            SessionTestData.TranscriptOf(10, 20));

        // Act
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, ContextUsageOrigin.Estimated, TestContext.Current.CancellationToken);

        // Assert: the record stored is empty rather than whitespace, so nothing downstream has to
        // decide what whitespace means
        var tier = outcome.Layout.CoarseTiers[0];
        Assert.Equal(string.Empty, tier.Content);
        Assert.Equal(0, tier.EstimatedTokens);
        Assert.True(tier.IsWithinBudget);

        // Assert: no saturation was manufactured out of whitespace the provider is never sent
        Assert.Empty(outcome.Saturations);
        Assert.False(outcome.IsSaturated);

        // Assert: the estimated conversation agrees with what would actually be sent - the blank
        // tier is charged nothing, exactly as BuildSeed emits nothing for it
        Assert.Equal(outcome.Layout.Transcript.EstimatedTokens, outcome.Layout.ConversationTokens);
        Assert.DoesNotContain(
            outcome.Layout.BuildSeed(),
            entry => entry.Kind == TranscriptEntryKind.ContextRecord);
    }

    /// <summary>
    ///     Proves a cascade already under way stops when the token is canceled, rather than running
    ///     every remaining consolidation to completion.
    /// </summary>
    /// <remarks>
    ///     <b>Both other cancellation tests hand in a token that was already canceled, which is why
    ///     this went unnoticed.</b> An already-canceled token is caught by the single check at the
    ///     top of the rotation, so neither test ever reaches the cascade and neither can say
    ///     anything about what happens once it is running. A cascade is one summarizer call per
    ///     tier — several model calls in production — and <c>ISummarizer</c> documents only that an
    ///     implementation <em>may</em> honor the token, so an implementation that simply ignores it
    ///     let the whole cascade proceed after the caller had given up on it. The engine therefore
    ///     checks for itself before every consolidation, and this exercises that by canceling from
    ///     inside the summarizer and then deliberately ignoring the token, exactly as a
    ///     contract-conformant but inattentive implementation would.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_RotateAsync_CanceledMidCascade_StopsWithoutFinishingTheCascade()
    {
        // Arrange: a summarizer that cancels on its first call and then ignores the token entirely,
        // exactly as a contract-conformant but inattentive implementation would, and that always
        // overflows its tier so the rotation would otherwise cascade through every tier
        using var source = new CancellationTokenSource();
        var summarizer = new InattentiveSummarizer(request =>
        {
            source.Cancel();
            return new string('x', 3 * request.BudgetTokens * TokenEstimator.CharactersPerToken);
        });

        // Arrange: a layout already carrying records, so the rotation has a full cascade to run
        var policy = SessionTestData.SmallPolicy;
        var seeded = ContextLayout.Create(policy, 0, 0).WithTiers(
            SessionTestData.TranscriptOf(10, 20),
            [
                new ContextTier(1, policy.TierBudgetTokens[1], new string('p', 50 * TokenEstimator.CharactersPerToken)),
                new ContextTier(2, policy.TierBudgetTokens[2], new string('p', 35 * TokenEstimator.CharactersPerToken)),
                new ContextTier(3, policy.TierBudgetTokens[3], new string('p', 25 * TokenEstimator.CharactersPerToken)),
            ]);

        // Act / Assert: the rotation is abandoned rather than carried through
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RotationEngine.RotateAsync(seeded, summarizer, ContextUsageOrigin.Estimated, source.Token));

        // Assert: it stopped at the cancellation instead of finishing the cascade. Exactly one
        // consolidation ran - the one that did the canceling - and the engine refused the next,
        // which the summarizer itself would never have done.
        Assert.Equal(1, summarizer.CallCount);
    }
}

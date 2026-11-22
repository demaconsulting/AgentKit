using DemaConsulting.AgentKit.Core;
using Rpc = Microsoft.Copilot.Rpc;

namespace DemaConsulting.AgentKit.Agents.Copilot.LiveTests;

/// <summary>
///     Live scenarios for the Copilot model catalog and model settings against the real service.
/// </summary>
/// <remarks>
///     <para>
///     Supplementary verification evidence for model selection on Copilot: the catalog reads the
///     service through the runtime AgentKit started, skips the automatic choice, filters by vision,
///     refreshes, and a session created from a selection runs on the chosen model, effort and tier.
///     Every scenario runs on the suite's shared runtime, on the run's runtime source — the installed
///     CLI by default, or the bundled runtime — as every scenario does; none starts a runtime of its own.
///     The service's raw limits, tiers and billing are logged so the meaning of its largest-window
///     figure can be checked against the limit a session actually reports per tier.
///     </para>
///     <para>
///     Every scenario skips unless <c>AGENTKIT_COPILOT_LIVE_MODEL</c> names the model to use, which
///     must be a vision model offering both context tiers; a cheap one is expected.
///     </para>
/// </remarks>
[Collection(CopilotLiveCollection.Name)]
public sealed class CopilotModelCatalogLiveTests
{
    /// <summary>
    ///     The vision-only requirement the scenarios list and select under.
    /// </summary>
    private const Core.ModelCapabilities Required = Core.ModelCapabilities.Vision | Core.ModelCapabilities.ToolCalls;

    /// <summary>
    ///     The run's shared state.
    /// </summary>
    private readonly CopilotLiveFixture _live;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotModelCatalogLiveTests"/> class.
    /// </summary>
    /// <param name="live">The run's shared state.</param>
    public CopilotModelCatalogLiveTests(CopilotLiveFixture live)
    {
        _live = live;
    }

    /// <summary>
    ///     M1: the listing reads through the runtime, skips the automatic choice, lists every model the
    ///     service reports as vision under the vision filter, and refreshes. The raw service values
    ///     are logged.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Catalog_ListsFiltersAndRefreshes()
    {
        // Arrange
        _live.RequireOptIn();
        var catalog = new CopilotModelCatalog(_live.Runtime);
        var raw = (await _live.Client.Rpc.Models.ListAsync(TestContext.Current.CancellationToken)).Models;
        var cached = await _live.Client.ListModelsAsync(TestContext.Current.CancellationToken);
        _live.Log($"M1 raw Rpc.Models.ListAsync count={raw.Count}; ListModelsAsync count={cached.Count}");
        foreach (var model in raw)
        {
            _live.Log(DescribeRaw(model));
        }

        // Act
        var everything = await catalog.ListAsync(new ModelQuery { IncludeUnusable = true }, TestContext.Current.CancellationToken);
        var vision = await catalog.ListAsync(new ModelQuery { Require = Required }, TestContext.Current.CancellationToken);
        await catalog.RefreshAsync(TestContext.Current.CancellationToken);
        var refreshed = await catalog.ListAsync(new ModelQuery { IncludeUnusable = true }, TestContext.Current.CancellationToken);
        foreach (var model in everything)
        {
            _live.Log(Describe(model));
        }

        // Assert
        Assert.Contains(everything, model => model.IsUsable);
        Assert.Equal(everything.Count, everything.Select(model => model.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(everything, model => model.Id == "auto");
        Assert.Equal(raw.Count(model => model.Id != "auto"), everything.Count);
        Assert.All(
            raw.Where(model => model.Id != "auto" && model.Capabilities?.Supports?.Vision == true),
            model => Assert.True(everything.Single(entry => entry.Id == model.Id).Supports(Core.ModelCapabilities.Vision)));
        Assert.Equal(
            everything.Where(model => model.IsUsable && model.Supports(Required)).Select(model => model.Id),
            vision.Select(model => model.Id));
        Assert.Equal(everything.Count, refreshed.Count);
    }

    /// <summary>
    ///     M2: a session created from a selection runs on the chosen model, effort and tier; the limit
    ///     the session reports is logged for each tier against the catalog's figures.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Selection_SessionRunsOnTheChosenEffortAndTier()
    {
        // Arrange
        _live.RequireOptIn();
        var catalog = new CopilotModelCatalog(_live.Runtime);
        var model = await catalog.GetAsync(_live.Model!, TestContext.Current.CancellationToken);
        Assert.NotNull(model);
        _live.Log(Describe(model));
        var effort = model.ReasoningEfforts.Count > 0 ? model.ReasoningEfforts[0].Id : null;

        foreach (var tier in model.ContextTiers)
        {
            var selection = await catalog.SelectAsync(
                new ModelSettings(model.Id) { ReasoningEffort = effort, ContextTier = tier.Id },
                Required,
                TestContext.Current.CancellationToken);
            var factory = new CopilotProviderSessionFactory(_live.Runtime, workingDirectory: _live.Sandbox, selection: selection);

            // Act
            await using var session = await factory.CreateAsync(new ProviderSessionSeed(null, [], []), TestContext.Current.CancellationToken);
            var turn = await session.SendAsync("Reply with the single word ready.", TestContext.Current.CancellationToken);
            _live.Log(
                $"M2 tier={tier.Id} effort={effort} catalogTierWindow={tier.WindowTokens} catalogMaxWindow={model.ContextWindowTokens} "
                + $"reportedLimit={session.CurrentUsage.WindowTokens} used={session.CurrentUsage.UsedTokens} answer='{turn.ResponseText}'");

            // Assert: the session answered and reported a limit
            Assert.False(string.IsNullOrWhiteSpace(turn.ResponseText));
            Assert.True(session.CurrentUsage.WindowTokens > 0);
        }
    }

    /// <summary>
    ///     M3: an effort the model does not offer is refused by validation before any session exists.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Selection_UnofferedEffort_IsRefusedBeforeAnySession()
    {
        // Arrange
        _live.RequireOptIn();
        var catalog = new CopilotModelCatalog(_live.Runtime);

        // Act
        var failure = await Assert.ThrowsAsync<ModelSettingsException>(() => catalog.SelectAsync(
            new ModelSettings(_live.Model!) { ReasoningEffort = "not-an-effort" },
            Required,
            TestContext.Current.CancellationToken));
        _live.Log($"M3 refused: {failure.Problems[0]}");

        // Assert
        Assert.Contains("does not support reasoning effort", failure.Problems[0], StringComparison.Ordinal);
    }

    /// <summary>
    ///     Renders the service's raw description of a model for the log.
    /// </summary>
    /// <param name="model">The service's model.</param>
    /// <returns>One line.</returns>
    private static string DescribeRaw(Rpc.Model model)
    {
        var supports = model.Capabilities?.Supports;
        var limits = model.Capabilities?.Limits;
        var prices = model.Billing?.TokenPrices;
        return $"[raw] {model.Id} vision={supports?.Vision} tools={supports?.ToolCalls} thinking={supports?.Thinking} "
            + $"effort={supports?.ReasoningEffort} maxWindow={limits?.MaxContextWindowTokens} maxPrompt={limits?.MaxPromptTokens} "
            + $"maxOutput={limits?.MaxOutputTokens} tierPrompt={prices?.MaxPromptTokens} longPrompt={prices?.LongContext?.MaxPromptTokens} "
            + $"tiers=[{string.Join(",", model.SupportedContextTiers ?? [])}] efforts=[{string.Join(",", model.SupportedReasoningEfforts ?? [])}] "
            + $"defaultEffort={model.DefaultReasoningEffort} multiplier={model.Billing?.Multiplier} policy={model.Policy?.State.Value} "
            + $"category={model.ModelPickerCategory?.Value} price={model.ModelPickerPriceCategory?.Value}";
    }

    /// <summary>
    ///     Renders a catalog model for the log.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <returns>One line.</returns>
    private static string Describe(Core.ModelInfo model) =>
        $"[catalog] {model.Id} usable={model.IsUsable} caps={model.Capabilities} maxWindow={model.ContextWindowTokens} "
        + $"tiers=[{string.Join(",", model.ContextTiers.Select(tier => $"{tier.Id}:{tier.WindowTokens}"))}] "
        + $"efforts=[{string.Join(",", model.ReasoningEfforts.Select(effort => effort.Id))}] reason={model.UnusableReason}";
}

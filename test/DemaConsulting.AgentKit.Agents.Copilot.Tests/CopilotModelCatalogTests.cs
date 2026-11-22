using System.Globalization;
using DemaConsulting.AgentKit.Core;
using Microsoft.Copilot;
using Microsoft.Extensions.AI;
using Rpc = Microsoft.Copilot.Rpc;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for the Copilot model catalog: the mapping of the service's model descriptions,
///     the read through the runtime, and the caching the catalog inherits.
/// </summary>
/// <remarks>
///     The SDK's model query is sealed and cannot be constructed, so the listing is supplied through
///     the catalog's read seam as hand-built SDK models. The fixture mirrors what the service sends
///     for <c>claude-haiku-5.5</c>: no tool-calling flag, no default effort, no declared tiers, and
///     tiers published through billing.
/// </remarks>
public sealed class CopilotModelCatalogTests
{
    /// <summary>
    ///     Proves tiers come from billing, labeled by AgentKit, each window its prompt budget plus the
    ///     output limit capped at the model's largest window.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_BillingTiers_AreDerivedLabeledAndCapped()
    {
        // Arrange / Act
        var model = CopilotModelCatalog.Map(Haiku());

        // Assert: 100,000 + 128,000 for standard; 936,000 + 128,000 capped at 1,000,000 for long context
        Assert.Equal(
            [
                new ModelContextTier("default", "Standard", 228_000),
                new ModelContextTier("long_context", "Long context", 1_000_000),
            ],
            model.ContextTiers);
        Assert.Equal("default", model.DefaultContextTier);
        Assert.Equal(1_000_000, model.ContextWindowTokens);
        Assert.Equal(128_000, model.MaxOutputTokens);
        Assert.Equal(
            ModelAdjustments.ReasoningEffort | ModelAdjustments.ContextTier | ModelAdjustments.ContextWindow,
            model.Adjustments);
    }

    /// <summary>
    ///     Proves a tier the model declares directly is added, with no window, and none is duplicated.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_DeclaredTiers_AreAddedOnce()
    {
        // Arrange
        var source = Haiku();
        source.SupportedContextTiers = ["default", "extended"];

        // Act
        var model = CopilotModelCatalog.Map(source);

        // Assert
        Assert.Equal(["default", "long_context", "extended"], model.ContextTiers.Select(tier => tier.Id));
        Assert.Null(model.ContextTiers[2].WindowTokens);
    }

    /// <summary>
    ///     Proves an unreported tool-calling flag counts as supported, the provider-specific rule.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_UnreportedToolCalling_IsSupported()
    {
        // Arrange / Act: the fixture reports no tool-calling flag, as the service does
        var model = CopilotModelCatalog.Map(Haiku());

        // Assert
        Assert.True(model.Supports(ModelCapabilities.ToolCalls));
        Assert.True(model.IsUsable);
    }

    /// <summary>
    ///     Proves a model reported as unable to call tools is unusable and does not carry the flag.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_ToolCallingDenied_IsUnusable()
    {
        // Arrange
        var source = Haiku();
        source.Capabilities.Supports!.ToolCalls = false;

        // Act
        var model = CopilotModelCatalog.Map(source);

        // Assert
        Assert.False(model.Supports(ModelCapabilities.ToolCalls));
        Assert.False(model.IsUsable);
        Assert.Equal("Model does not support tool calling.", model.UnusableReason);
    }

    /// <summary>
    ///     Proves vision is set only on a positive report.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_Vision_IsSetOnlyWhenReported()
    {
        // Arrange
        var reported = Haiku();
        var unreported = Haiku();
        unreported.Capabilities.Supports!.Vision = null;
        var denied = Haiku();
        denied.Capabilities.Supports!.Vision = false;

        // Act / Assert
        Assert.True(CopilotModelCatalog.Map(reported).Supports(ModelCapabilities.Vision));
        Assert.False(CopilotModelCatalog.Map(unreported).Supports(ModelCapabilities.Vision));
        Assert.False(CopilotModelCatalog.Map(denied).Supports(ModelCapabilities.Vision));
    }

    /// <summary>
    ///     Proves efforts keep the service's identifiers and order, map to the neutral scale where
    ///     they can, and no default is invented.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_Efforts_KeepTheServicesIdentifiersAndInventNoDefault()
    {
        // Arrange
        var source = Haiku();
        source.SupportedReasoningEfforts = ["minimal", "low", "medium", "high", "xhigh", "max"];

        // Act
        var model = CopilotModelCatalog.Map(source);

        // Assert
        Assert.Equal(["minimal", "low", "medium", "high", "xhigh", "max"], model.ReasoningEfforts.Select(effort => effort.Id));
        Assert.Equal(
            [null, ReasoningEffort.Low, ReasoningEffort.Medium, ReasoningEffort.High, ReasoningEffort.ExtraHigh, null],
            model.ReasoningEfforts.Select(effort => effort.Standard));
        Assert.Null(model.DefaultReasoningEffort);
        Assert.True(model.Supports(ModelCapabilities.Reasoning));
    }

    /// <summary>
    ///     Proves a default effort the service names is reported when it is one of the offered efforts.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_PublishedDefaultEffort_IsReported()
    {
        // Arrange
        var source = Haiku();
        source.DefaultReasoningEffort = "medium";

        // Act / Assert
        Assert.Equal("medium", CopilotModelCatalog.Map(source).DefaultReasoningEffort);
    }

    /// <summary>
    ///     Proves a disabled or unconfigured policy makes the model unusable with the right reason, and
    ///     an unconfigured model carries the service's terms.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_Policy_DecidesUsability()
    {
        // Arrange
        var disabled = Haiku();
        disabled.Policy = new Rpc.ModelPolicy { State = Rpc.ModelPolicyState.Disabled };
        var unconfigured = Haiku();
        unconfigured.Policy = new Rpc.ModelPolicy { State = Rpc.ModelPolicyState.Unconfigured, Terms = "Accept the preview terms." };

        // Act
        var first = CopilotModelCatalog.Map(disabled);
        var second = CopilotModelCatalog.Map(unconfigured);

        // Assert
        Assert.Equal("Disabled by your organization's Copilot policy.", first.UnusableReason);
        Assert.Equal("Not enabled for your account; enable it in GitHub Copilot settings.", second.UnusableReason);
        Assert.Contains(second.Notices, notice => notice.Message == "Accept the preview terms.");
        Assert.True(CopilotModelCatalog.Map(Haiku()).IsUsable);
    }

    /// <summary>
    ///     Proves the service's messages become notices of the matching kind.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_Messages_BecomeNotices()
    {
        // Arrange
        var source = Haiku();
        source.InfoMessages = [new Rpc.ModelMessage { Code = "i1", Message = "Fast model." }];
        source.WarningMessages = [new Rpc.ModelMessage { Code = "w1", Message = "Preview." }];
        source.WarningText = new Rpc.ModelWarningText { DataRetention = "Prompts are retained." };

        // Act
        var notices = CopilotModelCatalog.Map(source).Notices;

        // Assert
        Assert.Equal(
            [
                new ModelNotice(ModelNoticeKind.Info, "Fast model.", "i1"),
                new ModelNotice(ModelNoticeKind.Warning, "Preview.", "w1"),
                new ModelNotice(ModelNoticeKind.Required, "Prompts are retained.", "data_retention"),
            ],
            notices);
    }

    /// <summary>
    ///     Proves the picker metadata is carried: name, vendor, multiplier, category and price band.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_PickerMetadata_IsCarried()
    {
        // Arrange / Act
        var model = CopilotModelCatalog.Map(Haiku());

        // Assert
        Assert.Equal("claude-haiku-5.5", model.Id);
        Assert.Equal("Claude Haiku 5.5", model.DisplayName);
        Assert.Equal("Anthropic", model.Vendor);
        Assert.Equal(0.33, model.BillingMultiplier);
        Assert.Equal("lightweight", model.Category);
        Assert.Equal("low", model.PriceCategory);
        Assert.Equal(CopilotModelCatalog.ProviderId, model.Provider);
    }

    /// <summary>
    ///     Proves the prompt and vision limits are carried as read-only additional properties: the
    ///     media types are a read-only list, not an array that could be cast back and altered.
    /// </summary>
    [Fact]
    public void CopilotModelCatalog_Map_VisionLimits_AreCarriedAsReadOnlyExtras()
    {
        // Arrange: the fixture with the service's vision limits
        var source = Haiku();
        source.Capabilities.Limits!.Vision = new Rpc.ModelCapabilitiesLimitsVision
        {
            MaxPromptImages = 20,
            MaxPromptImageSize = 3_145_728,
            SupportedMediaTypes = ["image/png", "image/jpeg"],
        };

        // Act
        var properties = CopilotModelCatalog.Map(source).AdditionalProperties;

        // Assert
        Assert.NotNull(properties);
        Assert.Equal(936_000L, Convert.ToInt64(properties["max_prompt_tokens"], CultureInfo.InvariantCulture));
        var mediaTypes = Assert.IsAssignableFrom<IReadOnlyList<string>>(properties["vision_supported_media_types"]);
        Assert.Equal(["image/png", "image/jpeg"], mediaTypes);
        Assert.False(mediaTypes is string[] or List<string>);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)mediaTypes)[0] = "image/gif");
    }

    /// <summary>
    ///     Proves the automatic choice is not listed and a lookup of it finds nothing, while the
    ///     service's order is kept.
    /// </summary>
    [Fact]
    public async Task CopilotModelCatalog_ListAsync_AutoEntry_IsSkippedAndOrderKept()
    {
        // Arrange
        var other = Haiku();
        other.Id = "gpt-6";
        var catalog = new CopilotModelCatalog(_ => Task.FromResult<IList<Rpc.Model>>([new Rpc.Model { Id = "auto", Name = "Auto" }, Haiku(), other]));

        // Act
        var models = await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var auto = await catalog.GetAsync("auto", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["claude-haiku-5.5", "gpt-6"], models.Select(model => model.Id));
        Assert.Null(auto);
    }

    /// <summary>
    ///     Proves the listing is cached and a refresh reads the service again.
    /// </summary>
    [Fact]
    public async Task CopilotModelCatalog_RefreshAsync_ReadsTheServiceAgain()
    {
        // Arrange
        var reads = 0;
        var catalog = new CopilotModelCatalog(_ =>
        {
            reads++;
            return Task.FromResult<IList<Rpc.Model>>([Haiku()]);
        });

        // Act
        await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        await catalog.RefreshAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, reads);
    }

    /// <summary>
    ///     Proves a disposed runtime is refused when the catalog is built.
    /// </summary>
    [Fact]
    public async Task CopilotModelCatalog_Construct_DisposedRuntime_Throws()
    {
        // Arrange
        var runtime = RuntimeThatCannotStart();
        await runtime.DisposeAsync();

        // Act / Assert
        Assert.Throws<ObjectDisposedException>(() => new CopilotModelCatalog(runtime));
    }

    /// <summary>
    ///     Proves a runtime disposed after the catalog was built is refused at the read, rather than
    ///     restarted.
    /// </summary>
    [Fact]
    public async Task CopilotModelCatalog_ListAsync_RuntimeDisposedLater_Throws()
    {
        // Arrange
        var runtime = RuntimeThatCannotStart();
        var catalog = new CopilotModelCatalog(runtime);
        await runtime.DisposeAsync();

        // Act / Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a runtime whose client cannot answer fails with the sign-in message, keeping the
    ///     SDK's own failure as the cause.
    /// </summary>
    [Fact]
    public async Task CopilotModelCatalog_ListAsync_ClientNotConnected_FailsWithTheSignInMessage()
    {
        // Arrange: a runtime whose client was never started
        await using var runtime = RuntimeThatCannotStart();
        var catalog = new CopilotModelCatalog(runtime);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(CopilotModelCatalog.SignInMessage, failure.Message);
        Assert.Contains("gh auth login", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Copilot CLI's own sign-in is not used", failure.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(failure.InnerException, exactMatch: false);
    }

    /// <summary>
    ///     Builds an SDK model mirroring the service's description of <c>claude-haiku-5.5</c>.
    /// </summary>
    /// <returns>The model.</returns>
    internal static Rpc.Model Haiku() => new()
    {
        Id = "claude-haiku-5.5",
        Name = "Claude Haiku 5.5",
        Vendor = "Anthropic",
        Capabilities = new Rpc.ModelCapabilities
        {
            Supports = new Rpc.ModelCapabilitiesSupports { Vision = true, ReasoningEffort = true },
            Limits = new Rpc.ModelCapabilitiesLimits
            {
                MaxContextWindowTokens = 1_000_000,
                MaxOutputTokens = 128_000,
                MaxPromptTokens = 936_000,
            },
        },
        Billing = new Rpc.ModelBilling
        {
            Multiplier = 0.33,
            TokenPrices = new Rpc.ModelBillingTokenPrices
            {
                MaxPromptTokens = 100_000,
                LongContext = new Rpc.ModelBillingTokenPricesLongContext { MaxPromptTokens = 936_000 },
            },
        },
        SupportedReasoningEfforts = ["low", "medium", "high"],
        ModelPickerCategory = new Rpc.ModelPickerCategory("lightweight"),
        ModelPickerPriceCategory = new Rpc.ModelPickerPriceCategory("low"),
    };

    /// <summary>
    ///     Builds a selection of the Haiku fixture with the given choices.
    /// </summary>
    /// <param name="effort">The chosen effort, or <see langword="null"/>.</param>
    /// <param name="tier">The chosen tier, or <see langword="null"/>.</param>
    /// <param name="window">The chosen window ceiling, or <see langword="null"/>.</param>
    /// <returns>The validated selection.</returns>
    internal static ModelSelection Selection(string? effort = null, string? tier = null, int? window = null) =>
        new ModelSettings("claude-haiku-5.5") { ReasoningEffort = effort, ContextTier = tier, ContextWindowTokens = window }
            .Bind(CopilotModelCatalog.Map(Haiku()), ModelCapabilities.Vision);

    /// <summary>
    ///     Builds a runtime over a client that can never start.
    /// </summary>
    /// <returns>The runtime. Nothing has been started.</returns>
    private static CopilotRuntime RuntimeThatCannotStart()
    {
        var temporary = Path.GetTempPath();
        var unique = Guid.NewGuid().ToString("N");
        return new CopilotRuntime(
            new CopilotClient(new CopilotClientOptions
            {
                Mode = CopilotClientMode.Empty,
                Runtime = RuntimeConnection.ForSpawnedStdio(
                    path: Path.Combine(temporary, "agentkit-no-such-runtime-" + unique),
                    baseDirectory: Path.Combine(temporary, unique)),
            }),
            Path.Combine(temporary, unique));
    }
}

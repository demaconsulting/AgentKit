using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;
using Rpc = Microsoft.Copilot.Rpc;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     The models the GitHub Copilot service offers the signed-in user, described in
///     provider-neutral terms an application can filter on, show in a picker, and validate settings
///     against.
/// </summary>
/// <remarks>
///     <para>
///     <b>Where the description comes from.</b> The service's own model listing, read through the
///     runtime AgentKit started. The listing is read through the runtime's uncached model query, so
///     <see cref="CachingModelCatalog.RefreshAsync"/> sees a model the user was just granted. The
///     runtime's client is read at every read rather than kept, so a runtime disposed by its host is
///     refused rather than restarted.
///     </para>
///     <para>
///     <b>Capabilities — a provider-specific rule for tool calling.</b> Vision is set only when the
///     service reports it, and reasoning when it reports thinking, a reasoning-effort setting, or
///     any effort. Tool calling is the exception: it is treated as <em>supported unless the service
///     says otherwise</em>. The service reports no tool-calling flag for any model, and the Copilot
///     runtime is itself a tool-calling agent runtime, so reading silence as absence would make
///     every model unusable. A model the service explicitly reports as unable to call tools is
///     unusable.
///     </para>
///     <para>
///     <b>Context tiers.</b> The service publishes tiers through billing: a model with token prices
///     offers <c>default</c>, shown as "Standard", and one with long-context prices also offers
///     <c>long_context</c>, shown as "Long context"; any tier the model declares directly is added.
///     A tier's window is its prompt budget plus the model's output limit, capped at the model's
///     largest window. <see cref="Core.ModelInfo.ContextWindowTokens"/> is that largest window,
///     reported as the service gives it.
///     </para>
///     <para>
///     <b>Efforts</b> are the service's own identifiers, in its order. No default is invented: when
///     the service names none, none is reported and the runtime's own applies. An effort with no
///     provider-neutral equivalent — <c>max</c>, <c>minimal</c> — is still offered, because this
///     runtime applies it by name.
///     </para>
///     <para>
///     <b>Usability.</b> A model is usable when its policy is enabled (or absent) and it is not
///     reported as unable to call tools. A model the organization disabled, or one not yet enabled
///     for the account, is unusable with a reason; the latter carries the service's terms as a
///     notice. The service's information and warning messages become notices, and its
///     data-retention text a notice the service says must be shown.
///     </para>
///     <para>
///     <b>The automatic choice is not listed.</b> The service lists an <c>auto</c> entry that lets it
///     choose a model per request. It cannot promise a capability the application requires, so it is
///     skipped and a lookup of it finds nothing.
///     </para>
///     <para>
///     <b>Additional properties</b> written: <c>max_prompt_tokens</c>,
///     <c>vision_max_prompt_images</c>, <c>vision_max_prompt_image_size</c>,
///     <c>vision_supported_media_types</c> and <c>policy_terms</c>, where the service reports them.
///     <c>vision_supported_media_types</c> is an <see cref="IReadOnlyList{T}"/> of
///     <see cref="string"/>.
///     </para>
///     <para>
///     <b>Failures.</b> A client that cannot list models, for example because it is not connected,
///     fails with an <see cref="InvalidOperationException"/> that carries the sign-in message, with the
///     SDK's exception as the inner exception. A runtime disposed by its host fails with
///     <see cref="ObjectDisposedException"/>. Any other failure, including a cancellation no caller
///     requested, propagates unchanged. Nothing is cached.
///     </para>
///     <para>
///     Cached, single-flight and safe for concurrent use; see <see cref="CachingModelCatalog"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public async Task ListVisionModelsAsync(CopilotRuntime runtime, CancellationToken cancellationToken)
///     {
///         var catalog = new CopilotModelCatalog(runtime);
///
///         var models = await catalog.ListAsync(
///             new ModelQuery { Require = ModelCapabilities.Vision | ModelCapabilities.ToolCalls },
///             cancellationToken);
///
///         foreach (var model in models)
///         {
///             Console.WriteLine($"{model.Category}: {model.DisplayName} x{model.BillingMultiplier}");
///         }
///     }
///     </code>
/// </example>
public sealed class CopilotModelCatalog : CachingModelCatalog
{
    /// <summary>
    ///     The provider name every model this catalog describes carries.
    /// </summary>
    public const string ProviderId = "copilot";

    /// <summary>
    ///     The identifier of the service's automatic model choice, which is not listed.
    /// </summary>
    internal const string AutoModelId = "auto";

    /// <summary>
    ///     The identifier of the standard context tier.
    /// </summary>
    internal const string DefaultTier = "default";

    /// <summary>
    ///     The identifier of the long-context tier.
    /// </summary>
    internal const string LongContextTier = "long_context";

    /// <summary>
    ///     The message a read that could not reach the service fails with.
    /// </summary>
    internal const string SignInMessage =
        "The Copilot runtime could not list models; sign in to GitHub Copilot (the CLI must be installed and authenticated).";

    /// <summary>
    ///     Reads the service's listing.
    /// </summary>
    private readonly Func<CancellationToken, Task<IList<Rpc.Model>>> _read;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotModelCatalog"/> class over a runtime
    ///     AgentKit started.
    /// </summary>
    /// <param name="runtime">
    ///     The runtime to read through, from <see cref="CopilotRuntime.StartAsync"/>. Must not be
    ///     <see langword="null"/> or disposed. The catalog never disposes it.
    /// </param>
    /// <param name="lifetime">How long a listing is kept, or <see langword="null"/> for five minutes.</param>
    /// <param name="timeProvider">The clock, or <see langword="null"/> for the system clock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="runtime"/> has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not positive.</exception>
    public CopilotModelCatalog(CopilotRuntime runtime, TimeSpan? lifetime = null, TimeProvider? timeProvider = null)
        : this(ReadThrough(runtime), lifetime, timeProvider)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotModelCatalog"/> class over a supplied
    ///     read.
    /// </summary>
    /// <remarks>
    ///     The test seam: the SDK's model query is a sealed class with no public constructor, so the
    ///     mapping and caching cannot be exercised without a live service unless the read is
    ///     injectable.
    /// </remarks>
    /// <param name="read">Reads the service's listing.</param>
    /// <param name="lifetime">How long a listing is kept, or <see langword="null"/> for five minutes.</param>
    /// <param name="timeProvider">The clock, or <see langword="null"/> for the system clock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> is <see langword="null"/>.</exception>
    internal CopilotModelCatalog(
        Func<CancellationToken, Task<IList<Rpc.Model>>> read,
        TimeSpan? lifetime = null,
        TimeProvider? timeProvider = null)
        : base(ProviderId, lifetime, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(read);

        _read = read;
    }

    /// <summary>
    ///     Describes one model from the service's listing.
    /// </summary>
    /// <remarks>
    ///     A pure function, so every mapping rule can be exercised on hand-built SDK models.
    /// </remarks>
    /// <param name="model">The service's model.</param>
    /// <returns>The model, described.</returns>
    internal static Core.ModelInfo Map(Rpc.Model model)
    {
        var supports = model.Capabilities?.Supports;
        var limits = model.Capabilities?.Limits;
        var efforts = Efforts(model.SupportedReasoningEfforts);
        var maxWindow = Tokens(limits?.MaxContextWindowTokens);
        var maxOutput = Tokens(limits?.MaxOutputTokens);
        var tiers = Tiers(model, maxWindow, maxOutput);

        // Capabilities: vision strictly on a positive signal, tool calling unless denied.
        var capabilities = ModelCapabilities.None;
        if (supports?.ToolCalls != false)
        {
            capabilities |= ModelCapabilities.ToolCalls;
        }

        if (supports?.Vision == true)
        {
            capabilities |= ModelCapabilities.Vision;
        }

        if (supports?.ReasoningEffort == true || supports?.Thinking == true || efforts.Count > 0)
        {
            capabilities |= ModelCapabilities.Reasoning;
        }

        var adjustments = ModelAdjustments.ContextWindow;
        if (efforts.Count > 0)
        {
            adjustments |= ModelAdjustments.ReasoningEffort;
        }

        if (tiers.Count > 0)
        {
            adjustments |= ModelAdjustments.ContextTier;
        }

        var unusable = Unusable(model.Policy, supports?.ToolCalls);

        return new Core.ModelInfo
        {
            Provider = ProviderId,
            Id = model.Id,
            DisplayName = string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name,
            Vendor = string.IsNullOrWhiteSpace(model.Vendor) ? null : model.Vendor,
            Capabilities = capabilities,
            Adjustments = adjustments,
            IsUsable = unusable is null,
            UnusableReason = unusable,
            ContextWindowTokens = maxWindow,
            MaxOutputTokens = maxOutput,
            ReasoningEfforts = efforts,
            DefaultReasoningEffort = efforts.FirstOrDefault(
                effort => string.Equals(effort.Id, model.DefaultReasoningEffort, StringComparison.OrdinalIgnoreCase))?.Id,
            ContextTiers = tiers,
            DefaultContextTier = tiers.Any(tier => tier.Id == DefaultTier) ? DefaultTier : null,
            BillingMultiplier = model.Billing?.Multiplier,
            Category = model.ModelPickerCategory?.Value,
            PriceCategory = model.ModelPickerPriceCategory?.Value,
            Notices = Notices(model),
            AdditionalProperties = Properties(model, limits),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The service's order is kept, and the automatic choice is skipped. Performs network I/O.
    /// </remarks>
    protected override async Task<IReadOnlyList<Core.ModelInfo>> FetchAsync(CancellationToken cancellationToken)
    {
        var models = await _read(cancellationToken).ConfigureAwait(false) ?? [];

        return
        [
            .. models
                .Where(model => model is not null && !string.IsNullOrWhiteSpace(model.Id))
                .Where(model => !string.Equals(model.Id, AutoModelId, StringComparison.OrdinalIgnoreCase))
                .Select(Map),
        ];
    }

    /// <summary>
    ///     Builds the production read over a runtime.
    /// </summary>
    /// <param name="runtime">The runtime.</param>
    /// <returns>The read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="runtime"/> has been disposed.</exception>
    private static Func<CancellationToken, Task<IList<Rpc.Model>>> ReadThrough(CopilotRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.ThrowIfDisposed();

        return async cancellationToken =>
        {
            // The client is read at every read, never kept: a runtime its host disposed is refused
            // here rather than handed a client the SDK would restart.
            runtime.ThrowIfDisposed();
            var client = runtime.Client;

            try
            {
                // The uncached query, so a refresh sees a model the user was just granted; the
                // client's own listing caches for the life of the connection.
                var list = await client.Rpc.Models.ListAsync(cancellationToken).ConfigureAwait(false);
                return list?.Models ?? [];
            }
            catch (InvalidOperationException exception) when (exception is not ObjectDisposedException)
            {
                throw new InvalidOperationException(SignInMessage, exception);
            }
        };
    }

    /// <summary>
    ///     Builds the efforts from the service's identifiers, in its order.
    /// </summary>
    /// <param name="supported">The service's identifiers, if any.</param>
    /// <returns>The efforts.</returns>
    private static IReadOnlyList<ModelReasoningEffort> Efforts(IList<string>? supported)
    {
        if (supported is null)
        {
            return [];
        }

        return
        [
            .. supported
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(Effort),
        ];
    }

    /// <summary>
    ///     Describes one effort, naming it and mapping it to the neutral scale where it has a place.
    /// </summary>
    /// <param name="id">The service's identifier.</param>
    /// <returns>The effort.</returns>
    private static ModelReasoningEffort Effort(string id) => id.ToLowerInvariant() switch
    {
        "none" => new(id, "None", ReasoningEffort.None),
        "minimal" => new(id, "Minimal", null),
        "low" => new(id, "Low", ReasoningEffort.Low),
        "medium" => new(id, "Medium", ReasoningEffort.Medium),
        "high" => new(id, "High", ReasoningEffort.High),
        "xhigh" => new(id, "Extra high", ReasoningEffort.ExtraHigh),
        "max" => new(id, "Maximum", null),
        _ => new(id, id, null),
    };

    /// <summary>
    ///     Builds the tiers from the model's billing and its declared tiers.
    /// </summary>
    /// <param name="model">The service's model.</param>
    /// <param name="maxWindow">The model's largest window, if known.</param>
    /// <param name="maxOutput">The model's output limit, if known.</param>
    /// <returns>The tiers, standard first.</returns>
    private static IReadOnlyList<ModelContextTier> Tiers(Rpc.Model model, int? maxWindow, int? maxOutput)
    {
        var prices = model.Billing?.TokenPrices;
        var tiers = new List<ModelContextTier>();

        if (prices is not null)
        {
            tiers.Add(new ModelContextTier(DefaultTier, "Standard", TierWindow(prices.MaxPromptTokens, maxOutput, maxWindow)));
        }

        if (prices?.LongContext is { } longContext)
        {
            tiers.Add(new ModelContextTier(LongContextTier, "Long context", TierWindow(longContext.MaxPromptTokens, maxOutput, maxWindow)));
        }

        var declaredTiers = (model.SupportedContextTiers ?? [])
            .Where(declared => !string.IsNullOrWhiteSpace(declared))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(declared => !tiers.Exists(tier => string.Equals(tier.Id, declared, StringComparison.OrdinalIgnoreCase)))
            .Select(declared => new ModelContextTier(declared, TierLabel(declared), null))
            .ToList();
        tiers.AddRange(declaredTiers);

        return tiers;
    }

    /// <summary>
    ///     Computes a tier's window: its prompt budget plus the output limit, capped at the model's
    ///     largest window.
    /// </summary>
    /// <param name="prompt">The tier's prompt budget, if known.</param>
    /// <param name="maxOutput">The model's output limit, if known.</param>
    /// <param name="maxWindow">The model's largest window, if known.</param>
    /// <returns>The window, or <see langword="null"/> when the budget or the limit is unknown.</returns>
    private static int? TierWindow(long? prompt, int? maxOutput, int? maxWindow)
    {
        if (prompt is not > 0 || maxOutput is null)
        {
            return null;
        }

        var window = Math.Min(prompt.Value + maxOutput.Value, int.MaxValue);
        if (maxWindow is { } cap)
        {
            window = Math.Min(window, cap);
        }

        return (int)window;
    }

    /// <summary>
    ///     Names a tier the service declared directly.
    /// </summary>
    /// <param name="id">The tier identifier.</param>
    /// <returns>The label.</returns>
    private static string TierLabel(string id) => id switch
    {
        DefaultTier => "Standard",
        LongContextTier => "Long context",
        _ => id,
    };

    /// <summary>
    ///     Decides whether an agent can run on the model.
    /// </summary>
    /// <param name="policy">The model's policy, if reported.</param>
    /// <param name="toolCalls">The service's tool-calling flag, if reported.</param>
    /// <returns>The reason it cannot, or <see langword="null"/> when it can.</returns>
    private static string? Unusable(Rpc.ModelPolicy? policy, bool? toolCalls)
    {
        if (policy is not null && policy.State != Rpc.ModelPolicyState.Enabled)
        {
            if (policy.State == Rpc.ModelPolicyState.Disabled)
            {
                return "Disabled by your organization's Copilot policy.";
            }

            if (policy.State == Rpc.ModelPolicyState.Unconfigured)
            {
                return "Not enabled for your account; enable it in GitHub Copilot settings.";
            }

            return $"Not available under your Copilot policy ({policy.State.Value}).";
        }

        return toolCalls == false ? "Model does not support tool calling." : null;
    }

    /// <summary>
    ///     Collects the messages about the model an application should show.
    /// </summary>
    /// <param name="model">The service's model.</param>
    /// <returns>The notices.</returns>
    private static IReadOnlyList<ModelNotice> Notices(Rpc.Model model)
    {
        var notices = new List<ModelNotice>();
        Add(model.InfoMessages, ModelNoticeKind.Info);
        Add(model.WarningMessages, ModelNoticeKind.Warning);

        if (!string.IsNullOrWhiteSpace(model.WarningText?.DataRetention))
        {
            notices.Add(new ModelNotice(ModelNoticeKind.Required, model.WarningText.DataRetention, "data_retention"));
        }

        if (model.Policy is { } policy
            && policy.State == Rpc.ModelPolicyState.Unconfigured
            && !string.IsNullOrWhiteSpace(policy.Terms))
        {
            notices.Add(new ModelNotice(ModelNoticeKind.Info, policy.Terms, "policy_terms"));
        }

        return notices;

        void Add(IList<Rpc.ModelMessage>? messages, ModelNoticeKind kind)
        {
            notices.AddRange(
                (messages ?? [])
                    .Where(message => !string.IsNullOrWhiteSpace(message?.Message))
                    .Select(message => new ModelNotice(kind, message.Message, message.Code)));
        }
    }

    /// <summary>
    ///     Collects the service's details an application may want to show.
    /// </summary>
    /// <param name="model">The service's model.</param>
    /// <param name="limits">The model's limits, if reported.</param>
    /// <returns>The details, or <see langword="null"/> when there are none.</returns>
    private static AdditionalPropertiesDictionary? Properties(Rpc.Model model, Rpc.ModelCapabilitiesLimits? limits)
    {
        var properties = new AdditionalPropertiesDictionary();
        if (limits?.MaxPromptTokens is { } prompt)
        {
            properties["max_prompt_tokens"] = prompt;
        }

        if (limits?.Vision is { } vision)
        {
            properties["vision_max_prompt_images"] = vision.MaxPromptImages;
            properties["vision_max_prompt_image_size"] = vision.MaxPromptImageSize;
            if (vision.SupportedMediaTypes is { Count: > 0 } types)
            {
                properties["vision_supported_media_types"] = Array.AsReadOnly(types.ToArray());
            }
        }

        if (!string.IsNullOrWhiteSpace(model.Policy?.Terms))
        {
            properties["policy_terms"] = model.Policy.Terms;
        }

        return properties.Count > 0 ? properties : null;
    }

    /// <summary>
    ///     Narrows a token count the service reports to the range a window can hold.
    /// </summary>
    /// <param name="tokens">The count, if reported.</param>
    /// <returns>The count, or <see langword="null"/> when it is missing or not positive.</returns>
    private static int? Tokens(long? tokens) =>
        tokens is > 0 ? (int)Math.Min(tokens.Value, int.MaxValue) : null;
}

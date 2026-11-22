using System.Globalization;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     The choices a user makes about the model an agent runs on: which model, and how it is
///     configured.
/// </summary>
/// <remarks>
///     <para>
///     <b>What an application persists.</b> The record is plain data — strings and numbers — so it
///     serializes as it stands. It names choices by the identifiers a <see cref="ModelInfo"/>
///     publishes, which is what lets a choice saved yesterday be checked against what the provider
///     offers today.
///     </para>
///     <para>
///     <b>Validated before use.</b> Provider factories accept a <see cref="ModelSelection"/>,
///     which only <see cref="Bind"/> and <see cref="ModelCatalogExtensions.SelectAsync"/> create, so
///     settings that name a withdrawn model or an effort the model no longer accepts are refused before a
///     session starts. <see cref="Validate"/> reports the same problems without throwing, for a picker that
///     wants to explain them as the user chooses.
///     </para>
///     <para>
///     <b>The requirement is not part of the settings.</b> What capabilities the application needs
///     is the application's policy, not the user's choice, so it is passed to validation rather than
///     stored here; a persisted choice therefore cannot carry a weaker requirement than the
///     application now has.
///     </para>
///     <para>
///     Instances are immutable and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Validating a choice as the user makes it, and binding it when the session starts.
///     </para>
///     <code>
///     public ModelSelection? Choose(ModelInfo picked, string? effort, string? tier)
///     {
///         var settings = new ModelSettings(picked.Id)
///         {
///             ReasoningEffort = effort,
///             ContextTier = tier,
///         };
///
///         var problems = settings.Validate(picked, ModelCapabilities.Vision);
///         if (problems.Count > 0)
///         {
///             Console.WriteLine(string.Join(Environment.NewLine, problems));
///             return null;
///         }
///
///         return settings.Bind(picked, ModelCapabilities.Vision);
///     }
///     </code>
/// </example>
public sealed record ModelSettings
{
    /// <summary>
    ///     The model identifier, held behind <see cref="ModelId"/> so its validation also applies to a
    ///     copy made with a <see langword="with"/> expression.
    /// </summary>
    private readonly string _modelId = string.Empty;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelSettings"/> class for a model.
    /// </summary>
    /// <param name="modelId">The model identifier. Must not be <see langword="null"/> or blank.</param>
    /// <exception cref="ArgumentException"><paramref name="modelId"/> is <see langword="null"/> or blank.</exception>
    public ModelSettings(string modelId)
    {
        ModelId = modelId;
    }

    /// <summary>
    ///     Gets the identifier of the chosen model, as <see cref="ModelInfo.Id"/> names it.
    /// </summary>
    /// <exception cref="ArgumentException">Set to <see langword="null"/> or a blank value.</exception>
    public string ModelId
    {
        get => _modelId;
        init => _modelId = !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("A model id is required.", nameof(ModelId));
    }

    /// <summary>
    ///     Gets the identifier of the chosen reasoning effort, from
    ///     <see cref="ModelInfo.ReasoningEfforts"/>, or <see langword="null"/> to leave it to the
    ///     provider.
    /// </summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>
    ///     Gets the identifier of the chosen context tier, from <see cref="ModelInfo.ContextTiers"/>,
    ///     or <see langword="null"/> to leave it to the provider.
    /// </summary>
    public string? ContextTier { get; init; }

    /// <summary>
    ///     Gets the chosen context window in tokens, or <see langword="null"/> to leave it to the
    ///     provider.
    /// </summary>
    /// <remarks>
    ///     What the figure does depends on the path that receives it. On Ollama it is the exact length
    ///     the server is asked to run the model at, applied by a client that asks for it on every
    ///     request — a selection carrying a window over an <c>IChatClient</c> is refused unless such a
    ///     client applies it. On Copilot it is a ceiling that only lowers the window a session
    ///     accounts against, applied by the provider-session factory; the plain Copilot agent and
    ///     the summarizer account against no window and refuse it. It may not exceed what the model
    ///     provides.
    /// </remarks>
    public int? ContextWindowTokens { get; init; }

    /// <summary>
    ///     Gets the chosen maximum number of output tokens per response, or <see langword="null"/> to
    ///     leave it to the provider.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    ///     Creates the settings a picker starts from for a model: the model with the provider's own
    ///     defaults.
    /// </summary>
    /// <remarks>
    ///     The default effort and tier are taken from the model, and are <see langword="null"/> where
    ///     the provider publishes none — nothing is invented. No window or output limit is set,
    ///     because a provider's defaults for those are already what a session gets by saying nothing.
    /// </remarks>
    /// <param name="model">The model. Must not be <see langword="null"/>.</param>
    /// <returns>Settings naming the model and its published defaults.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The model's identifier is blank.</exception>
    public static ModelSettings DefaultsFor(ModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new ModelSettings(model.Id)
        {
            ReasoningEffort = model.DefaultReasoningEffort,
            ContextTier = model.DefaultContextTier,
        };
    }

    /// <summary>
    ///     Lists every reason these settings cannot be applied to a model, without throwing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Every rule is checked and every problem reported, so a user fixes them all at once rather
    ///     than one per attempt. Each message is written to be shown verbatim.
    ///     </para>
    ///     <para>
    ///     The rules: the settings name this model; the model is usable; it reports every required
    ///     capability; a chosen effort or tier is one the model offers; a chosen window or output limit
    ///     is positive, is a setting the model accepts, and does not exceed what the model provides. A
    ///     window is held to the chosen tier's window, else the default tier's, else the model's
    ///     largest window, whichever is known first; an unknown limit is not enforced.
    ///     </para>
    /// </remarks>
    /// <param name="model">The model to check against. Must not be <see langword="null"/>.</param>
    /// <param name="required">The capabilities the application requires of the model.</param>
    /// <returns>The problems found, as a read-only list; empty when the settings are valid.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<string> Validate(ModelInfo model, ModelCapabilities required = ModelCapabilities.None)
    {
        ArgumentNullException.ThrowIfNull(model);

        var problems = new List<string>();

        // The settings must be about this model, the model must be runnable, and it must do what
        // the application needs.
        if (!string.Equals(ModelId, model.Id, StringComparison.Ordinal))
        {
            problems.Add($"Settings name model '{ModelId}' but were checked against model '{model.Id}'.");
        }

        if (!model.IsUsable)
        {
            problems.Add($"Model '{model.Id}' cannot be used: {model.UnusableReason ?? "the provider reports it as unavailable."}");
        }

        var missing = required & ~model.Capabilities;
        if (missing != ModelCapabilities.None)
        {
            problems.Add($"Model '{model.Id}' does not support {Describe(missing)}, which this application requires.");
        }

        // Each adjustment must be one the model accepts, with a value it offers.
        ValidateEffort(model, problems);
        ValidateTier(model, problems);
        ValidateWindow(model, problems);
        ValidateMaxOutput(model, problems);

        // Read-only, so the result cannot be cast back to a list and altered.
        return problems.AsReadOnly();
    }

    /// <summary>
    ///     Validates these settings against a model and produces the selection a provider factory
    ///     accepts.
    /// </summary>
    /// <param name="model">The model to apply the settings to. Must not be <see langword="null"/>.</param>
    /// <param name="required">The capabilities the application requires of the model.</param>
    /// <returns>The validated selection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    /// <exception cref="ModelSettingsException">
    ///     The settings are not valid for the model; <see cref="ModelSettingsException.Problems"/>
    ///     lists every reason, as <see cref="Validate"/> would.
    /// </exception>
    public ModelSelection Bind(ModelInfo model, ModelCapabilities required = ModelCapabilities.None)
    {
        var problems = Validate(model, required);
        if (problems.Count > 0)
        {
            throw new ModelSettingsException(ModelId, problems);
        }

        var effort = FindEffort(model, ReasoningEffort);

        return new ModelSelection(model, this, required, effort?.Standard, TierWindow(model, ContextTier));
    }

    /// <summary>
    ///     Finds a model's effort by identifier, ignoring case.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="id">The identifier, or <see langword="null"/>.</param>
    /// <returns>The effort, or <see langword="null"/> when none matches.</returns>
    private static ModelReasoningEffort? FindEffort(ModelInfo model, string? id) =>
        id is null
            ? null
            : model.ReasoningEfforts.FirstOrDefault(effort => string.Equals(effort.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Finds a model's tier by identifier, ignoring case.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="id">The identifier, or <see langword="null"/>.</param>
    /// <returns>The tier, or <see langword="null"/> when none matches.</returns>
    private static ModelContextTier? FindTier(ModelInfo model, string? id) =>
        id is null
            ? null
            : model.ContextTiers.FirstOrDefault(tier => string.Equals(tier.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Returns the window of the chosen tier, or of the default tier when none is chosen or the
    ///     chosen one publishes none.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="chosen">The chosen tier identifier, or <see langword="null"/>.</param>
    /// <returns>The tier window in tokens, or <see langword="null"/> when no tier publishes one.</returns>
    private static int? TierWindow(ModelInfo model, string? chosen) =>
        FindTier(model, chosen)?.WindowTokens ?? FindTier(model, model.DefaultContextTier)?.WindowTokens;

    /// <summary>
    ///     Names a set of capabilities in words a user reads.
    /// </summary>
    /// <param name="capabilities">The capabilities; not <see cref="ModelCapabilities.None"/>.</param>
    /// <returns>The capabilities, joined with "and".</returns>
    private static string Describe(ModelCapabilities capabilities)
    {
        var names = new List<string>();
        if (capabilities.HasFlag(ModelCapabilities.Vision))
        {
            names.Add("vision");
        }

        if (capabilities.HasFlag(ModelCapabilities.ToolCalls))
        {
            names.Add("tool calling");
        }

        if (capabilities.HasFlag(ModelCapabilities.Reasoning))
        {
            names.Add("reasoning");
        }

        // A member added later and not named above is still reported rather than lost.
        var unnamed = capabilities & ~(ModelCapabilities.Vision | ModelCapabilities.ToolCalls | ModelCapabilities.Reasoning);
        if (unnamed != ModelCapabilities.None)
        {
            names.Add(unnamed.ToString());
        }

        return string.Join(" and ", names);
    }

    /// <summary>
    ///     Formats a token count with group separators, independent of the machine's culture.
    /// </summary>
    /// <param name="tokens">The count.</param>
    /// <returns>The count, for example <c>262,144</c>.</returns>
    private static string Tokens(int tokens) => tokens.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    ///     Checks the chosen reasoning effort, when there is one.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="problems">Receives any problem found.</param>
    private void ValidateEffort(ModelInfo model, List<string> problems)
    {
        if (ReasoningEffort is null)
        {
            return;
        }

        var efforts = model.ReasoningEfforts;
        if (!model.Adjustments.HasFlag(ModelAdjustments.ReasoningEffort) || efforts.Count == 0)
        {
            problems.Add($"Model '{model.Id}' does not support reasoning effort settings.");
        }
        else if (FindEffort(model, ReasoningEffort) is null)
        {
            problems.Add(
                $"Model '{model.Id}' does not support reasoning effort '{ReasoningEffort}'. "
                + $"Supported: {string.Join(", ", efforts.Select(effort => effort.Id))}.");
        }
    }

    /// <summary>
    ///     Checks the chosen context tier, when there is one.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="problems">Receives any problem found.</param>
    private void ValidateTier(ModelInfo model, List<string> problems)
    {
        if (ContextTier is null)
        {
            return;
        }

        var tiers = model.ContextTiers;
        if (!model.Adjustments.HasFlag(ModelAdjustments.ContextTier) || tiers.Count == 0)
        {
            problems.Add($"Model '{model.Id}' offers no context tiers.");
        }
        else if (FindTier(model, ContextTier) is null)
        {
            problems.Add(
                $"Model '{model.Id}' does not offer context tier '{ContextTier}'. "
                + $"Offered: {string.Join(", ", tiers.Select(tier => tier.Id))}.");
        }
    }

    /// <summary>
    ///     Checks the chosen context window, when there is one, against what the model provides.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="problems">Receives any problem found.</param>
    private void ValidateWindow(ModelInfo model, List<string> problems)
    {
        if (ContextWindowTokens is not { } window)
        {
            return;
        }

        if (window <= 0)
        {
            problems.Add("A context window must be a positive number of tokens.");
            return;
        }

        if (!model.Adjustments.HasFlag(ModelAdjustments.ContextWindow))
        {
            problems.Add($"Model '{model.Id}' does not allow setting the context window.");
            return;
        }

        // A tier that publishes its window bounds the setting - the chosen tier's, else the default
        // tier's - and otherwise the model's largest window does. An unknown limit is not enforced,
        // because nothing could be said about it.
        var tier = new[] { FindTier(model, ContextTier), FindTier(model, model.DefaultContextTier) }
            .FirstOrDefault(candidate => candidate?.WindowTokens is not null);
        if (tier?.WindowTokens is { } tierWindow)
        {
            if (window > tierWindow)
            {
                problems.Add(
                    $"Model '{model.Id}' provides at most {Tokens(tierWindow)} tokens in context tier '{tier.Id}'; "
                    + $"a {Tokens(window)}-token window cannot be honored.");
            }
        }
        else if (model.ContextWindowTokens is { } limit && window > limit)
        {
            problems.Add(
                $"Model '{model.Id}' supports at most {Tokens(limit)} tokens; "
                + $"a {Tokens(window)}-token window is not supported.");
        }
    }

    /// <summary>
    ///     Checks the chosen output limit, when there is one, against what the model provides.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="problems">Receives any problem found.</param>
    private void ValidateMaxOutput(ModelInfo model, List<string> problems)
    {
        if (MaxOutputTokens is not { } maximum)
        {
            return;
        }

        if (maximum <= 0)
        {
            problems.Add("A maximum output must be a positive number of tokens.");
        }
        else if (!model.Adjustments.HasFlag(ModelAdjustments.MaxOutputTokens))
        {
            problems.Add($"Model '{model.Id}' does not allow setting the maximum output.");
        }
        else if (model.MaxOutputTokens is { } limit && maximum > limit)
        {
            problems.Add($"Model '{model.Id}' allows at most {Tokens(limit)} output tokens.");
        }
    }
}

/// <summary>
///     Model settings validated against the model they name: the form in which provider factories
///     accept model settings.
/// </summary>
/// <remarks>
///     <para>
///     Created only by <see cref="ModelSettings.Bind"/> and
///     <see cref="ModelCatalogExtensions.SelectAsync"/>, so holding one shows the settings were
///     valid for the model, and the model reported every capability the application required, when
///     it was made. A factory that receives one applies it once, when a session starts, and refuses
///     with an <see cref="ArgumentException"/> naming the setting any setting it cannot apply; the
///     settings do not change for the life of that session.
///     </para>
///     <para>
///     Instances are immutable and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class ModelSelection
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelSelection"/> class from settings already
    ///     validated against the model.
    /// </summary>
    /// <param name="model">The model the settings were validated against.</param>
    /// <param name="settings">The validated settings.</param>
    /// <param name="required">The capabilities the application required.</param>
    /// <param name="standardReasoningEffort">The provider-neutral equivalent of the chosen effort, if any.</param>
    /// <param name="contextTierWindowTokens">The window of the chosen or default tier, if known.</param>
    internal ModelSelection(
        ModelInfo model,
        ModelSettings settings,
        ModelCapabilities required,
        ReasoningEffort? standardReasoningEffort,
        int? contextTierWindowTokens)
    {
        Model = model;
        Settings = settings;
        Required = required;
        StandardReasoningEffort = standardReasoningEffort;
        ContextTierWindowTokens = contextTierWindowTokens;
    }

    /// <summary>
    ///     Gets the model the settings apply to.
    /// </summary>
    public ModelInfo Model { get; }

    /// <summary>
    ///     Gets the validated settings.
    /// </summary>
    public ModelSettings Settings { get; }

    /// <summary>
    ///     Gets the capabilities the application required when the selection was made.
    /// </summary>
    public ModelCapabilities Required { get; }

    /// <summary>
    ///     Gets the host capabilities the selected model makes available, for
    ///     <see cref="ToolPackBuilder.WithHostCapabilities"/>.
    /// </summary>
    /// <remarks>
    ///     <see cref="HostCapabilities.Vision"/> when the model accepts images, so the image tool
    ///     family is offered only to a model that can see what it returns. See
    ///     <see cref="ModelCapabilitiesExtensions.ToHostCapabilities"/>.
    /// </remarks>
    public HostCapabilities HostCapabilities => Model.Capabilities.ToHostCapabilities();

    /// <summary>
    ///     Gets the provider-neutral equivalent of the chosen reasoning effort, or
    ///     <see langword="null"/> when no effort was chosen or the provider's level has none.
    /// </summary>
    public ReasoningEffort? StandardReasoningEffort { get; }

    /// <summary>
    ///     Gets the window of the chosen context tier — or of the default tier when none was chosen —
    ///     in tokens, or <see langword="null"/> when no tier publishes one.
    /// </summary>
    public int? ContextTierWindowTokens { get; }
}

/// <summary>
///     The exception thrown when model settings cannot be applied, listing every reason.
/// </summary>
/// <remarks>
///     Derived from <see cref="InvalidOperationException"/> because the settings are well-formed but
///     wrong for the model as it now is. <see cref="Problems"/> holds each reason separately, each
///     suitable to show verbatim.
/// </remarks>
public sealed class ModelSettingsException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelSettingsException"/> class.
    /// </summary>
    /// <param name="modelId">The model the settings named.</param>
    /// <param name="problems">Every reason the settings cannot be applied.</param>
    public ModelSettingsException(string modelId, IReadOnlyList<string> problems)
        : base($"Model settings for '{modelId}' are not valid: {string.Join(" ", problems ?? [])}")
    {
        ModelId = modelId;
        Problems = Array.AsReadOnly<string>([.. problems ?? []]);
    }

    /// <summary>
    ///     Gets the model the settings named.
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    ///     Gets every reason the settings cannot be applied, each suitable to show verbatim.
    /// </summary>
    /// <remarks>
    ///     A read-only copy of the list given to the constructor.
    /// </remarks>
    public IReadOnlyList<string> Problems { get; }
}

using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     What a model can do, as its provider reports it.
/// </summary>
/// <remarks>
///     <para>
///     <b>A flag is set only on a positive signal.</b> A provider that does not say whether a
///     model can see an image has not said that it can, so the flag stays clear and an application
///     that requires vision does not offer the model. Being conservative is the safe direction: a
///     model wrongly withheld costs a user one choice, while a model wrongly offered describes an
///     image it never received and nothing reports an error. A catalog departs from this only
///     where its provider documents otherwise, and says so in its own remarks — the Copilot catalog
///     treats an unreported tool-calling capability as supported, because the service reports none
///     for any model and its runtime is itself a tool-calling agent runtime.
///     </para>
///     <para>
///     Members are added only when an application needs to filter on one. Each is zero or a single
///     bit, so the flags combine and a requirement such as <c>Vision | ToolCalls</c> is satisfied
///     only by a model reporting both.
///     </para>
/// </remarks>
[Flags]
public enum ModelCapabilities
{
    /// <summary>
    ///     No capability is required or reported.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The model calls tools through the provider's canonical tool-calling channel.
    /// </summary>
    /// <remarks>
    ///     Every AgentKit agent is a tool-using agent, so a model without this cannot run one.
    /// </remarks>
    ToolCalls = 1,

    /// <summary>
    ///     The model accepts image input.
    /// </summary>
    /// <remarks>
    ///     This is what <see cref="ModelCapabilitiesExtensions.ToHostCapabilities"/> turns into
    ///     <see cref="HostCapabilities.Vision"/>, which gates the image tool family.
    /// </remarks>
    Vision = 2,

    /// <summary>
    ///     The model thinks before answering, or accepts a reasoning-effort setting.
    /// </summary>
    Reasoning = 4,
}

/// <summary>
///     Which <see cref="ModelSettings"/> fields a provider can honor for a particular model.
/// </summary>
/// <remarks>
///     A settings field the provider cannot honor is refused by validation rather than silently
///     dropped, because a user who chose a reasoning effort or a window and was given something else
///     would have no way to find out. A picker hides the controls whose flag is clear.
/// </remarks>
[Flags]
public enum ModelAdjustments
{
    /// <summary>
    ///     No setting beyond the model itself can be honored.
    /// </summary>
    None = 0,

    /// <summary>
    ///     A reasoning effort from <see cref="ModelInfo.ReasoningEfforts"/> can be chosen.
    /// </summary>
    ReasoningEffort = 1,

    /// <summary>
    ///     A context tier from <see cref="ModelInfo.ContextTiers"/> can be chosen.
    /// </summary>
    ContextTier = 2,

    /// <summary>
    ///     A context window in tokens can be stated.
    /// </summary>
    /// <remarks>
    ///     What the figure means depends on the provider and the path. On Ollama it is the exact
    ///     length the server is asked to load the model at; over an <c>IChatClient</c> a client
    ///     beneath must apply it, reporting so as an <see cref="IContextWindowChatClient"/>. On
    ///     Copilot it is a ceiling that only lowers the window a session accounts against, applied by
    ///     the provider-session factory and never sent to the runtime; the plain Copilot agent and
    ///     the summarizer refuse it.
    /// </remarks>
    ContextWindow = 4,

    /// <summary>
    ///     A maximum number of output tokens per response can be stated.
    /// </summary>
    MaxOutputTokens = 8,
}

/// <summary>
///     The kind of a <see cref="ModelNotice"/>, which decides how prominently an application shows
///     it.
/// </summary>
public enum ModelNoticeKind
{
    /// <summary>
    ///     Informational text a picker may show, for example in a tooltip.
    /// </summary>
    Info,

    /// <summary>
    ///     A caution the user should see before choosing the model.
    /// </summary>
    Warning,

    /// <summary>
    ///     Text the provider says must be shown to the user, such as a data-retention statement.
    /// </summary>
    Required,
}

/// <summary>
///     A reasoning effort a model accepts.
/// </summary>
/// <remarks>
///     The identity is the provider's own string, because the provider-neutral
///     <see cref="Microsoft.Extensions.AI.ReasoningEffort"/> scale cannot represent every level a
///     provider offers — Copilot's <c>max</c> and <c>minimal</c> have no equivalent there.
///     <see cref="Standard"/> carries the neutral equivalent when one exists, which is what an
///     <see cref="IChatClient"/> pipeline applies.
/// </remarks>
/// <param name="Id">The provider's identifier for the effort, stored in <see cref="ModelSettings.ReasoningEffort"/>.</param>
/// <param name="DisplayName">The text a picker shows for the effort.</param>
/// <param name="Standard">
///     The equivalent provider-neutral effort, or <see langword="null"/> when the provider's level
///     has none.
/// </param>
public sealed record ModelReasoningEffort(string Id, string DisplayName, ReasoningEffort? Standard);

/// <summary>
///     A context tier a model offers: a named window size the provider bills and serves separately.
/// </summary>
/// <param name="Id">The provider's identifier for the tier, stored in <see cref="ModelSettings.ContextTier"/>.</param>
/// <param name="DisplayName">The text a picker shows for the tier.</param>
/// <param name="WindowTokens">
///     The total context window the tier provides, in tokens, or <see langword="null"/> when the
///     provider does not publish it.
/// </param>
public sealed record ModelContextTier(string Id, string DisplayName, int? WindowTokens);

/// <summary>
///     A message about a model that an application should show its user.
/// </summary>
/// <param name="Kind">How prominently to show it.</param>
/// <param name="Message">The text, written by the provider or by AgentKit, suitable to show verbatim.</param>
/// <param name="Code">The provider's machine-readable code for the message, if it supplied one.</param>
public sealed record ModelNotice(ModelNoticeKind Kind, string Message, string? Code = null);

/// <summary>
///     One model a provider offers, described in provider-neutral terms an application can filter
///     on, show in a picker, and validate settings against.
/// </summary>
/// <remarks>
///     <para>
///     <b>Produced by a catalog, but deliberately an ordinary record.</b> An
///     <see cref="IModelCatalog"/> builds these from what its provider reports. An application on a
///     provider no catalog serves — any <see cref="IChatClient"/> — can build one by hand, and then
///     validates settings and builds a <see cref="ModelSelection"/> exactly as it would against a
///     catalog's.
///     </para>
///     <para>
///     <b>Capability flags are set only on a positive signal</b>, except where a provider documents
///     otherwise; see <see cref="ModelCapabilities"/>.
///     </para>
///     <para>
///     <b>Each list is copied when set and exposed read-only</b>, and so is
///     <see cref="AdditionalProperties"/>, so changing a collection the description was built from
///     does not change the description, and a list it exposes cannot be cast back and altered. A
///     <see langword="with"/> expression copies any list it replaces. A <see langword="null"/> list
///     is taken as empty; a list holding a <see langword="null"/> entry is refused.
///     </para>
///     <para>
///     Instances are immutable and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Describing a model for a provider no catalog serves, and checking settings against it.
///     </para>
///     <code>
///     var model = new ModelInfo
///     {
///         Provider = "my-endpoint",
///         Id = "vision-large",
///         DisplayName = "Vision Large",
///         Capabilities = ModelCapabilities.ToolCalls | ModelCapabilities.Vision,
///         Adjustments = ModelAdjustments.ReasoningEffort,
///         ReasoningEfforts =
///         [
///             new ModelReasoningEffort("low", "Low", ReasoningEffort.Low),
///             new ModelReasoningEffort("high", "High", ReasoningEffort.High),
///         ],
///     };
///
///     var settings = new ModelSettings(model.Id) { ReasoningEffort = "high" };
///     var problems = settings.Validate(model, ModelCapabilities.Vision);
///     Console.WriteLine(problems.Count == 0 ? "valid" : string.Join(Environment.NewLine, problems));
///     </code>
/// </example>
public sealed record ModelInfo
{
    /// <summary>
    ///     The owned, read-only copy behind <see cref="ReasoningEfforts"/>.
    /// </summary>
    private readonly IReadOnlyList<ModelReasoningEffort> _reasoningEfforts = [];

    /// <summary>
    ///     The owned, read-only copy behind <see cref="ContextTiers"/>.
    /// </summary>
    private readonly IReadOnlyList<ModelContextTier> _contextTiers = [];

    /// <summary>
    ///     The owned, read-only copy behind <see cref="Notices"/>.
    /// </summary>
    private readonly IReadOnlyList<ModelNotice> _notices = [];

    /// <summary>
    ///     The owned, read-only copy behind <see cref="AdditionalProperties"/>.
    /// </summary>
    private readonly IReadOnlyDictionary<string, object?>? _additionalProperties;

    /// <summary>
    ///     Gets the provider that offers the model, for example <c>copilot</c> or <c>ollama</c>.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    ///     Gets the provider's identifier for the model, which <see cref="ModelSettings.ModelId"/>
    ///     names.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    ///     Gets the name a picker shows for the model.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    ///     Gets the organization that publishes the model, when the provider reports one.
    /// </summary>
    public string? Vendor { get; init; }

    /// <summary>
    ///     Gets what the model can do, as its provider reports it.
    /// </summary>
    public ModelCapabilities Capabilities { get; init; }

    /// <summary>
    ///     Gets which <see cref="ModelSettings"/> fields the provider can honor for this model.
    /// </summary>
    public ModelAdjustments Adjustments { get; init; }

    /// <summary>
    ///     Gets a value indicating whether AgentKit can run an agent on this model.
    /// </summary>
    /// <remarks>
    ///     A catalog lists an unusable model only when asked to, so a picker can show it grayed out
    ///     with <see cref="UnusableReason"/>; settings naming it fail validation.
    /// </remarks>
    public bool IsUsable { get; init; } = true;

    /// <summary>
    ///     Gets why the model cannot be used, suitable to show verbatim, or <see langword="null"/>
    ///     when it can.
    /// </summary>
    public string? UnusableReason { get; init; }

    /// <summary>
    ///     Gets the largest context window the provider reports for the model, in tokens, or
    ///     <see langword="null"/> when it reports none.
    /// </summary>
    /// <remarks>
    ///     A ceiling, never a window to account against: on Ollama it is the length the model was
    ///     trained for, which the running instance is usually far below, and on Copilot it is the
    ///     model's maximum across its tiers. A stated window above it is refused.
    /// </remarks>
    public int? ContextWindowTokens { get; init; }

    /// <summary>
    ///     Gets the most output tokens one response can carry, or <see langword="null"/> when the
    ///     provider reports no limit.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    ///     Gets the reasoning efforts the model accepts, in the order a picker should show them.
    /// </summary>
    /// <remarks>
    ///     Copied when set and exposed read-only. A <see langword="null"/> list is taken as empty.
    /// </remarks>
    /// <exception cref="ArgumentException">The list set holds a <see langword="null"/> entry.</exception>
    public IReadOnlyList<ModelReasoningEffort> ReasoningEfforts
    {
        get => _reasoningEfforts;
        init => _reasoningEfforts = Own(value, nameof(ReasoningEfforts));
    }

    /// <summary>
    ///     Gets the identifier of the effort the provider applies when none is chosen, or
    ///     <see langword="null"/> when the provider does not say.
    /// </summary>
    /// <remarks>
    ///     Never invented: a provider that publishes no default leaves the choice to the model, and
    ///     an application that sends nothing gets exactly that.
    /// </remarks>
    public string? DefaultReasoningEffort { get; init; }

    /// <summary>
    ///     Gets the context tiers the model offers, in the order a picker should show them.
    /// </summary>
    /// <remarks>
    ///     Copied when set and exposed read-only. A <see langword="null"/> list is taken as empty.
    /// </remarks>
    /// <exception cref="ArgumentException">The list set holds a <see langword="null"/> entry.</exception>
    public IReadOnlyList<ModelContextTier> ContextTiers
    {
        get => _contextTiers;
        init => _contextTiers = Own(value, nameof(ContextTiers));
    }

    /// <summary>
    ///     Gets the identifier of the tier the provider applies when none is chosen, or
    ///     <see langword="null"/> when the model offers no tiers.
    /// </summary>
    public string? DefaultContextTier { get; init; }

    /// <summary>
    ///     Gets the provider's billing multiplier for the model, or <see langword="null"/> when it
    ///     publishes none.
    /// </summary>
    public double? BillingMultiplier { get; init; }

    /// <summary>
    ///     Gets the provider's grouping for a picker, for example <c>lightweight</c> or
    ///     <c>powerful</c>, or <see langword="null"/> when it publishes none.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    ///     Gets the provider's price band for a picker badge, or <see langword="null"/> when it
    ///     publishes none.
    /// </summary>
    public string? PriceCategory { get; init; }

    /// <summary>
    ///     Gets the messages about the model an application should show its user.
    /// </summary>
    /// <remarks>
    ///     Copied when set and exposed read-only. A <see langword="null"/> list is taken as empty.
    /// </remarks>
    /// <exception cref="ArgumentException">The list set holds a <see langword="null"/> entry.</exception>
    public IReadOnlyList<ModelNotice> Notices
    {
        get => _notices;
        init => _notices = Own(value, nameof(Notices));
    }

    /// <summary>
    ///     Gets provider-specific extras the neutral fields do not carry, or <see langword="null"/>
    ///     when there are none.
    /// </summary>
    /// <remarks>
    ///     Each catalog documents the keys it writes. Nothing in AgentKit reads them; they exist for
    ///     an application that wants to show more than the neutral fields hold. The entries are copied
    ///     when set into a dictionary whose keys compare ordinally ignoring case, as an
    ///     <see cref="AdditionalPropertiesDictionary"/>'s do, and exposed read-only; a catalog may pass
    ///     the <see cref="AdditionalPropertiesDictionary"/> it built. The values are not copied, so a
    ///     catalog stores only immutable values, such as numbers, strings and read-only lists.
    /// </remarks>
    /// <exception cref="ArgumentException">
    ///     The dictionary set holds two keys that differ only in case.
    /// </exception>
    public IReadOnlyDictionary<string, object?>? AdditionalProperties
    {
        get => _additionalProperties;
        init => _additionalProperties = value is null
            ? null
            : new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(value, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Tests whether the model reports every capability in a requirement.
    /// </summary>
    /// <param name="required">The capabilities required. <see cref="ModelCapabilities.None"/> is satisfied by every model.</param>
    /// <returns><see langword="true"/> when every required flag is set on <see cref="Capabilities"/>.</returns>
    public bool Supports(ModelCapabilities required) => (Capabilities & required) == required;

    /// <summary>
    ///     Copies a list set on the description into an array exposed read-only.
    /// </summary>
    /// <typeparam name="T">The entry type.</typeparam>
    /// <param name="value">The list set, or <see langword="null"/> for an empty one.</param>
    /// <param name="name">The property's name, reported when an entry is <see langword="null"/>.</param>
    /// <returns>A read-only view over the copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> holds a <see langword="null"/> entry.</exception>
    private static IReadOnlyList<T> Own<T>(IEnumerable<T>? value, string name)
        where T : class
    {
        T[] copy = [.. value ?? []];
        if (Array.Exists(copy, item => item is null))
        {
            throw new ArgumentException($"{name} must not contain a null entry.", name);
        }

        return Array.AsReadOnly(copy);
    }
}

/// <summary>
///     Conversions from model capabilities to the vocabulary tool packs are gated on.
/// </summary>
public static class ModelCapabilitiesExtensions
{
    /// <summary>
    ///     Derives the host capabilities a model makes available.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Only <see cref="ModelCapabilities.Vision"/> maps, to <see cref="HostCapabilities.Vision"/>.
    ///     <see cref="HostCapabilities.Delegation"/> stays the application's to declare, because
    ///     whether a second agent can be started depends on the application's provider and
    ///     credentials rather than on the model.
    ///     </para>
    ///     <para>
    ///     The result is the model's half of the host's declaration. A host passes it to
    ///     <see cref="ToolPackBuilder.WithHostCapabilities"/>, combined with anything else it
    ///     provides; the explicit declaration is retained, so a host may still narrow or widen it.
    ///     </para>
    /// </remarks>
    /// <param name="capabilities">The model's capabilities.</param>
    /// <returns>The host capabilities the model contributes.</returns>
    public static HostCapabilities ToHostCapabilities(this ModelCapabilities capabilities) =>
        (capabilities & ModelCapabilities.Vision) != 0 ? HostCapabilities.Vision : HostCapabilities.None;
}

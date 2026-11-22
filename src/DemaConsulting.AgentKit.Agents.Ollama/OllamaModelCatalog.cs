using System.Globalization;
using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using ModelInfo = DemaConsulting.AgentKit.Core.ModelInfo;

namespace DemaConsulting.AgentKit.Agents.Ollama;

/// <summary>
///     The models an Ollama server holds, described in provider-neutral terms an application can
///     filter on, show in a picker, and validate settings against.
/// </summary>
/// <remarks>
///     <para>
///     <b>Where the description comes from.</b> The server's model list names what is installed;
///     each model is then described by the server's own show endpoint, read a few at a time. A model
///     the server will not describe is still listed — unusable, saying so — rather than failing the
///     whole listing.
///     </para>
///     <para>
///     <b>Capabilities are the server's.</b> Ollama reports what a model can do — completion,
///     vision, tools, thinking — and that report is used as it stands, including its absence of a
///     capability. A model is usable when it reports both completion and tools, because every
///     AgentKit agent calls tools. A server too old to report capabilities at all (before Ollama
///     0.6.4) has vision inferred from the model's projector or its architecture's vision keys, is
///     marked unusable because tool support cannot be confirmed, and carries a notice saying so.
///     </para>
///     <para>
///     <b>The trained length is a ceiling, never a window.</b>
///     <see cref="Core.ModelInfo.ContextWindowTokens"/> is the context length the model file
///     publishes, which is what a stated window is checked against and refused above. It is never a
///     window to account against: the running instance is usually far below it, which is why
///     <see cref="OllamaContextWindow"/> never reads it.
///     </para>
///     <para>
///     <b>Efforts.</b> A thinking model offers <c>none</c>, <c>low</c>, <c>medium</c> and
///     <c>high</c>, applied as <see cref="ChatOptions.Reasoning"/>, which OllamaSharp sends as
///     Ollama's <c>think</c>. A GPT-OSS model cannot stop thinking, so it is not offered
///     <c>none</c>. No default is published: a thinking model thinks unless told otherwise. A level
///     is a hint the model interprets; Ollama does not validate it.
///     </para>
///     <para>
///     <b>Identifiers.</b> Models are identified by the name the server lists, tagged, and sorted
///     by it. A lookup by a bare name finds the <c>latest</c> tag, as the server itself resolves it.
///     </para>
///     <para>
///     <b>Additional properties</b> written: <c>family</c>, <c>parameter_size</c>,
///     <c>quantization_level</c> and <c>size_bytes</c>, where the server reports them.
///     </para>
///     <para>
///     <b>Failures.</b> A listing that cannot reach the server fails with the client's exception:
///     <see cref="System.Net.Http.HttpRequestException"/> when the server is unreachable, or
///     <see cref="TaskCanceledException"/> (an <see cref="OperationCanceledException"/>) when the
///     <see cref="System.Net.Http.HttpClient"/> times out. The failure is not cached. A model whose
///     description fails, including a timed-out description, is listed as unusable instead.
///     </para>
///     <para>
///     Cached, single-flight and safe for concurrent use; see <see cref="CachingModelCatalog"/>.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public async Task ListVisionModelsAsync(CancellationToken cancellationToken)
///     {
///         var client = new OllamaApiClient(new Uri("http://localhost:11434"));
///         var catalog = new OllamaModelCatalog(client);
///
///         var models = await catalog.ListAsync(
///             new ModelQuery { Require = ModelCapabilities.Vision | ModelCapabilities.ToolCalls },
///             cancellationToken);
///
///         foreach (var model in models)
///         {
///             Console.WriteLine($"{model.Id}: trained for {model.ContextWindowTokens} tokens");
///         }
///     }
///     </code>
/// </example>
public sealed class OllamaModelCatalog : CachingModelCatalog
{
    /// <summary>
    ///     The provider name every model this catalog describes carries.
    /// </summary>
    public const string ProviderId = "ollama";

    /// <summary>
    ///     How many models are described at once.
    /// </summary>
    /// <remarks>
    ///     Enough to keep a listing of a dozen models quick, few enough not to crowd a server that is
    ///     also serving a conversation.
    /// </remarks>
    private const int DescribeParallelism = 4;

    /// <summary>
    ///     The client the server is read through.
    /// </summary>
    private readonly IOllamaApiClient _client;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OllamaModelCatalog"/> class.
    /// </summary>
    /// <param name="client">
    ///     The client to read the server through. Must not be <see langword="null"/>. Its selected
    ///     model is not used and not changed.
    /// </param>
    /// <param name="lifetime">How long a listing is kept, or <see langword="null"/> for five minutes.</param>
    /// <param name="timeProvider">The clock, or <see langword="null"/> for the system clock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not positive.</exception>
    public OllamaModelCatalog(IOllamaApiClient client, TimeSpan? lifetime = null, TimeProvider? timeProvider = null)
        : base(ProviderId, lifetime, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    /// <summary>
    ///     Describes one model from what the server listed and what it showed.
    /// </summary>
    /// <remarks>
    ///     A pure function, so every mapping rule can be exercised on captured payloads without a
    ///     server.
    /// </remarks>
    /// <param name="listed">The model as the server listed it.</param>
    /// <param name="shown">The server's description of it, or <see langword="null"/> when it gave none.</param>
    /// <returns>The model, described.</returns>
    internal static ModelInfo Map(Model listed, ShowModelResponse? shown)
    {
        var id = listed.Name;
        var properties = Properties(listed);

        // A model the server would not describe is listed, but cannot be offered.
        if (shown is null)
        {
            return new ModelInfo
            {
                Provider = ProviderId,
                Id = id,
                DisplayName = id,
                IsUsable = false,
                UnusableReason = "Ollama did not describe this model.",
                AdditionalProperties = properties,
            };
        }

        var architecture = shown.Info?.Architecture;
        var extra = shown.Info?.ExtraInfo;
        var notices = new List<ModelNotice>();

        // Capabilities: the server's report where it gave one, otherwise inferred.
        ModelCapabilities capabilities;
        string? unusable;
        if (shown.Capabilities is { } reported)
        {
            capabilities = CapabilitiesFrom(reported);
            unusable = UsabilityFrom(reported);
        }
        else
        {
            capabilities = InferVision(shown, architecture) ? ModelCapabilities.Vision : ModelCapabilities.None;
            unusable = "Ollama 0.6.4 or later is required to confirm tool support.";
            notices.Add(new ModelNotice(
                ModelNoticeKind.Info,
                "Ollama did not report capabilities (server older than 0.6.4); capabilities were inferred."));
        }

        var efforts = capabilities.HasFlag(ModelCapabilities.Reasoning)
            ? EffortsFor(listed.Details?.Family ?? shown.Details?.Family, architecture)
            : [];

        var adjustments = ModelAdjustments.ContextWindow | ModelAdjustments.MaxOutputTokens;
        if (efforts.Count > 0)
        {
            adjustments |= ModelAdjustments.ReasoningEffort;
        }

        return new ModelInfo
        {
            Provider = ProviderId,
            Id = id,
            DisplayName = id,
            Capabilities = capabilities,
            Adjustments = adjustments,
            IsUsable = unusable is null,
            UnusableReason = unusable,
            ContextWindowTokens = TrainedLength(extra, architecture),
            ReasoningEfforts = efforts,
            Notices = notices,
            AdditionalProperties = properties,
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Lists the installed models, then describes each one, a few at a time. A failure to list
    ///     fails the read; a failure to describe one model costs only that model. Performs network
    ///     I/O.
    /// </remarks>
    protected override async Task<IReadOnlyList<ModelInfo>> FetchAsync(CancellationToken cancellationToken)
    {
        var listed = (await _client.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false) ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model?.Name))
            .ToArray();

        var described = new ModelInfo[listed.Length];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, listed.Length),
            new ParallelOptions { MaxDegreeOfParallelism = DescribeParallelism, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var shown = await TryShowAsync(listed[index].Name, token).ConfigureAwait(false);
                described[index] = Map(listed[index], shown);
            }).ConfigureAwait(false);

        return [.. described.OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     A bare name and its <c>latest</c> tag name the same model, as the server resolves them.
    /// </remarks>
    protected override bool IdEquals(string requested, string catalogId) =>
        string.Equals(
            OllamaContextWindow.Tagged(requested),
            OllamaContextWindow.Tagged(catalogId),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Asks the server to describe one model, treating any failure but cancellation as no answer.
    /// </summary>
    /// <param name="model">The model name.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The description, or <see langword="null"/> when the server gave none.</returns>
    private async Task<ShowModelResponse?> TryShowAsync(string model, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ShowModelAsync(model, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // One model the server cannot describe - removed mid-listing, damaged, or refused - is
            // listed as unusable rather than taking the whole listing down with it.
            return null;
        }
    }

    /// <summary>
    ///     Maps the server's capability strings to capability flags.
    /// </summary>
    /// <param name="reported">The strings the server reported.</param>
    /// <returns>The flags; a capability the server did not report is not set.</returns>
    private static ModelCapabilities CapabilitiesFrom(string[] reported)
    {
        var capabilities = ModelCapabilities.None;
        if (Has(reported, "tools"))
        {
            capabilities |= ModelCapabilities.ToolCalls;
        }

        if (Has(reported, "vision"))
        {
            capabilities |= ModelCapabilities.Vision;
        }

        if (Has(reported, "thinking"))
        {
            capabilities |= ModelCapabilities.Reasoning;
        }

        return capabilities;
    }

    /// <summary>
    ///     Decides from the server's capability strings whether an agent can run on the model.
    /// </summary>
    /// <param name="reported">The strings the server reported.</param>
    /// <returns>The reason it cannot, or <see langword="null"/> when it can.</returns>
    private static string? UsabilityFrom(string[] reported)
    {
        if (!Has(reported, "completion"))
        {
            return Has(reported, "embedding")
                ? "embedding-only model"
                : "Model does not support chat completion.";
        }

        return Has(reported, "tools") ? null : "Model does not support tool calling.";
    }

    /// <summary>
    ///     Infers vision for a server too old to report capabilities.
    /// </summary>
    /// <remarks>
    ///     A projector alone misses models whose newer engine embeds vision in the model itself, so
    ///     the architecture's vision keys count too.
    /// </remarks>
    /// <param name="shown">The server's description.</param>
    /// <param name="architecture">The model's architecture, if reported.</param>
    /// <returns><see langword="true"/> when either signal is present.</returns>
    private static bool InferVision(ShowModelResponse shown, string? architecture)
    {
        if (shown.Projector is not null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(architecture) || shown.Info?.ExtraInfo is not { } extra)
        {
            return false;
        }

        var prefix = architecture + ".vision.";
        return extra.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Reads the context length the model was trained for.
    /// </summary>
    /// <param name="extra">The model's metadata, if reported.</param>
    /// <param name="architecture">The model's architecture, if reported.</param>
    /// <returns>The length in tokens, or <see langword="null"/> when the metadata does not carry it.</returns>
    private static int? TrainedLength(IDictionary<string, object>? extra, string? architecture)
    {
        if (extra is null
            || string.IsNullOrEmpty(architecture)
            || !extra.TryGetValue(architecture + ".context_length", out var value))
        {
            return null;
        }

        // The metadata dictionary is deserialized loosely, so the number may arrive in any of the
        // shapes a JSON reader produces.
        long? tokens = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.String } element when long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            long number => number,
            int number => number,
            double number => (long)number,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

        return tokens is > 0 and <= int.MaxValue ? (int)tokens.Value : null;
    }

    /// <summary>
    ///     Builds the efforts a thinking model accepts.
    /// </summary>
    /// <param name="family">The model's family, if reported.</param>
    /// <param name="architecture">The model's architecture, if reported.</param>
    /// <returns>The efforts, in picker order.</returns>
    private static IReadOnlyList<ModelReasoningEffort> EffortsFor(string? family, string? architecture)
    {
        ModelReasoningEffort[] levels =
        [
            new("low", "Low", ReasoningEffort.Low),
            new("medium", "Medium", ReasoningEffort.Medium),
            new("high", "High", ReasoningEffort.High),
        ];

        // GPT-OSS cannot stop thinking, so turning it off is not offered.
        var alwaysThinks = IsGptOss(family) || IsGptOss(architecture);
        return alwaysThinks ? levels : [new ModelReasoningEffort("none", "Off", ReasoningEffort.None), .. levels];

        static bool IsGptOss(string? name) => string.Equals(name, "gptoss", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Collects the listed details an application may want to show.
    /// </summary>
    /// <param name="listed">The model as the server listed it.</param>
    /// <returns>The details, or <see langword="null"/> when there are none.</returns>
    private static AdditionalPropertiesDictionary? Properties(Model listed)
    {
        var properties = new AdditionalPropertiesDictionary();
        if (!string.IsNullOrEmpty(listed.Details?.Family))
        {
            properties["family"] = listed.Details.Family;
        }

        if (!string.IsNullOrEmpty(listed.Details?.ParameterSize))
        {
            properties["parameter_size"] = listed.Details.ParameterSize;
        }

        if (!string.IsNullOrEmpty(listed.Details?.QuantizationLevel))
        {
            properties["quantization_level"] = listed.Details.QuantizationLevel;
        }

        if (listed.Size > 0)
        {
            properties["size_bytes"] = listed.Size;
        }

        return properties.Count > 0 ? properties : null;
    }

    /// <summary>
    ///     Tests whether the server reported a capability.
    /// </summary>
    /// <param name="reported">The strings the server reported.</param>
    /// <param name="capability">The capability.</param>
    /// <returns><see langword="true"/> when it was reported.</returns>
    private static bool Has(string[] reported, string capability) =>
        reported.Contains(capability, StringComparer.OrdinalIgnoreCase);
}

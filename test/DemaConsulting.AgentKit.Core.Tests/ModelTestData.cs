using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Hand-built models and catalog doubles shared by the model-selection tests.
/// </summary>
/// <remarks>
///     The models are built by hand, exactly as an application on a provider no catalog serves
///     would build them, so the Core rules are exercised without any provider's mapping in the way.
/// </remarks>
internal static class ModelTestData
{
    /// <summary>
    ///     A usable vision model that accepts every adjustment, with two tiers and three efforts.
    /// </summary>
    /// <param name="id">The model identifier.</param>
    /// <returns>The model.</returns>
    public static ModelInfo VisionModel(string id = "vision-large") => new()
    {
        Provider = "test",
        Id = id,
        DisplayName = "Vision Large",
        Capabilities = ModelCapabilities.ToolCalls | ModelCapabilities.Vision | ModelCapabilities.Reasoning,
        Adjustments = ModelAdjustments.ReasoningEffort
            | ModelAdjustments.ContextTier
            | ModelAdjustments.ContextWindow
            | ModelAdjustments.MaxOutputTokens,
        ContextWindowTokens = 1_000_000,
        MaxOutputTokens = 64_000,
        ReasoningEfforts =
        [
            new ModelReasoningEffort("low", "Low", ReasoningEffort.Low),
            new ModelReasoningEffort("high", "High", ReasoningEffort.High),
            new ModelReasoningEffort("max", "Maximum", null),
        ],
        DefaultReasoningEffort = "low",
        ContextTiers =
        [
            new ModelContextTier("default", "Standard", 128_000),
            new ModelContextTier("long_context", "Long context", 1_000_000),
        ],
        DefaultContextTier = "default",
    };

    /// <summary>
    ///     A usable text-only model that accepts no adjustment beyond a window, with a known maximum.
    /// </summary>
    /// <param name="id">The model identifier.</param>
    /// <returns>The model.</returns>
    public static ModelInfo TextModel(string id = "text-small") => new()
    {
        Provider = "test",
        Id = id,
        DisplayName = "Text Small",
        Capabilities = ModelCapabilities.ToolCalls,
        Adjustments = ModelAdjustments.ContextWindow,
        ContextWindowTokens = 32_768,
    };

    /// <summary>
    ///     A model AgentKit cannot run.
    /// </summary>
    /// <param name="id">The model identifier.</param>
    /// <returns>The model.</returns>
    public static ModelInfo UnusableModel(string id = "embedder") => new()
    {
        Provider = "test",
        Id = id,
        DisplayName = "Embedder",
        IsUsable = false,
        UnusableReason = "embedding-only model",
    };
}

/// <summary>
///     A clock a test moves by hand, so a catalog's lifetime can be crossed without waiting.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    /// <summary>
    ///     The current time.
    /// </summary>
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    ///     Moves the clock forward.
    /// </summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
///     A catalog whose read is scripted by the test and counted, so caching and single-flight can be
///     observed from outside.
/// </summary>
internal sealed class ScriptedModelCatalog : CachingModelCatalog
{
    /// <summary>
    ///     The scripted read.
    /// </summary>
    private readonly Func<int, CancellationToken, Task<IReadOnlyList<ModelInfo>>> _read;

    /// <summary>
    ///     The number of reads started.
    /// </summary>
    private int _reads;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ScriptedModelCatalog"/> class.
    /// </summary>
    /// <param name="read">The read, handed the one-based number of the read being made.</param>
    /// <param name="timeProvider">The clock, or <see langword="null"/> for the system clock.</param>
    /// <param name="lifetime">The cache lifetime, or <see langword="null"/> for the default.</param>
    public ScriptedModelCatalog(
        Func<int, CancellationToken, Task<IReadOnlyList<ModelInfo>>> read,
        TimeProvider? timeProvider = null,
        TimeSpan? lifetime = null)
        : base("test", lifetime, timeProvider)
    {
        _read = read;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ScriptedModelCatalog"/> class that always
    ///     returns the same models.
    /// </summary>
    /// <param name="models">The models every read returns.</param>
    /// <param name="timeProvider">The clock, or <see langword="null"/> for the system clock.</param>
    public ScriptedModelCatalog(IReadOnlyList<ModelInfo> models, TimeProvider? timeProvider = null)
        : this((_, _) => Task.FromResult(models), timeProvider)
    {
    }

    /// <summary>
    ///     Gets the number of reads started.
    /// </summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <inheritdoc/>
    protected override Task<IReadOnlyList<ModelInfo>> FetchAsync(CancellationToken cancellationToken) =>
        _read(Interlocked.Increment(ref _reads), cancellationToken);
}

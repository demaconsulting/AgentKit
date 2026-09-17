namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     A summarizer that contacts no model: it computes its answer from the request and records
///     every request it was given.
/// </summary>
/// <remarks>
///     <para>
///     This is what makes the rotation engine testable at all. The engine is a pure function of the
///     layout it is handed and the summarizer it is injected with, so replacing the one
///     non-deterministic collaborator with a deterministic one makes the whole rotation
///     reproducible: the same layout always produces the same tiers and the same consolidation
///     count.
///     </para>
///     <para>
///     Recording the requests matters as much as producing the answers. The design rules that
///     cannot be observed from the resulting layout alone — that a rotation consolidates a span of
///     history into one slot, that a full tier consolidates its slots as peers, which aggressiveness
///     clause was handed over — are visible in the request sequence.
///     </para>
/// </remarks>
internal sealed class FakeSummarizer : ISummarizer
{
    /// <summary>
    ///     Computes the consolidated record for a request.
    /// </summary>
    private readonly Func<ConsolidationRequest, string?> _responder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeSummarizer"/> class that compresses its
    ///     input by a fixed ratio.
    /// </summary>
    /// <param name="compressionRatio">
    ///     The fraction of the material length the answer occupies. Must be positive.
    /// </param>
    public FakeSummarizer(double compressionRatio = 0.25)
        : this(request => Compress(request, compressionRatio))
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeSummarizer"/> class with a supplied
    ///     responder.
    /// </summary>
    /// <param name="responder">Computes the consolidated record for a request.</param>
    public FakeSummarizer(Func<ConsolidationRequest, string?> responder) => _responder = responder;

    /// <summary>
    ///     Creates a summarizer whose every answer occupies exactly the requested number of tokens,
    ///     regardless of its input.
    /// </summary>
    /// <remarks>
    ///     Models the measured reality that a summarizer's output lands at its natural length rather
    ///     than a size it was asked for. A test that wants a slot of a known size uses this.
    /// </remarks>
    /// <param name="tokens">The tokens every answer occupies.</param>
    /// <returns>A summarizer producing answers of a fixed size.</returns>
    public static FakeSummarizer Fixed(int tokens) =>
        new(_ => new string('s', tokens * TokenEstimator.CharactersPerToken));

    /// <summary>
    ///     Creates a summarizer that returns more than it was given, for proving rule 5 terminates
    ///     even when consolidation buys nothing.
    /// </summary>
    /// <returns>A summarizer whose answer is longer than its material.</returns>
    public static FakeSummarizer Expanding() =>
        new(request => request.Material + request.Material);

    /// <summary>
    ///     Creates a summarizer that echoes its material unchanged.
    /// </summary>
    /// <returns>A summarizer whose answer is its material.</returns>
    public static FakeSummarizer Echoing() => new(request => request.Material);

    /// <summary>
    ///     Creates a summarizer that returns a blank answer, which the engine must normalize to
    ///     empty.
    /// </summary>
    /// <returns>A summarizer whose answer is whitespace.</returns>
    public static FakeSummarizer Blank() => new(_ => "   ");

    /// <summary>
    ///     Creates a summarizer that returns null, which the engine must refuse.
    /// </summary>
    /// <returns>A summarizer whose answer is null.</returns>
    public static FakeSummarizer Null() => new(_ => null);

    /// <summary>
    ///     Gets every request this summarizer was given, in order.
    /// </summary>
    public List<ConsolidationRequest> Requests { get; } = [];

    /// <summary>
    ///     Gets how many consolidations this summarizer performed.
    /// </summary>
    public int CallCount => Requests.Count;

    /// <summary>
    ///     Gets the tier index and aggressiveness clause of each request, in order.
    /// </summary>
    public IReadOnlyList<(int Tier, string Instruction)> Shape =>
        [.. Requests.Select(request => (request.TierIndex, request.Instruction))];

    /// <inheritdoc/>
    public Task<string> ConsolidateAsync(
        ConsolidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(request);
        return Task.FromResult(_responder(request)!);
    }

    /// <summary>
    ///     Produces a record of a fixed fraction of the material length, labeled so a test can tell
    ///     which tier produced it.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="ratio">The fraction of the material length the answer occupies.</param>
    /// <returns>The labeled, deterministically sized record.</returns>
    private static string Compress(ConsolidationRequest request, double ratio)
    {
        var label = $"[T{request.TierIndex}]";
        var target = Math.Max(label.Length, (int)(request.Material.Length * ratio));

        return label.PadRight(target, '.');
    }
}

/// <summary>
///     A summarizer that ignores the cancellation token it is handed, and records how many
///     consolidations it was asked for.
/// </summary>
/// <remarks>
///     <b>Contract-conformant, and that is the point.</b> <see cref="ISummarizer"/> documents only
///     that an implementation <em>may</em> throw on cancellation, so an implementation that never
///     looks at the token is within its rights. The rotation engine therefore cannot delegate the
///     check, and this proves it does not.
/// </remarks>
/// <param name="responder">Computes the consolidated record for a request.</param>
internal sealed class InattentiveSummarizer(Func<ConsolidationRequest, string> responder) : ISummarizer
{
    /// <summary>
    ///     Gets how many consolidations this summarizer was asked to perform.
    /// </summary>
    public int CallCount { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     Deliberately never inspects <paramref name="cancellationToken"/>.
    /// </remarks>
    public Task<string> ConsolidateAsync(
        ConsolidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CallCount++;
        return Task.FromResult(responder(request));
    }
}

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
///     reproducible: the same layout always produces the same tiers, the same consolidation count,
///     and the same saturation reports.
///     </para>
///     <para>
///     Recording the requests matters as much as producing the answers. The design rules that
///     cannot be observed from the resulting layout alone — that a consolidation receives the
///     previous record as an input, that a cascade degrades the older record rather than the newer
///     material, that only overflowing tiers are consolidated — are all visible in the request
///     sequence and nowhere else.
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
    /// <remarks>
    ///     A ratio rather than a fixed size, so that a test which grows its transcript sees the
    ///     tiers grow proportionately, the way a real consolidation would.
    /// </remarks>
    /// <param name="compressionRatio">
    ///     The fraction of the combined input length the answer occupies. Must be positive.
    /// </param>
    public FakeSummarizer(double compressionRatio = 0.25)
        : this(request => Compress(request, compressionRatio))
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeSummarizer"/> class with a supplied
    ///     responder.
    /// </summary>
    /// <remarks>
    ///     Used by the tests that need a specific pathological shape: an answer that expands rather
    ///     than compresses, an answer that echoes its input, or a null answer.
    /// </remarks>
    /// <param name="responder">Computes the consolidated record for a request.</param>
    public FakeSummarizer(Func<ConsolidationRequest, string?> responder)
    {
        _responder = responder;
    }

    /// <summary>
    ///     Gets every request this summarizer was given, in order.
    /// </summary>
    public List<ConsolidationRequest> Requests { get; } = [];

    /// <summary>
    ///     Gets how many consolidations this summarizer performed.
    /// </summary>
    public int CallCount => Requests.Count;

    /// <summary>
    ///     Gets the tier index and degradation flag of each request, in order.
    /// </summary>
    /// <remarks>
    ///     The single most useful projection for asserting cascade behavior: a cascade is visible as
    ///     a merge at one tier, a degradation at the next coarser tier, then a fresh recording at the
    ///     first.
    /// </remarks>
    public IReadOnlyList<(int Tier, bool Degradation)> Shape =>
        [.. Requests.Select(request => (request.TierIndex, request.IsDegradation))];

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
    ///     Produces a record of a fixed fraction of the combined input length, labeled so a test can
    ///     tell which tier produced it and whether it carried a previous record forward.
    /// </summary>
    /// <param name="request">The request to answer.</param>
    /// <param name="ratio">The fraction of the combined input length the answer occupies.</param>
    /// <returns>The labeled, deterministically sized record.</returns>
    private static string Compress(ConsolidationRequest request, double ratio)
    {
        var inputLength = (request.PreviousRecord?.Length ?? 0) + request.Material.Length;
        var label = $"[T{request.TierIndex}{(request.IsDegradation ? "D" : "M")}]";
        var target = Math.Max(label.Length, (int)(inputLength * ratio));

        return label.PadRight(target, '.');
    }
}

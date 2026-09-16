namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     One unit of work handed to a summarizer: the previous record for some material, the new
///     material to fold into it, and which tier the result belongs to.
/// </summary>
/// <remarks>
///     <para>
///     <b>Consolidation is a ratchet, and this request is how the ratchet is expressed.</b>
///     <see cref="PreviousRecord"/> is the record a previous consolidation produced for the same
///     stretch of history. It is an input, not context: the result must incorporate it and must not
///     drop detail it kept, unless the request is a deliberate degradation to a coarser tier. A
///     summarizer that ignored it would re-summarize a summary, which is the downward ratchet that
///     makes a flat rolling summary forget everything beyond a handful of rotations.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class ConsolidationRequest
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ConsolidationRequest"/> class.
    /// </summary>
    /// <param name="tierIndex">
    ///     The tier the result belongs to. Must be one or greater: tier zero is verbatim history
    ///     and is never consolidated into.
    /// </param>
    /// <param name="previousRecord">
    ///     The record a previous consolidation produced for this tier, or <see langword="null"/> or
    ///     empty when this is the first consolidation into it.
    /// </param>
    /// <param name="material">
    ///     The new material to fold in, already rendered as labeled text. Must not be
    ///     <see langword="null"/> or blank — there is nothing to consolidate otherwise.
    /// </param>
    /// <param name="budgetTokens">
    ///     The tier's token budget, supplied for information only. Must be positive.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="tierIndex"/> is less than one, or <paramref name="budgetTokens"/> is not
    ///     positive.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="material"/> is <see langword="null"/> or blank.</exception>
    public ConsolidationRequest(int tierIndex, string? previousRecord, string material, int budgetTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tierIndex, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetTokens);

        if (string.IsNullOrWhiteSpace(material))
        {
            throw new ArgumentException("Consolidation material must not be blank.", nameof(material));
        }

        TierIndex = tierIndex;
        PreviousRecord = previousRecord;
        Material = material;
        BudgetTokens = budgetTokens;
    }

    /// <summary>
    ///     Gets the tier the result belongs to, counting the verbatim tier as zero.
    /// </summary>
    /// <remarks>
    ///     Higher means coarser. A summarizer may use this to choose how much detail to keep, but
    ///     must not use it as a length instruction — see <see cref="BudgetTokens"/>.
    /// </remarks>
    public int TierIndex { get; }

    /// <summary>
    ///     Gets the record a previous consolidation produced for this tier, or
    ///     <see langword="null"/> when there is none.
    /// </summary>
    public string? PreviousRecord { get; }

    /// <summary>
    ///     Gets the new material to fold in, rendered as labeled transcript text.
    /// </summary>
    public string Material { get; }

    /// <summary>
    ///     Gets the tier's token budget, for information only.
    /// </summary>
    /// <remarks>
    ///     <b>This is not an instruction to the model, and must never be turned into one.</b>
    ///     Asking a model to hit a token count does not work: in the compaction spike that preceded
    ///     this package, consolidations asked for between 9,870 and 19,741 tokens returned 1,665
    ///     and 4,259 tokens (n = 2 requests, recorded in that spike). A model cannot count its own
    ///     output. The engine therefore prompts for specificity and content, measures the result
    ///     itself, and treats output size as a signal about how much information the material
    ///     carried rather than as something to be dictated. The budget is exposed so a summarizer
    ///     implementation can log or reason about it, not so it can be pasted into a prompt.
    /// </remarks>
    public int BudgetTokens { get; }

    /// <summary>
    ///     Gets a value indicating whether this consolidation deliberately degrades material to a
    ///     coarser tier rather than extending an existing record.
    /// </summary>
    /// <remarks>
    ///     True when there is no previous record to incorporate. The ratchet rule — never drop
    ///     detail an earlier consolidation kept — applies only when a previous record exists, so
    ///     this is what distinguishes a legitimate coarsening from an accidental loss.
    ///     <para>
    ///     A blank record counts as none, matching <see cref="ContextTier.IsEmpty"/> and the
    ///     refusal of blank <see cref="Material"/> above. Whitespace holds no detail to carry
    ///     forward, so presenting it to a summarizer as a record to preserve would ask for the
    ///     impossible.
    ///     </para>
    /// </remarks>
    public bool IsDegradation => string.IsNullOrWhiteSpace(PreviousRecord);
}

/// <summary>
///     Consolidates older session history into a tier record, out of session.
/// </summary>
/// <remarks>
///     <para>
///     <b>Injected rather than fixed, for one decisive reason: the rotation engine must be testable
///     without a model.</b> A test supplies a deterministic fake and gets a rotation engine that is
///     a pure function of its inputs; production supplies an implementation backed by a model.
///     Nothing in the engine knows or cares which it has.
///     </para>
///     <para>
///     <b>Implementations must be stateless and must run out of session.</b> The material arrives
///     as an argument precisely so that consolidation does not happen inside the live session being
///     compacted — asking a session to summarize itself spends that session's own context on the
///     summary and provokes the provider's built-in compactor, which defeats the purpose.
///     </para>
///     <para>
///     Implementations must be safe for concurrent use, because an application may run more than
///     one session against the same summarizer.
///     </para>
/// </remarks>
public interface ISummarizer
{
    /// <summary>
    ///     Produces the tier record that incorporates the previous record and the new material.
    /// </summary>
    /// <remarks>
    ///     The result must incorporate <see cref="ConsolidationRequest.PreviousRecord"/> when there
    ///     is one, must not drop detail that record kept, and must not interpret, speculate about,
    ///     or comment on material that is not present. The engine measures the result's size
    ///     itself; an implementation is not asked, and must not be asked, to hit a length.
    /// </remarks>
    /// <param name="request">The material to consolidate. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the consolidation.</param>
    /// <returns>
    ///     The consolidated record. Must not be <see langword="null"/>; an implementation with
    ///     nothing to say returns an empty string rather than null.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<string> ConsolidateAsync(ConsolidationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
///     The consolidation prompt this library recommends, and the composition of a request into it.
/// </summary>
/// <remarks>
///     <para>
///     Published as a documented default rather than buried inside an implementation, so an
///     application can read exactly what its summarizer is being asked to do, and can replace it
///     with wording suited to its own domain. Nothing in the engine calls this: a summarizer
///     implementation does, if it wants to.
///     </para>
///     <para>
///     <b>The prompt asks for content, never for a size.</b> Every clause names a category of
///     information that must survive — paths, values, decisions and the reasons for them,
///     constraints, errors and how they were resolved, and what is still outstanding. Size is
///     allowed to vary with how much of that the material actually contained, and that variation is
///     signal rather than noise.
///     </para>
///     <para>
///     This class is static, holds no state, and is safe for concurrent use.
///     </para>
/// </remarks>
public static class ConsolidationPrompt
{
    /// <summary>
    ///     The recommended instruction given to a model performing a consolidation.
    /// </summary>
    /// <remarks>
    ///     Deliberately contains no length, word count, or token count. See
    ///     <see cref="ConsolidationRequest.BudgetTokens"/> for the measurement that settled that.
    /// </remarks>
    public const string Instruction = """
        You are consolidating the working record of an agent session. You are not writing a
        summary for a reader; you are writing the only account of this material that will
        survive, for the agent itself to rely on later.

        Preserve, specifically and by name:
        - every file, path, identifier and location that was touched, and what happened to it
        - every concrete value, setting, number and name that was established
        - every decision that was made, and the reason it was made
        - every constraint, requirement or rule that was discovered or imposed
        - every error encountered and how it was resolved, or that it is unresolved
        - everything still outstanding, and what the next step on it is

        Rules:
        - If a previous record is supplied, it is part of your input. Carry every detail it
          holds into your output. Do not drop anything it kept.
        - Do not interpret, speculate about, or comment on anything not present in the material.
        - Do not editorialize, and do not describe the material; state what it establishes.
        - Write plainly and densely. Length should follow from how much the material contains.
        """;

    /// <summary>
    ///     Composes a request into the full text a model is sent.
    /// </summary>
    /// <remarks>
    ///     Deterministic: the same request always composes to the same string, so a summarizer
    ///     implementation can be tested without a model and a cache can key on the result. The
    ///     previous record is placed before the new material because that is the order the ratchet
    ///     reads in — carry this forward, then fold this in.
    /// </remarks>
    /// <param name="request">The request to compose. Must not be <see langword="null"/>.</param>
    /// <returns>The instruction, the previous record if any, and the new material.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static string Compose(ConsolidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A first consolidation into a tier, or a deliberate degradation from a finer tier, has no
        // previous record to carry forward - say so explicitly rather than presenting an empty
        // section a model might try to fill.
        var previous = request.IsDegradation
            ? "PREVIOUS RECORD: none. This material is being recorded at this level for the first time."
            : $"PREVIOUS RECORD (carry every detail forward):\n{request.PreviousRecord}";

        return $"{Instruction}\n\n{previous}\n\nNEW MATERIAL (fold in):\n{request.Material}";
    }
}

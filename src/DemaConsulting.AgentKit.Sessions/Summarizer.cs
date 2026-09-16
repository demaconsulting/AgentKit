namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     One unit of work handed to a summarizer: the material to consolidate, how aggressively to
///     consolidate it, and which tier the result belongs to.
/// </summary>
/// <remarks>
///     <para>
///     <b>The pieces consolidated together are peers.</b> A rotation gathers a span of history — or
///     a full tier's worth of already-consolidated slots — and hands it over as one body of
///     material to be reduced into a single record. There is no previous record being extended and
///     nothing being folded into an existing account, so the request carries only what to
///     consolidate and how tersely.
///     </para>
///     <para>
///     <b>Terseness is an instruction, never a size.</b> Asking a model to hit a token count does
///     not work — it cannot count its own output — so the request carries a plain-language
///     <see cref="Instruction"/> that says how much to keep, and the session measures the result
///     itself rather than dictating it.
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
    ///     The tier the result belongs to. Must be one or greater: the verbatim tail is tier zero
    ///     and is never consolidated into.
    /// </param>
    /// <param name="material">
    ///     The material to consolidate, already rendered as labeled text. Must not be
    ///     <see langword="null"/> or blank — there is nothing to consolidate otherwise.
    /// </param>
    /// <param name="instruction">
    ///     The aggressiveness clause telling the summarizer how tersely to consolidate. Must not be
    ///     <see langword="null"/> or blank.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tierIndex"/> is less than one.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="material"/> or <paramref name="instruction"/> is <see langword="null"/>
    ///     or blank.
    /// </exception>
    public ConsolidationRequest(int tierIndex, string material, string instruction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tierIndex, 1);

        if (string.IsNullOrWhiteSpace(material))
        {
            throw new ArgumentException("Consolidation material must not be blank.", nameof(material));
        }

        if (string.IsNullOrWhiteSpace(instruction))
        {
            throw new ArgumentException("A consolidation instruction must not be blank.", nameof(instruction));
        }

        TierIndex = tierIndex;
        Material = material;
        Instruction = instruction;
    }

    /// <summary>
    ///     Gets the tier the result belongs to, counting the verbatim tail as tier zero.
    /// </summary>
    /// <remarks>
    ///     Higher means coarser and older. Supplied for information — a summarizer may log it or
    ///     reason about it — but it is not a length instruction; see <see cref="Instruction"/>.
    /// </remarks>
    public int TierIndex { get; }

    /// <summary>
    ///     Gets the material to consolidate, rendered as labeled transcript text.
    /// </summary>
    public string Material { get; }

    /// <summary>
    ///     Gets the aggressiveness clause telling the summarizer how tersely to consolidate.
    /// </summary>
    /// <remarks>
    ///     A plain-language instruction — "Summarize concisely.", "Be terse: decisions, facts and
    ///     open threads only.", or "One or two lines. Essential facts only." — chosen by the
    ///     session's current compaction level. It is never a number handed to the model.
    /// </remarks>
    public string Instruction { get; }
}

/// <summary>
///     Consolidates a span of session history into a single record, out of session.
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
///     <b>The pieces of material are peers.</b> There is no previous record to preserve; the whole
///     of the material is the input, and the summarizer is free to collapse repetition across it,
///     which is what makes a consolidation buy room. What must survive is specific named facts,
///     decisions, errors and outstanding work — not length.
///     </para>
///     <para>
///     Implementations must be safe for concurrent use, because an application may run more than
///     one session against the same summarizer.
///     </para>
/// </remarks>
public interface ISummarizer
{
    /// <summary>
    ///     Produces the record that consolidates the request's material.
    /// </summary>
    /// <remarks>
    ///     The result must preserve specific named facts, decisions, errors and outstanding work,
    ///     may collapse repetition across the material, and must not interpret, speculate about, or
    ///     comment on anything not present. The engine measures the result's size itself; an
    ///     implementation is not asked, and must not be asked, to hit a length.
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
///     The consolidation prompt this library uses, and the composition of a request into it.
/// </summary>
/// <remarks>
///     <para>
///     <b>The prompt asks for content, never for a size.</b> Every clause names a category of
///     information that must survive — paths, values, decisions and the reasons for them,
///     constraints, errors and how they were resolved, and what is still outstanding — and licenses
///     collapsing repetition, which is what a consolidation buys room by. Size is allowed to vary
///     with how much of that the material actually contains.
///     </para>
///     <para>
///     This class is static, holds no state, and is safe for concurrent use.
///     </para>
/// </remarks>
public static class ConsolidationPrompt
{
    /// <summary>
    ///     The base instruction given to a model performing a consolidation.
    /// </summary>
    /// <remarks>
    ///     Deliberately contains no length, word count, or token count. The per-level aggressiveness
    ///     clause is appended to it by <see cref="Compose"/>.
    /// </remarks>
    public const string Instruction = """
        You are consolidating the working record of an agent session. You are not writing a
        summary for a reader; you are writing the only account of this material that will
        survive, for the agent itself to rely on later. The pieces you are given are peers
        covering the same span of history — treat them together as one body of material.

        Preserve, specifically and by name:
        - every file, path, identifier and location that was touched, and what happened to it
        - every concrete value, setting, number and name that was established
        - every decision that was made, and the reason it was made
        - every constraint, requirement or rule that was discovered or imposed
        - every error encountered and how it was resolved, or that it is unresolved
        - everything still outstanding, and what the next step on it is

        Rules:
        - Collapse repetition. Where the same fact, path, value or decision appears more than
          once, state it once. Removing that redundancy is the point of this step.
        - Do not interpret, speculate about, or comment on anything not present in the material.
        - Do not editorialize, and do not describe the material; state what it establishes.
        - Write plainly and densely. Length should follow from how much the material contains.
        """;

    /// <summary>
    ///     The aggressiveness clause a rotation at the low compaction level asks for.
    /// </summary>
    public const string LowInstruction = "Summarize concisely.";

    /// <summary>
    ///     The aggressiveness clause a rotation at the medium compaction level asks for.
    /// </summary>
    public const string MediumInstruction = "Be terse: decisions, facts and open threads only.";

    /// <summary>
    ///     The aggressiveness clause a rotation at the high compaction level asks for.
    /// </summary>
    public const string HighInstruction = "One or two lines. Essential facts only.";

    /// <summary>
    ///     Returns the aggressiveness clause for a compaction level.
    /// </summary>
    /// <param name="level">The compaction level the session is at.</param>
    /// <returns>The plain-language clause telling the summarizer how tersely to consolidate.</returns>
    public static string InstructionFor(CompactionLevel level) => level switch
    {
        CompactionLevel.Medium => MediumInstruction,
        CompactionLevel.High => HighInstruction,
        _ => LowInstruction,
    };

    /// <summary>
    ///     Composes a request into the full text a model is sent.
    /// </summary>
    /// <remarks>
    ///     Deterministic: the same request always composes to the same string, so a summarizer
    ///     implementation can be tested without a model and a cache can key on the result. There is
    ///     no previous-record section, because the material is a set of peers with nothing being
    ///     folded into.
    /// </remarks>
    /// <param name="request">The request to compose. Must not be <see langword="null"/>.</param>
    /// <returns>The base instruction, the aggressiveness clause, and the material.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static string Compose(ConsolidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return $"{Instruction}\n\n{request.Instruction}\n\nMATERIAL:\n{request.Material}";
    }
}

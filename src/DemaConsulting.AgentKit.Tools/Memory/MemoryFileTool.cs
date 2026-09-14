using System.ComponentModel;
using System.Globalization;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     The <c>memory_file</c> tool: stores one memory, unless an existing memory already says
///     nearly the same thing.
/// </summary>
/// <remarks>
///     <para>
///     <b>The descriptor is the only text embedded; the details are the payload.</b> A model files
///     one short sentence saying what the memory is about, and separately the fuller text an answer
///     would need. Findability and sufficiency were measured to be in tension, and splitting them
///     is what lets a recall find the memory on a one-line question and still return enough to
///     answer with.
///     </para>
///     <para>
///     <b>One memory per document or section, not one per extracted fact.</b> Coarser granularity
///     was monotonically better across five spike configurations: fine-grained extraction split one
///     answer across several memories, and a top-k recall then returned incoherent partials. The
///     tool's description says so to the model, because the model is the one choosing the
///     granularity.
///     </para>
///     <para>
///     <b>Near-duplicate detection is arithmetic, and it happens before anything is stored.</b> The
///     new descriptor's vector is compared against the vectors already held — which are already
///     computed, so the check is nearly free — and a nearest match at or above the author's
///     configured threshold means the memory is not stored. Every attempt during the spike to have
///     the <em>model</em> notice a conflict by judgement failed; the arithmetic succeeded, catching
///     a 12-psi versus 18-psi contradiction at 0.965 cosine.
///     </para>
///     <para>
///     <b>That comparison covers one call, and is not atomic against another call in flight.</b>
///     A call searches the store and then adds to it, so two calls filing at the same time can both
///     search before either adds, and both then store. This is deliberately not closed here:
///     <see cref="IMemoryStore"/> already requires implementations to be safe for concurrent use,
///     and the window lies between two operations rather than inside either, so removing it would
///     mean imposing an atomic check-and-add on whatever persistence the author substituted. The
///     check is a backstop over what one call can see, not a uniqueness guarantee over the store.
///     </para>
///     <para>
///     <b>A near-duplicate is reported as structured data, not as prose.</b> This is a defect fix,
///     not a style choice: when the spike returned a prose sentence explaining that the memory had
///     not been stored, the model went on to assert that it had stored the fact. The result
///     therefore carries an unambiguous <c>stored</c> flag, the conflicting memory's identifier and
///     descriptor, the similarity, and the threshold that was applied — facts the model can act on,
///     and no instruction about what to do next.
///     </para>
///     <para>
///     <b>Storing a memory is not filing a duplicate under protest.</b> The tool has no
///     force-anyway argument. An author who wants near-duplicates stored lowers — or raises — the
///     threshold; a model cannot overrule the control the author set, which is what makes the
///     control worth setting.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the store, generator and options it was given.
///     </para>
/// </remarks>
public static class MemoryFileTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "memory_file";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Stores one memory. Supply a descriptor — a single short sentence saying what the memory "
        + "is about, which is the only text searched — and details, the fuller text a later answer "
        + "would need. File roughly one memory per document or per section rather than one per "
        + "fact. Optionally state the source document and where in it the fact was read. Returns "
        + "whether the memory was stored; a memory whose descriptor is too close to one already "
        + "held is not stored, and the result names that memory and the similarity instead.";

    /// <summary>
    ///     The number of nearest memories examined when deciding whether a descriptor is a
    ///     near-duplicate.
    /// </summary>
    /// <remarks>
    ///     One. The store returns matches nearest first, so if the closest memory is below the
    ///     threshold no other memory can be above it, and fetching more would only cost a longer
    ///     result to discard.
    /// </remarks>
    private const int NearestNeighborCount = 1;

    /// <summary>
    ///     The number of hexadecimal characters of a new identifier's random part.
    /// </summary>
    /// <remarks>
    ///     Eight. Long enough that a collision within one store is not a practical concern, and
    ///     short enough that a model can read an identifier back and quote it in a later call
    ///     without transcription errors — which a full GUID demonstrably produces.
    /// </remarks>
    private const int IdentifierLength = 8;

    /// <summary>
    ///     Creates the <c>memory_file</c> tool over one composition's store.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment. An application
    ///     obtains this tool by attaching <see cref="MemoryPack"/>, which supplies the store, the
    ///     generator and the author's options.
    /// </remarks>
    /// <param name="store">The store this tool writes to.</param>
    /// <param name="generator">The embedding generator the application supplied.</param>
    /// <param name="options">The controls the application author configured.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/>, <paramref name="generator"/> or
    ///     <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(
        IMemoryStore store,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        MemoryOptions options)
    {
        // A missing collaborator is a programming error in the composing family.
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(options);

        // Declared to return Task<object> on purpose; see the remarks on GuardedToolFactory.
        var file = (
                [Description(
                    "A single short sentence saying what this memory is about. This is the only "
                    + "text that is searched, so write it the way a later question would be asked.")]
                string? descriptor = null,
                [Description(
                    "The fuller text a later answer would need: the numbers, names and "
                    + "qualifications. Not searched, so length here costs nothing at search time.")]
                string? details = null,
                [Description("The document this was read from. Optional.")]
                string? sourceDocument = null,
                [Description(
                    "Where in that document it was read — a section, heading or line range. "
                    + "Optional.")]
                string? sourceLocator = null,
                CancellationToken cancellationToken = default) =>
            FileAsync(store, generator, options, descriptor, details, sourceDocument, sourceLocator, cancellationToken);

        return GuardedToolFactory.Create(file, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Stores one memory, refusing rather than throwing whenever the request itself cannot be
    ///     honored.
    /// </summary>
    /// <remarks>
    ///     The order of operations is the guarantee: validate, embed, compare, and only then store.
    ///     Storing first and checking afterwards would leave a near-duplicate in the store on every
    ///     detection.
    /// </remarks>
    /// <param name="store">The store to write to.</param>
    /// <param name="generator">The embedding generator the application supplied.</param>
    /// <param name="options">The controls the application author configured.</param>
    /// <param name="descriptor">The descriptor the model supplied, or null when it supplied none.</param>
    /// <param name="details">The details the model supplied, or null when it supplied none.</param>
    /// <param name="sourceDocument">The source document the model stated, or null for none.</param>
    /// <param name="sourceLocator">The source locator the model stated, or null for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     A structured statement of whether the memory was stored, or a refusal naming its reason.
    /// </returns>
    private static async Task<object> FileAsync(
        IMemoryStore store,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        MemoryOptions options,
        string? descriptor,
        string? details,
        string? sourceDocument,
        string? sourceLocator,
        CancellationToken cancellationToken)
    {
        // Malformed requests are refused rather than thrown; the model supplied them. The refusals
        // state what is missing and nothing about what to do instead.
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "A descriptor is required. It is the only text a later recall searches.");
        }

        if (string.IsNullOrWhiteSpace(details))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "Details are required. A memory holding only a descriptor carries nothing a later "
                + "answer could be built from.");
        }

        var vector = await MemoryEmbedding
            .GenerateAsync(generator, descriptor, cancellationToken)
            .ConfigureAwait(false);

        // The nearest already-stored descriptor decides whether this one is new. Their vectors are
        // already computed, so the comparison costs one scan and no embedding call.
        var nearest = await store
            .SearchAsync(vector, NearestNeighborCount, cancellationToken)
            .ConfigureAwait(false);

        if (nearest.Count > 0 && nearest[0].Similarity >= options.NearDuplicateThreshold)
        {
            return NotStored(nearest[0], options.NearDuplicateThreshold);
        }

        var memory = new MemoryRecord(
            NewIdentifier(),
            descriptor,
            details,
            // A blank source is an absent one: a stored empty string would be reported back on
            // every recall as a source that exists and says nothing.
            string.IsNullOrWhiteSpace(sourceDocument) ? null : sourceDocument,
            string.IsNullOrWhiteSpace(sourceLocator) ? null : sourceLocator,
            vector);

        await store.AddAsync(memory, cancellationToken).ConfigureAwait(false);
        var count = await store.CountAsync(cancellationToken).ConfigureAwait(false);

        // The result states the identifier the model will need to update, revise or forget this
        // memory, and the size of the store, so it never has to recall merely to check its own
        // write.
        return ToolResult.Structured(new
        {
            stored = true,
            memoryId = memory.Id,
            memoryCount = count,
        });
    }

    /// <summary>
    ///     Reports that a memory was not stored because an existing one already says nearly the
    ///     same thing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Structured rather than prose, and deliberately so. A prose sentence reporting the same
    ///     outcome was observed to be followed by the model asserting that it had stored the fact;
    ///     a field named <c>stored</c> holding <see langword="false"/> is not open to that reading.
    ///     </para>
    ///     <para>
    ///     The conflicting memory's identifier and descriptor are included because they are what
    ///     the model would otherwise have to search for, and the similarity and threshold are
    ///     included because they are why this happened. Nothing here tells the model what to do
    ///     next: naming a remedy in a denial has, in this project, been followed by a model
    ///     enthusiastically destroying a file.
    ///     </para>
    /// </remarks>
    /// <param name="conflict">The nearest stored memory and its similarity.</param>
    /// <param name="threshold">The threshold the author configured.</param>
    /// <returns>The structured result.</returns>
    private static object NotStored(MemoryMatch conflict, double threshold)
    {
        return ToolResult.Structured(new
        {
            stored = false,
            reason = "near_duplicate",
            similarity = conflict.Similarity,
            threshold,
            conflictingMemoryId = conflict.Memory.Id,
            conflictingDescriptor = conflict.Memory.Descriptor,
            conflictingDetails = conflict.Memory.Details,
        });
    }

    /// <summary>
    ///     Produces the identifier a new memory is addressed by.
    /// </summary>
    /// <remarks>
    ///     A short readable prefix and eight hexadecimal characters. The identifier is generated
    ///     here rather than chosen by the model, because a model asked to invent unique identifiers
    ///     reuses them, and rather than assigned by the store, because a substituted store must be
    ///     free to be a thin adapter over somebody else's persistence.
    /// </remarks>
    /// <returns>A new identifier, for example <c>mem-1a2b3c4d</c>.</returns>
    private static string NewIdentifier()
    {
        return "mem-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..IdentifierLength];
    }
}

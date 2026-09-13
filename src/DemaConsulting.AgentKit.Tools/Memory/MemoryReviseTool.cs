using System.ComponentModel;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     The <c>memory_revise</c> tool: replaces a memory's descriptor, details and provenance, and
///     re-embeds it so it is found by what it now says.
/// </summary>
/// <remarks>
///     <para>
///     <b>A revision changes what the memory is about, so it pays for a new vector.</b> That is the
///     whole difference between this tool and <c>memory_update</c>: an update corrects the payload
///     of a memory that is still about the same thing and costs no embedding call, while a revision
///     changes the subject and therefore must change how the memory is found. A revision that kept
///     the old vector would leave the memory findable only under its previous meaning.
///     </para>
///     <para>
///     <b>Provenance is a parameter here, and this is a defect fix.</b> The spike version of this
///     tool silently preserved the original source document and locator. A fact revised from a
///     <em>different</em> document therefore kept citing the superseded one — observed in practice
///     with details drawn from <c>02-revision.md</c> sitting beside a structured field still
///     reading <c>01-initial.md</c>. The tool knew the memory had changed and kept data it had
///     every reason to believe was stale, and gave the model no way to correct it. Now it can be
///     stated.
///     </para>
///     <para>
///     <b>A revision sets provenance to exactly what the caller states, including nothing.</b>
///     Omitting the source clears it rather than inheriting the old one. Inheriting silently is the
///     original defect wearing a different hat: the tool would again be asserting a source for text
///     it knows has been rewritten. The result reports the provenance the memory now carries, so
///     what happened is visible rather than assumed.
///     </para>
///     <para>
///     <b>No policy about provenance is baked in.</b> Whether a revised memory should keep its
///     first source, replace it, accumulate both, or be revised at all without corroboration is the
///     application author's instruction to give the agent. This tool's only guarantee is that it
///     never silently keeps data it knows may be wrong.
///     </para>
///     <para>
///     <b>A revision is not near-duplicate checked.</b> The near-duplicate check exists to stop a
///     second memory being filed about a fact already held; a revision names one existing memory
///     and changes it, so there is no second memory to prevent, and checking would refuse the very
///     corrections the family exists to make possible.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the store and generator it was given.
///     </para>
/// </remarks>
public static class MemoryReviseTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "memory_revise";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    /// <remarks>
    ///     The description states plainly that an omitted source clears the source. A model that
    ///     did not know this would omit the argument meaning "unchanged" and get "none", which is a
    ///     surprise the tool has no way to detect and the model no way to discover.
    /// </remarks>
    private const string ToolDescription =
        "Replaces what a memory says and what it is about, and makes it findable by the new "
        + "wording. Supply the memory's id, a new one-sentence descriptor and new details. State "
        + "the source document and locator the revised memory should carry: whatever you state "
        + "replaces what the memory held, and stating nothing leaves it with no source. Use this "
        + "when the subject or the source has changed; to correct details alone, use memory_update.";

    /// <summary>
    ///     Creates the <c>memory_revise</c> tool over one composition's store.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment. An application
    ///     obtains this tool by attaching <see cref="MemoryPack"/>.
    /// </remarks>
    /// <param name="store">The store this tool writes to.</param>
    /// <param name="generator">The embedding generator the application supplied.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/> or <paramref name="generator"/> is
    ///     <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(
        IMemoryStore store,
        IEmbeddingGenerator<string, Embedding<float>> generator)
    {
        // A missing collaborator is a programming error in the composing family.
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(generator);

        // Declared to return Task<object> on purpose; see the remarks on GuardedToolFactory.
        var revise = (
                [Description("The id of the memory to revise, as a recall or a file call reported it.")]
                string? memoryId = null,
                [Description(
                    "The new single short sentence saying what this memory is about. The memory "
                    + "will be found by this wording from now on.")]
                string? descriptor = null,
                [Description("The new details the memory should hold.")]
                string? details = null,
                [Description(
                    "The document the revised memory should cite. Whatever you state replaces the "
                    + "source the memory held; stating nothing leaves it with none.")]
                string? sourceDocument = null,
                [Description(
                    "Where in that document the revised fact was read. Replaces the locator the "
                    + "memory held; stating nothing leaves it with none.")]
                string? sourceLocator = null,
                CancellationToken cancellationToken = default) =>
            ReviseAsync(
                store,
                generator,
                memoryId,
                descriptor,
                details,
                sourceDocument,
                sourceLocator,
                cancellationToken);

        return GuardedToolFactory.Create(revise, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Replaces one memory wholesale, refusing rather than throwing whenever the request cannot
    ///     be honored.
    /// </summary>
    /// <remarks>
    ///     The existing memory is looked up before the descriptor is embedded, so that a revision
    ///     naming an identifier the store does not hold costs no embedding call.
    /// </remarks>
    /// <param name="store">The store to write to.</param>
    /// <param name="generator">The embedding generator the application supplied.</param>
    /// <param name="memoryId">The identifier the model supplied, or null when it supplied none.</param>
    /// <param name="descriptor">The descriptor the model supplied, or null when it supplied none.</param>
    /// <param name="details">The details the model supplied, or null when it supplied none.</param>
    /// <param name="sourceDocument">The source document the model stated, or null for none.</param>
    /// <param name="sourceLocator">The source locator the model stated, or null for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A structured statement of what the memory now holds, or a refusal.</returns>
    private static async Task<object> ReviseAsync(
        IMemoryStore store,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        string? memoryId,
        string? descriptor,
        string? details,
        string? sourceDocument,
        string? sourceLocator,
        CancellationToken cancellationToken)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return MemoryDenials.MissingIdentifier();
        }

        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "A descriptor is required. A revision replaces the sentence the memory is found "
                + "by, so there is nothing to replace it with.");
        }

        if (string.IsNullOrWhiteSpace(details))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "Details are required. A revision replaces everything the memory holds, so a "
                + "memory revised without details would carry nothing.");
        }

        var existing = await store.FindAsync(memoryId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return await MemoryDenials.NotFoundAsync(store, memoryId, cancellationToken).ConfigureAwait(false);
        }

        var vector = await MemoryEmbedding
            .GenerateAsync(generator, descriptor, cancellationToken)
            .ConfigureAwait(false);

        // Everything except the identifier is replaced by what the caller stated. Provenance is
        // taken from the call and not from the memory, which is the defect fix this tool exists
        // for: nothing the tool knows to be possibly stale is carried across silently.
        var revised = new MemoryRecord(
            existing.Id,
            descriptor,
            details,
            // A blank source is an absent one, exactly as it is when a memory is first filed.
            string.IsNullOrWhiteSpace(sourceDocument) ? null : sourceDocument,
            string.IsNullOrWhiteSpace(sourceLocator) ? null : sourceLocator,
            vector);

        await store.ReplaceAsync(revised, cancellationToken).ConfigureAwait(false);

        // The provenance the memory now carries is reported back — including its absence — so the
        // model can see what its own call did to the citation rather than assume. The explicit
        // flag is not redundant: null fields are dropped by JSON serialization, so an absent
        // source would otherwise be reported by the absence of a field, which reads identically
        // to a field the reader simply did not notice.
        return ToolResult.Structured(new
        {
            revised = true,
            memoryId = revised.Id,
            descriptor = revised.Descriptor,
            sourceStated = revised.SourceDocument is not null || revised.SourceLocator is not null,
            sourceDocument = revised.SourceDocument,
            sourceLocator = revised.SourceLocator,
        });
    }
}

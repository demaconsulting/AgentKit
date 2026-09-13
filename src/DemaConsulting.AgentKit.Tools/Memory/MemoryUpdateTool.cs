using System.ComponentModel;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     The <c>memory_update</c> tool: replaces a memory's details, leaving its descriptor and its
///     vector exactly as they were.
/// </summary>
/// <remarks>
///     <para>
///     <b>This tool exists because re-embedding is the expensive half of a correction and most
///     corrections do not need it.</b> When a memory is about the right thing but says the wrong
///     thing — a number transcribed wrongly, a qualification left out — the descriptor is still the
///     sentence a later question would be asked as, and re-embedding it would produce the same
///     vector at the cost of a round trip to the embedding backend.
///     </para>
///     <para>
///     <b>What the memory is <em>about</em> cannot be changed here, and that is the point.</b> An
///     update that silently changed the descriptor without recomputing the vector would leave the
///     memory findable only under its old meaning — findable by the wrong questions, invisible to
///     the right ones, with nothing in the store to show it had happened. A change of subject is
///     <c>memory_revise</c>, which pays for the new vector.
///     </para>
///     <para>
///     <b>Provenance is not touched here either.</b> Details that were corrected from the same
///     source keep that source; details that came from a different source are a revision, because
///     a memory citing a document it no longer reflects is the exact defect this family was built
///     to avoid.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the store it was given.
///     </para>
/// </remarks>
public static class MemoryUpdateTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "memory_update";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Replaces the details of a memory you already have, keeping its descriptor, its source and "
        + "how it is found unchanged. Use this when the memory is about the right thing but says "
        + "the wrong thing. Supply the memory's id and the new details. To change what the memory "
        + "is about, or where it came from, use memory_revise.";

    /// <summary>
    ///     Creates the <c>memory_update</c> tool over one composition's store.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment. An application
    ///     obtains this tool by attaching <see cref="MemoryPack"/>. No embedding generator is taken,
    ///     because this tool never embeds anything — which is the whole of its value.
    /// </remarks>
    /// <param name="store">The store this tool writes to.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(IMemoryStore store)
    {
        // A missing store is a programming error in the composing family.
        ArgumentNullException.ThrowIfNull(store);

        // Declared to return Task<object> on purpose; see the remarks on GuardedToolFactory.
        var update = (
                [Description("The id of the memory to update, as a recall or a file call reported it.")]
                string? memoryId = null,
                [Description("The details to hold in place of the ones the memory currently holds.")]
                string? details = null,
                CancellationToken cancellationToken = default) =>
            UpdateAsync(store, memoryId, details, cancellationToken);

        return GuardedToolFactory.Create(update, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Replaces one memory's details, refusing rather than throwing whenever the request cannot
    ///     be honored.
    /// </summary>
    /// <param name="store">The store to write to.</param>
    /// <param name="memoryId">The identifier the model supplied, or null when it supplied none.</param>
    /// <param name="details">The details the model supplied, or null when it supplied none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A structured statement of what the memory now holds, or a refusal.</returns>
    private static async Task<object> UpdateAsync(
        IMemoryStore store,
        string? memoryId,
        string? details,
        CancellationToken cancellationToken)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return MemoryDenials.MissingIdentifier();
        }

        if (string.IsNullOrWhiteSpace(details))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "Details are required. An update replaces the details a memory holds, so there is "
                + "nothing to replace them with.");
        }

        var existing = await store.FindAsync(memoryId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return await MemoryDenials.NotFoundAsync(store, memoryId, cancellationToken).ConfigureAwait(false);
        }

        // Everything but the details is carried across unchanged, including the vector: the
        // descriptor did not change, so neither did what it embeds to.
        var updated = existing with { Details = details };
        await store.ReplaceAsync(updated, cancellationToken).ConfigureAwait(false);

        // The descriptor and the untouched provenance are echoed back so the model can see what the
        // memory is still filed under and still cites, which are the facts most likely to be wrong
        // in its own head after an update. The explicit flag carries an absent source, which JSON
        // serialization would otherwise report only by omitting a field.
        return ToolResult.Structured(new
        {
            updated = true,
            memoryId = updated.Id,
            descriptor = updated.Descriptor,
            sourceStated = updated.SourceDocument is not null || updated.SourceLocator is not null,
            sourceDocument = updated.SourceDocument,
            sourceLocator = updated.SourceLocator,
        });
    }
}

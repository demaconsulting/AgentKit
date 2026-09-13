using System.ComponentModel;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     The <c>memory_recall</c> tool: finds the memories whose descriptors are closest to a
///     question, and returns each one whole.
/// </summary>
/// <remarks>
///     <para>
///     <b>The search is over descriptors alone.</b> A memory's details were never embedded, so
///     what a recall matches against is the one short sentence the memory was filed under. That is
///     why the file tool's description tells the model to write a descriptor the way a later
///     question would be asked: the two texts meet in vector space and nowhere else.
///     </para>
///     <para>
///     <b>Each match is returned whole — descriptor, details and provenance.</b> Returning only
///     descriptors, or only a snippet, was the shape that made a recall useless: the model found
///     the right memory and still could not answer from it. The details are the evidence payload
///     and are the reason the family exists.
///     </para>
///     <para>
///     <b>How many memories come back is the application author's decision, not the model's.</b>
///     There is no count argument. The author configures <see cref="MemoryOptions.RecallCount"/>
///     once, against their own context budget; a model that could raise it would be spending a
///     budget it cannot see.
///     </para>
///     <para>
///     <b>Finding nothing is an answer, not a refusal.</b> An empty store and a query nothing
///     matches both return an empty match list, because a refusal would invite the model to
///     conclude the tool is broken and stop using it.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the store, generator and options it was given.
///     </para>
/// </remarks>
public static class MemoryRecallTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "memory_recall";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Searches your memories and returns the closest ones, each with its descriptor, its full "
        + "details and where the fact came from. Supply the question or statement you want "
        + "memories about; the search compares it against the one-sentence descriptor each memory "
        + "was filed under. Finding nothing is a valid answer and is reported as an empty list.";

    /// <summary>
    ///     Creates the <c>memory_recall</c> tool over one composition's store.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment. An application
    ///     obtains this tool by attaching <see cref="MemoryPack"/>.
    /// </remarks>
    /// <param name="store">The store this tool searches.</param>
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
        var recall = (
                [Description(
                    "The question or statement you want memories about. Compared against the "
                    + "one-sentence descriptor each memory was filed under.")]
                string? query = null,
                CancellationToken cancellationToken = default) =>
            RecallAsync(store, generator, options, query, cancellationToken);

        return GuardedToolFactory.Create(recall, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Searches the store, refusing rather than throwing whenever the request itself cannot be
    ///     honored.
    /// </summary>
    /// <remarks>
    ///     An empty store is answered without embedding anything. The embedding call is the
    ///     expensive part of a recall and there is nothing for its result to be compared against,
    ///     so skipping it is both cheaper and impossible to observe from the outside.
    /// </remarks>
    /// <param name="store">The store to search.</param>
    /// <param name="generator">The embedding generator the application supplied.</param>
    /// <param name="options">The controls the application author configured.</param>
    /// <param name="query">The query the model supplied, or null when it supplied none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The structured matches, or a refusal naming its reason.</returns>
    private static async Task<object> RecallAsync(
        IMemoryStore store,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        MemoryOptions options,
        string? query,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown; the model supplied it. The refusal
        // states what is missing and nothing about what to do instead.
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "A query is required. It is compared against the descriptor each memory was filed "
                + "under.");
        }

        var held = await store.CountAsync(cancellationToken).ConfigureAwait(false);
        if (held == 0)
        {
            return Report(query, []);
        }

        var vector = await MemoryEmbedding
            .GenerateAsync(generator, query, cancellationToken)
            .ConfigureAwait(false);

        var matches = await store
            .SearchAsync(vector, options.RecallCount, cancellationToken)
            .ConfigureAwait(false);

        return Report(query, matches);
    }

    /// <summary>
    ///     Renders the matches as the structured result the model reads.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Structured rather than rendered prose, because whatever shape a tool emits is the shape
    ///     the model mirrors back. A recall that returned a paragraph would teach the model to
    ///     answer in paragraphs that blur which memory said what; a list of fielded matches teaches
    ///     it to attribute.
    ///     </para>
    ///     <para>
    ///     Every match carries its own identifier and provenance, so that the model can cite a
    ///     source, and can name the memory it means in a later update, revision or removal, without
    ///     a second call.
    ///     </para>
    /// </remarks>
    /// <param name="query">The query that was searched for.</param>
    /// <param name="matches">The matches the store returned, nearest first.</param>
    /// <returns>The structured result.</returns>
    private static object Report(string query, IReadOnlyList<MemoryMatch> matches)
    {
        return ToolResult.Structured(new
        {
            query,
            matchCount = matches.Count,
            matches = matches
                .Select(match => new
                {
                    memoryId = match.Memory.Id,
                    descriptor = match.Memory.Descriptor,
                    details = match.Memory.Details,
                    sourceDocument = match.Memory.SourceDocument,
                    sourceLocator = match.Memory.SourceLocator,
                    similarity = match.Similarity,
                })
                .ToList(),
        });
    }
}

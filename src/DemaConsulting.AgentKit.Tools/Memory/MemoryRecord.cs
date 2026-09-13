namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     One memory: a short descriptor that is the only text ever embedded, the richer details the
///     descriptor stands for, where the fact came from, and the vector the descriptor produced.
/// </summary>
/// <remarks>
///     <para>
///     <b>The descriptor/details split is the family's central finding, not a convenience.</b>
///     Findability and sufficiency are different jobs and were measured to be in tension: a
///     descriptor long enough to carry a whole answer embeds poorly, and a descriptor short enough
///     to embed well cannot answer anything on its own. Embedding one short sentence and returning
///     an never-embedded payload beside it lets each half do its own job. Only
///     <see cref="Descriptor"/> contributes to <see cref="Embedding"/>; <see cref="Details"/> is
///     never embedded, and is not searched.
///     </para>
///     <para>
///     <b>A memory is roughly one document or one section, not one extracted fact.</b> Five spike
///     configurations over a seventeen-document technical corpus all plateaued at the same answer
///     accuracy, and coarser granularity was monotonically better within that plateau: fine-grained
///     extraction fragmented answers across several memories, so a top-k recall returned incoherent
///     partials. The type carries no field that would encourage finer splitting, and carries no
///     link, edge or relation to another memory: two spikes produced 530 such links, not one of
///     which ever contributed to a correct answer, and a deterministic probe confirmed the links
///     were correctly wired — so the absence is a measured result rather than an omission.
///     </para>
///     <para>
///     <b>Provenance is data the memory carries, not a claim the library adjudicates.</b>
///     <see cref="SourceDocument"/> and <see cref="SourceLocator"/> record where the fact was read;
///     both are nullable, because a memory whose source is unknown is a real memory and pretending
///     otherwise would only invite the model to invent a citation. Whether a revision replaces,
///     accumulates or must corroborate a source is the application author's instruction to give;
///     this library only guarantees that a revision can state it and that nothing stale is retained
///     silently.
///     </para>
///     <para>
///     A record rather than a class, so a store can hand a memory out without any caller being able
///     to mutate what the store holds. Instances are immutable and safe for concurrent use.
///     </para>
/// </remarks>
/// <param name="Id">
///     The identifier the memory is addressed by, unique within the store. Assigned when the memory
///     is filed and never changed, so that an identifier the model has read back stays valid across
///     an update or a revision.
/// </param>
/// <param name="Descriptor">
///     The short single sentence describing what the memory is about. The only text that is
///     embedded, and therefore the only text a recall matches against.
/// </param>
/// <param name="Details">
///     The evidence payload returned on recall. Never embedded, so it may carry the numbers,
///     qualifications and wording an answer needs without degrading findability.
/// </param>
/// <param name="SourceDocument">
///     The document the fact was read from, or <see langword="null"/> when none was stated.
/// </param>
/// <param name="SourceLocator">
///     Where in that document the fact was read — a section, a heading, a line range — or
///     <see langword="null"/> when none was stated.
/// </param>
/// <param name="Embedding">
///     The vector <see cref="Descriptor"/> produced, held so that near-duplicate detection and
///     recall both read an already-computed value rather than re-embedding stored text.
/// </param>
public sealed record MemoryRecord(
    string Id,
    string Descriptor,
    string Details,
    string? SourceDocument,
    string? SourceLocator,
    ReadOnlyMemory<float> Embedding);

/// <summary>
///     One memory a search found, and how close its descriptor was to the text that was searched
///     for.
/// </summary>
/// <remarks>
///     <para>
///     The score is carried beside the memory rather than folded into it because it is a property
///     of one comparison, not of the memory: the same memory scores differently against every
///     query, and a near-duplicate decision reads the score while a recall result reports it.
///     </para>
///     <para>
///     Instances are immutable and safe for concurrent use.
///     </para>
/// </remarks>
/// <param name="Memory">The memory that was found.</param>
/// <param name="Similarity">
///     The cosine similarity between the searched-for vector and
///     <see cref="MemoryRecord.Embedding"/>, in the inclusive range -1.0 to 1.0, where 1.0 is
///     identical direction. Cosine — rather than a distance — because it is the measure the
///     configured near-duplicate threshold is expressed in, and a store that returned a distance
///     would make that threshold mean something different for every implementation.
/// </param>
public sealed record MemoryMatch(MemoryRecord Memory, double Similarity);

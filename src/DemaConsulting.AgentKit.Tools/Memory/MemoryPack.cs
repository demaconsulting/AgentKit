using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     The memory tool family: the pack an application attaches to give an agent a searchable
///     record of what it has learned, which it can file into, recall from, correct and forget.
/// </summary>
/// <remarks>
///     <para>
///     <b>Attaching these tools is not enough. The application must instruct the agent to use
///     them.</b> This is the single most important thing to know about this family, and it is
///     stated here rather than buried in a sample because an author who attaches the tools without
///     such an instruction will watch the model ignore them and conclude they do not work. A model
///     reading a document will answer from the document it is holding and never think to write
///     anything down, because from inside one turn there is no observable difference between
///     knowing something and having just read it. The same was measured of the task-list family,
///     whose type-level remarks on <c>TodoPack</c> carry the numbers; the wording that
///     works is published here as <see cref="SuggestedInstruction"/> so an application can append it
///     rather than transcribe it.
///     </para>
///     <para>
///     <b>A memory is a short descriptor and a richer payload, and only the descriptor is
///     embedded.</b> This split is the family's central design finding: a sentence short enough to
///     embed well cannot answer a question, and a passage long enough to answer one does not embed
///     well. Keeping them apart lets each do its own job, and the payload is returned whole on
///     recall so a found memory is a sufficient one.
///     </para>
///     <para>
///     <b>File one memory per document or section, not one per fact.</b> Five spike configurations
///     over a seventeen-document technical corpus all plateaued at the same answer accuracy — the
///     ceiling belonged to the corpus and the questions, not to any configuration — and within that
///     plateau coarser granularity was monotonically better. Fine-grained extraction fragmented
///     answers across memories, so a top-k recall returned incoherent partials.
///     </para>
///     <para>
///     <b>There is no graph.</b> No link, no edge, no traversal, and none is planned. Two spikes
///     built one and produced 530 links between memories; not one of them ever contributed to a
///     correct answer, and a deterministic probe confirmed the links were correctly wired — so the
///     graph genuinely added nothing rather than having been built wrongly.
///     </para>
///     <para>
///     <b>Near-duplicate detection is a backstop, and it is not optional.</b> Every file compares
///     the new descriptor against the vectors already held and declines to store a memory at or
///     above the author's configured threshold, reporting the conflicting memory instead. The
///     arithmetic caught a 12-psi versus 18-psi contradiction at 0.965 cosine where repeated
///     attempts to have a spike model notice the same conflict by reading had failed. Its role is
///     precisely that unnoticed case: the refusal fires when the model files a near-identical
///     descriptor, which is to say when it has <em>not</em> seen that it is contradicting something
///     the store already holds.
///     </para>
///     <para>
///     <b>When a model does notice a conflict it revises in place, and that is the better
///     outcome.</b> Measured over five live runs of the <c>research-assistant</c> sample whose
///     prompts never mention descriptors, the model recognized the superseded value by reading,
///     cited the superseding document and corrected the memory with <c>memory_revise</c> in 5 of 5,
///     leaving one memory rather than two. The refusal fired in none of those runs, and neither did
///     the silent double-store it exists to catch. Revision in place is the primary path in
///     practice; the arithmetic is the floor under it.
///     </para>
///     <para>
///     <b>Do not read the refusal as routine or reliable.</b> The mechanism is guaranteed — every
///     file is checked — but what it catches is phrasing-dependent, and it did not fire in any of
///     the eight live sample runs measured. It fires when two descriptors are written as the same
///     subject, and does not when the same two facts are written as <c>"…for the bilge pump"</c>
///     and <c>"…per field revision (Revision B)"</c>, which is what a model produces when it has
///     understood that the facts differ. That second outcome is not by itself a defect: two
///     memories carrying accurate provenance for two different documents is a legitimate
///     representation, and which representation is wanted over a given corpus is the author's
///     policy. What the library cannot supply is the phrasing, which is why
///     <see cref="SuggestedInstruction"/> spends four sentences on it.
///     </para>
///     <para>
///     <b>The application author governs the settings; this family guarantees the mechanism.</b>
///     The embedding backend arrives as an
///     <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>, so whether it is Ollama, a local
///     Foundry model or an offline ONNX model is invisible here. The threshold and the recall
///     count arrive as <see cref="MemoryOptions"/>. Persistence arrives as an
///     <see cref="IMemoryStore"/>. None of the three is decided by this library.
///     </para>
///     <para>
///     <b>Where an omitted store leaves the memories.</b> An application that supplies no store gets
///     a fresh <see cref="InMemoryMemoryStore"/> per <see cref="CreateTools"/> call, so memories
///     live exactly as long as the composition and a delegated agent keeps a store of its own — the
///     same containment <c>TodoPack</c> gives a task list. An application that supplies a
///     store gets exactly that store, shared by every composition it is handed to, because sharing or
///     persisting memories is precisely the thing an author supplies a store in order to do.
///     </para>
///     <para>
///     <b>The family asks nothing of the host and touches no files.</b> Its tools take no path and
///     never consult the policy they are handed, so the policy is accepted and ignored.
///     </para>
///     <para>
///     The class is immutable after construction and is safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Attaching the family and — the part that actually makes it work — instructing the agent to
///     use it. The embedding generator is whatever the application chose; AgentKit never learns
///     which backend it is.
///     </para>
///     <code>
///     var workspace = Path.GetFullPath("workspace");
///     var policy = new PathPolicy(workspace, [PathRule.ReadOnly(workspace)]);
///
///     // Supplied by the application: Ollama, a local Foundry model, or an offline ONNX model.
///     IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt; embeddings = default!;
///
///     IReadOnlyList&lt;AIFunction&gt; tools = new ToolPackBuilder(policy)
///         .Add(new TextFilePack())
///         .Add(new MemoryPack(embeddings, new MemoryOptions(nearDuplicateThreshold: 0.9)))
///         .Build();
///
///     // memory_file, memory_recall, memory_update, memory_revise, memory_forget.
///     var instructions =
///         "You are a research assistant working in the permitted locations.\n\n"
///         + MemoryPack.SuggestedInstruction;
///     </code>
/// </example>
public sealed class MemoryPack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "memory";

    /// <summary>
    ///     The instruction an application should give an agent that carries this family.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Published as a constant so an application can append it to its own instructions rather
    ///     than transcribe it, and so the wording that ships cannot drift from the wording that was
    ///     written against the measured behavior. An application is free to write its own.
    ///     </para>
    ///     <para>
    ///     Its imperatives are the ones the family needs and a model will not supply on its own:
    ///     file as you read rather than at the end, recall before answering rather than after
    ///     deciding you already know, and — the one that costs a correction its provenance when it
    ///     is missing — correct a fact from a <em>new</em> document with <c>memory_revise</c>,
    ///     citing that document. The granularity instruction is included because granularity is the
    ///     one choice the model makes that the library cannot make for it.
    ///     </para>
    ///     <para>
    ///     <b>The revision sentence is here because of an observed failure, not a hypothetical
    ///     one.</b> Offered a conflict raised by a different document, a model repeatedly chose
    ///     <c>memory_update</c>, which retains the provenance the memory already carried; the
    ///     memory then held corrected text beside a citation of the superseded source. It did so
    ///     even though <c>memory_update</c> reports the source it retained and its own description
    ///     says a changed source calls for a revision. Nothing in the tools can decide this — which
    ///     source a corrected memory should cite is the author's policy, not the library's — so the
    ///     instruction is where it belongs, and an application writing its own instructions should
    ///     carry this sentence across.
    ///     </para>
    ///     <para>
    ///     <b>The descriptor-phrasing sentences are here because of a measured behavior, not a
    ///     hypothetical one.</b> Asked to file two contradicting statements of one fact and told to
    ///     keep them separate, a model wrote <c>"Relief valve setting for the bilge pump"</c> for the
    ///     first and <c>"Relief valve setting per field revision (Revision B)"</c> for the second —
    ///     both stored, no refusal raised. The descriptors were different because the model had
    ///     <em>understood</em> that the facts differed and had said so in the only field that is
    ///     embedded. Detection keys on descriptor similarity, so a model that names the source or
    ///     revision that distinguishes a conflict steps past detection precisely when a conflict
    ///     exists. Instructing subject-only descriptors is the lever that restores the collision, and
    ///     stating the reason rather than the bare rule is deliberate: a rule without a reason gets
    ///     applied inconsistently by exactly the reasoning that produced the distinct descriptors.
    ///     </para>
    ///     <para>
    ///     <b>What that instruction is worth, measured.</b> Over eight live runs of the
    ///     <c>research-assistant</c> sample, the subject-only rule was obeyed in 5 of 5 runs whose
    ///     prompts never mentioned descriptors, and in 0 of 3 runs whose prompt demanded a separate
    ///     memory and so pressed for a distinguishing label. In the same five neutral runs the model
    ///     chose <c>memory_revise</c> over <c>memory_update</c> and cited the new document 5 of 5.
    ///     Those figures are the whole of what has been measured for this text; the task-list family's
    ///     published 1-of-5 versus 3-of-3 comparison belongs to that family's wording and does not
    ///     transfer to this one.
    ///     </para>
    ///     <para>
    ///     It is deliberately not applied automatically. This library composes tools; it does not
    ///     author an agent's instructions, and silently injecting text into a system prompt is
    ///     exactly the kind of invisible behavior an application author cannot audit.
    ///     </para>
    /// </remarks>
    public const string SuggestedInstruction =
        "As you read anything you may need later, write it down with memory_file: one memory per "
        + "document or per section, a single short sentence as the descriptor and the fuller text "
        + "as the details, and state the document you read it in. Write the descriptor as the "
        + "SUBJECT the memory is about and nothing else — name the thing the fact concerns, never "
        + "the document it came from, the revision it belongs to, the qualifier that narrows it, "
        + "or the value it states. Those all belong in the details and in the source parameters. "
        + "The reason matters more than the rule: only the descriptor is compared when a new "
        + "memory is checked against the ones you already hold, so two statements of one subject "
        + "have to read alike for a contradiction between them to be noticed at all. A descriptor "
        + "that names its source or its revision says where a fact came from rather than what it "
        + "is about, reads as a different subject, and is filed silently beside the memory it "
        + "contradicts. Before answering any question, "
        + "call memory_recall first and answer from "
        + "what it returns — do not rely on what you think you already know. If memory_file reports "
        + "that a memory was not stored, read the conflicting memory it names and decide whether it "
        + "is the same fact or a different one. When you correct something you already filed, the "
        + "source decides the tool: use memory_update only when the correction comes from the same "
        + "document the memory already cites, and use memory_revise when it comes from a different "
        + "document — stating that new document as the source. memory_update keeps the source the "
        + "memory already had, so using it for a correction drawn from a new document would leave "
        + "the memory citing a superseded one.";

    /// <summary>
    ///     The embedding generator the application supplied.
    /// </summary>
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

    /// <summary>
    ///     The controls the application author configured.
    /// </summary>
    private readonly MemoryOptions _options;

    /// <summary>
    ///     The store the application supplied, or <see langword="null"/> when it supplied none.
    /// </summary>
    /// <remarks>
    ///     Held as null rather than as an eagerly created default, because the difference between
    ///     "the author's store, shared" and "a fresh store per composition" is exactly whether the
    ///     author supplied one, and collapsing the two here would silently share a default store
    ///     between a parent agent and its children.
    /// </remarks>
    private readonly IMemoryStore? _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemoryPack"/> class.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The embedding generator is required and the other two are not, so the shortest useful
    ///     composition is <c>new MemoryPack(embeddings)</c>: default controls, and memories that
    ///     live as long as the agent does.
    ///     </para>
    ///     <para>
    ///     The pack does not own the generator or the store and disposes of neither. Both may
    ///     outlive the pack, both may be shared with the rest of the application, and a library that
    ///     disposed of something it was merely handed would break a host that was still using it.
    ///     </para>
    /// </remarks>
    /// <param name="embeddingGenerator">
    ///     The generator that turns a descriptor into a vector. Must not be <see langword="null"/>.
    ///     Which backend it wraps is the application's decision and is never inspected.
    /// </param>
    /// <param name="options">
    ///     The near-duplicate threshold and recall count, or <see langword="null"/> for
    ///     <see cref="MemoryOptions.Default"/>.
    /// </param>
    /// <param name="store">
    ///     The store memories are kept in, or <see langword="null"/> to give each composition a
    ///     fresh in-memory store of its own. A supplied store is shared by every composition this
    ///     pack creates tools for.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="embeddingGenerator"/> is <see langword="null"/>.
    /// </exception>
    public MemoryPack(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        MemoryOptions? options = null,
        IMemoryStore? store = null)
    {
        // Without a generator nothing in this family can be filed or found, so an absent one is a
        // composing-application error named at the point it was made.
        ArgumentNullException.ThrowIfNull(embeddingGenerator);

        _generator = embeddingGenerator;
        _options = options ?? MemoryOptions.Default;
        _store = store;
    }

    /// <summary>
    ///     Gets the family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Implemented explicitly so that the constant above can keep the name the contract uses.
    ///     The class is sealed, so the member is not hidden from a derived type.
    /// </remarks>
    string IToolPack.FamilyPrefix => FamilyPrefix;

    /// <summary>
    ///     Gets the capabilities the host must provide for this pack's tools to operate.
    /// </summary>
    /// <remarks>
    ///     <see cref="HostCapabilities.None"/>. The capability this family really depends on — an
    ///     embedding backend — is not a host declaration but a constructor argument: an application
    ///     that cannot embed cannot construct the pack at all, so there is nothing left for a
    ///     capability flag to gate.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.None;

    /// <summary>
    ///     Gets the controls the application author configured.
    /// </summary>
    /// <remarks>
    ///     Exposed so an application can report the configuration it is running under — the
    ///     threshold in particular is the number that explains why a memory was not stored — without
    ///     having to keep its own copy beside the pack.
    /// </remarks>
    public MemoryOptions Options => _options;

    /// <summary>
    ///     Creates the family's tools over the store this composition uses.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>A fresh <see cref="InMemoryMemoryStore"/> is allocated here, on every call, when and
    ///     only when the application supplied no store.</b> That is what binds a default set of
    ///     memories to one agent: two compositions never share a default store, and a delegated
    ///     agent — whose tools are created by a separate call to this method — therefore keeps its
    ///     own. An application that supplied a store gets that store every time, which is what makes
    ///     a shared or persistent memory expressible at all.
    ///     </para>
    ///     <para>
    ///     The order — file, recall, update, revise, forget — is fixed rather than incidental,
    ///     because the order a model sees the tools in is observable. It is the order of the work:
    ///     write something down, find it again, correct what it says, change what it is about, and
    ///     finally drop it.
    ///     </para>
    ///     <para>
    ///     The policy is accepted and ignored: this family touches no files, so it has nothing to
    ///     judge against a policy. It is still validated, so that a composing application that
    ///     forgot one is told at the point it forgot.
    ///     </para>
    /// </remarks>
    /// <param name="policy">
    ///     The access policy of the composition. Required for the contract but unused: no tool in
    ///     this family reads, writes or names a path.
    /// </param>
    /// <returns>The file, recall, update, revise and forget tools, in that order.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated even though it is unused, so that a composing application that omitted a policy
        // is told at the point it made the mistake rather than by a sibling family later.
        ArgumentNullException.ThrowIfNull(policy);

        // One store per composition unless the author supplied one to share. When they did not, the
        // store is a local — never a field — which is the mechanism by which a delegated agent
        // cannot reach its parent's memories.
        var store = _store ?? new InMemoryMemoryStore();

        return
        [
            MemoryFileTool.Create(store, _generator, _options),
            MemoryRecallTool.Create(store, _generator, _options),
            MemoryUpdateTool.Create(store),
            MemoryReviseTool.Create(store, _generator),
            MemoryForgetTool.Create(store)
        ];
    }
}

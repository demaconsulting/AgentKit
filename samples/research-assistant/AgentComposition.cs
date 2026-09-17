using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Agents.Copilot;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Agent;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Markdown;
using DemaConsulting.AgentKit.Tools.Memory;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.Todo;
using GitHub.Copilot;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     A built conversation plan paired with the handle that releases whatever runtime resources it
///     owns.
/// </summary>
/// <param name="Conversation">The root conversation plan, ready to start and run.</param>
/// <param name="Cleanup">The handle that releases the agent's runtime resources; disposed by the host.</param>
/// <param name="Recall">
///     A conversation on an agent carrying the memory family and nothing else, over the same store,
///     or <see langword="null"/> when no recall question was asked. See
///     <see cref="AgentComposition.BuildRecallTools"/> for why it exists.
/// </param>
public sealed record AgentSetup(
    ConversationPlan Conversation,
    IAsyncDisposable Cleanup,
    ConversationPlan? Recall = null);

/// <summary>
///     One provider's ability to build an agent, plus the handle that releases it.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is the seam that makes delegation provider-neutral.</b> A child agent has to be
///     started by the application — only the application knows its provider, model and credentials —
///     so the sample resolves "how do I build an agent here?" exactly once, into this delegate, and
///     then uses it for both the root agent and every delegated one. Neither the composition below
///     nor the <c>agent_run</c> tool ever learns which provider is behind it.
///     </para>
///     <para>
///     <b>A second delegate answers "can this provider carry an AgentKit session?"</b> It can when
///     AgentKit ships a provider session for it, which today means any <c>IChatClient</c>. A
///     provider with none leaves it unset, and the conversation runs on that runtime's own session
///     instead — which works, and does not compact. The difference is stated here rather than
///     smoothed over, because a conversation that silently stops compacting is exactly the failure
///     the session engine exists to prevent.
///     </para>
/// </remarks>
/// <param name="CreateAgent">
///     Builds an agent from a tool list, system instructions and a name. Called once for the root
///     agent and once per delegated run.
/// </param>
/// <param name="Cleanup">The handle releasing the provider's client and transport.</param>
/// <param name="PlanSession">
///     Builds the compacting-session plan for a tool list and instructions, or
///     <see langword="null"/> when AgentKit ships no provider session for this provider.
/// </param>
public sealed record ProviderBackend(
    Func<IList<AIFunction>, string, string, AIAgent> CreateAgent,
    IAsyncDisposable Cleanup,
    Func<IList<AIFunction>, string, CompactingSessionPlan>? PlanSession = null);

/// <summary>
///     Composes the guarded tool set — task list, memories and delegation — and builds the provider
///     agent. This is the one place in the sample where the choice of provider is visible.
/// </summary>
/// <remarks>
///     <para>
///     <b>This sample is about the three families that let an agent work across turns.</b> The
///     document-assistant sample shows what an agent may <em>touch</em>; this one shows how it
///     <em>proceeds</em>: it writes its plan down with <c>todo</c>, records what it learns with
///     <c>memory</c>, and hands a bounded subtask to a child agent with <c>agent</c>. The file
///     families are present because research needs something to read, not because they are the
///     subject.
///     </para>
///     <para>
///     <b>Composition here is deliberately asymmetric, and the asymmetry is the safety story.</b>
///     The corpus is granted read-only and is the relative-path anchor; the notes folder is granted
///     read-write and lies outside it. The child agents are given a <em>different, smaller</em> set
///     of packs than the parent — no task list, no memory, no delegation of their own — so a
///     delegated run cannot reach the parent's plan or its record even by accident. See
///     <see cref="CreateChildPacks"/> for why that is stated as a pack list rather than trusted to
///     a profile's tool names alone.
///     </para>
///     <para>
///     <b>The embedding backend is the application's choice and nothing else changes with it.</b>
///     <c>MemoryPack</c> takes an <c>IEmbeddingGenerator</c> and never inspects it, so
///     <see cref="CreateEmbeddingGenerator"/> is the whole of the difference between the sample's
///     offline generator and a real model served by Ollama.
///     </para>
/// </remarks>
public static class AgentComposition
{
    /// <summary>
    ///     The name the root agent carries, so tool-call transcripts and any provider-side logging
    ///     identify the sample.
    /// </summary>
    public const string AgentName = "research-assistant";

    /// <summary>
    ///     The name the recall agent carries, so a transcript distinguishes the turn that could
    ///     only have answered from memory from the turns that had the documents in front of them.
    /// </summary>
    public const string RecallAgentName = "research-assistant-recall";

    /// <summary>
    ///     The name of the profile that reads one document and reports what it says.
    /// </summary>
    /// <remarks>
    ///     Published as a constant because the sample's tests, its README and its live-model CI job
    ///     all name this profile, and three transcriptions of one string would eventually disagree.
    /// </remarks>
    public const string ReaderProfileName = "document-reader";

    /// <summary>
    ///     The name of the profile that condenses text the parent passes it, using no tools at all.
    /// </summary>
    public const string SummarizerProfileName = "summarizer";

    /// <summary>
    ///     Builds the system instructions handed to the root agent, naming this run's two locations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The two published suggested instructions are appended rather than transcribed.</b>
    ///     <c>TodoPack.SuggestedInstruction</c> and <c>MemoryPack.SuggestedInstruction</c> are
    ///     constants precisely so an application can do this: the wording an application ships then
    ///     cannot drift from the wording the library wrote against its own measurements. For the
    ///     task list those measurements are published — an explicit instruction produced use in 3
    ///     of 3 runs where a soft one produced 1 of 5. <b>No equivalent adherence figure exists for
    ///     the memory instruction</b>, and the sample does not imply one.
    ///     </para>
    ///     <para>
    ///     <b>Six pieces of guidance are the application's own, and each answers something
    ///     observed in a live run rather than something imagined.</b>
    ///     </para>
    ///     <list type="number">
    ///         <item>
    ///             <description>
    ///             <b>A descriptor names the subject only.</b> This is the sample's sharpest
    ///             finding and it is self-defeating without the instruction. In three live runs the
    ///             near-duplicate refusal never fired — including one run whose two prompts
    ///             explicitly demanded a separate new memory of the same fact. The model wrote
    ///             <c>"Relief valve setting for the bilge pump"</c> for the 12-psi statement and
    ///             <c>"Relief valve setting per field revision (Revision B)"</c> for the 18-psi
    ///             one, and both were stored. Nothing malfunctioned: only the descriptor is
    ///             embedded, so detection keys on descriptor similarity, and a model that has
    ///             <em>understood</em> that two facts differ naturally writes descriptors that say
    ///             so — defeating detection exactly when a conflict exists. The instruction
    ///             therefore states the rule and its reason, because a rule without a reason is
    ///             overridden by precisely the reasoning that produced the failure.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///             <b>A correction from a different document is a revision, and must cite that
    ///             document.</b> Measured against a live model, after a conflict raised by a
    ///             <em>new</em> source the model frequently chose <c>memory_update</c> — which
    ///             keeps the original provenance — leaving a corrected memory citing a superseded
    ///             document. It did so even though <c>memory_update</c> reports the source it
    ///             retained and its own description says a changed source means a revision.
    ///             Provenance came out right in every run only when the instructions demanded the
    ///             new document be cited. This is therefore instruction, not tool behavior: the
    ///             author governs policy, AgentKit guarantees mechanism.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///             <b>Recall has no similarity floor.</b> <c>memory_recall</c> returns the nearest
    ///             memories it holds, so a question about something never recorded still comes back
    ///             with matches — whatever was least unlike it. A model was observed reporting "no
    ///             matches found" when two weak matches were in fact returned, which is the same
    ///             confusion in the opposite direction. The agent is told to read each returned
    ///             descriptor and judge it, rather than treat the top match as an answer or an empty
    ///             judgement as an empty result.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///             <b>What a delegated agent is for.</b> A child here reads one document and reports
    ///             back; it has no task list and no memories of its own to file into, so its report
    ///             is text the parent must record. Saying so keeps the model from assuming a child
    ///             shares its state — which is exactly what a child must never do.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///             <b>Conclusions are written into the notes folder before the answer is given.</b>
    ///             In three live runs the notes folder — the sample's only writable location, and
    ///             therefore the only demonstration of its read-write grant — stayed empty. The
    ///             model answered in prose and wrote nothing, because nothing had told it to. A
    ///             granted capability that is never exercised demonstrates nothing about the policy
    ///             governing it, so the instruction now names the tool and the location and says
    ///             when to use them.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///             <b>A plan is written before the work, not after it.</b> One live run opened by
    ///             filing a task already marked complete — "Review all corpus documents", recorded
    ///             as done before any document had been read — which reads as a summary wearing a
    ///             plan's clothes and tells a watcher nothing about what is coming. The instruction
    ///             therefore forbids adding an already-finished item.
    ///             </description>
    ///         </item>
    ///     </list>
    /// </remarks>
    /// <param name="corpusRoot">The absolute corpus path named in the instructions, granted read-only.</param>
    /// <param name="notesRoot">The absolute notes path named in the instructions, granted read-write.</param>
    /// <param name="delegationEnabled">Whether delegation is offered this run.</param>
    /// <returns>The system instructions for this run.</returns>
    public static string BuildInstructions(string corpusRoot, string notesRoot, bool delegationEnabled)
    {
        var delegationGuidance = delegationEnabled
            ? "You may hand a single bounded subtask to a child agent with " + AgentRunTool.ToolName
            + ": ask '" + ReaderProfileName + "' to read one document and report what it says, or '"
            + SummarizerProfileName + "' to condense text you pass it. A child agent is not you: it "
            + "has no task list and no memories, it cannot write anything, and it reports back only "
            + "text. Whatever a child tells you that is worth keeping, you must file yourself, "
            + "citing the document the child read. "
            : "You have no way to delegate work this run; there is no sub-agent tool. Do the work "
            + "yourself. ";

        return
            "You are a research assistant working over a small corpus of documents. You have access "
            + "to exactly two locations and nothing else. The corpus is '" + corpusRoot
            + "' (read-only); it is what relative names are interpreted against, so address its "
            + "documents by their plain relative names. Your notes folder is '" + notesRoot
            + "' (read-write); it lies outside the corpus, so only its full absolute path reaches "
            + "it, and it is the only place you can write. You cannot edit the corpus, and that is "
            + "deliberate: you cite sources, you do not revise them. You have no shell, terminal, "
            + "code-execution, or web/fetch tool; do not claim otherwise. To see what documents "
            + "exist, call " + FileListTool.ToolName + " with no directory argument: it reports "
            + "every location you may reach, each as an absolute path with its files beneath it. "
            + "\n\n"
            + TodoPack.SuggestedInstruction
            + " Write the plan down before you begin the work it describes, and mark an item "
            + "completed only once you have actually completed it. Never add an item that is "
            + "already finished: a plan that opens with a completed item is a summary pretending "
            + "to be a plan, and it tells whoever is watching nothing about what you are about to "
            + "do."
            + "\n\n"
            + MemoryPack.SuggestedInstruction
            + "\n\n"
            + "Before you give your final answer on a piece of research, write your conclusions "
            + "down: call " + TextFileCreateTool.ToolName + " with an absolute path beneath '"
            + notesRoot + "' and record what you concluded and which document each conclusion came "
            + "from. That folder is the only location you can write to, and a conclusion you did "
            + "not write down does not outlive this run. Write the notes file first, then answer. "
            + "\n\n"
            + "Always record where a fact came from: state the document and the section or heading "
            + "when you file a memory, because a finding you cannot attribute is one you cannot "
            + "defend. State it in the source parameters and in the details — never in the "
            + "descriptor. The descriptor names the subject only: 'relief valve pressure setting', "
            + "not 'relief valve setting per field revision (Revision B)' and not 'relief valve "
            + "setting is 18 psi'. Only the descriptor is compared against the memories you "
            + "already hold, so a descriptor that carries the document, the revision or the value "
            + "reads as a different subject and will be filed silently beside the memory it "
            + "contradicts. When a later document restates a fact you have already filed, the "
            + "descriptor you write for it should be the one you already used. "
            + "When a later document contradicts something you have already filed, correct "
            + "it with " + MemoryReviseTool.ToolName + " and state the NEW document as the source. "
            + "Do not use " + MemoryUpdateTool.ToolName + " for this: it keeps the source the "
            + "memory already had, which would leave your corrected memory citing the superseded "
            + "document. Use " + MemoryUpdateTool.ToolName + " only to add or fix detail from the "
            + "same source the memory already cites. "
            + MemoryRecallTool.ToolName + " has no similarity floor: it always returns the nearest "
            + "memories it holds, so it will return something even when nothing you have recorded "
            + "is about the question. Read the descriptor of each memory it returns and judge "
            + "whether it is actually about what was asked; if none of them is, say plainly that "
            + "you have nothing recorded on that subject rather than answering from the closest "
            + "match. "
            + delegationGuidance
            + "When a tool refuses a request, read the refusal: it states how your request was "
            + "interpreted and what is permitted, which is enough to reissue it correctly. Do not "
            + "retry the identical call, and do not invent a path.";
    }

    /// <summary>
    ///     Creates the packs a delegated agent may draw its tools from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This list is the sample's most important safety decision, so it is a method of its own
    ///     and it is tested.</b> A child here gets the reading families and nothing else. It gets no
    ///     <c>TodoPack</c>, no <c>MemoryPack</c>, and — because <c>AgentPack</c> forbids listing
    ///     itself — no delegation beyond what the family grants a profile that admits
    ///     <c>agent_run</c>, which none of these profiles does.
    ///     </para>
    ///     <para>
    ///     <b>Withholding the pack is stronger than withholding the tool name.</b> An
    ///     <c>AgentProfile</c>'s tool list is a filter over what the application attached, so a
    ///     profile that simply omits <c>todo_set</c> is already safe — but it is safe by careful
    ///     editing of a list that will be edited again. Leaving the pack out means a future profile
    ///     naming <c>todo_set</c> conjures nothing: there is no such tool to filter for. AgentKit
    ///     already guarantees that a child's tools are composed afresh, so a child given
    ///     <c>TodoPack</c> would still get its own list rather than the parent's — this sample
    ///     withholds it because a child that reads one document has no use for a plan, and the
    ///     narrowest correct capability set is the one worth shipping. The concrete failure this
    ///     shape rules out is real: in a separate system a sub-agent sharing its parent's task store
    ///     replaced that parent's plan wholesale.
    ///     </para>
    ///     <para>
    ///     <b>Note especially what is absent for a second reason.</b> The parent's
    ///     <c>MemoryPack</c> is constructed over a store this application owns; if that same pack
    ///     instance were listed here, every child would file into and recall from the parent's
    ///     memories, because a supplied store is shared by every composition the pack is used in.
    ///     That is a legitimate thing for an application to want and an easy thing to get by
    ///     accident, so this sample states the opposite intent explicitly.
    ///     </para>
    /// </remarks>
    /// <returns>The packs a delegated agent's tools are composed from.</returns>
    public static IReadOnlyList<IToolPack> CreateChildPacks() =>
    [
        new TextFilePack(),
        new FilePack(),
        new MarkdownPack()
    ];

    /// <summary>
    ///     Builds the instructions for an agent that may answer only from what it recalls.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This exists because a single-session transcript cannot tell "remembered" from "still
    ///     in context".</b> In every live run of the sample the final answering turn made no
    ///     <c>memory_recall</c> call at all: the model had read the documents a few turns earlier,
    ///     so the answer was already in front of it and recall was genuinely unnecessary. The
    ///     memories were filed, and nothing demonstrated that they were ever the thing an answer
    ///     came from.
    ///     </para>
    ///     <para>
    ///     The recall turn removes both alternatives at once rather than asking the model not to
    ///     use them. It runs in a <em>fresh session</em>, so no earlier turn is in the context, and
    ///     it is composed from <see cref="BuildRecallTools"/>, which attaches the memory family and
    ///     nothing else — no reading tool exists to re-open a document with. What remains is the
    ///     store the earlier turns filled, so an answer that contains a fact from the corpus can
    ///     only have arrived through <c>memory_recall</c>.
    ///     </para>
    /// </remarks>
    /// <returns>The system instructions for the recall turn.</returns>
    public static string BuildRecallInstructions() =>
        "You are answering from memory alone. This is a fresh conversation: nothing you read "
        + "earlier is in front of you, and you have no file, document or search tool of any kind — "
        + "the only tools you have are the memory tools. Call " + MemoryRecallTool.ToolName
        + " and build your answer from the memories it returns and from nothing else, naming the "
        + "source document each memory records. " + MemoryRecallTool.ToolName + " has no "
        + "similarity floor: it returns the nearest memories it holds whatever their similarity, so "
        + "read each returned descriptor and judge whether it is actually about the question. If "
        + "none of them is, say plainly that you have nothing recorded on that subject. Do not "
        + "answer from general knowledge, and do not name a source that no memory states.";

    /// <summary>
    ///     Composes the tools for the recall turn: the memory family over the store the earlier
    ///     turns filled, and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The absence here is the demonstration.</b> No <c>TextFilePack</c>, no <c>FilePack</c>,
    ///     no <c>MarkdownPack</c>: an agent composed from this list cannot read a document, because
    ///     no tool that reads one was created. The policy carries no grant for the same reason —
    ///     grants without tools would be decoration, and the memory family touches no file anyway.
    ///     </para>
    ///     <para>
    ///     The store is the one the root composition was given. That a supplied store is shared by
    ///     every composition the pack is handed to is stated as a hazard on
    ///     <see cref="CreateChildPacks"/>; here it is exactly the intent, which is why it is said
    ///     twice rather than assumed once.
    ///     </para>
    /// </remarks>
    /// <param name="corpusRoot">The working directory the policy anchors on. Nothing is granted.</param>
    /// <param name="embeddingGenerator">The same generator the memories were filed with.</param>
    /// <param name="memoryStore">The store the earlier turns filed into.</param>
    /// <returns>The tool list for the recall turn.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="embeddingGenerator"/> or <paramref name="memoryStore"/> is
    ///     <see langword="null"/>.
    /// </exception>
    public static IList<AIFunction> BuildRecallTools(
        string corpusRoot,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IMemoryStore memoryStore)
    {
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(memoryStore);

        // A working directory is required to express a policy; no grant accompanies it, so nothing
        // on disk is reachable even if a reading tool somehow arrived.
        var policy = new PathPolicy(corpusRoot, []);

        return
        [
            .. new ToolPackBuilder(policy)
                .Add(new MemoryPack(embeddingGenerator, MemoryOptions.Default, memoryStore))
                .Build()
        ];
    }

    /// <summary>
    ///     Creates the agent profiles the application is willing to have started.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Every word of what a child is comes from here, written by the application. The parent
    ///     agent supplies only the task. The reader narrows its grants to the corpus alone, so a
    ///     child cannot reach the notes folder its parent can write to — narrowing is permitted,
    ///     widening is a composition-time error.
    ///     </para>
    ///     <para>
    ///     The summarizer deliberately declares no tools, which is a legitimate profile: condensing
    ///     text the parent passes in the task needs no capability at all. It carries no grants
    ///     because grants without tools would be decoration — tools are what reach a location, and
    ///     a child with none reaches nothing.
    ///     </para>
    /// </remarks>
    /// <param name="corpusRoot">The absolute corpus path the reader profile is narrowed to.</param>
    /// <returns>The registered profiles, in the order the model sees them.</returns>
    public static IReadOnlyList<AgentProfile> CreateProfiles(string corpusRoot) =>
    [
        new AgentProfile(
            name: ReaderProfileName,
            instructions:
                "You read exactly one document and report what it says. Read the document named in "
                + "your task, then answer with a short factual summary: the specific values, "
                + "settings and statements it contains, each with the heading or section you found "
                + "it under. Quote a number exactly as the document gives it. If the document does "
                + "not state something you were asked about, say so rather than inferring it. You "
                + "cannot write anything and you have nothing to remember with; your report is your "
                + "entire output.",
            tools:
            [
                TextFileReadTool.ToolName,
                TextFileSearchTool.ToolName,
                MarkdownOutlineTool.ToolName,
                FileListTool.ToolName
            ],
            grants: [PathRule.ReadOnly(corpusRoot)],
            description: "Reads one document in the corpus and reports its contents with sections. Cannot write."),

        new AgentProfile(
            name: SummarizerProfileName,
            instructions:
                "You condense text you are given into a short, faithful summary. Work only from the "
                + "text in your task: you have no tools, no files and no memory, so you cannot look "
                + "anything up. Do not add facts, and do not drop a number or a name that the text "
                + "states.",
            tools: [],
            description: "Condenses text passed in the task. Has no tools and reads nothing.")
    ];

    /// <summary>
    ///     Composes the guarded tool set for this run, gating delegation on the host's choice.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     One policy governs everything. The corpus is the working directory — the anchor a
    ///     relative path resolves against — and is granted read-only; the notes folder is granted
    ///     read-write and is never an anchor, so it is always addressed absolutely. Anchoring grants
    ///     nothing and granting anchors nothing, which is what lets "read these sources, write
    ///     conclusions elsewhere" be expressed directly.
    ///     </para>
    ///     <para>
    ///     The memory pack is given a store this application constructed, rather than letting the
    ///     pack allocate one. For a single root composition the two are equivalent; it is written
    ///     this way because deciding where memories live is the application's job, and because it
    ///     makes visible the consequence spelled out on <see cref="CreateChildPacks"/> — a supplied
    ///     store is shared by every composition the pack is handed to.
    ///     </para>
    ///     <para>
    ///     Delegation is gated exactly as vision is in the document-assistant sample: the pack is
    ///     added and the <c>Delegation</c> capability declared only when the host offers it. A pack
    ///     whose required capability is not declared is never asked for its tools, so withholding
    ///     the declaration alone would suffice — omitting the pack as well makes the intent obvious.
    ///     </para>
    /// </remarks>
    /// <param name="corpusRoot">The absolute corpus path: the anchor, granted read-only.</param>
    /// <param name="notesRoot">The absolute notes path, granted read-write.</param>
    /// <param name="embeddingGenerator">The application's chosen embedding backend for memories.</param>
    /// <param name="memoryStore">The store the application keeps its agent's memories in.</param>
    /// <param name="delegationEnabled">Whether to offer <c>agent_run</c> and declare the capability.</param>
    /// <param name="runner">
    ///     The host's means of starting a child agent, used only when delegation is enabled.
    /// </param>
    /// <returns>The tool list to hand to a provider factory.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="embeddingGenerator"/>, <paramref name="memoryStore"/> or
    ///     <paramref name="runner"/> is <see langword="null"/>.
    /// </exception>
    public static IList<AIFunction> BuildTools(
        string corpusRoot,
        string notesRoot,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IMemoryStore memoryStore,
        bool delegationEnabled,
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner)
    {
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(memoryStore);
        ArgumentNullException.ThrowIfNull(runner);

        // The corpus anchors relative paths and is granted read-only; the notes folder is granted
        // read-write and is addressed absolutely. Nothing is implied by the anchor itself.
        var policy = new PathPolicy(
            corpusRoot,
            [PathRule.ReadOnly(corpusRoot), PathRule.ReadWrite(notesRoot)]);

        var builder = new ToolPackBuilder(policy)
            .Add(new TextFilePack())
            .Add(new FilePack())
            .Add(new MarkdownPack())
            .Add(new TodoPack())
            .Add(new MemoryPack(embeddingGenerator, MemoryOptions.Default, memoryStore));

        // Delegation is a host capability: the application, and only the application, can start a
        // second agent. Declaring it is what makes the family available at all.
        if (delegationEnabled)
        {
            builder = builder
                .WithHostCapabilities(HostCapabilities.Delegation)
                .Add(new AgentPack(
                    CreateProfiles(corpusRoot),
                    runner,
                    CreateChildPacks(),
                    // A child is given no capability of its own: none of these profiles admits
                    // agent_run, so a child cannot delegate further.
                    HostCapabilities.None));
        }

        return [.. builder.Build()];
    }

    /// <summary>
    ///     Creates the embedding generator the memory family will use, per the run's choice.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This method is the entire cost of choosing an embedding backend.</b> Everything
    ///     downstream — the pack, the tools, the instructions, the transcript — is identical either
    ///     way, because <c>MemoryPack</c> takes the standard abstraction and never asks what is
    ///     behind it.
    ///     </para>
    ///     <para>
    ///     The offline default is the sample's own <see cref="LexicalEmbeddingGenerator"/>, chosen
    ///     so the sample runs from a fresh clone with no server, no credential and no model file in
    ///     the repository. Its limits are real and are documented on the type; a run that wants
    ///     genuine semantic similarity passes <c>--embeddings ollama</c>.
    ///     </para>
    /// </remarks>
    /// <param name="options">The validated options naming the backend and, for Ollama, its address.</param>
    /// <returns>
    ///     The generator, and a handle disposing whatever it owns. The generator is disposed by the
    ///     host, never by the pack: a library that disposed of something handed to it would break a
    ///     host still using it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="CommandLineException">The options name an unsupported backend.</exception>
    public static (IEmbeddingGenerator<string, Embedding<float>> Generator, IAsyncDisposable Cleanup)
        CreateEmbeddingGenerator(CommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        switch (options.Embeddings)
        {
            case EmbeddingBackend.Local:
                var lexical = new LexicalEmbeddingGenerator();
                return (lexical, new AsyncDisposableAction(() =>
                {
                    lexical.Dispose();
                    return ValueTask.CompletedTask;
                }));

            case EmbeddingBackend.Ollama:
                // A client of its own, on the embedding model: embedding and chat are different
                // jobs, and one client cannot be pointed at two models at once.
                var http = new HttpClient
                {
                    BaseAddress = new Uri(options.Host),
                    Timeout = TimeSpan.FromMinutes(5),
                };

                var ollama = new OllamaApiClient(http, options.EmbeddingModel);
                return (ollama, new AsyncDisposableAction(() =>
                {
                    ollama.Dispose();
                    http.Dispose();
                    return ValueTask.CompletedTask;
                }));

            default:
                throw new CommandLineException($"Unsupported embedding backend '{options.Embeddings}'.");
        }
    }

    /// <summary>
    ///     Builds the conversation plans for this run, together with the handle releasing
    ///     everything they own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The order matters and is the shape of the whole sample: resolve the provider into a
    ///     single agent-building delegate, express delegation as a runner over that delegate,
    ///     compose the tools around the runner, then build the root conversation with the same
    ///     delegate every child will be built with. After this method returns, nothing knows which
    ///     provider was chosen.
    ///     </para>
    ///     <para>
    ///     <b>The tools and the instructions are stated once and reach both shapes unchanged.</b> A
    ///     compacting session is seeded with them at creation and again at every rotation, and an
    ///     agent is built with them; the same list serves either, which is what lets the provider
    ///     choice stop mattering here.
    ///     </para>
    /// </remarks>
    /// <param name="options">The validated command-line options selecting provider, model and backend.</param>
    /// <param name="corpusRoot">The absolute corpus path: the anchor, granted read-only.</param>
    /// <param name="notesRoot">The absolute notes path, granted read-write.</param>
    /// <param name="transcript">
    ///     The machine-readable tool-call record, or <see langword="null"/> for none. Supplied here
    ///     because a compacting session reveals tool activity only to the chat client beneath it,
    ///     which is built in this method.
    /// </param>
    /// <param name="cancellationToken">Cancels a slow provider start.</param>
    /// <returns>The conversation plans and their cleanup handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static async Task<AgentSetup> CreateAgentAsync(
        CommandLineOptions options,
        string corpusRoot,
        string notesRoot,
        TextWriter? transcript,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (embeddings, embeddingCleanup) = CreateEmbeddingGenerator(options);

        ProviderBackend backend;
        try
        {
            backend = await CreateBackendAsync(options, corpusRoot, transcript, cancellationToken);
        }
        catch
        {
            // Do not leak the embedding backend if the provider failed to start.
            await embeddingCleanup.DisposeAsync();
            throw;
        }

        // The application's own store. See BuildTools and CreateChildPacks for why this is stated
        // rather than defaulted.
        var memoryStore = new InMemoryMemoryStore();

        // The runner starts a child on the same provider the parent runs on, from the instructions
        // and tools the library built for that child. It adds no tool and rewrites no instruction:
        // a tool the application did not attach is a capability the profile did not admit, and the
        // instructions are the application's authorship of what the child is.
        async Task<string?> RunChildAsync(ChildAgentRequest request, CancellationToken childToken)
        {
            var child = backend.CreateAgent([.. request.Tools], request.Instructions, request.ProfileName);
            var response = await child.RunAsync(request.Task, cancellationToken: childToken);
            return response.Text;
        }

        var tools = BuildTools(
            corpusRoot,
            notesRoot,
            embeddings,
            memoryStore,
            options.DelegationEnabled,
            RunChildAsync);

        var instructions = BuildInstructions(corpusRoot, notesRoot, options.DelegationEnabled);
        var conversation = PlanConversation(backend, tools, instructions, AgentName);

        // The recall conversation is built only when one was asked for, from the same store and the
        // same provider, carrying the memory family and nothing else. See BuildRecallTools.
        ConversationPlan? recall = null;
        if (options.RecallQuestion is not null)
        {
            recall = PlanConversation(
                backend,
                BuildRecallTools(corpusRoot, embeddings, memoryStore),
                BuildRecallInstructions(),
                RecallAgentName);
        }

        // One handle releases everything, in reverse order of acquisition, so the host disposes a
        // single thing and never learns what was behind it.
        var cleanup = new AsyncDisposableAction(async () =>
        {
            await backend.Cleanup.DisposeAsync();
            await embeddingCleanup.DisposeAsync();
        });

        return new AgentSetup(conversation, cleanup, recall);
    }

    /// <summary>
    ///     Builds one conversation plan: the agent, and the compacting session when the provider
    ///     supports one.
    /// </summary>
    /// <remarks>
    ///     The agent is built either way. On a provider that carries an AgentKit session it is the
    ///     thing a delegated child is started from rather than the thing the root conversation runs
    ///     on, and building it costs nothing.
    /// </remarks>
    /// <param name="backend">The resolved provider backend.</param>
    /// <param name="tools">The tools this conversation offers.</param>
    /// <param name="instructions">The system instructions this conversation is seeded with.</param>
    /// <param name="name">The agent name carried into transcripts and provider-side logging.</param>
    /// <returns>The conversation plan.</returns>
    private static ConversationPlan PlanConversation(
        ProviderBackend backend,
        IList<AIFunction> tools,
        string instructions,
        string name) =>
        new(
            backend.CreateAgent(tools, instructions, name),
            backend.PlanSession?.Invoke(tools, instructions));

    /// <summary>
    ///     Resolves the provider into one agent-building delegate — the single provider switch.
    /// </summary>
    /// <param name="options">The options selecting the provider, model and credentials.</param>
    /// <param name="corpusRoot">The corpus, set as the Copilot runtime's working directory.</param>
    /// <param name="transcript">The tool-call record the session path reports into, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels a slow start.</param>
    /// <returns>The backend's agent factory and cleanup handle.</returns>
    /// <exception cref="CommandLineException">The options name an unsupported provider.</exception>
    private static async Task<ProviderBackend> CreateBackendAsync(
        CommandLineOptions options,
        string corpusRoot,
        TextWriter? transcript,
        CancellationToken cancellationToken)
    {
        return options.Provider switch
        {
            AgentProvider.Copilot => await CreateCopilotBackendAsync(options, corpusRoot, cancellationToken),
            AgentProvider.Ollama => await CreateOllamaBackendAsync(options, transcript, cancellationToken),
            _ => throw new CommandLineException($"Unsupported provider '{options.Provider}'."),
        };
    }

    /// <summary>
    ///     Builds the GitHub Copilot backend, which the host owns and disposes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Authentication is resolved by the application, not by AgentKit. A token supplied on the
    ///     command line or in the environment is passed to the runtime; with none, the runtime uses
    ///     whoever is logged in. The token is never echoed anywhere.
    ///     </para>
    ///     <para>
    ///     Every agent — root and child alike — is built through <c>CopilotAgentFactory</c>, which
    ///     derives each session's tool allow-list from exactly the tools it is given. That is what
    ///     suppresses the runtime's own built-in shell and fetch tools for a delegated agent as well
    ///     as for the parent.
    ///     </para>
    /// </remarks>
    /// <param name="options">The options carrying the model and any token.</param>
    /// <param name="corpusRoot">The working directory for the runtime process.</param>
    /// <param name="cancellationToken">Cancels a slow start.</param>
    /// <returns>The Copilot agent factory and the client as its cleanup handle.</returns>
    private static async Task<ProviderBackend> CreateCopilotBackendAsync(
        CommandLineOptions options,
        string corpusRoot,
        CancellationToken cancellationToken)
    {
        var token = options.ResolveGitHubToken();

        var client = new CopilotClient(new CopilotClientOptions
        {
            WorkingDirectory = corpusRoot,

            // An explicit token wins; with none, fall back to the logged-in user, which is what an
            // interactive developer run uses.
            GitHubToken = token,
            UseLoggedInUser = token is null,
        });

        try
        {
            await client.StartAsync(cancellationToken);
        }
        catch
        {
            // Do not leak a partially started client if StartAsync (or cancellation) fails.
            await client.DisposeAsync();
            throw;
        }

        return new ProviderBackend(
            (tools, instructions, name) =>
                CopilotAgentFactory.Create(client, tools, instructions, name: name, model: options.Model),
            client);
    }

    /// <summary>
    ///     Builds the Ollama backend, reached over HTTP, and the compacting session that runs on it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A generous timeout is set because a cold model's first request can be slow, and a
    ///     premature timeout would masquerade as a tool or model failure.
    ///     </para>
    ///     <para>
    ///     <b>This is where an application states the three things a compacting session needs.</b>
    ///     A provider-session factory carrying the client and the window, a summarizer that runs
    ///     outside the conversation, and — supplied later, with the tools — the options. Nothing
    ///     else about compaction is configured anywhere: the tiers, the slot counts, the rotation
    ///     threshold and the escalation rules are the library's, not settings.
    ///     </para>
    ///     <para>
    ///     <b>The window is read from the provider rather than chosen.</b> An <c>IChatClient</c>
    ///     publishes no context window and AgentKit refuses to guess one, so the application must
    ///     answer — and Ollama can be asked. See <see cref="OllamaContextWindow"/> for which of the
    ///     two figures it reports is the one the server actually enforces.
    ///     </para>
    ///     <para>
    ///     <b>The consolidation model is separate from the conversation model, deliberately.</b>
    ///     <c>ChatClientSummarizer</c> documents why: a consolidation sent through the live session
    ///     would spend the very context it exists to reclaim, and summarization is a cheaper job
    ///     than reasoning. <c>--summary-model</c> is the flag that makes that choice; without it the
    ///     conversation's own model does the work, on a client of its own.
    ///     </para>
    ///     <para>
    ///     <b>The layering beneath the session is not decoration, and two of its four layers should
    ///     not have been necessary.</b> From the bottom: the Ollama client; a
    ///     <see cref="PromptSizeRecorder"/> capturing the size of each real prompt; the
    ///     tool-calling loop, without which a session's tools are declared to the model and never
    ///     invoked; and a <see cref="TurnReportingChatClient"/> that both surfaces the tool activity
    ///     a session turn does not report and repairs the occupancy figure the loop's summed usage
    ///     destroys. Those two classes carry the explanation.
    ///     </para>
    /// </remarks>
    /// <param name="options">The options carrying the Ollama host, models and any stated window.</param>
    /// <param name="transcript">The tool-call record the session reports into, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the window queries.</param>
    /// <returns>The Ollama agent factory, session planner, and a handle disposing what they own.</returns>
    private static async Task<ProviderBackend> CreateOllamaBackendAsync(
        CommandLineOptions options,
        TextWriter? transcript,
        CancellationToken cancellationToken)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(options.Host),
            Timeout = TimeSpan.FromMinutes(10),
        };

        // Ollama needs a concrete model name on the wire, so an unstated --model resolves to the
        // sample's own default here rather than being pre-filled on the options.
        var model = options.Model ?? CommandLineOptions.DefaultOllamaModel;
        var ollama = new OllamaApiClient(http, model);

        // The consolidation client is a second client over the same transport, so the summarizer
        // can run on a different model without a second connection or a second timeout policy.
        var summaryClient = new OllamaApiClient(http, options.SummaryModel ?? model);

        // Beneath the session, bottom up: the provider, the prompt-size recorder, the tool-calling
        // loop, and the turn reporter. See this method's remarks and the two decorator classes.
        var promptSize = new PromptSizeRecorder(ollama);
        var toolCalling = promptSize.AsBuilder().UseFunctionInvocation().Build();
        var sessionClient = new TurnReportingChatClient(
            toolCalling,
            promptSize,
            call =>
            {
                ToolTrace.PrintCall(call);
                ToolTrace.Record(transcript, call);
            },
            (result, call) => ToolTrace.PrintResult(result, call, MemoryPack.FamilyPrefix));

        var window = await OllamaContextWindow.ReadAsync(
            ollama,
            model,
            options.ContextWindow,
            cancellationToken);

        var providerSessions = new ChatClientProviderSessionFactory(sessionClient, window.Tokens);
        var summarizer = new ChatClientSummarizer(summaryClient);

        var cleanup = new AsyncDisposableAction(() =>
        {
            // Disposing the outermost client releases the whole chain beneath it, including the
            // Ollama client itself; the transport is the sample's and is released last.
            sessionClient.Dispose();
            summaryClient.Dispose();
            http.Dispose();
            return ValueTask.CompletedTask;
        });

        return new ProviderBackend(
            (tools, instructions, name) => ChatClientAgentFactory.Create(ollama, tools, instructions, name: name),
            cleanup,
            (tools, instructions) => new CompactingSessionPlan(
                new AgentSessionOptions(summarizer, instructions, [.. tools]),
                providerSessions,
                window));
    }

    /// <summary>
    ///     An <see cref="IAsyncDisposable"/> that runs a supplied action once on disposal.
    /// </summary>
    /// <remarks>
    ///     Used to present several differently shaped resources — an async-disposable Copilot
    ///     client, a disposable HTTP client, an embedding generator — through one handle, so the
    ///     host's cleanup is uniform and provider-neutral.
    /// </remarks>
    private sealed class AsyncDisposableAction : IAsyncDisposable
    {
        /// <summary>
        ///     The disposal action, cleared after it runs so a double-dispose is a no-op.
        /// </summary>
        private Func<ValueTask>? _action;

        /// <summary>
        ///     Creates a handle that runs <paramref name="action"/> the first time it is disposed.
        /// </summary>
        /// <param name="action">The cleanup to run on disposal. Must not be <see langword="null"/>.</param>
        public AsyncDisposableAction(Func<ValueTask> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _action = action;
        }

        /// <summary>
        ///     Runs the cleanup action once; subsequent calls do nothing.
        /// </summary>
        /// <returns>A task that completes when the action has run.</returns>
        public ValueTask DisposeAsync()
        {
            var action = _action;
            _action = null;
            return action?.Invoke() ?? ValueTask.CompletedTask;
        }
    }
}

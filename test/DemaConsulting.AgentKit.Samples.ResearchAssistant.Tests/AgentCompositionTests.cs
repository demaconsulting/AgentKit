using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Agent;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Markdown;
using DemaConsulting.AgentKit.Tools.Memory;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.Todo;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Unit tests for the composition the research-assistant sample builds: its instructions, the
///     packs a delegated agent may draw on, the profiles it registers, and the tool set itself.
/// </summary>
/// <remarks>
///     Two groups of scenario matter here. The first pins the instructions, because this sample's
///     instructions are not decoration: the task-list family is used reliably only when told to
///     imperatively, and a correction from a changed source cites the right document only when the
///     instructions demand it. The second pins the delegation boundary — which packs a child is
///     composed from, and which tools each profile admits — because that boundary is what keeps a
///     child agent out of its parent's plan and record.
/// </remarks>
public class AgentCompositionTests
{
    /// <summary>
    ///     A corpus path unlike any real one, so a match in the instructions cannot be accidental.
    /// </summary>
    private const string CorpusPath = "/fixture-roots/research-corpus";

    /// <summary>
    ///     A notes path outside the corpus, as the sample's real notes folder always is.
    /// </summary>
    private const string NotesPath = "/fixture-roots/research-notes";

    /// <summary>
    ///     Proves the instructions state both locations and the access each carries.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_AnyRun_NamesBothLocationsWithAccess()
    {
        // Arrange / Act: build the instructions for a delegating run
        var instructions = AgentComposition.BuildInstructions(CorpusPath, NotesPath, true);

        // Assert: an application that knows its locations says so, and says which one it can write
        Assert.Multiple(
            () => Assert.Contains($"'{CorpusPath}' (read-only)", instructions, StringComparison.Ordinal),
            () => Assert.Contains($"'{NotesPath}' (read-write)", instructions, StringComparison.Ordinal),
            () => Assert.Contains("You cannot edit the corpus", instructions, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the sample appends the library's published instructions rather than transcribing
    ///     them, so the wording it ships cannot drift from the wording the library wrote.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_AnyRun_AppendsThePublishedSuggestedInstructions()
    {
        // Arrange / Act: build the instructions
        var instructions = AgentComposition.BuildInstructions(CorpusPath, NotesPath, true);

        // Assert: both families' published constants appear verbatim
        Assert.Multiple(
            () => Assert.Contains(TodoPack.SuggestedInstruction, instructions, StringComparison.Ordinal),
            () => Assert.Contains(MemoryPack.SuggestedInstruction, instructions, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the instructions require a correction drawn from a different document to be made
    ///     as a revision citing that document.
    /// </summary>
    /// <remarks>
    ///     This is the scenario a live model was measured getting wrong: offered a conflict raised
    ///     by a new source, it reached for <c>memory_update</c>, which retains the original
    ///     provenance and therefore leaves the corrected memory citing a superseded document.
    ///     Provenance came out right in every run only when the instructions demanded the new
    ///     document be cited, so the demand is pinned here.
    /// </remarks>
    [Fact]
    public void AgentComposition_BuildInstructions_AnyRun_RequiresARevisionToCiteTheNewDocument()
    {
        // Arrange / Act: build the instructions
        var instructions = AgentComposition.BuildInstructions(CorpusPath, NotesPath, true);

        // Assert: the revision tool is named for the changed-source case, the update tool is
        // explicitly ruled out for it, and the reason is stated
        Assert.Multiple(
            () => Assert.Contains(
                $"correct it with {MemoryReviseTool.ToolName} and state the NEW document as the source",
                instructions,
                StringComparison.Ordinal),
            () => Assert.Contains(
                $"Do not use {MemoryUpdateTool.ToolName} for this",
                instructions,
                StringComparison.Ordinal),
            () => Assert.Contains("superseded", instructions, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the instructions warn that recall applies no similarity floor.
    /// </summary>
    /// <remarks>
    ///     <c>memory_recall</c> returns the nearest memories it holds whatever their similarity, so
    ///     a question about an unrecorded subject still comes back with matches. Telling the agent
    ///     to judge each returned descriptor is the application's answer to that, and it is pinned
    ///     here because removing it would silently turn nearest-match noise into confident answers.
    /// </remarks>
    [Fact]
    public void AgentComposition_BuildInstructions_AnyRun_WarnsThatRecallHasNoSimilarityFloor()
    {
        // Arrange / Act: build the instructions
        var instructions = AgentComposition.BuildInstructions(CorpusPath, NotesPath, true);

        // Assert: the absence of a floor is stated, and the agent is told what to do about it
        Assert.Multiple(
            () => Assert.Contains(
                $"{MemoryRecallTool.ToolName} has no similarity floor",
                instructions,
                StringComparison.Ordinal),
            () => Assert.Contains(
                "say plainly that you have nothing recorded on that subject",
                instructions,
                StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the instructions describe delegation truthfully in both directions: what a child
    ///     is when one is offered, and that there is none when it is not.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_DelegationDisabled_DoesNotOfferASubAgent()
    {
        // Arrange / Act: build the instructions with and without delegation
        var delegating = AgentComposition.BuildInstructions(CorpusPath, NotesPath, true);
        var solo = AgentComposition.BuildInstructions(CorpusPath, NotesPath, false);

        // Assert: a delegating run names the tool and states a child shares none of the parent's
        // state; a solo run names no such tool at all
        Assert.Multiple(
            () => Assert.Contains(AgentRunTool.ToolName, delegating, StringComparison.Ordinal),
            () => Assert.Contains("A child agent is not you", delegating, StringComparison.Ordinal),
            () => Assert.DoesNotContain(AgentRunTool.ToolName, solo, StringComparison.Ordinal),
            () => Assert.Contains("no way to delegate work this run", solo, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a delegated agent is composed from the reading families alone.
    /// </summary>
    /// <remarks>
    ///     This is the sample's central safety decision. A child that carried the task-list or
    ///     memory families would have state of the same kind as its parent's, and while AgentKit
    ///     composes a child's tools afresh — so the stores would still be separate — the narrowest
    ///     correct capability set is the one worth shipping. Withholding the pack is stronger than
    ///     withholding the tool name, because a name nothing published can never be conjured back
    ///     by a later edit to a profile.
    /// </remarks>
    [Fact]
    public void AgentComposition_CreateChildPacks_AnyRun_WithholdsTheTodoMemoryAndAgentFamilies()
    {
        // Arrange / Act: ask for the packs a delegated agent is composed from
        var packs = AgentComposition.CreateChildPacks();

        // Assert: the reading families are present and the state-carrying ones are absent
        Assert.Multiple(
            () => Assert.Contains(packs, pack => pack is TextFilePack),
            () => Assert.Contains(packs, pack => pack is FilePack),
            () => Assert.Contains(packs, pack => pack is MarkdownPack),
            () => Assert.DoesNotContain(packs, pack => pack is TodoPack),
            () => Assert.DoesNotContain(packs, pack => pack is MemoryPack),
            () => Assert.DoesNotContain(packs, pack => pack is AgentPack));
    }

    /// <summary>
    ///     Proves the reading profile is narrowed to the corpus and admits only reading tools.
    /// </summary>
    [Fact]
    public void AgentComposition_CreateProfiles_Reader_NarrowsToTheCorpusAndReadsOnly()
    {
        // Arrange / Act: ask for the registered profiles
        var profiles = AgentComposition.CreateProfiles(CorpusPath);
        var reader = profiles.Single(profile => profile.Name == AgentComposition.ReaderProfileName);

        // Assert: one read-only grant over the corpus, no writing tool, and none of the parent's
        // plan or record
        Assert.Multiple(
            () => Assert.Single(reader.Grants),
            () => Assert.Equal(AccessLevel.ReadOnly, reader.Grants[0].Access),
            () => Assert.Contains(TextFileReadTool.ToolName, reader.Tools),
            () => Assert.DoesNotContain(TextFileCreateTool.ToolName, reader.Tools),
            () => Assert.DoesNotContain(TodoSetTool.ToolName, reader.Tools),
            () => Assert.DoesNotContain(MemoryFileTool.ToolName, reader.Tools),
            () => Assert.DoesNotContain(AgentRunTool.ToolName, reader.Tools));
    }

    /// <summary>
    ///     Proves the summarizing profile declares no tools at all, which is a legitimate profile
    ///     rather than an oversight.
    /// </summary>
    [Fact]
    public void AgentComposition_CreateProfiles_Summarizer_DeclaresNoTools()
    {
        // Arrange / Act: ask for the registered profiles
        var profiles = AgentComposition.CreateProfiles(CorpusPath);
        var summarizer = profiles.Single(profile => profile.Name == AgentComposition.SummarizerProfileName);

        // Assert: a child that can only think and answer, and says so to the model choosing it
        Assert.Multiple(
            () => Assert.Empty(summarizer.Tools),
            () => Assert.NotNull(summarizer.Description));
    }

    /// <summary>
    ///     Proves the composed tool set carries all three subject families plus the reading tools.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildTools_DelegationEnabled_ComposesPlanningMemoryAndDelegation()
    {
        // Arrange: the collaborators an application supplies
        using var embeddings = new LexicalEmbeddingGenerator();
        var store = new InMemoryMemoryStore();

        // Act: compose the tool set for a delegating run
        var tools = AgentComposition.BuildTools(
            CorpusPath,
            NotesPath,
            embeddings,
            store,
            true,
            (_, _) => Task.FromResult<string?>("the child's report"));

        var names = tools.Select(tool => tool.Name).ToList();

        // Assert: the three families this sample exists to demonstrate are all present, over the
        // reading tools that give them something to work on
        Assert.Multiple(
            () => Assert.Contains(TodoSetTool.ToolName, names),
            () => Assert.Contains(TodoListTool.ToolName, names),
            () => Assert.Contains(MemoryFileTool.ToolName, names),
            () => Assert.Contains(MemoryRecallTool.ToolName, names),
            () => Assert.Contains(MemoryReviseTool.ToolName, names),
            () => Assert.Contains(AgentRunTool.ToolName, names),
            () => Assert.Contains(TextFileReadTool.ToolName, names),
            () => Assert.Contains(FileListTool.ToolName, names));
    }

    /// <summary>
    ///     Proves that with delegation withheld the sub-agent tool is never created.
    /// </summary>
    /// <remarks>
    ///     Capability gating, not call-time refusal: a pack whose required capability the host did
    ///     not declare is never asked for its tools, so the model is never offered one it cannot
    ///     use.
    /// </remarks>
    [Fact]
    public void AgentComposition_BuildTools_DelegationDisabled_NeverCreatesTheSubAgentTool()
    {
        // Arrange: the collaborators an application supplies
        using var embeddings = new LexicalEmbeddingGenerator();
        var store = new InMemoryMemoryStore();

        // Act: compose the tool set for a run that offers no delegation
        var tools = AgentComposition.BuildTools(
            CorpusPath,
            NotesPath,
            embeddings,
            store,
            false,
            (_, _) => Task.FromResult<string?>(null));

        var names = tools.Select(tool => tool.Name).ToList();

        // Assert: the planning and memory families remain; delegation is absent entirely
        Assert.Multiple(
            () => Assert.Contains(TodoSetTool.ToolName, names),
            () => Assert.Contains(MemoryFileTool.ToolName, names),
            () => Assert.DoesNotContain(AgentRunTool.ToolName, names));
    }

    /// <summary>
    ///     Proves the run's choice of embedding backend is the whole of what that choice costs.
    /// </summary>
    [Fact]
    public void AgentComposition_CreateEmbeddingGenerator_LocalBackend_UsesTheOfflineGenerator()
    {
        // Arrange: a run asking for the offline backend
        var options = new CommandLineOptions { Corpus = CorpusPath, Embeddings = EmbeddingBackend.Local };

        // Act: resolve the generator
        var (generator, cleanup) = AgentComposition.CreateEmbeddingGenerator(options);

        // Assert: the sample's own generator, needing no server and no credential
        Assert.IsType<LexicalEmbeddingGenerator>(generator);
        Assert.NotNull(cleanup);
    }
}

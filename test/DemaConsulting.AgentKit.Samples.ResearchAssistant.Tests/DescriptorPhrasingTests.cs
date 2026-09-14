using System.Text.Json;
using DemaConsulting.AgentKit.Tools.Memory;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Scenarios that pin how descriptor phrasing decides whether a contradiction is caught.
/// </summary>
/// <remarks>
///     <para>
///     <b>These exist because the sample's headline demonstration did not fire in live runs, and
///     the reason was neither a defect nor bad luck.</b> Only the descriptor is embedded, so
///     near-duplicate detection compares descriptors. A model asked to file two contradicting
///     statements of one fact wrote <c>"Relief valve setting for the bilge pump"</c> for the 12-psi
///     statement and <c>"Relief valve setting per field revision (Revision B)"</c> for the 18-psi
///     one — descriptors that distinguish the two <em>because the model had understood that the
///     facts differed</em>. Both were stored, and the store was left holding two contradictory
///     relief-valve values with nothing raised.
///     </para>
///     <para>
///     The resolution is an instruction — descriptors name the subject only — and an instruction
///     cannot be asserted in a live model. What <em>can</em> be asserted is the arithmetic that
///     makes the instruction the right one, and that is what these scenarios do: the same two
///     facts, filed through the sample's own composition, are refused when their descriptors name
///     the subject and are stored side by side when their descriptors name their sources. That
///     converts the finding from prose into a property the suite protects, so an edit that softened
///     the instruction would have to argue with a failing test rather than with a paragraph.
///     </para>
///     <para>
///     These are deliberately end-to-end through <c>AgentComposition.BuildTools</c> rather than
///     against cosine values alone: the claim is about what the sample would actually do, and a
///     bare similarity number would leave the threshold, the store and the tool untested.
///     </para>
/// </remarks>
public class DescriptorPhrasingTests
{
    /// <summary>
    ///     A corpus path unlike any real one, so nothing in these scenarios touches a real file.
    /// </summary>
    private const string CorpusPath = "/fixture-roots/research-corpus";

    /// <summary>
    ///     A notes path outside the corpus, as the sample's real notes folder always is.
    /// </summary>
    private const string NotesPath = "/fixture-roots/research-notes";

    /// <summary>
    ///     The original statement's details and source, from Revision A of the corpus.
    /// </summary>
    private const string OriginalDetails = "The relief valve is set at 12 psi.";

    /// <summary>
    ///     The contradicting statement's details, from Revision B of the corpus.
    /// </summary>
    private const string ContradictingDetails = "The relief valve is set at 18 psi.";

    /// <summary>
    ///     Proves that two contradicting facts filed under one subject-only descriptor collide, so
    ///     the second is refused and the conflict is raised where a reader can see it.
    /// </summary>
    /// <remarks>
    ///     This is what the instruction asks for and what it buys. The two descriptors are
    ///     identical, so their similarity is exactly 1.0 — a property of the descriptor/payload
    ///     split rather than of this generator, since any backend returns 1.0 for identical input —
    ///     and the refusal names the memory it conflicts with.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFile_SubjectOnlyDescriptors_RefuseTheContradictingFact()
    {
        // Arrange: the sample's own composition over one store
        using var embeddings = new LexicalEmbeddingGenerator();
        var file = CreateFileTool(embeddings, new InMemoryMemoryStore());

        // Act: file both statements of the one fact under the subject they are both about
        await FileAsync(file, "Relief valve pressure setting", OriginalDetails, "01-initial-spec.md");
        var second = await FileAsync(
            file, "Relief valve pressure setting", ContradictingDetails, "02-field-revision.md");

        // Assert: the second is declined, the conflict is named, and the arithmetic is reported
        var element = Assert.IsType<JsonElement>(second);
        Assert.Multiple(
            () => Assert.False(element.GetProperty("stored").GetBoolean()),
            () => Assert.Equal("near_duplicate", element.GetProperty("reason").GetString()),
            () => Assert.Equal(1.0, element.GetProperty("similarity").GetDouble(), 5),
            () => Assert.Contains(
                "12 psi",
                element.GetProperty("conflictingDetails").GetString() ?? string.Empty,
                StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves that the same two contradicting facts, filed under the descriptors a live model
    ///     actually wrote, do not collide — both are stored and nothing is raised.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the measured failure, reproduced deterministically. The two descriptors share
    ///     only "relief", "valve" and "setting", so under the sample's lexical generator they score
    ///     roughly 0.40 — far below the 0.88 default — and the store ends up holding two
    ///     contradictory values for one setting.
    ///     </para>
    ///     <para>
    ///     Nothing here is a defect to be fixed in the tools. The check ran, compared what it is
    ///     given to compare, and correctly concluded that two differently-worded subjects are not
    ///     near-duplicates. The descriptor is the input, the model writes the input, and that is
    ///     precisely why near-duplicate detection is best-effort rather than a guarantee.
    ///     </para>
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFile_SourceQualifiedDescriptors_StoreBothContradictingFacts()
    {
        // Arrange: the sample's own composition over one store
        using var embeddings = new LexicalEmbeddingGenerator();
        var store = new InMemoryMemoryStore();
        var file = CreateFileTool(embeddings, store);

        // Act: file both statements under the descriptors the live model wrote for them
        await FileAsync(
            file, "Relief valve setting for the bilge pump", OriginalDetails, "01-initial-spec.md");
        var second = await FileAsync(
            file,
            "Relief valve setting per field revision (Revision B)",
            ContradictingDetails,
            "02-field-revision.md");

        // Assert: stored, unremarked, and the store now holds two contradictory memories
        var element = Assert.IsType<JsonElement>(second);
        Assert.Multiple(
            () => Assert.True(element.GetProperty("stored").GetBoolean()),
            () => Assert.Equal(2, element.GetProperty("memoryCount").GetInt32()));
        Assert.Equal(2, await store.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves the two phrasings sit on opposite sides of the published default threshold, which
    ///     is the arithmetic the two scenarios above rest on.
    /// </summary>
    /// <remarks>
    ///     Stating both numbers in one place is what makes the instruction arguable rather than
    ///     asserted: a subject-only pair scores 1.0 and is caught by any threshold at or below it,
    ///     while the source-qualified pair scores well under half and is caught by none that any
    ///     author would choose. No threshold separates the second pair from genuinely different
    ///     facts, so no threshold is the answer — the phrasing is.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task LexicalEmbeddingGenerator_ConflictingFacts_ScoreByPhrasingNotByContradiction()
    {
        // Arrange: one conflict, described two ways
        using var generator = new LexicalEmbeddingGenerator();
        var subjectFirst = await EmbedAsync(generator, "Relief valve pressure setting");
        var subjectSecond = await EmbedAsync(generator, "Relief valve pressure setting");
        var qualifiedFirst = await EmbedAsync(generator, "Relief valve setting for the bilge pump");
        var qualifiedSecond = await EmbedAsync(generator, "Relief valve setting per field revision (Revision B)");

        // Act: score each pair
        var subjectScore = Cosine(subjectFirst, subjectSecond);
        var qualifiedScore = Cosine(qualifiedFirst, qualifiedSecond);

        // Assert: the subject-only pair collides outright; the source-qualified pair is not close
        Assert.Multiple(
            () => Assert.Equal(1.0, subjectScore, 5),
            () => Assert.True(
                qualifiedScore < 0.5,
                $"The source-qualified pair scored {qualifiedScore}, higher than the measured 0.40."),
            () => Assert.True(
                qualifiedScore < MemoryOptions.DefaultNearDuplicateThreshold,
                $"The source-qualified pair scored {qualifiedScore} and would have been refused."));
    }

    /// <summary>
    ///     Proves the sample's instructions demand a subject-only descriptor and state why.
    /// </summary>
    /// <remarks>
    ///     The reason is pinned alongside the rule deliberately. A rule without a reason gets
    ///     applied inconsistently, and here the reasoning that overrides it — "these two facts are
    ///     different, so I will describe them differently" — is exactly the reasoning that produced
    ///     the failure this instruction answers.
    /// </remarks>
    [Fact]
    public void AgentComposition_BuildInstructions_AnyRun_RequiresASubjectOnlyDescriptor()
    {
        // Arrange / Act: build the instructions for a delegating run
        var instructions = AgentComposition.BuildInstructions(CorpusPath, NotesPath, true);

        // Assert: the rule, the fields provenance belongs in instead, and the reason
        Assert.Multiple(
            () => Assert.Contains(
                "The descriptor names the subject only", instructions, StringComparison.Ordinal),
            () => Assert.Contains(
                "never in the descriptor", instructions, StringComparison.Ordinal),
            () => Assert.Contains(
                "Only the descriptor is compared", instructions, StringComparison.Ordinal),
            () => Assert.Contains(
                "filed silently beside the memory it", instructions, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Builds the sample's own tool set and returns its filing tool.
    /// </summary>
    /// <param name="embeddings">The embedding generator the composition is given.</param>
    /// <param name="store">The store the composition files into.</param>
    /// <returns>The sample composition's <c>memory_file</c> tool.</returns>
    private static AIFunction CreateFileTool(LexicalEmbeddingGenerator embeddings, IMemoryStore store)
    {
        return AgentComposition
            .BuildTools(CorpusPath, NotesPath, embeddings, store, false, (_, _) => Task.FromResult<string?>(null))
            .Single(tool => tool.Name == MemoryFileTool.ToolName);
    }

    /// <summary>
    ///     Files one memory through the sample's filing tool.
    /// </summary>
    /// <param name="file">The filing tool to invoke.</param>
    /// <param name="descriptor">The descriptor to file.</param>
    /// <param name="details">The details to file.</param>
    /// <param name="sourceDocument">The document to cite.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> FileAsync(
        AIFunction file,
        string descriptor,
        string details,
        string sourceDocument)
    {
        return await file.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["descriptor"] = descriptor,
                ["details"] = details,
                ["sourceDocument"] = sourceDocument,
            }),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Embeds one text and returns its vector.
    /// </summary>
    /// <param name="generator">The generator under test.</param>
    /// <param name="text">The text to embed.</param>
    /// <returns>The generated vector.</returns>
    private static async Task<float[]> EmbedAsync(LexicalEmbeddingGenerator generator, string text)
    {
        var generated = await generator.GenerateAsync(
            [text],
            cancellationToken: TestContext.Current.CancellationToken);
        return generated[0].Vector.ToArray();
    }

    /// <summary>
    ///     Computes the cosine similarity of two vectors.
    /// </summary>
    /// <remarks>
    ///     Written out here rather than borrowed from the library, because the point of these
    ///     scenarios is what the memory family <em>will</em> compute from these vectors; reusing
    ///     the library's arithmetic would make the test agree with itself.
    /// </remarks>
    /// <param name="first">The first vector.</param>
    /// <param name="second">The second vector.</param>
    /// <returns>The cosine similarity, in the range -1 to 1.</returns>
    private static double Cosine(float[] first, float[] second)
    {
        var dot = 0.0;
        var firstMagnitude = 0.0;
        var secondMagnitude = 0.0;

        for (var index = 0; index < first.Length; index++)
        {
            dot += first[index] * (double)second[index];
            firstMagnitude += first[index] * (double)first[index];
            secondMagnitude += second[index] * (double)second[index];
        }

        return dot / (Math.Sqrt(firstMagnitude) * Math.Sqrt(secondMagnitude));
    }
}

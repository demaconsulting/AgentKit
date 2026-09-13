using DemaConsulting.AgentKit.Tools.Memory;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Unit tests for the sample's offline embedding generator.
/// </summary>
/// <remarks>
///     These scenarios establish the two properties the memory family actually depends on from an
///     embedding backend — that a vector is stable and that cosine similarity ranks a restatement
///     above an unrelated sentence — and they establish the limitation a reader must understand
///     before copying the generator: it measures shared wording, not shared meaning.
/// </remarks>
public class LexicalEmbeddingGeneratorTests
{
    /// <summary>
    ///     Proves the same text embeds to the same vector every time, in any process.
    /// </summary>
    /// <remarks>
    ///     Without this a stored vector could not outlive the process that produced it, and two
    ///     runs would disagree about whether two memories were near-duplicates.
    /// </remarks>
    [Fact]
    public async Task LexicalEmbeddingGenerator_GenerateAsync_SameText_ProducesTheSameVector()
    {
        // Arrange: one generator, asked twice
        using var generator = new LexicalEmbeddingGenerator();

        // Act: embed the same descriptor twice
        var first = await EmbedAsync(generator, "The relief valve is set at 12 psi.");
        var second = await EmbedAsync(generator, "The relief valve is set at 12 psi.");

        // Assert: identical vectors, coordinate for coordinate
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Proves vectors are unit length, which is what makes one fixed cosine threshold
    ///     meaningful across descriptors of different sizes.
    /// </summary>
    [Fact]
    public async Task LexicalEmbeddingGenerator_GenerateAsync_AnyText_ProducesAUnitVector()
    {
        // Arrange: a generator and a descriptor of realistic length
        using var generator = new LexicalEmbeddingGenerator();

        // Act: embed it and measure its magnitude
        var vector = await EmbedAsync(generator, "The float switch closes at 45 millimeters of water depth.");
        var magnitude = Math.Sqrt(vector.Sum(coordinate => (double)coordinate * coordinate));

        // Assert: unit length, to floating-point tolerance
        Assert.Equal(1.0, magnitude, 5);
    }

    /// <summary>
    ///     Proves a restatement of one fact scores as a near-duplicate while an unrelated fact from
    ///     the same corpus does not.
    /// </summary>
    /// <remarks>
    ///     This is the property the memory family's near-duplicate arithmetic rests on, measured
    ///     against the library's own published default threshold rather than an arbitrary one. It
    ///     is what lets the sample demonstrate a contradiction being caught rather than filed twice.
    /// </remarks>
    [Fact]
    public async Task LexicalEmbeddingGenerator_GenerateAsync_RestatedFact_ScoresAboveTheDefaultThreshold()
    {
        // Arrange: one fact, a contradicting restatement of it, and an unrelated fact
        using var generator = new LexicalEmbeddingGenerator();
        var original = await EmbedAsync(
            generator, "Relief valve setting for the Harbor Skiff bilge pump is 12 psi.");
        var contradiction = await EmbedAsync(
            generator, "Relief valve setting for the Harbor Skiff bilge pump is 18 psi.");
        var unrelated = await EmbedAsync(
            generator, "Diaphragm inspection interval for the Harbor Skiff bilge pump is 250 running hours.");

        // Act: score both against the original
        var restatementScore = Cosine(original, contradiction);
        var unrelatedScore = Cosine(original, unrelated);

        // Assert: the contradiction is caught by the published default threshold; the unrelated
        // fact is well clear of it and is therefore stored as its own memory
        Assert.Multiple(
            () => Assert.True(
                restatementScore >= MemoryOptions.DefaultNearDuplicateThreshold,
                $"A restatement of one fact scored {restatementScore}, below the near-duplicate threshold."),
            () => Assert.True(
                unrelatedScore < MemoryOptions.DefaultNearDuplicateThreshold,
                $"An unrelated fact scored {unrelatedScore}, at or above the near-duplicate threshold."));
    }

    /// <summary>
    ///     Proves the generator measures wording rather than meaning, which is the limitation that
    ///     makes it unsuitable for a real application.
    /// </summary>
    /// <remarks>
    ///     Asserting the weakness rather than merely documenting it is deliberate: a reader who
    ///     copies this generator into a production application needs the limitation to be a stated,
    ///     checked fact rather than a caveat in a comment. Two sentences stating the same fact in
    ///     different words score low, and that is exactly why <c>--embeddings ollama</c> exists.
    /// </remarks>
    [Fact]
    public async Task LexicalEmbeddingGenerator_GenerateAsync_ParaphrasedFact_ScoresLowBecauseItIsLexical()
    {
        // Arrange: one fact, and the same fact in entirely different words
        using var generator = new LexicalEmbeddingGenerator();
        var stated = await EmbedAsync(
            generator, "Relief valve setting for the Harbor Skiff bilge pump is 12 psi.");
        var paraphrased = await EmbedAsync(
            generator, "Pressure relief opens at twelve pounds per square inch.");

        // Act: score the paraphrase against the original
        var score = Cosine(stated, paraphrased);

        // Assert: a real embedding model would score these high; this one does not, by construction
        Assert.True(
            score < MemoryOptions.DefaultNearDuplicateThreshold,
            $"A paraphrase scored {score}: the offline generator is lexical and is not expected to.");
    }

    /// <summary>
    ///     Proves text with nothing to embed still yields a usable vector.
    /// </summary>
    /// <remarks>
    ///     The zero vector has no direction and its cosine against anything is undefined, which
    ///     would surface as a near-duplicate check that neither fires nor cleanly declines.
    /// </remarks>
    [Fact]
    public async Task LexicalEmbeddingGenerator_GenerateAsync_EmptyText_ProducesADefinedVector()
    {
        // Arrange: a generator asked to embed nothing usable
        using var generator = new LexicalEmbeddingGenerator();

        // Act: embed an empty descriptor and one that is only punctuation
        var empty = await EmbedAsync(generator, string.Empty);
        var punctuation = await EmbedAsync(generator, " ... ");

        // Assert: both are unit vectors, and alike — which is what they are
        Assert.Multiple(
            () => Assert.Equal(1.0, Math.Sqrt(empty.Sum(value => (double)value * value)), 5),
            () => Assert.Equal(empty, punctuation));
    }

    /// <summary>
    ///     Proves a batch is answered in input order, which every caller of the abstraction relies
    ///     on to match a vector back to the text it came from.
    /// </summary>
    [Fact]
    public async Task LexicalEmbeddingGenerator_GenerateAsync_SeveralTexts_AnswersInInputOrder()
    {
        // Arrange: three distinct descriptors
        using var generator = new LexicalEmbeddingGenerator();
        string[] texts = ["relief valve pressure", "float switch depth", "diaphragm inspection interval"];

        // Act: embed them as one batch, and each on its own
        var batch = await generator.GenerateAsync(texts, cancellationToken: TestContext.Current.CancellationToken);
        var individually = new List<float[]>();
        foreach (var text in texts)
        {
            individually.Add(await EmbedAsync(generator, text));
        }

        // Assert: same count, same order, same vectors
        Assert.Equal(texts.Length, batch.Count);
        for (var index = 0; index < texts.Length; index++)
        {
            Assert.Equal(individually[index], batch[index].Vector.ToArray());
        }
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
    ///     Written out here rather than reached for from the library, because the point of these
    ///     scenarios is what the memory family <em>will</em> compute from these vectors; borrowing
    ///     the library's own arithmetic would make the test agree with itself.
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

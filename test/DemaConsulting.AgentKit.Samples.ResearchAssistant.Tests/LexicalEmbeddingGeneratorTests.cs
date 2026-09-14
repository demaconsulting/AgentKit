using DemaConsulting.AgentKit.Tools.Memory;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Unit tests for the sample's offline embedding generator.
/// </summary>
/// <remarks>
///     These scenarios establish the two properties the memory family actually depends on from an
///     embedding backend — that a vector is stable and that cosine similarity ranks a restatement
///     above an unrelated sentence — and they establish the limitations a reader must understand
///     before copying the generator: it measures shared wording, not shared meaning, and a single
///     differing numeral is only ever one token among many.
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
    ///     Proves a numeral is only ever one token among many, so whether a numeric contradiction
    ///     is caught depends on how long the descriptor is rather than on what the numbers mean.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the generator's sharpest limitation for the use case the sample is built around —
    ///     a superseded engineering value, 12 psi becoming 18 psi — and it is asserted here rather
    ///     than merely described, for the same reason the paraphrase scenario is.
    ///     </para>
    ///     <para>
    ///     Two descriptors that differ in exactly one word out of <c>n</c> share <c>n - 1</c> unit
    ///     coordinates out of <c>n</c>, so their cosine is <c>(n - 1) / n</c> whatever that word is.
    ///     Nothing about the word being a <em>number</em>, nor about the two numbers disagreeing,
    ///     enters the arithmetic. The consequence is stated plainly by the two pairs below: an
    ///     eight-token statement of the conflict falls <em>below</em> the published default
    ///     threshold and would therefore be stored as a second, contradicting memory, while a
    ///     twelve-token statement of the same conflict clears it and is refused.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task LexicalEmbeddingGenerator_GenerateAsync_DifferingNumeral_ScoresByTokenCountAlone()
    {
        // Arrange: the same conflict stated twice, once briefly and once at length
        using var generator = new LexicalEmbeddingGenerator();
        var shortOriginal = await EmbedAsync(generator, "The relief valve is set at 12 psi.");
        var shortConflict = await EmbedAsync(generator, "The relief valve is set at 18 psi.");
        var longOriginal = await EmbedAsync(
            generator, "Relief valve setting for the Harbor Skiff bilge pump is 12 psi.");
        var longConflict = await EmbedAsync(
            generator, "Relief valve setting for the Harbor Skiff bilge pump is 18 psi.");

        // Act: score each pair
        var shortScore = Cosine(shortOriginal, shortConflict);
        var longScore = Cosine(longOriginal, longConflict);

        // Assert: each score is exactly (n - 1) / n for that descriptor's token count — eight
        // tokens and twelve tokens — and the shorter statement of the very same conflict falls
        // below the threshold the longer one clears
        Assert.Multiple(
            () => Assert.Equal(7.0 / 8.0, shortScore, 5),
            () => Assert.Equal(11.0 / 12.0, longScore, 5),
            () => Assert.True(
                shortScore < MemoryOptions.DefaultNearDuplicateThreshold,
                $"The brief statement of the conflict scored {shortScore} and would be stored."),
            () => Assert.True(
                longScore >= MemoryOptions.DefaultNearDuplicateThreshold,
                $"The fuller statement of the conflict scored {longScore} and would not be refused."));
    }

    /// <summary>
    ///     Proves two descriptors built from the same words score exactly 1.0, however much the
    ///     details they introduce disagree.
    /// </summary>
    /// <remarks>
    ///     Only the descriptor is embedded; the details payload is never searched and never
    ///     vectorized. A model that writes a topic as its descriptor and puts the value in the
    ///     details therefore produces identical vectors for two memories that contradict each other
    ///     outright. That is a property of the descriptor/payload split rather than of this
    ///     generator — any embedding backend gives 1.0 for identical input — and it is what a
    ///     near-duplicate refusal at similarity 1.0 in a live run actually demonstrates.
    /// </remarks>
    [Fact]
    public async Task LexicalEmbeddingGenerator_GenerateAsync_SameWordsInAnyOrder_ScoresExactlyOne()
    {
        // Arrange: one topic descriptor, and the same words reordered
        using var generator = new LexicalEmbeddingGenerator();
        var first = await EmbedAsync(generator, "Relief valve pressure setting");
        var second = await EmbedAsync(generator, "Setting pressure valve relief");

        // Act: score them against each other
        var score = Cosine(first, second);

        // Assert: a bag of words has no order, so these are the same vector
        Assert.Equal(1.0, score, 5);
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

using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     A deterministic, offline embedding generator standing in for a real one throughout the
///     memory family's tests.
/// </summary>
/// <remarks>
///     <para>
///     <b>No network, no model file, no service.</b> The memory family's own behavior — what it
///     stores, what it refuses, what it returns — does not depend on which embedding backend an
///     application chose, so verifying it against a real one would prove something about the
///     backend rather than about the family, while making the suite dependent on a running service.
///     A real generator is the application author's concern and is exercised where an author
///     exercises it: in their own application.
///     </para>
///     <para>
///     <b>The vectors carry real, if crude, semantics.</b> Each whitespace-separated word of the
///     text contributes to one coordinate, so two sentences sharing most of their words score high
///     cosine against each other and two sentences sharing none score zero. That is enough to write
///     a near-duplicate test whose inputs read like the sentences a model would actually file,
///     rather than as hand-picked float arrays whose similarity a reader has to take on trust.
///     </para>
///     <para>
///     <see cref="CallCount"/> is recorded so a test can prove that a tool which must not embed —
///     <c>memory_update</c> — did not.
///     </para>
/// </remarks>
internal sealed class StubEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    /// <summary>
    ///     The number of coordinates each vector carries.
    /// </summary>
    private readonly int _dimensions;

    /// <summary>
    ///     Whether the generator returns no embedding at all, standing in for a misbehaving backend.
    /// </summary>
    private readonly bool _producesNothing;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StubEmbeddingGenerator"/> class.
    /// </summary>
    /// <param name="dimensions">The number of coordinates each vector carries.</param>
    /// <param name="producesNothing">
    ///     When true, the generator returns an empty result, standing in for a backend that
    ///     answered without producing a vector.
    /// </param>
    public StubEmbeddingGenerator(int dimensions = 1024, bool producesNothing = false)
    {
        _dimensions = dimensions;
        _producesNothing = producesNothing;
    }

    /// <summary>
    ///     Gets the number of texts this generator has been asked to embed.
    /// </summary>
    public int CallCount { get; private set; }

    /// <summary>
    ///     Produces a vector for each supplied text.
    /// </summary>
    /// <param name="values">The texts to embed.</param>
    /// <param name="options">Ignored; the stub has nothing to configure.</param>
    /// <param name="cancellationToken">Ignored; the stub completes synchronously.</param>
    /// <returns>The generated embeddings, or an empty collection when so configured.</returns>
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var generated = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var value in values)
        {
            CallCount++;

            if (!_producesNothing)
            {
                generated.Add(new Embedding<float>(Vectorize(value)));
            }
        }

        return Task.FromResult(generated);
    }

    /// <summary>
    ///     Reports a service this generator provides, of which there are none.
    /// </summary>
    /// <param name="serviceType">The service being asked for.</param>
    /// <param name="serviceKey">The key of the service being asked for.</param>
    /// <returns>The generator itself when it is the type asked for; otherwise null.</returns>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceKey is null && serviceType?.IsInstanceOfType(this) == true ? this : null;
    }

    /// <summary>
    ///     Releases nothing, because the stub holds nothing.
    /// </summary>
    public void Dispose()
    {
        // Nothing to release: the stub holds no unmanaged or disposable state.
    }

    /// <summary>
    ///     Turns text into a bag-of-words vector.
    /// </summary>
    /// <remarks>
    ///     Each word is folded to lower case and mapped to one coordinate by an ordinal hash, so the
    ///     mapping is stable across runs and across platforms. Repeated words add weight, which is
    ///     what makes a sentence more similar to a rewording of itself than to an unrelated
    ///     sentence.
    /// </remarks>
    /// <param name="text">The text to vectorize.</param>
    /// <returns>The vector.</returns>
    private float[] Vectorize(string text)
    {
        var vector = new float[_dimensions];

        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = word.ToLowerInvariant().Trim('.', ',', ';', ':', '-');
            if (normalized.Length == 0)
            {
                continue;
            }

            vector[StableIndex(normalized)] += 1.0f;
        }

        return vector;
    }

    /// <summary>
    ///     Maps a word to a coordinate, stably across runs and platforms.
    /// </summary>
    /// <remarks>
    ///     <c>string.GetHashCode</c> is randomized per process in .NET, so it cannot be used here: a
    ///     test whose similarity depends on the hash would pass and fail on alternate runs.
    /// </remarks>
    /// <param name="word">The word to map.</param>
    /// <returns>A coordinate in range.</returns>
    private int StableIndex(string word)
    {
        var accumulated = 17;
        foreach (var character in word)
        {
            accumulated = ((accumulated * 31) + character) % 1_000_003;
        }

        return Math.Abs(accumulated) % _dimensions;
    }
}

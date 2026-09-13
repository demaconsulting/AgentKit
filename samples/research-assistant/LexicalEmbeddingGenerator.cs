using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     The sample's own offline embedding generator: a deterministic hashed bag-of-words vector,
///     needing no server, no credential and no downloaded model.
/// </summary>
/// <remarks>
///     <para>
///     <b>This type exists because choosing an embedding backend is the application's decision.</b>
///     <c>MemoryPack</c> requires an <c>IEmbeddingGenerator</c> and never inspects which backend it
///     wraps, so a sample has to supply one — and every candidate a sample could supply costs the
///     reader something. A hosted model costs a credential, a local server costs an installation,
///     and a bundled ONNX model costs a binary in the repository that every clone pays for. This
///     generator costs none of those: it is forty lines of arithmetic, so the sample runs from a
///     fresh clone with nothing installed, and the flag that replaces it with a real model is the
///     demonstration that the choice was never AgentKit's to make.
///     </para>
///     <para>
///     <b>It measures shared wording, not shared meaning, and that limitation is the point to
///     understand before copying it.</b> Two sentences using the same words score high against each
///     other; two sentences saying the same thing in different words score low. So it detects the
///     near-duplicate case the memory family cares most about — a fact being re-filed, or
///     contradicted, in substantially the same words — but it will not recall a memory phrased
///     differently from the question. <b>Do not use it in a real application</b>: run
///     <c>--embeddings ollama</c>, or supply any other <c>IEmbeddingGenerator</c>, and nothing else
///     in this sample changes.
///     </para>
///     <para>
///     Vectors are L2-normalized, because the memory family compares descriptors by cosine
///     similarity against an author-configured threshold. Normalizing here means a long descriptor
///     and a short one are compared by direction rather than by length, which is what makes a
///     single fixed threshold meaningful across descriptors of different sizes.
///     </para>
///     <para>
///     The class is stateless after construction and is safe for concurrent use.
///     </para>
/// </remarks>
public sealed class LexicalEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    /// <summary>
    ///     The number of coordinates each generated vector carries.
    /// </summary>
    /// <remarks>
    ///     Large enough that two unrelated words rarely collide onto one coordinate, and small
    ///     enough that a corpus of memories costs nothing to hold. It is not chosen to match any
    ///     real embedding model's width, because nothing compares vectors from two generators:
    ///     every vector in one store came from one generator.
    /// </remarks>
    public const int Dimensions = 512;

    /// <summary>
    ///     The characters trimmed from the ends of a token before it is hashed.
    /// </summary>
    /// <remarks>
    ///     Punctuation attached to a word would make <c>psi.</c> a different token from <c>psi</c>,
    ///     which would lower the similarity of two statements of the same fact for no reason a
    ///     reader would accept.
    /// </remarks>
    private static readonly char[] TokenTrimCharacters =
        ['.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'', '`', '*', '_', '-', '#'];

    /// <summary>
    ///     Initializes a new instance of the <see cref="LexicalEmbeddingGenerator"/> class.
    /// </summary>
    /// <remarks>
    ///     Declared explicitly rather than left implicit so that the documentation the sample
    ///     builds describes every public member, as the library projects do.
    /// </remarks>
    public LexicalEmbeddingGenerator()
    {
    }

    /// <summary>
    ///     Produces one vector for each supplied text.
    /// </summary>
    /// <remarks>
    ///     Completes synchronously: there is no I/O to await. The returned collection is in the
    ///     order of the inputs, which is the contract every caller of an embedding generator relies
    ///     on.
    /// </remarks>
    /// <param name="values">The texts to embed. Must not be <see langword="null"/>.</param>
    /// <param name="options">Ignored; this generator has nothing to configure.</param>
    /// <param name="cancellationToken">Ignored; the work is synchronous and bounded.</param>
    /// <returns>The generated embeddings, one per input, in input order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var generated = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
        {
            generated.Add(new Embedding<float>(Vectorize(value)));
        }

        return Task.FromResult(generated);
    }

    /// <summary>
    ///     Reports a service this generator provides.
    /// </summary>
    /// <param name="serviceType">The service being asked for.</param>
    /// <param name="serviceKey">The key of the service being asked for.</param>
    /// <returns>The generator itself when it is the type asked for; otherwise <see langword="null"/>.</returns>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceKey is null && serviceType?.IsInstanceOfType(this) == true ? this : null;
    }

    /// <summary>
    ///     Releases nothing, because this generator holds nothing.
    /// </summary>
    public void Dispose()
    {
        // Nothing to release: the generator holds no unmanaged or disposable state.
    }

    /// <summary>
    ///     Turns one text into a normalized hashed bag-of-words vector.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A repeated word adds weight but with diminishing return (the square root of its count),
    ///     so a descriptor that says "pressure" three times does not drown out everything else it
    ///     says. Text with no usable token at all — an empty descriptor, or one that is only
    ///     punctuation — gets a single fixed coordinate rather than the zero vector, because the
    ///     zero vector has no direction and its cosine against anything is undefined.
    ///     </para>
    /// </remarks>
    /// <param name="text">The text to vectorize; may be <see langword="null"/> or empty.</param>
    /// <returns>A unit-length vector of <see cref="Dimensions"/> coordinates.</returns>
    private static float[] Vectorize(string? text)
    {
        var counts = new float[Dimensions];

        foreach (var token in Tokenize(text))
        {
            counts[StableIndex(token)] += 1.0f;
        }

        var vector = new float[Dimensions];
        var sumOfSquares = 0.0;
        for (var index = 0; index < Dimensions; index++)
        {
            // Diminishing weight for repetition: a word said twice matters more than once, but not
            // twice as much.
            var weight = counts[index] == 0.0f ? 0.0f : MathF.Sqrt(counts[index]);
            vector[index] = weight;
            sumOfSquares += weight * (double)weight;
        }

        if (sumOfSquares == 0.0)
        {
            // No usable token. A unit vector on one fixed coordinate keeps every cosine defined and
            // makes all such texts alike, which is exactly what they are.
            vector[0] = 1.0f;
            return vector;
        }

        var magnitude = (float)Math.Sqrt(sumOfSquares);
        for (var index = 0; index < Dimensions; index++)
        {
            vector[index] /= magnitude;
        }

        return vector;
    }

    /// <summary>
    ///     Splits text into the lower-cased, punctuation-trimmed tokens that carry its wording.
    /// </summary>
    /// <param name="text">The text to split; may be <see langword="null"/>.</param>
    /// <returns>The tokens, in order, with empty results dropped.</returns>
    private static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = word.ToLowerInvariant().Trim(TokenTrimCharacters);
            if (token.Length > 0)
            {
                yield return token;
            }
        }
    }

    /// <summary>
    ///     Maps a token to a coordinate, stably across runs, processes and platforms.
    /// </summary>
    /// <remarks>
    ///     <c>string.GetHashCode</c> is randomized per process in .NET, so it cannot be used: two
    ///     runs of the sample would then disagree about whether two memories were near-duplicates,
    ///     and a stored vector could never outlive the process that produced it. FNV-1a is used
    ///     instead because it is small, well-defined, and deterministic everywhere — it is not, and
    ///     does not need to be, cryptographic.
    /// </remarks>
    /// <param name="token">The token to map.</param>
    /// <returns>A coordinate in the range 0 to <see cref="Dimensions"/> - 1.</returns>
    private static int StableIndex(string token)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var character in token)
        {
            hash ^= character;
            hash *= prime;
        }

        return (int)(hash % Dimensions);
    }
}

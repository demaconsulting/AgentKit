using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     The one place the memory family turns a descriptor into a vector, shared by the tools that
///     file, revise and recall.
/// </summary>
/// <remarks>
///     <para>
///     <b>The text is handed to the generator exactly as the model wrote it.</b> No task prefix, no
///     instruction wrapper, no normalization. Several embedding models publish prefixes such as
///     <c>search_document:</c> that callers are invited to prepend; adding one here was measured to
///     change the numbers, and the absence of the prefix is what reproduced a reference
///     implementation's output at 1.00000 cosine. More importantly, a prefix would be this library
///     deciding something about the author's chosen backend, which it is not entitled to do — the
///     whole point of taking an
///     <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> is that AgentKit never learns whether
///     the author picked Ollama, a local Foundry model or an offline ONNX model.
///     </para>
///     <para>
///     <b>A generator that fails is not converted into a refusal.</b> A refusal is for a request a
///     model could have made differently; an embedding backend that is unreachable or misconfigured
///     is a host fault, the model can do nothing about it, and telling the model "the memory was not
///     stored" while the real problem is that nothing can be stored at all invites it to keep
///     trying. The exception therefore propagates to the host, which is the party that can fix it.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
internal static class MemoryEmbedding
{
    /// <summary>
    ///     Embeds one piece of text through the generator the application supplied.
    /// </summary>
    /// <remarks>
    ///     The single-value overload of the generator contract is used rather than a batch, because
    ///     every caller in this family embeds exactly one descriptor per tool call and a batch of
    ///     one would only add a shape to unpack.
    /// </remarks>
    /// <param name="generator">The embedding generator the application supplied.</param>
    /// <param name="text">The text to embed. Must be non-null and non-empty.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The vector the generator produced.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="generator"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="text"/> is <see langword="null"/> or empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the generator returns no embedding, or returns one holding no values. A
    ///     vector with nothing in it cannot be compared against anything, so accepting it would
    ///     store a memory that could never be recalled.
    /// </exception>
    public static async Task<ReadOnlyMemory<float>> GenerateAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentException.ThrowIfNullOrEmpty(text);

        // The text goes across unaltered; see the type-level remarks on why no prefix is added.
        var generated = await generator
            .GenerateAsync([text], options: null, cancellationToken)
            .ConfigureAwait(false);

        var embedding = generated.Count > 0 ? generated[0] : null;
        if (embedding is null || embedding.Vector.IsEmpty)
        {
            throw new InvalidOperationException(
                "The embedding generator returned no vector for the text it was given, so the "
                + "memory could not be made findable.");
        }

        return embedding.Vector;
    }
}

namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     Where an agent's memories are kept, and the only thing the memory tools know about
///     persistence.
/// </summary>
/// <remarks>
///     <para>
///     <b>The interface exists so the application author chooses persistence, not this library.</b>
///     The tools file, recall, update, revise and forget through this contract alone, so an author
///     who wants memories to survive a process — in a database, a vector service or a file — writes
///     one implementation and changes nothing else. AgentKit supplies
///     <see cref="InMemoryMemoryStore"/> as the default because a working default is what makes the
///     family attachable in one line, not because in-memory is the right answer for every
///     application.
///     </para>
///     <para>
///     <b>Similarity is cosine, and ordering is the store's responsibility.</b>
///     <see cref="SearchAsync"/> must return matches in descending
///     <see cref="MemoryMatch.Similarity"/>, because the near-duplicate decision reads only the
///     first one. An implementation that returned a distance, or returned matches unordered, would
///     silently change what the author's configured threshold means.
///     </para>
///     <para>
///     Implementations must be safe for concurrent use: one store is shared by all five tools of a
///     composition, and an agent may have more than one tool call in flight.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     The shape an author implements to substitute persistence. Only the five operations below are
///     ever called, and the tools never reach past them.
///     </para>
///     <code>
///     internal sealed class LoggingMemoryStore : IMemoryStore
///     {
///         private readonly IMemoryStore _inner = new InMemoryMemoryStore();
///
///         public Task AddAsync(MemoryRecord memory, CancellationToken cancellationToken)
///         {
///             Console.WriteLine("filed " + memory.Id);
///             return _inner.AddAsync(memory, cancellationToken);
///         }
///
///         public Task&lt;MemoryRecord?&gt; FindAsync(string id, CancellationToken cancellationToken) =&gt;
///             _inner.FindAsync(id, cancellationToken);
///
///         public Task&lt;bool&gt; ReplaceAsync(MemoryRecord memory, CancellationToken cancellationToken) =&gt;
///             _inner.ReplaceAsync(memory, cancellationToken);
///
///         public Task&lt;bool&gt; RemoveAsync(string id, CancellationToken cancellationToken) =&gt;
///             _inner.RemoveAsync(id, cancellationToken);
///
///         public Task&lt;IReadOnlyList&lt;MemoryMatch&gt;&gt; SearchAsync(
///             ReadOnlyMemory&lt;float&gt; vector,
///             int count,
///             CancellationToken cancellationToken) =&gt;
///             _inner.SearchAsync(vector, count, cancellationToken);
///
///         public Task&lt;int&gt; CountAsync(CancellationToken cancellationToken) =&gt;
///             _inner.CountAsync(cancellationToken);
///     }
///     </code>
/// </example>
public interface IMemoryStore
{
    /// <summary>
    ///     Adds a memory the store does not already hold.
    /// </summary>
    /// <remarks>
    ///     The identifier is assigned by the caller before the memory reaches the store, so an
    ///     identifier the store already holds is a programming error rather than something a model
    ///     provoked.
    /// </remarks>
    /// <param name="memory">The memory to add. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the memory is held.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="memory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the store already holds the identifier.
    /// </exception>
    Task AddAsync(MemoryRecord memory, CancellationToken cancellationToken);

    /// <summary>
    ///     Reports the memory carrying an identifier.
    /// </summary>
    /// <remarks>
    ///     A miss is reported as <see langword="null"/> rather than as an exception, because an
    ///     identifier a model stated wrongly is an ordinary outcome the tool turns into a refusal.
    /// </remarks>
    /// <param name="id">The identifier to find. Must be non-null and non-empty.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The memory, or <see langword="null"/> when the store holds no such identifier.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="id"/> is <see langword="null"/> or empty.
    /// </exception>
    Task<MemoryRecord?> FindAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    ///     Replaces the memory carrying an identifier, leaving the store unchanged on a miss.
    /// </summary>
    /// <remarks>
    ///     Replacement of the whole record rather than field-by-field mutation, so that an update
    ///     that keeps the embedding and a revision that recomputes it travel the same path and
    ///     neither can half-apply.
    /// </remarks>
    /// <param name="memory">
    ///     The memory to store in place of the one carrying its identifier. Must not be
    ///     <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <see langword="true"/> when a memory was replaced; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="memory"/> is <see langword="null"/>.
    /// </exception>
    Task<bool> ReplaceAsync(MemoryRecord memory, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes the memory carrying an identifier.
    /// </summary>
    /// <param name="id">The identifier to remove. Must be non-null and non-empty.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <see langword="true"/> when a memory was removed; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="id"/> is <see langword="null"/> or empty.
    /// </exception>
    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    ///     Reports the memories whose descriptors are closest to a vector, nearest first.
    /// </summary>
    /// <remarks>
    ///     The vector is supplied already computed, because both callers — a recall and the
    ///     near-duplicate check a file performs — have just embedded the text they are searching
    ///     for. A store must never embed anything itself: which embedding backend is in use is the
    ///     application author's decision and is invisible to this library.
    /// </remarks>
    /// <param name="vector">
    ///     The vector to compare against. Its length must match the length of the vectors the store
    ///     holds.
    /// </param>
    /// <param name="count">
    ///     The greatest number of matches to return. Must not be negative; zero returns none.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The matches, ordered by descending <see cref="MemoryMatch.Similarity"/>, holding at most
    ///     <paramref name="count"/> entries and empty when the store holds nothing.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="count"/> is negative.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="vector"/> is empty, or when its length differs from the
    ///     length of the vectors the store holds.
    /// </exception>
    Task<IReadOnlyList<MemoryMatch>> SearchAsync(
        ReadOnlyMemory<float> vector,
        int count,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Reports how many memories the store holds.
    /// </summary>
    /// <remarks>
    ///     Exists so a tool result can state the size of the store, which is what keeps a model from
    ///     calling recall merely to find out whether its own write landed.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of memories held.</returns>
    Task<int> CountAsync(CancellationToken cancellationToken);
}

/// <summary>
///     The default memory store: memories held in this process, for as long as the tools built over
///     them live.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is a default, not a recommendation.</b> It exists so the family can be attached
///     without an author first choosing a database, and it is deliberately the simplest thing that
///     satisfies the contract: a list, a lock, and an exhaustive cosine scan. An exhaustive scan is
///     the right algorithm at the scale this store is for — an agent session's worth of memories,
///     tens to low thousands — and an author whose corpus outgrows it substitutes a store that
///     indexes, which is the reason <see cref="IMemoryStore"/> is public.
///     </para>
///     <para>
///     <b>A vector of a different length than the store already holds is refused rather than
///     scored.</b> Mixing two embedding models in one store produces similarity numbers that look
///     ordinary and mean nothing, which would quietly break both recall and the near-duplicate
///     threshold; it can only happen when an author changes generator against a persisted store, so
///     it is surfaced as the configuration error it is.
///     </para>
///     <para>
///     Access is guarded by a lock, so concurrent tool calls against one composition are safe.
///     </para>
/// </remarks>
public sealed class InMemoryMemoryStore : IMemoryStore
{
    /// <summary>
    ///     The lock guarding <see cref="_memories"/> so concurrent tool calls are safe.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The memories held, in the order they were filed.
    /// </summary>
    /// <remarks>
    ///     A list rather than a dictionary because every search is an exhaustive scan anyway, and
    ///     insertion order gives a stable tie-break between two memories of equal similarity.
    /// </remarks>
    private readonly List<MemoryRecord> _memories = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryMemoryStore"/> class.
    /// </summary>
    /// <remarks>
    ///     Declared explicitly rather than left implicit so that the documentation the package ships
    ///     describes every public member. A new store holds nothing.
    /// </remarks>
    public InMemoryMemoryStore()
    {
    }

    /// <inheritdoc/>
    public Task AddAsync(MemoryRecord memory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memory);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            // The caller assigns identifiers, so a collision is a defect in this library rather
            // than something a model did, and is reported as one.
            if (IndexOf(memory.Id) >= 0)
            {
                throw new ArgumentException(
                    "The store already holds a memory with the id '" + memory.Id + "'.",
                    nameof(memory));
            }

            RequireMatchingDimension(memory.Embedding, nameof(memory));
            _memories.Add(memory);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<MemoryRecord?> FindAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var index = IndexOf(id);
            return Task.FromResult(index < 0 ? null : _memories[index]);
        }
    }

    /// <inheritdoc/>
    public Task<bool> ReplaceAsync(MemoryRecord memory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memory);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var index = IndexOf(memory.Id);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            RequireMatchingDimension(memory.Embedding, nameof(memory));

            // Replaced in place, so the order memories were filed in is not disturbed by a
            // correction to one of them.
            _memories[index] = memory;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc/>
    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var index = IndexOf(id);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _memories.RemoveAt(index);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MemoryMatch>> SearchAsync(
        ReadOnlyMemory<float> vector,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        cancellationToken.ThrowIfCancellationRequested();

        if (vector.IsEmpty)
        {
            throw new ArgumentException(
                "The vector to search for holds no values, so it cannot be compared.",
                nameof(vector));
        }

        lock (_gate)
        {
            // An empty store is a true empty answer: there is nothing to compare against, and a
            // dimension check against nothing would be meaningless.
            if (_memories.Count == 0 || count == 0)
            {
                return Task.FromResult<IReadOnlyList<MemoryMatch>>([]);
            }

            RequireMatchingDimension(vector, nameof(vector));

            // An exhaustive scan. Ordering is by descending similarity with insertion order as the
            // tie-break, so two equally close memories come back in a stable order rather than
            // whichever the sort happened to visit first.
            IReadOnlyList<MemoryMatch> matches =
            [
                .. _memories
                    .Select(memory => new MemoryMatch(memory, CosineSimilarity(vector.Span, memory.Embedding.Span)))
                    .OrderByDescending(match => match.Similarity)
                    .Take(count)
            ];

            return Task.FromResult(matches);
        }
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_memories.Count);
        }
    }

    /// <summary>
    ///     Reports the cosine similarity of two vectors.
    /// </summary>
    /// <remarks>
    ///     Computed rather than taken from a library so the unit carries no dependency a consumer
    ///     would inherit for one loop. A zero-length vector has no direction, so its similarity to
    ///     anything is reported as zero rather than as a division by zero: a memory whose descriptor
    ///     embedded to all zeros is degenerate, and reporting it as maximally distant is the honest
    ///     answer.
    /// </remarks>
    /// <param name="left">The first vector. Must be the same length as <paramref name="right"/>.</param>
    /// <param name="right">The second vector.</param>
    /// <returns>The cosine similarity, in the inclusive range -1.0 to 1.0.</returns>
    private static double CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        // One pass accumulating the dot product and both magnitudes; the vectors are short and the
        // scan is exhaustive, so the cost that matters is the number of passes, not the arithmetic.
        double dot = 0.0;
        double leftMagnitude = 0.0;
        double rightMagnitude = 0.0;

        for (var index = 0; index < left.Length; index++)
        {
            double leftValue = left[index];
            double rightValue = right[index];

            dot += leftValue * rightValue;
            leftMagnitude += leftValue * leftValue;
            rightMagnitude += rightValue * rightValue;
        }

        if (leftMagnitude == 0.0 || rightMagnitude == 0.0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    /// <summary>
    ///     Rejects a vector whose length differs from the length the store already holds.
    /// </summary>
    /// <remarks>
    ///     Callers hold <see cref="_gate"/>; this member does not take it, because the check and the
    ///     mutation it guards must be one atomic step. The first memory filed establishes the
    ///     length, so an empty store accepts any vector.
    /// </remarks>
    /// <param name="vector">The vector to check.</param>
    /// <param name="parameterName">The name of the caller's parameter, for the exception.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when the store holds vectors of a different length.
    /// </exception>
    private void RequireMatchingDimension(ReadOnlyMemory<float> vector, string parameterName)
    {
        if (_memories.Count == 0)
        {
            return;
        }

        var held = _memories[0].Embedding.Length;
        if (vector.Length != held)
        {
            throw new ArgumentException(
                "The store holds vectors of length "
                + held.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " and was given one of length "
                + vector.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", so the two cannot be compared. One store holds the output of one embedding "
                + "model.",
                parameterName);
        }
    }

    /// <summary>
    ///     Finds the position of a memory by identifier.
    /// </summary>
    /// <remarks>
    ///     Callers hold <see cref="_gate"/>. Identifiers are compared ordinally, so an identifier
    ///     means exactly itself.
    /// </remarks>
    /// <param name="id">The identifier to find.</param>
    /// <returns>The zero-based position, or -1 when no memory carries the identifier.</returns>
    private int IndexOf(string id)
    {
        return _memories.FindIndex(memory => string.Equals(memory.Id, id, StringComparison.Ordinal));
    }
}

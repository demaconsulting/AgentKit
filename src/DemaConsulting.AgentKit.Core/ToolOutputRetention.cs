using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     One conversation's retention of large tool outputs: wraps tools so an output too large to
///     return is retained and previewed, publishes the two reader tools that page through and search
///     what was retained, and exports and restores what it holds so references survive persistence.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why a handle.</b> <see cref="ToolOutputSpill.Apply"/> retains outputs for one tool list,
///     allocating one store per call, so a tool added to a conversation later could only be covered
///     by re-applying retention to the whole list — creating a new store and losing every reference
///     the model already holds. An application that owns its agent loop, or injects tools into an
///     agent runtime per conversation, needs the store as an object it can keep: wrap tools into it
///     at different times, publish its readers once, and save and reload it with the conversation.
///     This type is that object. <see cref="ToolOutputSpill.Apply"/> and
///     <see cref="ToolPackBuilder.WithToolOutputSpill"/> are built on it.
///     </para>
///     <para>
///     <b>What a wrapped tool does.</b> Exactly what <see cref="ToolOutputSpill"/> describes: a
///     result whose text exceeds <see cref="ToolLimits.MaxResultCharacters"/> is retained whole and
///     the model receives a bounded preview opening with a reference such as <c>output-3</c>; any
///     other result is delivered unchanged. Every tool wrapped by one instance shares its store, so a
///     reference issued by one tool is read through <see cref="ReaderTools"/> whatever tool issued it.
///     </para>
///     <para>
///     <b>Persistence.</b> <see cref="Export"/> captures the retained outputs and the reference
///     counter as a <see cref="ToolOutputRetentionSnapshot"/>, which serializes with
///     <see cref="ToolOutputRetentionSnapshot.JsonTypeInfo"/>; <see cref="Restore"/> rebuilds a
///     retention that answers the same references with the same content and continues numbering
///     after every reference ever issued, so an old reference in a reloaded history can never be
///     re-issued to different content. A snapshot holds the full text of the retained outputs — up
///     to <see cref="ToolOutputOptions.MaxRetainedCharacters"/> characters — and carries the same
///     privacy class as the chat history that already holds tool results; a host that persists it
///     should treat it as such.
///     </para>
///     <para>
///     <b>Lifetime and isolation.</b> One instance per conversation. Two instances never share
///     references; one instance handed to two conversations shares them, exactly as a tool list
///     would.
///     </para>
///     <para>
///     Instances are safe for concurrent use: the store is lock-guarded and every other member is
///     immutable after construction.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Retaining the outputs of a tool the application built itself, then saving and reloading the
///     conversation's retained outputs.
///     </para>
///     <code>
///     var limits = new ToolLimits(maxResultCharacters: 4_000);
///     var retention = new ToolOutputRetention(limits);
///
///     AIFunction report = AIFunctionFactory.Create(
///         () => string.Join("\n", Enumerable.Range(1, 5_000).Select(n => "row " + n)),
///         name: "report_build",
///         description: "Builds the full report.");
///
///     // The wrapped tool and the two readers are what the agent is given.
///     List&lt;AIFunction&gt; tools = [retention.Wrap(report), .. retention.ReaderTools];
///
///     // Persist with the conversation, and later restore under the same or newer ceilings.
///     string json = System.Text.Json.JsonSerializer.Serialize(
///         retention.Export(), ToolOutputRetentionSnapshot.JsonTypeInfo);
///     var snapshot = System.Text.Json.JsonSerializer.Deserialize(json, ToolOutputRetentionSnapshot.JsonTypeInfo)!;
///     var restored = ToolOutputRetention.Restore(snapshot, limits);
///     </code>
/// </example>
public sealed class ToolOutputRetention
{
    /// <summary>
    ///     The store the outputs are retained in.
    /// </summary>
    private readonly ToolOutputStore _store;

    /// <summary>
    ///     The attachment every wrapped tool hands its results to.
    /// </summary>
    private readonly SpillAttachment _attachment;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ToolOutputRetention"/> class with an empty
    ///     store.
    /// </summary>
    /// <param name="limits">
    ///     The ceilings in force: <see cref="ToolLimits.MaxResultCharacters"/> is the threshold above
    ///     which an output is retained, and bounds every preview and reader result. Must not be null,
    ///     and the result ceiling must be at least <see cref="ToolOutputSpill.MinimumResultCharacters"/>.
    /// </param>
    /// <param name="options">
    ///     The preview size and retention ceilings, or <see langword="null"/> for
    ///     <see cref="ToolOutputOptions.Default"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="limits"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the result ceiling of <paramref name="limits"/> is below
    ///     <see cref="ToolOutputSpill.MinimumResultCharacters"/>, which a preview naming a reference
    ///     could not be guaranteed to fit in.
    /// </exception>
    public ToolOutputRetention(ToolLimits limits, ToolOutputOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(limits);

        // A ceiling the reference cannot fit in would make every retained output an over-ceiling
        // result, so it is a configuration mistake reported before anything is allocated.
        if (!ToolOutputSpill.AdmitsCeiling(limits))
        {
            throw new ArgumentException(ToolOutputSpill.DescribeCeilingTooSmall(limits), nameof(limits));
        }

        Limits = limits;
        Options = options ?? ToolOutputOptions.Default;
        _store = new ToolOutputStore(Options);
        _attachment = new SpillAttachment(_store, limits);

        // The readers are built once and published as stable instances, so a host can add them to
        // a tool list at any time and every reference this instance issues is readable through them.
        ReaderTools = [ToolOutputReadTool.Create(_store, limits), ToolOutputSearchTool.Create(_store, limits)];
    }

    /// <summary>
    ///     Gets the ceilings this retention was created with.
    /// </summary>
    public ToolLimits Limits { get; }

    /// <summary>
    ///     Gets the preview size and retention ceilings this retention observes.
    /// </summary>
    public ToolOutputOptions Options { get; }

    /// <summary>
    ///     Gets the two reader tools, <c>tool_output_read</c> then <c>tool_output_search</c>, that page
    ///     through and search the outputs this retention holds.
    /// </summary>
    /// <remarks>
    ///     The same two instances on every read. They take an output reference and paging or search
    ///     arguments and nothing else, so they cannot reach the file system, and their own results are
    ///     bounded and refused when over the ceiling rather than retained.
    /// </remarks>
    public IReadOnlyList<AIFunction> ReaderTools { get; }

    /// <summary>
    ///     Wraps one tool so its oversized outputs are retained by this instance.
    /// </summary>
    /// <remarks>
    ///     A tool built by <see cref="GuardedToolFactory"/> or bounded by
    ///     <see cref="GuardedToolFactory.AsGuarded"/> is re-wrapped over the same inner function,
    ///     keeping any binary ceiling it carries; any other function is wrapped the same way, its
    ///     structured results first serialized with its own serializer options. The supplied tool is
    ///     not modified. Metadata and service lookup pass through, so a wrapped
    ///     <see cref="ApprovalRequiredAIFunction"/> remains discoverable.
    /// </remarks>
    /// <param name="tool">The tool to wrap. Must not be <see langword="null"/>.</param>
    /// <returns>The wrapped tool.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="tool"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the tool's name carries the reserved <c>tool_output</c> prefix, or is longer
    ///     than <see cref="ToolName.MaxLength"/>.
    /// </exception>
    public AIFunction Wrap(AIFunction tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ValidateName(tool, nameof(tool));
        return WrapValidated(tool);
    }

    /// <summary>
    ///     Wraps a list of tools so their oversized outputs are retained by this instance.
    /// </summary>
    /// <remarks>
    ///     The list is validated whole before anything is wrapped, so a rejected list wraps nothing.
    ///     The result holds the wrapped tools in the supplied order; the readers are not appended — add
    ///     <see cref="ReaderTools"/> once to the agent's tools.
    /// </remarks>
    /// <param name="tools">The tools to wrap. Must be non-null and contain no null entry.</param>
    /// <returns>The wrapped tools, in order.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="tools"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="tools"/> holds a null entry, two tools of one name, a tool whose
    ///     name carries the reserved <c>tool_output</c> prefix, or a tool whose name is longer than
    ///     <see cref="ToolName.MaxLength"/>.
    /// </exception>
    public IReadOnlyList<AIFunction> Wrap(IEnumerable<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        // Materialize once and validate before wrapping, so a rejected list leaves nothing behind
        // and a lazily evaluated sequence cannot change between checks.
        var supplied = tools.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in supplied)
        {
            if (tool is null)
            {
                throw new ArgumentException("A tool in the collection is null.", nameof(tools));
            }

            ValidateName(tool, nameof(tools));
            if (!seen.Add(tool.Name))
            {
                throw new ArgumentException(
                    "Two tools carry the name '" + tool.Name + "', and a model selects a tool by name.",
                    nameof(tools));
            }
        }

        return [.. supplied.Select(WrapValidated)];
    }

    /// <summary>
    ///     Captures the outputs this retention holds and its reference counter.
    /// </summary>
    /// <remarks>
    ///     The snapshot is one consistent moment, taken under the store's lock: the outputs oldest
    ///     first, each as the normalized lines a reader pages, and the last reference issued. It is
    ///     independent of this instance afterwards.
    /// </remarks>
    /// <returns>The snapshot.</returns>
    public ToolOutputRetentionSnapshot Export()
    {
        var entries = _store.Snapshot(out var lastIssued);
        return new ToolOutputRetentionSnapshot(
            lastIssued,
            [.. entries.Select(entry => new RetainedToolOutput(entry.Id, [.. entry.Lines], entry.Wrapped))]);
    }

    /// <summary>
    ///     Rebuilds a retention from a snapshot, answering the same references with the same content.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The supplied ceilings apply, not the exported ones: an output larger than the per-output
    ///     ceiling is dropped and the oldest outputs are evicted until the count and character ceilings
    ///     hold, and a dropped reference is then answered as no longer retained. Numbering continues
    ///     after the snapshot's counter, so a reference already in a conversation's history is never
    ///     re-issued to different content.
    ///     </para>
    ///     <para>
    ///     A snapshot whose references are malformed, repeated or above its counter, whose counter is
    ///     negative, or whose lines are missing, empty or not normalized (a line holding a line break,
    ///     or longer than a retained line may be) is rejected rather than partly restored.
    ///     </para>
    /// </remarks>
    /// <param name="snapshot">The snapshot to restore. Must not be <see langword="null"/>.</param>
    /// <param name="limits">
    ///     The ceilings in force, with the same constraints as the constructor's.
    /// </param>
    /// <param name="options">
    ///     The preview size and retention ceilings, or <see langword="null"/> for
    ///     <see cref="ToolOutputOptions.Default"/>.
    /// </param>
    /// <returns>The restored retention.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="snapshot"/> or <paramref name="limits"/> is
    ///     <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the result ceiling of <paramref name="limits"/> is below
    ///     <see cref="ToolOutputSpill.MinimumResultCharacters"/>, or when
    ///     <paramref name="snapshot"/> is malformed as described above.
    /// </exception>
    public static ToolOutputRetention Restore(
        ToolOutputRetentionSnapshot snapshot,
        ToolLimits limits,
        ToolOutputOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Validate the ceilings first, through the constructor, so the same mistake is reported in
        // the same words whichever way a retention is made.
        var retention = new ToolOutputRetention(limits, options);
        var entries = ValidateSnapshot(snapshot);
        retention._store.Restore(entries, snapshot.LastIssued);
        return retention;
    }

    /// <summary>
    ///     Wraps a tool that has already been validated.
    /// </summary>
    /// <param name="tool">The validated tool.</param>
    /// <returns>The wrapped tool.</returns>
    private AIFunction WrapValidated(AIFunction tool)
    {
        return tool is GuardedFunction guarded
            ? guarded.WithSpill(_attachment)
            : new GuardedFunction(tool, _attachment);
    }

    /// <summary>
    ///     Rejects a tool name a preview header could not carry or a reader could be confused with.
    /// </summary>
    /// <param name="tool">The tool whose name is checked.</param>
    /// <param name="parameterName">The parameter to name in the exception.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when the name is longer than <see cref="ToolName.MaxLength"/> or carries the
    ///     reserved prefix.
    /// </exception>
    private static void ValidateName(AIFunction tool, string parameterName)
    {
        // A name is part of every preview header, so it must be bounded for the header to be.
        // Only a hand-built function can carry such a name: the guarded factory refuses one.
        if (ToolOutputSpill.IsNameTooLong(tool.Name))
        {
            throw new ArgumentException(ToolOutputSpill.DescribeNameTooLong(tool.Name), parameterName);
        }

        // A tool already named like a reader would be indistinguishable from the readers; the usual
        // cause is wrapping a list that already holds them.
        if (ToolOutputSpill.IsReservedName(tool.Name))
        {
            throw new ArgumentException(
                "The tool '" + tool.Name + "' carries the reserved '" + ToolOutputSpill.ReservedPrefix
                + "' prefix, which belongs to the reader tools of a retention.",
                parameterName);
        }
    }

    /// <summary>
    ///     Validates a snapshot and orders its outputs for restoring.
    /// </summary>
    /// <param name="snapshot">The snapshot to validate.</param>
    /// <returns>The outputs with their numeric references, in ascending order.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when the snapshot is malformed.
    /// </exception>
    private static List<(long Number, IReadOnlyList<string> Lines, bool Wrapped)> ValidateSnapshot(
        ToolOutputRetentionSnapshot snapshot)
    {
        if (snapshot.LastIssued < 0)
        {
            throw new ArgumentException("The snapshot's reference counter is negative.", nameof(snapshot));
        }

        var entries = new List<(long Number, IReadOnlyList<string> Lines, bool Wrapped)>();
        var seen = new HashSet<long>();
        foreach (var output in snapshot.Outputs)
        {
            // Each output must name a reference this library could have issued, exactly once, and at
            // or below the counter: a number above it was never issued, so it would install content
            // under a reference the retention never handed out.
            if (output is null
                || !ToolOutputStore.TryParse(output.Id, out var number)
                || number > snapshot.LastIssued
                || !seen.Add(number))
            {
                throw new ArgumentException(
                    "The snapshot holds a missing, malformed, repeated or never-issued output reference.",
                    nameof(snapshot));
            }

            // Each output must be the normalized lines a reader pages: at least one, none null, none
            // holding a line break, none longer than a retained line may be.
            if (output.Lines is null
                || output.Lines.Count == 0
                || output.Lines.Any(line => line is null
                                            || line.Length > ToolOutputStore.MaxLineCharacters
                                            || line.AsSpan().IndexOfAny('\r', '\n') >= 0))
            {
                throw new ArgumentException(
                    "The snapshot's output '" + output.Id + "' does not hold normalized lines.",
                    nameof(snapshot));
            }

            entries.Add((number, output.Lines, output.Wrapped));
        }

        return [.. entries.OrderBy(entry => entry.Number)];
    }
}

/// <summary>
///     A point-in-time copy of the outputs a <see cref="ToolOutputRetention"/> holds and its
///     reference counter, serializable so a conversation's retained outputs survive persistence.
/// </summary>
/// <remarks>
///     <para>
///     Serialize and deserialize it with <see cref="JsonTypeInfo"/>, which is source-generated, so no
///     reflection-based serializer configuration is needed and the type is safe under trimming and
///     ahead-of-time compilation. Property names are camel case.
///     </para>
///     <para>
///     The snapshot carries the full text of the retained outputs and belongs to the same privacy
///     class as the conversation history. Instances are immutable and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class ToolOutputRetentionSnapshot
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ToolOutputRetentionSnapshot"/> class.
    /// </summary>
    /// <param name="lastIssued">
    ///     The number of the last reference issued, for example 3 after <c>output-3</c>; zero when none
    ///     was issued.
    /// </param>
    /// <param name="outputs">The retained outputs, oldest first. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="outputs"/> is <see langword="null"/>.
    /// </exception>
    [JsonConstructor]
    public ToolOutputRetentionSnapshot(long lastIssued, IReadOnlyList<RetainedToolOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        LastIssued = lastIssued;
        Outputs = outputs;
    }

    /// <summary>
    ///     Gets the number of the last reference issued, which a restored retention never issues again.
    /// </summary>
    public long LastIssued { get; }

    /// <summary>
    ///     Gets the retained outputs, oldest first.
    /// </summary>
    public IReadOnlyList<RetainedToolOutput> Outputs { get; }

    /// <summary>
    ///     Gets the source-generated serialization contract for a snapshot.
    /// </summary>
    /// <remarks>
    ///     Pass it to <c>JsonSerializer.Serialize</c> and <c>JsonSerializer.Deserialize</c>, or use its
    ///     <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo.Options"/> wherever a
    ///     serializer options instance is expected, such as an agent session's state bag.
    /// </remarks>
    public static JsonTypeInfo<ToolOutputRetentionSnapshot> JsonTypeInfo =>
        ToolOutputRetentionJsonContext.Default.ToolOutputRetentionSnapshot;
}

/// <summary>
///     One retained output in a <see cref="ToolOutputRetentionSnapshot"/>.
/// </summary>
/// <remarks>
///     Instances are immutable and safe for concurrent use.
/// </remarks>
public sealed class RetainedToolOutput
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RetainedToolOutput"/> class.
    /// </summary>
    /// <param name="id">The reference the output was retained under, for example <c>output-3</c>.</param>
    /// <param name="lines">
    ///     The output as the normalized lines a reader pages: line breaks removed, long lines already
    ///     wrapped.
    /// </param>
    /// <param name="wrapped">Whether any line of the original output was wrapped.</param>
    [JsonConstructor]
    public RetainedToolOutput(string id, IReadOnlyList<string> lines, bool wrapped)
    {
        Id = id;
        Lines = lines;
        Wrapped = wrapped;
    }

    /// <summary>
    ///     Gets the reference the output was retained under.
    /// </summary>
    public string Id { get; }

    /// <summary>
    ///     Gets the output as the normalized lines a reader pages.
    /// </summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>
    ///     Gets a value indicating whether any line of the original output was wrapped, which a reader
    ///     page notes.
    /// </summary>
    public bool Wrapped { get; }
}

/// <summary>
///     The source-generated serialization context for <see cref="ToolOutputRetentionSnapshot"/>.
/// </summary>
/// <remarks>
///     Internal: the public entry point is <see cref="ToolOutputRetentionSnapshot.JsonTypeInfo"/>.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ToolOutputRetentionSnapshot))]
internal sealed partial class ToolOutputRetentionJsonContext : JsonSerializerContext;

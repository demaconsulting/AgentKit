using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Composes retention of large tool outputs into a tool list: an output too large to return
///     is retained, the model is handed a bounded preview and a reference, and two confined reader
///     tools are offered to page through and search what was retained.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why retain rather than refuse.</b> Without retention, a tool whose output exceeds
///     <see cref="ToolLimits.MaxResultCharacters"/> must refuse — a truncated result would hide
///     what the model never knew to ask for — and the work that produced the output is lost. A
///     delegated agent's whole report, a listing of a large tree, or a broad search becomes a
///     refusal the model can only answer by guessing how to narrow the request. With retention,
///     the same output is kept whole and the model reads exactly the part it needs.
///     </para>
///     <para>
///     <b>It is opt-in and changes nothing until chosen.</b> A composition that does not call
///     <see cref="Apply"/> — directly or through
///     <see cref="ToolPackBuilder.WithToolOutputSpill"/> — gives byte-for-byte the refusals it
///     always gave, and a custom tool's oversized result still passes through unbounded.
///     </para>
///     <para>
///     <b>What an oversized result becomes.</b> A tool that returns
///     <see cref="ToolResult.Oversized"/> is always retained. Any other textual or structured
///     result is judged by the text a provider receives: a string, a JSON document, text content,
///     or a sequence made only of text content is measured as it is, and any other value that is
///     not content — a record, an anonymous object, a collection, a number, a date, a byte array —
///     is first serialized to a JSON document with the tool's own
///     <see cref="AIFunction.JsonSerializerOptions"/>, exactly as
///     <see cref="GuardedToolFactory"/> serializes structured data, and is measured and delivered as
///     that document. A result longer than the ceiling is retained. A value the serializer cannot
///     handle fails the invocation with the serializer's exception, as it would from the guarded
///     factory. Content other than text — binary and image content, and any other
///     <see cref="AIContent"/> — is never retained and passes through unchanged, as does a sequence
///     containing it and a result with no value; the library's own tools bound binary content by
///     <see cref="ToolLimits.MaxBinaryBytes"/>. A sequence of content that is not already a list is
///     enumerated once and delivered as
///     the list it was enumerated into, the same parts in the same order; a list or an array is
///     delivered as returned. The model receives a successful result that opens with a bracketed
///     header naming the reference (for example <c>output-3</c>), the output's size, and the reader
///     tools, followed by about three quarters of the preview from the head of the output and a
///     quarter from its tail, numbered <c>N| </c> exactly as <c>text_file_read</c> numbers a file.
///     The preview and its header always fit within the result ceiling; the header alone is
///     delivered when <see cref="ToolOutputOptions.PreviewCharacters"/> is zero or the lines are
///     too long for the preview size, because the reference is the result.
///     </para>
///     <para>
///     <b>Retention requires a result ceiling the reference fits in.</b> A composition whose
///     <see cref="ToolLimits.MaxResultCharacters"/> is below <see cref="MinimumResultCharacters"/>
///     is rejected when it is composed, as is a tool whose name is longer than
///     <see cref="ToolName.MaxLength"/>. Together they bound the header, so every successful result
///     the composition produces — a preview, a reader page, a search listing — fits within the
///     ceiling, and every refusal the composition writes itself is far shorter than the minimum. A
///     refusal a tool writes itself is the tool's own text, exactly as without retention.
///     </para>
///     <para>
///     <b>An output too large even to retain is refused honestly.</b> Above
///     <see cref="ToolOutputOptions.MaxOutputCharacters"/> the model receives the refusal the tool
///     supplied to <see cref="ToolResult.Oversized"/>, or for any other result a refusal naming the
///     retention ceiling — never a silent truncation.
///     </para>
///     <para>
///     <b>The readers are confined by construction.</b> <see cref="ToolOutputReadTool"/> and
///     <see cref="ToolOutputSearchTool"/> take an output identifier and paging or search arguments,
///     and nothing else: there is no path parameter, so there is no expressible way for them to
///     reach the file system. They read only the store this call allocated. They are never
///     themselves retained — their results are bounded by the ceiling and refused when over it —
///     so a page can never become another reference.
///     </para>
///     <para>
///     <b>Lifetime and isolation.</b> Each call allocates one <see cref="ToolOutputRetention"/>,
///     whose store is shared by the tools it returns and nothing else. A host that needs to add
///     tools to a conversation later, or to persist its retained outputs, uses a
///     <see cref="ToolOutputRetention"/> directly. References therefore survive a session's compaction and rotation,
///     which carry the tool list unchanged, and are lost when the tool list is discarded. A host
///     that hands one composed tool list to several sessions shares the store between them, exactly
///     as it would share a task list; composing per session keeps them apart.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use; the tools it returns are safe
///     for concurrent invocation.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Applying retention to a hand-assembled tool list. Composing with
///     <see cref="ToolPackBuilder.WithToolOutputSpill"/> does the same thing for a builder.
///     </para>
///     <code>
///     var report = GuardedToolFactory.Create(
///         () => ToolResult.Text(string.Join("\n", Enumerable.Range(1, 20_000).Select(n => "row " + n))),
///         name: "report_build",
///         description: "Builds the full report.");
///
///     IReadOnlyList&lt;AIFunction&gt; tools = ToolOutputSpill.Apply([report], ToolLimits.Default);
///
///     // tools now holds report_build, tool_output_read and tool_output_search. Calling
///     // report_build returns a preview naming a reference such as 'output-1', which
///     // tool_output_read pages through.
///     </code>
/// </example>
public static class ToolOutputSpill
{
    /// <summary>
    ///     The smallest <see cref="ToolLimits.MaxResultCharacters"/> a composition retaining large
    ///     outputs accepts.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A retained output is replaced by a preview whose header names the reference, and the
    ///     reference is the result: a ceiling too small to carry it would make every retained
    ///     output either an over-ceiling result or a lost one. The header's length is bounded once
    ///     the tool name is bounded by <see cref="ToolName.MaxLength"/>: with a 64-character name, a
    ///     19-digit reference, every count at 10 digits and the wrapped-lines note, the header, the
    ///     omission marker and the line break before it come to 531 characters.
    ///     </para>
    ///     <para>
    ///     1,000 covers that worst case with room left for preview lines, and covers every fixed
    ///     result the reader tools return — an empty window, <c>No matches.</c> — and every refusal
    ///     the composition writes itself. A composition that does not retain large outputs is not
    ///     affected: it accepts any ceiling <see cref="ToolLimits"/> accepts.
    ///     </para>
    /// </remarks>
    public const int MinimumResultCharacters = 1_000;

    /// <summary>
    ///     The name prefix reserved for the reader tools this composition appends.
    /// </summary>
    /// <remarks>
    ///     Reserved rather than published as a family prefix: the readers are infrastructure the
    ///     composition adds, not a pack an application attaches, so no pack may claim the prefix in
    ///     a composition that retains outputs.
    /// </remarks>
    internal const string ReservedPrefix = "tool_output";

    /// <summary>
    ///     Wraps every tool in a list so oversized outputs are retained, and appends the reader tools.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A tool built by <see cref="GuardedToolFactory"/> is re-wrapped over the same inner
    ///     function; any other <see cref="AIFunction"/> is wrapped the same way, so a hand-built
    ///     tool's oversized results are retained too, its structured results first serialized with
    ///     its own serializer options exactly as the guarded factory would have serialized them.
    ///     The supplied tools are not modified: a list composed without retention keeps behaving as
    ///     it did.
    ///     </para>
    ///     <para>
    ///     The result holds the supplied tools in their order, followed by
    ///     <see cref="ToolOutputReadTool.ToolName"/> and then
    ///     <see cref="ToolOutputSearchTool.ToolName"/>. Each call allocates a fresh store, so two
    ///     calls never share references.
    ///     </para>
    /// </remarks>
    /// <param name="tools">The tools to compose. Must be non-null and contain no null entry.</param>
    /// <param name="limits">
    ///     The ceilings in force: <see cref="ToolLimits.MaxResultCharacters"/> is the threshold
    ///     above which an output is retained, and bounds every preview and reader result. Must not
    ///     be null, and the result ceiling must be at least <see cref="MinimumResultCharacters"/>.
    /// </param>
    /// <param name="options">
    ///     The preview size and retention ceilings, or <see langword="null"/> for
    ///     <see cref="ToolOutputOptions.Default"/>.
    /// </param>
    /// <returns>The wrapped tools followed by the two reader tools.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="tools"/> or <paramref name="limits"/> is
    ///     <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the result ceiling of <paramref name="limits"/> is below
    ///     <see cref="MinimumResultCharacters"/>, or when <paramref name="tools"/> holds a null
    ///     entry, two tools of one name, a tool whose name carries the reserved <c>tool_output</c>
    ///     prefix, or a tool whose name is longer than <see cref="ToolName.MaxLength"/>.
    /// </exception>
    public static IReadOnlyList<AIFunction> Apply(
        IEnumerable<AIFunction> tools,
        ToolLimits limits,
        ToolOutputOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tools);

        // One retention per call: this is the isolation boundary between compositions. Its
        // constructor rejects missing or too-small ceilings, and its wrap validates the whole list
        // before wrapping anything, with the same exceptions this method has always thrown.
        var retention = new ToolOutputRetention(limits, options);

        // The readers are appended unwrapped: their own results are bounded and refused when over
        // the ceiling, so a page can never be retained as yet another reference.
        return [.. retention.Wrap(tools), .. retention.ReaderTools];
    }

    /// <summary>
    ///     Determines whether a tool name falls under the reserved reader prefix.
    /// </summary>
    /// <param name="name">The tool name to test.</param>
    /// <returns><see langword="true"/> when the name carries the reserved prefix.</returns>
    internal static bool IsReservedName(string name) =>
        name.StartsWith(ReservedPrefix + "_", StringComparison.Ordinal);

    /// <summary>
    ///     Determines whether a result ceiling is large enough for retention to be composed against.
    /// </summary>
    /// <param name="limits">The ceilings in force.</param>
    /// <returns>
    ///     <see langword="true"/> when the result ceiling is at least
    ///     <see cref="MinimumResultCharacters"/>.
    /// </returns>
    internal static bool AdmitsCeiling(ToolLimits limits) =>
        limits.MaxResultCharacters >= MinimumResultCharacters;

    /// <summary>
    ///     Composes the explanation given when a result ceiling is too small for retention.
    /// </summary>
    /// <remarks>
    ///     Shared by <see cref="Apply"/> and <see cref="ToolPackBuilder.Build"/>, so the mistake is
    ///     explained in the same words whichever reports it.
    /// </remarks>
    /// <param name="limits">The ceilings in force.</param>
    /// <returns>The explanation.</returns>
    internal static string DescribeCeilingTooSmall(ToolLimits limits) =>
        "Retaining large tool outputs requires a result ceiling of at least "
        + MinimumResultCharacters.ToString(CultureInfo.InvariantCulture)
        + " characters, so that the preview naming a retained output's reference always fits "
        + "within it; the ceiling is "
        + limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture) + " characters.";

    /// <summary>
    ///     Determines whether a tool name is longer than any name a preview header is sized for.
    /// </summary>
    /// <param name="name">The tool name to test.</param>
    /// <returns><see langword="true"/> when the name is longer than <see cref="ToolName.MaxLength"/>.</returns>
    internal static bool IsNameTooLong(string name) => name.Length > ToolName.MaxLength;

    /// <summary>
    ///     Composes the explanation given when a tool name is too long for retention.
    /// </summary>
    /// <param name="name">The over-long tool name.</param>
    /// <returns>The explanation.</returns>
    internal static string DescribeNameTooLong(string name) =>
        "The tool '" + name + "' has a name longer than "
        + ToolName.MaxLength.ToString(CultureInfo.InvariantCulture)
        + " characters, which no model provider accepts and which a retained output's preview "
        + "could not name within the result ceiling.";

    /// <summary>
    ///     Computes the most characters a preview's header, omission marker and line break can
    ///     occupy, whatever the output, the tool and the ceiling.
    /// </summary>
    /// <remarks>
    ///     Composed from the same code that composes a real preview, at the extremes every part can
    ///     reach: a name of <see cref="ToolName.MaxLength"/> characters, a reference numbered at
    ///     <see cref="long.MaxValue"/>, every count at <see cref="int.MaxValue"/>, the
    ///     wrapped-lines note, and both the size-comparison and the declared-oversized wording.
    ///     <see cref="MinimumResultCharacters"/> must be at least this, which a test asserts, so
    ///     lengthening the header's wording cannot silently break the guarantee.
    /// </remarks>
    /// <returns>The worst-case fixed overhead of a preview, in characters.</returns>
    internal static int WorstCaseOverheadCharacters()
    {
        var name = new string('a', ToolName.MaxLength);
        var id = ToolOutputStore.IdPrefix + long.MaxValue.ToString(CultureInfo.InvariantCulture);
        var widest = 0;
        foreach (var declared in new[] { false, true })
        {
            var showing = SpillAttachment.ComposeHeader(
                name,
                originalCharacters: int.MaxValue,
                declared,
                id,
                wrapped: true,
                ceiling: int.MaxValue,
                total: int.MaxValue,
                headCount: int.MaxValue,
                tailCount: 1);
            var none = SpillAttachment.ComposeHeader(
                name,
                originalCharacters: int.MaxValue,
                declared,
                id,
                wrapped: true,
                ceiling: int.MaxValue,
                total: int.MaxValue,
                headCount: 0,
                tailCount: 0);
            widest = Math.Max(widest, Math.Max(showing.Length, none.Length));
        }

        return widest
               + SpillAttachment.ComposeOmission(int.MaxValue, int.MaxValue).Length
               + 1;
    }
}

/// <summary>
///     One composition's retention: decides whether a result is oversized, retains it, and renders
///     the preview the model receives in its place.
/// </summary>
/// <remarks>
///     <para>
///     Allocated by a <see cref="ToolOutputRetention"/> and carried by every tool it wraps, so a
///     result is retained in the store of the retention that offered the tool.
///     It holds no mutable state of its own; the store it wraps is lock-guarded.
///     </para>
///     <para>
///     Instances are safe for concurrent use.
///     </para>
/// </remarks>
internal sealed class SpillAttachment
{
    /// <summary>
    ///     The share of the preview budget given to the head of the output, in quarters.
    /// </summary>
    /// <remarks>
    ///     Three quarters to the head because an output's beginning usually says what it is — a
    ///     header, the first matches, the opening of a report — and one quarter to the tail because
    ///     its end says how it finished. Budget the head leaves unused flows to the tail.
    /// </remarks>
    private const int HeadQuarters = 3;

    /// <summary>
    ///     The delimiter between a line number and the line's content, matching
    ///     <c>text_file_read</c>.
    /// </summary>
    private const string LineNumberDelimiter = "| ";

    /// <summary>
    ///     The store oversized outputs are retained in.
    /// </summary>
    private readonly ToolOutputStore _store;

    /// <summary>
    ///     The ceilings in force for the composition.
    /// </summary>
    private readonly ToolLimits _limits;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SpillAttachment"/> class.
    /// </summary>
    /// <param name="store">The store to retain outputs in. Must not be null.</param>
    /// <param name="limits">The ceilings in force. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/> or <paramref name="limits"/> is
    ///     <see langword="null"/>.
    /// </exception>
    public SpillAttachment(ToolOutputStore store, ToolLimits limits)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(limits);

        _store = store;
        _limits = limits;
    }

    /// <summary>
    ///     Resolves a tool's result for delivery: serializes a value that is not text, JSON or
    ///     content to a JSON document as the guarded factory would, retains and previews an
    ///     oversized output, delivers a sequence of content that is not already a list as the list
    ///     it was enumerated into — the same parts in the same order — and returns anything else, a
    ///     list or an array included, as it arrived.
    /// </summary>
    /// <remarks>
    ///     A structured result is delivered as the JSON document it was serialized to, not as the
    ///     object the tool returned: that document's JSON text is what was measured and what every
    ///     adapter sends, whereas each adapter would serialize the object with options of its own.
    ///     An exception the serializer raises for a value it cannot handle propagates and fails the
    ///     invocation, exactly as from the guarded factory.
    /// </remarks>
    /// <param name="toolName">The name of the tool that produced the result, named in the preview.</param>
    /// <param name="serializerOptions">
    ///     The options of the tool that produced the result, used to serialize a structured result;
    ///     <see langword="null"/> for <see cref="AIJsonUtilities.DefaultOptions"/>.
    /// </param>
    /// <param name="result">The result the tool produced; may be null.</param>
    /// <returns>
    ///     A preview string, a refusal when the output is too large to retain, the JSON document a
    ///     structured result was serialized to, the list a sequence of content was enumerated into,
    ///     or the result itself.
    /// </returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the serializer options cannot describe or serialize a structured result's type.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the serializer options cannot build a valid contract for a structured
    ///     result's type, such as one whose JSON property names collide, or when reading a member
    ///     of the result throws it, as a stream's timeout properties do.
    /// </exception>
    /// <exception cref="JsonException">
    ///     Thrown when a structured result's object graph cannot be written, such as one containing
    ///     a cycle.
    /// </exception>
    public object? Deliver(string toolName, JsonSerializerOptions? serializerOptions, object? result)
    {
        // A tool that declared its output oversized is retained whatever the measured size, and
        // its own refusal is the one given if the output cannot be retained. When the output fits
        // the ceiling - its tool judged it against a tighter one - the preview must not claim it
        // is larger than this ceiling.
        if (result is OversizedResult oversized)
        {
            return Retain(
                toolName,
                oversized.Text,
                oversized.Text.Length,
                oversized.DenialMessage,
                declared: oversized.Text.Length <= _limits.MaxResultCharacters);
        }

        // A tool may return a single-pass sequence of content, which is enumerated exactly once
        // and delivered as the list it was enumerated into; a list or an array is delivered as the
        // very instance the tool returned. A function the guarded factory did not build may return
        // structured data as it is - a record, an anonymous object, a collection, a number - which
        // is serialized exactly as the factory would, with the function's own options, so it is
        // measured as the JSON every adapter sends and, when it fits, delivered as that JSON. A
        // value the serializer cannot handle throws, failing the invocation just as it would from
        // the factory. The normalization is shared with the result ceilings, so a result is judged
        // identically wherever it is bounded.
        result = ResultMeasure.Normalize(result, serializerOptions);

        // Only text is retained, JSON included. Content other than text, a sequence containing it
        // and an absent result pass through, as does any text within the ceiling - measured by the
        // result's own representation.
        if (!ResultMeasure.TryGetText(result, out var text, out var delivered)
            || delivered <= _limits.MaxResultCharacters)
        {
            return result;
        }

        // Structured data is indented only once it is known to be retained, so a document that
        // fits is never reshaped and the ceiling is judged on the JSON text the tool returned.
        if (result is JsonElement { ValueKind: JsonValueKind.Object or JsonValueKind.Array } element)
        {
            text = ToolOutputStore.Indent(element);
        }

        return Retain(toolName, text, delivered, denialMessage: null, declared: false);
    }

    /// <summary>
    ///     Retains an output and renders its preview, or refuses when it is too large to retain.
    /// </summary>
    /// <param name="toolName">The tool that produced the output.</param>
    /// <param name="text">The output text to retain.</param>
    /// <param name="delivered">
    ///     The characters of the result as the tool returned it, reported in the preview header.
    ///     A refusal instead reports the size the output would be retained at, the figure the
    ///     retention limit is measured on.
    /// </param>
    /// <param name="denialMessage">
    ///     The refusal the tool supplied, or <see langword="null"/> to compose one naming the
    ///     retention ceiling.
    /// </param>
    /// <param name="declared">
    ///     Whether the output is retained only because its tool declared it oversized, though it
    ///     fits the result ceiling.
    /// </param>
    /// <returns>The preview, or the refusal.</returns>
    private object Retain(string toolName, string text, int delivered, string? denialMessage, bool declared)
    {
        var entry = _store.Add(text);
        if (entry is not null)
        {
            return RenderPreview(toolName, delivered, declared, entry);
        }

        // The store refuses on the normalized size - line endings normalized, long lines wrapped,
        // structured data indented - so that is the size stated; the size the tool returned may
        // be smaller and would contradict the limit it is said to exceed. Measured without a copy.
        return ToolResult.Denied(
            DenialReason.ResourceTooLarge,
            denialMessage
            ?? "The output of '" + toolName + "' is "
            + ToolOutputStore.Measure(text).ToString(CultureInfo.InvariantCulture)
            + " characters, which exceeds the "
            + _store.Options.MaxOutputCharacters.ToString(CultureInfo.InvariantCulture)
            + "-character retention limit.");
    }

    /// <summary>
    ///     Renders the result the model receives in place of a retained output.
    /// </summary>
    /// <param name="toolName">The tool that produced the output.</param>
    /// <param name="originalCharacters">The characters the tool produced.</param>
    /// <param name="declared">
    ///     Whether the output was declared oversized by its tool though it fits the result ceiling.
    /// </param>
    /// <param name="entry">The retained entry.</param>
    /// <returns>The header followed by the numbered head and tail lines.</returns>
    private string RenderPreview(string toolName, int originalCharacters, bool declared, ToolOutputEntry entry)
    {
        var lines = entry.Lines;
        var total = lines.Count;
        var width = total.ToString(CultureInfo.InvariantCulture).Length;

        // The header's own length depends on which lines are shown, so the budget is computed
        // against a header naming the widest possible ranges; the real header is never longer,
        // except that the header naming no lines may be two characters longer when the output has
        // fewer than ten lines - and that header comes with no body and no omission marker, whose
        // reserved room covers it. The composition rejects any ceiling below
        // ToolOutputSpill.MinimumResultCharacters, which is at least this overhead, so the clamp
        // to zero below never discards part of the header's room.
        var ceiling = _limits.MaxResultCharacters;
        var widestHeader = ComposeHeader(toolName, originalCharacters, declared, entry.Id, entry.Wrapped, ceiling, total, headCount: total, tailCount: 1);
        var omission = ComposeOmission(total, total);
        var budget = Math.Min(
            _store.Options.PreviewCharacters,
            Math.Max(0, ceiling - widestHeader.Length - omission.Length - 1));

        // Take the head with three quarters of the budget, then the tail with what remains.
        var headBudget = (int)((long)budget * HeadQuarters / 4);
        var headCount = 0;
        var used = 0;
        while (headCount < total && used + LineCost(lines[headCount], headCount + 1, width) <= headBudget)
        {
            used += LineCost(lines[headCount], headCount + 1, width);
            headCount++;
        }

        var tailCount = 0;
        while (headCount + tailCount < total
               && used + LineCost(lines[total - tailCount - 1], total - tailCount, width) <= budget)
        {
            used += LineCost(lines[total - tailCount - 1], total - tailCount, width);
            tailCount++;
        }

        // Compose the real header and the body.
        var builder = new StringBuilder();
        builder.Append(ComposeHeader(toolName, originalCharacters, declared, entry.Id, entry.Wrapped, ceiling, total, headCount, tailCount));
        for (var index = 0; index < headCount; index++)
        {
            AppendLine(builder, lines[index], index + 1, width);
        }

        if (headCount + tailCount < total && (headCount > 0 || tailCount > 0))
        {
            builder.Append('\n').Append(ComposeOmission(headCount + 1, total - tailCount));
        }

        for (var index = total - tailCount; index < total; index++)
        {
            AppendLine(builder, lines[index], index + 1, width);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Composes the bracketed header naming the reference, the output's size, the lines shown,
    ///     and the reader tools.
    /// </summary>
    /// <param name="toolName">The tool that produced the output.</param>
    /// <param name="originalCharacters">The characters the tool produced.</param>
    /// <param name="declared">
    ///     Whether the output was declared oversized by its tool though it fits the result ceiling,
    ///     in which case the header names the declaration rather than a size comparison it would fail.
    /// </param>
    /// <param name="id">The reference the output was retained under.</param>
    /// <param name="wrapped">Whether long lines were wrapped when the output was retained.</param>
    /// <param name="ceiling">The result ceiling the output exceeded.</param>
    /// <param name="total">The number of retained lines.</param>
    /// <param name="headCount">The number of head lines shown.</param>
    /// <param name="tailCount">The number of tail lines shown.</param>
    /// <returns>The header text.</returns>
    internal static string ComposeHeader(
        string toolName,
        int originalCharacters,
        bool declared,
        string id,
        bool wrapped,
        int ceiling,
        int total,
        int headCount,
        int tailCount)
    {
        var builder = new StringBuilder();
        builder.Append("[Output ").Append(id)
            .Append(" from ").Append(toolName).Append(": ")
            .Append(originalCharacters.ToString(CultureInfo.InvariantCulture)).Append(" characters in ")
            .Append(total.ToString(CultureInfo.InvariantCulture)).Append(" lines, ");

        // An output its tool declared oversized may fit this ceiling - the tool judged it against
        // a tighter one - so the header then states the declaration, never a comparison it fails.
        // The declared form is shorter than the comparison at its widest, so the worst-case
        // overhead is unchanged.
        if (declared)
        {
            builder.Append("which its tool reported as too large to return. ");
        }
        else
        {
            builder.Append("larger than the ").Append(ceiling.ToString(CultureInfo.InvariantCulture))
                .Append("-character result limit. ");
        }

        // State plainly which lines the preview holds, so the model knows what it has not seen.
        // Every form is no longer than the two-range form the budget is computed against, except
        // the no-lines form, which comes with no body.
        if (headCount > 0 && tailCount > 0)
        {
            builder.Append("Showing lines 1-").Append(headCount.ToString(CultureInfo.InvariantCulture))
                .Append(" and ").Append((total - tailCount + 1).ToString(CultureInfo.InvariantCulture))
                .Append('-').Append(total.ToString(CultureInfo.InvariantCulture)).Append(". ");
        }
        else if (headCount > 0)
        {
            builder.Append("Showing lines 1-").Append(headCount.ToString(CultureInfo.InvariantCulture))
                .Append(". ");
        }
        else if (tailCount > 0)
        {
            // A first line too long for the head's share can leave room for the tail alone.
            builder.Append("Showing lines ").Append((total - tailCount + 1).ToString(CultureInfo.InvariantCulture))
                .Append('-').Append(total.ToString(CultureInfo.InvariantCulture)).Append(". ");
        }
        else
        {
            builder.Append("No lines fit in the preview. ");
        }

        if (wrapped)
        {
            builder.Append("Lines longer than ")
                .Append(ToolOutputStore.MaxLineCharacters.ToString(CultureInfo.InvariantCulture))
                .Append(" characters were wrapped onto continuation lines. ");
        }

        builder.Append("The full output is retained: read it with ").Append(ToolOutputReadTool.ToolName)
            .Append(" (id '").Append(id).Append("', startLine, lineCount) or find text in it with ")
            .Append(ToolOutputSearchTool.ToolName).Append(".]");

        return builder.ToString();
    }

    /// <summary>
    ///     Composes the line marking the lines the preview omits.
    /// </summary>
    /// <param name="first">The first omitted line.</param>
    /// <param name="last">The last omitted line.</param>
    /// <returns>The omission marker.</returns>
    internal static string ComposeOmission(int first, int last) =>
        "[... lines " + first.ToString(CultureInfo.InvariantCulture)
        + "-" + last.ToString(CultureInfo.InvariantCulture) + " omitted ...]";

    /// <summary>
    ///     Computes the characters one numbered line costs in the preview, including the line break
    ///     before it.
    /// </summary>
    /// <param name="line">The line's content.</param>
    /// <param name="number">The line's 1-based number.</param>
    /// <param name="width">The width numbers are padded to.</param>
    /// <returns>The cost in characters.</returns>
    private static int LineCost(string line, int number, int width) =>
        1 + Math.Max(width, number.ToString(CultureInfo.InvariantCulture).Length)
        + LineNumberDelimiter.Length + line.Length;

    /// <summary>
    ///     Appends one numbered line to the preview.
    /// </summary>
    /// <param name="builder">The preview under construction.</param>
    /// <param name="line">The line's content.</param>
    /// <param name="number">The line's 1-based number.</param>
    /// <param name="width">The width numbers are padded to.</param>
    private static void AppendLine(StringBuilder builder, string line, int number, int width)
    {
        builder.Append('\n')
            .Append(number.ToString(CultureInfo.InvariantCulture).PadLeft(width))
            .Append(LineNumberDelimiter)
            .Append(line);
    }
}

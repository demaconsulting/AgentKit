using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     The outcome of looking up a retained output by the identifier a model supplied.
/// </summary>
/// <remarks>
///     The three failure outcomes are distinguished because each tells the model a different fact:
///     a malformed identifier was never a reference, an unknown one was never issued, and an
///     evicted one was issued and has since been released to keep the store within its ceilings.
/// </remarks>
internal enum ToolOutputLookup
{
    /// <summary>
    ///     The identifier names an output the store still holds.
    /// </summary>
    Found = 1,

    /// <summary>
    ///     The identifier is not of the form the store issues.
    /// </summary>
    Malformed = 2,

    /// <summary>
    ///     The identifier is well formed but the store never issued it.
    /// </summary>
    Unknown = 3,

    /// <summary>
    ///     The identifier was issued, and the output it named has since been evicted.
    /// </summary>
    Evicted = 4
}

/// <summary>
///     One retained output, held as the lines a model pages through.
/// </summary>
/// <remarks>
///     Immutable once constructed, so a reader holding an entry needs no lock while it renders a
///     page or runs a search, even if the store evicts the entry meanwhile.
/// </remarks>
/// <param name="Id">The identifier the store issued, for example <c>output-3</c>.</param>
/// <param name="Lines">The normalized lines, in order; never null and never empty.</param>
/// <param name="Characters">
///     The characters the entry holds, counting one per line break, as charged against the store's
///     ceilings.
/// </param>
/// <param name="Wrapped">Whether any physical line was wrapped onto continuation lines.</param>
internal sealed record ToolOutputEntry(string Id, IReadOnlyList<string> Lines, long Characters, bool Wrapped);

/// <summary>
///     The in-memory store of the large tool outputs one composition has retained.
/// </summary>
/// <remarks>
///     <para>
///     <b>The store belongs to exactly one composition.</b> One instance is allocated per
///     <see cref="ToolOutputSpill.Apply"/> call and shared only by the tools that call produced:
///     the wrapped tools that write to it and the two readers that read from it. It is internal and
///     no public type accepts one, so a delegated agent composed separately gets a separate store,
///     and nothing a model supplies can name another composition's outputs.
///     </para>
///     <para>
///     <b>There is no path anywhere in it.</b> Outputs live in memory and are named by opaque,
///     monotonic identifiers of the form <c>output-1</c>, <c>output-2</c>, and so on. Holding them
///     on disk would introduce host paths, a cleanup obligation and a new surface for confinement
///     to defend; holding them in memory makes the readers unable to reach the file system by
///     construction. The store lives exactly as long as the tool list built with it, which is why
///     a reference survives a session's rotation: rotation carries the tool list unchanged.
///     </para>
///     <para>
///     <b>Outputs are normalized for paging when they are retained.</b> Line endings become
///     <c>\n</c>; structured data arrives indented (see <see cref="ToolOutputSpill"/>), because
///     compact JSON is a single line a model cannot page; and a physical line longer than
///     <see cref="MaxLineCharacters"/> is wrapped onto continuation lines so no single numbered line
///     can exceed a page.
///     </para>
///     <para>
///     <b>Retention is bounded three ways.</b> One output above
///     <see cref="ToolOutputOptions.MaxOutputCharacters"/> is not retained at all. Otherwise the
///     oldest outputs are evicted until both <see cref="ToolOutputOptions.MaxRetainedOutputs"/>
///     and <see cref="ToolOutputOptions.MaxRetainedCharacters"/> hold. Because identifiers are
///     monotonic, an identifier lower than the next one to be issued that is no longer present is
///     known to have been evicted, so no record of evicted entries is kept.
///     </para>
///     <para>
///     Access is guarded by a lock, so concurrent tool calls against one composition are safe.
///     Normalization runs outside the lock; only the insertion, eviction and lookup take it.
///     </para>
/// </remarks>
internal sealed class ToolOutputStore
{
    /// <summary>
    ///     The longest physical line held as one numbered line; longer lines are wrapped.
    /// </summary>
    /// <remarks>
    ///     2,000 characters is far longer than any line a person writes and far shorter than the
    ///     default result ceiling, so ordinary text is never wrapped while a single minified line of
    ///     hundreds of kilobytes becomes many pageable lines.
    /// </remarks>
    internal const int MaxLineCharacters = 2_000;

    /// <summary>
    ///     The fixed prefix of every identifier the store issues.
    /// </summary>
    internal const string IdPrefix = "output-";

    /// <summary>
    ///     The longest malformed identifier a refusal echoes back.
    /// </summary>
    /// <remarks>
    ///     A well-formed identifier is at most 26 characters (the prefix and a 19-digit number), so
    ///     a near miss is always echoed; anything longer is text the model supplied, and echoing it
    ///     whole would let a refusal grow without bound.
    /// </remarks>
    internal const int MaxEchoedIdCharacters = 64;

    /// <summary>
    ///     The writer options used to indent structured data for paging.
    /// </summary>
    /// <remarks>
    ///     The relaxed encoder keeps non-ASCII text and characters such as angle brackets readable;
    ///     the output is shown to a model, never embedded in HTML, so escaping them would only make
    ///     it harder to read.
    /// </remarks>
    private static readonly JsonWriterOptions IndentedWriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     The lock guarding <see cref="_entries"/>, <see cref="_order"/>,
    ///     <see cref="_retainedCharacters"/> and <see cref="_issued"/>.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The retained entries, keyed by their numeric identifier.
    /// </summary>
    private readonly Dictionary<long, ToolOutputEntry> _entries = [];

    /// <summary>
    ///     The numeric identifiers of the retained entries, oldest first, for eviction.
    /// </summary>
    private readonly Queue<long> _order = new();

    /// <summary>
    ///     The ceilings this store observes.
    /// </summary>
    private readonly ToolOutputOptions _options;

    /// <summary>
    ///     The characters currently retained across every entry.
    /// </summary>
    private long _retainedCharacters;

    /// <summary>
    ///     The numeric identifier most recently issued, or zero when none has been.
    /// </summary>
    private long _issued;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ToolOutputStore"/> class.
    /// </summary>
    /// <param name="options">The ceilings the store observes. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public ToolOutputStore(ToolOutputOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    ///     Gets the ceilings this store observes.
    /// </summary>
    public ToolOutputOptions Options => _options;

    /// <summary>
    ///     Normalizes and retains one output, evicting the oldest outputs as needed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     An output whose normalized size exceeds the per-output ceiling is not retained and
    ///     evicts nothing: refusing one output must not cost the model the outputs it already
    ///     holds references to.
    ///     </para>
    ///     <para>
    ///     An output whose raw length alone proves it exceeds the ceiling is refused before it is
    ///     normalized, so a very large output is never copied into lines only to be discarded. The
    ///     test never refuses an output that would fit; see <see cref="MustExceed"/>.
    ///     </para>
    /// </remarks>
    /// <param name="text">The output text. Must not be null.</param>
    /// <returns>
    ///     The retained entry, or <see langword="null"/> when the output exceeds
    ///     <see cref="ToolOutputOptions.MaxOutputCharacters"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public ToolOutputEntry? Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Refuse on the raw length before any copy is made. The bound is exact, so an output that
        // would fit once normalized is never refused here; it is refused below if it does not fit.
        if (MustExceed(text.Length, _options.MaxOutputCharacters))
        {
            return null;
        }

        // Normalize outside the lock: it is pure and can be the most expensive step.
        var lines = Normalize(text, out var characters, out var wrapped);
        if (characters > _options.MaxOutputCharacters)
        {
            return null;
        }

        lock (_gate)
        {
            // Evict oldest first until the new entry fits both ceilings. The per-output ceiling is
            // no larger than the store's, so this always terminates with room for the new entry.
            while (_order.Count > 0
                   && (_order.Count + 1 > _options.MaxRetainedOutputs
                       || _retainedCharacters + characters > _options.MaxRetainedCharacters))
            {
                var oldest = _order.Dequeue();
                _retainedCharacters -= _entries[oldest].Characters;
                _entries.Remove(oldest);
            }

            var number = ++_issued;
            var entry = new ToolOutputEntry(
                IdPrefix + number.ToString(CultureInfo.InvariantCulture),
                lines,
                characters,
                wrapped);

            _entries.Add(number, entry);
            _order.Enqueue(number);
            _retainedCharacters += characters;
            return entry;
        }
    }

    /// <summary>
    ///     Looks up a retained output by the identifier a model supplied.
    /// </summary>
    /// <remarks>
    ///     Surrounding whitespace is ignored; otherwise the identifier must be exactly the prefix
    ///     followed by a positive decimal number with no leading zero, compared ordinally, so an
    ///     identifier means exactly the output it was issued for.
    /// </remarks>
    /// <param name="id">The identifier to look up. May be null.</param>
    /// <param name="entry">On return, the entry when found; otherwise <see langword="null"/>.</param>
    /// <returns>The outcome of the lookup.</returns>
    public ToolOutputLookup Find(string? id, out ToolOutputEntry? entry)
    {
        entry = null;
        if (!TryParse(id, out var number))
        {
            return ToolOutputLookup.Malformed;
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(number, out entry))
            {
                return ToolOutputLookup.Found;
            }

            // Identifiers are issued in order, so an absent one at or below the last issued was
            // retained once and has been evicted; one above it was never issued.
            return number <= _issued ? ToolOutputLookup.Evicted : ToolOutputLookup.Unknown;
        }
    }

    /// <summary>
    ///     Composes the refusal a reader returns when a lookup did not find an output.
    /// </summary>
    /// <remarks>
    ///     Kept beside the lookup so both readers state the same three facts in the same words. Each
    ///     refusal states what is so and stops: a malformed identifier names the form identifiers
    ///     take, an unknown one says it was never issued, and an evicted one says it is no longer
    ///     retained and why. A malformed identifier is echoed only when it is no longer than
    ///     <see cref="MaxEchoedIdCharacters"/>, so no refusal's length is the model's to choose.
    /// </remarks>
    /// <param name="outcome">The lookup outcome. Must not be <see cref="ToolOutputLookup.Found"/>.</param>
    /// <param name="id">The identifier the model supplied. May be null.</param>
    /// <returns>The composed refusal.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="outcome"/> is <see cref="ToolOutputLookup.Found"/> or undefined.
    /// </exception>
    internal static object Refusal(ToolOutputLookup outcome, string? id)
    {
        var trimmed = id?.Trim() ?? string.Empty;
        return outcome switch
        {
            ToolOutputLookup.Malformed => ToolResult.Denied(DenialReason.InvalidRequest, DescribeMalformed(trimmed)),
            ToolOutputLookup.Unknown => ToolResult.Denied(
                DenialReason.TargetNotFound,
                "The output '" + trimmed + "' was never retained."),
            ToolOutputLookup.Evicted => ToolResult.Denied(
                DenialReason.TargetNotFound,
                "The output '" + trimmed + "' is no longer retained; it was evicted to keep the "
                + "retained outputs within their ceiling."),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The lookup did not fail."),
        };
    }

    /// <summary>
    ///     Composes the explanation of a malformed identifier.
    /// </summary>
    /// <param name="trimmed">The identifier the model supplied, trimmed.</param>
    /// <returns>The explanation, echoing the identifier only when it is short.</returns>
    private static string DescribeMalformed(string trimmed)
    {
        if (string.IsNullOrEmpty(trimmed))
        {
            return "An output id is required, of the form 'output-3' named in a retained output's preview.";
        }

        // A near miss is worth echoing so the model sees its mistake; a long value is not, and
        // echoing it whole would let the model choose the refusal's length.
        return trimmed.Length <= MaxEchoedIdCharacters
            ? "'" + trimmed + "' is not an output id; ids have the form 'output-3'."
            : "The supplied value is not an output id; ids have the form 'output-3'.";
    }

    /// <summary>
    ///     Indents a JSON document so its structure spans lines a model can page through.
    /// </summary>
    /// <remarks>
    ///     Written through a <see cref="Utf8JsonWriter"/> rather than a serializer, so the unit
    ///     carries no reflection and therefore no trimming warning into a consumer's build.
    /// </remarks>
    /// <param name="element">The JSON document to indent.</param>
    /// <returns>The indented JSON text.</returns>
    internal static string Indent(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, IndentedWriterOptions))
        {
            element.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    ///     Determines whether an output's raw length alone proves that its normalized size exceeds
    ///     a per-output ceiling.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Normalization keeps every character that is not part of a line break, counts each break
    ///     sequence as one character whether it was <c>\r\n</c>, <c>\r</c> or <c>\n</c>, drops at
    ///     most one break at the very end, and wrapping only adds breaks. With <c>C</c> characters
    ///     outside breaks and <c>B</c> break sequences, the raw length <c>L</c> is at most
    ///     <c>C + 2B</c> and the normalized size <c>N</c> is at least <c>C + B - 1</c>, so
    ///     <c>N</c> is at least <c>L / 2 - 1</c>. A raw length above <c>2(M + 1)</c> therefore
    ///     forces <c>N</c> above <c>M</c>.
    ///     </para>
    ///     <para>
    ///     The bound is tight: an output of <c>M + 1</c> <c>\r\n</c> pairs is <c>2(M + 1)</c>
    ///     characters long, normalizes to exactly <c>M</c>, and fits. Computed in 64 bits because
    ///     the ceiling may be as large as <see cref="int.MaxValue"/>.
    ///     </para>
    /// </remarks>
    /// <param name="rawLength">The output's length before normalization.</param>
    /// <param name="maxCharacters">The per-output ceiling, in normalized characters.</param>
    /// <returns>
    ///     <see langword="true"/> when the output cannot fit; <see langword="false"/> when it might,
    ///     and must be normalized to tell.
    /// </returns>
    internal static bool MustExceed(int rawLength, int maxCharacters) =>
        rawLength > 2L * ((long)maxCharacters + 1);

    /// <summary>
    ///     Splits an output into the lines it is retained as.
    /// </summary>
    /// <remarks>
    ///     <c>\r\n</c>, <c>\r</c> and <c>\n</c> all end a line, and a break at the very end ends
    ///     the last line rather than opening an empty one. A line longer than
    ///     <see cref="MaxLineCharacters"/> is split into chunks of at most that length, never
    ///     between the two halves of a surrogate pair. An empty output is one empty line, so every
    ///     entry has at least one line to report.
    /// </remarks>
    /// <param name="text">The output text.</param>
    /// <param name="characters">
    ///     On return, the characters retained, counting one per line break.
    /// </param>
    /// <param name="wrapped">On return, whether any line was wrapped.</param>
    /// <returns>The lines, in order.</returns>
    internal static string[] Normalize(string text, out long characters, out bool wrapped)
    {
        var lines = new List<string>();
        characters = Scan(text, lines, out wrapped);
        return [.. lines];
    }

    /// <summary>
    ///     Measures the size an output would be retained at, without copying it.
    /// </summary>
    /// <remarks>
    ///     Exactly the size <see cref="Normalize"/> reports and <see cref="Add"/> compares to
    ///     <see cref="ToolOutputOptions.MaxOutputCharacters"/>: both run the same scan, this one
    ///     without assembling the lines. A refusal can therefore state the size it refuses without
    ///     a very large output ever being copied into lines only to be discarded.
    /// </remarks>
    /// <param name="text">The output text.</param>
    /// <returns>The characters the output would be retained as, counting one per line break.</returns>
    internal static long Measure(string text) => Scan(text, lines: null, out _);

    /// <summary>
    ///     Scans an output into the lines it is retained as, and measures them.
    /// </summary>
    /// <remarks>
    ///     The single scan behind <see cref="Normalize"/> and <see cref="Measure"/>, so the size a
    ///     refusal states can never drift from the size the store judged.
    /// </remarks>
    /// <param name="text">The output text.</param>
    /// <param name="lines">The list to assemble the lines in, or <see langword="null"/> to only measure.</param>
    /// <param name="wrapped">On return, whether any line was wrapped.</param>
    /// <returns>The characters retained, counting one per line break.</returns>
    private static long Scan(string text, List<string>? lines, out bool wrapped)
    {
        wrapped = false;
        long characters = 0;
        long count = 0;

        var start = 0;
        while (true)
        {
            // Find the end of the current physical line.
            var end = text.IndexOfAny(['\r', '\n'], start);
            var lineEnd = end < 0 ? text.Length : end;

            // Wrapping splits a line without dropping a character, so the line's characters are
            // counted whole and only the number of lines it becomes depends on the wrap.
            var chunks = AppendWrapped(lines, text, start, lineEnd);
            wrapped |= chunks > 1;
            count += chunks;
            characters += lineEnd - start;

            if (end < 0)
            {
                break;
            }

            // A CRLF pair is one line break, not two.
            start = end + 1;
            if (text[end] == '\r' && start < text.Length && text[start] == '\n')
            {
                start++;
            }

            // A break at the very end terminates the last line rather than opening an empty one,
            // matching how text_file_read counts the lines of a file.
            if (start == text.Length)
            {
                break;
            }
        }

        // Count the line breaks between lines, so the figure is the length of the normalized text.
        return characters + count - 1;
    }

    /// <summary>
    ///     Appends one physical line, wrapped into chunks no longer than
    ///     <see cref="MaxLineCharacters"/>.
    /// </summary>
    /// <param name="lines">The lines being assembled, or <see langword="null"/> to only count them.</param>
    /// <param name="text">The output text.</param>
    /// <param name="start">The index the physical line starts at.</param>
    /// <param name="end">The index one past the physical line's last character.</param>
    /// <returns>
    ///     The number of lines the physical line becomes: one, or more when it was wrapped.
    /// </returns>
    private static int AppendWrapped(List<string>? lines, string text, int start, int end)
    {
        if (end - start <= MaxLineCharacters)
        {
            lines?.Add(text[start..end]);
            return 1;
        }

        var chunks = 0;
        var position = start;
        while (position < end)
        {
            var length = Math.Min(MaxLineCharacters, end - position);

            // Never split a surrogate pair: a lone half is not a character a model can read.
            if (position + length < end && char.IsHighSurrogate(text[position + length - 1]))
            {
                length--;
            }

            lines?.Add(text.Substring(position, length));
            position += length;
            chunks++;
        }

        return chunks;
    }

    /// <summary>
    ///     Parses an identifier of the form the store issues.
    /// </summary>
    /// <param name="id">The identifier to parse. May be null.</param>
    /// <param name="number">On success, the numeric part; otherwise zero.</param>
    /// <returns><see langword="true"/> when the identifier is well formed.</returns>
    private static bool TryParse(string? id, out long number)
    {
        number = 0;
        var trimmed = id?.Trim();
        if (trimmed is null || !trimmed.StartsWith(IdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Digits only, no sign, no leading zero: one spelling per output, so the identifier a model
        // repeats back is unambiguously the one it was given.
        var digits = trimmed[IdPrefix.Length..];
        if (digits.Length == 0 || digits[0] == '0' || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}

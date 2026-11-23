using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.CanvasNet.Codecs;
using DemaConsulting.CanvasNet.Files;
using DemaConsulting.CanvasNet.ImageSources;
using DemaConsulting.CanvasNet.Mermaid;
using DemaConsulting.CanvasNet.PlantUml;
using DemaConsulting.CanvasNet.Render3D.SceneRendering;
using DemaConsulting.CanvasNet.Scad;
using DemaConsulting.CanvasNet.VegaLite;
using DemaConsulting.CanvasNet.Viewer;

namespace DemaConsulting.AgentKit.Tools.Visual.Rendering;

/// <summary>
///     Turns a failed render into text a model can act on: a stable code, the line and column the
///     failure points at, an excerpt of the source with a caret under the column, a hint written
///     for the model rather than for a host, and the notes the render reported.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why AgentKit shapes the failure itself.</b> The rendering library raises a format's
///     syntax error as a general "could not be drawn" exception wrapping the format library's own
///     exception, with no stable code and with the location only inside the message text
///     (<see href="https://github.com/demaconsulting/CanvasNet/issues/166">CanvasNet #166</see>).
///     A model handed that message raw sees the library's framing, a host path, and hints written
///     for a command line. Shaping it is what makes the draw → read the error → fix → draw again
///     loop work on the first retry.
///     </para>
///     <para>
///     <b>Classification is by exception type, never by message text.</b> The chain of inner
///     exceptions is walked and the most specific known type decides the code: a file-access
///     refusal anywhere in the chain wins, then a budget, then an unsupported feature, then the
///     format library the exception belongs to, then corrupt input. Message text is not a stable
///     contract, and at least one message is known to be misleading — malformed Vega-Lite JSON is
///     reported as nesting too deep
///     (<see href="https://github.com/demaconsulting/CanvasNet/issues/163">CanvasNet #163</see>)
///     — so a classifier reading words would report a syntax error as a budget problem.
///     </para>
///     <para>
///     <b>The location is read from the typed exception where there is one,</b> and otherwise
///     parsed from the uniform <c>file:line:column[ pointer]: </c> prefix every format writes,
///     and for SVG from the XML reader's "line N, column M". The prefix is removed from the
///     message either way, together with the library's framing and a leading detail code, which
///     is reported separately.
///     </para>
///     <para>
///     <b>No host path reaches the model.</b> The working directory, every grant root and the
///     input's folder are replaced by the form a model already knows — relative to the working
///     directory, or <c>&lt;outside&gt;</c> — and any other absolute path by
///     <c>&lt;path&gt;</c>. Only the outermost exception's message and the library's hint
///     are surfaced; an inner exception's text, which may name a host file, never is. The
///     library's naming of its file-access policy
///     and its host-oriented advice ("add the folder to the allowed roots", "without --strict")
///     are replaced by words that describe the agent's own situation.
///     </para>
///     <para>
///     The class is static and stateless and is therefore safe for concurrent use from any number
///     of threads.
///     </para>
/// </remarks>
internal static partial class RenderErrorShaper
{
    /// <summary>
    ///     The code of a render that did not finish within the configured timeout.
    /// </summary>
    internal const string RenderTimeoutCode = "render-timeout";

    /// <summary>
    ///     The code of a render that produced an empty or almost empty picture.
    /// </summary>
    internal const string EmptyRenderCode = "empty-render";

    /// <summary>
    ///     The longest excerpt of a source line shown under a failure, in characters.
    /// </summary>
    private const int MaxExcerptLength = 160;

    /// <summary>
    ///     The text a host folder outside the working directory is replaced with.
    /// </summary>
    private const string OutsidePlaceholder = "<outside>";

    /// <summary>
    ///     The text any other absolute host path is replaced with.
    /// </summary>
    private const string PathPlaceholder = "<path>";

    /// <summary>
    ///     The hint for a referenced file the access policy refused.
    /// </summary>
    private const string FileAccessHint =
        "Reference only files inside the locations you may read, by a path relative to the "
        + "referring file's folder (for inline source, the workspace root).";

    /// <summary>
    ///     The hint for a picture or model that exceeded a size budget.
    /// </summary>
    private const string BudgetHint =
        "Make the drawing smaller (fewer nodes, marks or triangles) or request a smaller width.";

    /// <summary>
    ///     The hint for a render that did not finish in time.
    /// </summary>
    private const string TimeoutHint =
        "Simplify the source (fewer nodes, marks or triangles) or request a smaller width.";

    /// <summary>
    ///     The hint for a feature the format library does not draw.
    /// </summary>
    private const string UnsupportedHint =
        "Remove or replace the construct the message names; the notes list what is not drawn.";

    /// <summary>
    ///     The hint for an empty or almost empty picture.
    /// </summary>
    private const string EmptyHint =
        "Check that the source draws visible content; the notes name anything that was not drawn.";

    /// <summary>
    ///     Whether two spellings of a host path that differ only in case name the same folder here.
    /// </summary>
    /// <remarks>
    ///     True on Windows and macOS, as <see cref="PathPolicy"/> treats them. A scrubber that
    ///     compared more strictly than the policy would leave a differently cased host path in text
    ///     bound for the model.
    /// </remarks>
    private static readonly bool NamesIgnoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>
    ///     How long matching one host folder against a text may take.
    /// </summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     The separators a path may be written with, on any platform.
    /// </summary>
    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    ///     Classifies the exception a render threw.
    /// </summary>
    /// <remarks>
    ///     See the type remarks for the order in which the chain of inner exceptions decides the
    ///     code. The message is the outermost exception's, cleaned of the library's framing and of
    ///     the location prefix; host paths are left for <see cref="Shape"/> to remove.
    /// </remarks>
    /// <param name="exception">The exception the render threw. Must not be null.</param>
    /// <returns>The classified failure, carrying no notes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    internal static RenderFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // The chain is collected once; every classification step walks the same list.
        var chain = new List<Exception>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            chain.Add(current);
        }

        var code = ClassifyCode(chain);
        var hint = chain.OfType<ViewerException>().Select(viewer => viewer.Hint).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
            ?? chain.OfType<FileReferenceException>().Select(reference => reference.Hint).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        // The message is cleaned of its framing and location prefix; the typed location, where a
        // format library supplied one, is preferred over the parsed one.
        var message = CleanMessage(exception.Message, out var file, out var line, out var column, out var pointer, out var detail);
        if (TryTypedLocation(chain, out var typedFile, out var typedLine, out var typedColumn, out var typedPointer))
        {
            file = typedFile ?? file;
            line = typedLine;
            column = typedColumn;
            pointer = typedPointer ?? pointer;
        }

        return new RenderFailure(code, detail, message, hint, file, line, column, pointer, []);
    }

    /// <summary>
    ///     Creates the failure of a render that did not finish in time.
    /// </summary>
    /// <param name="timeout">The timeout the render exceeded.</param>
    /// <returns>The <c>render-timeout</c> failure.</returns>
    internal static RenderFailure Timeout(TimeSpan timeout)
    {
        return new RenderFailure(
            RenderTimeoutCode,
            null,
            "The render did not finish within "
            + timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)
            + " seconds.",
            null,
            null,
            null,
            null,
            null,
            []);
    }

    /// <summary>
    ///     Cleans a message the library composed: removes the "could not be drawn" framing, the
    ///     location prefix and a leading detail code, reporting each separately.
    /// </summary>
    /// <param name="message">The message to clean. Must not be null.</param>
    /// <param name="file">The file name of the location prefix, or null.</param>
    /// <param name="line">The line of the location, or null.</param>
    /// <param name="column">The column of the location, or null.</param>
    /// <param name="pointer">The JSON pointer of the location, or null.</param>
    /// <param name="detail">The leading detail code, or null.</param>
    /// <returns>The message without its framing, location prefix and detail code.</returns>
    internal static string CleanMessage(
        string message,
        out string? file,
        out int? line,
        out int? column,
        out string? pointer,
        out string? detail)
    {
        ArgumentNullException.ThrowIfNull(message);

        file = null;
        line = null;
        column = null;
        pointer = null;
        detail = null;

        // The framing names the content by the name it was drawn as and the source that drew it,
        // neither of which tells a model anything.
        var text = Framing().Replace(message.Trim(), string.Empty);

        // The uniform location prefix every format writes.
        var location = LocationPrefix().Match(text);
        if (location.Success)
        {
            file = location.Groups["file"].Value;
            line = int.Parse(location.Groups["line"].Value, CultureInfo.InvariantCulture);
            column = int.Parse(location.Groups["column"].Value, CultureInfo.InvariantCulture);
            pointer = location.Groups["pointer"].Success ? location.Groups["pointer"].Value : null;
            text = text[location.Length..];
        }
        else
        {
            // An SVG's XML reader states its position inside the text instead.
            var xml = XmlPosition().Match(text);
            if (xml.Success)
            {
                line = int.Parse(xml.Groups["line"].Value, CultureInfo.InvariantCulture);
                column = int.Parse(xml.Groups["column"].Value, CultureInfo.InvariantCulture);
            }
        }

        // A finer code leading the description is reported beside the main code instead.
        var detailMatch = DetailCode().Match(text);
        if (detailMatch.Success)
        {
            detail = detailMatch.Groups["code"].Value;
            text = text[detailMatch.Length..];
        }

        return text.Trim();
    }

    /// <summary>
    ///     Determines whether a text is a code in the library's form, such as
    ///     <c>mermaid-syntax-error</c>.
    /// </summary>
    /// <param name="text">The text to test. Must not be null.</param>
    /// <returns>True when the text is a lower-case, hyphenated code.</returns>
    internal static bool IsCode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return CodeOnly().IsMatch(text);
    }

    /// <summary>
    ///     Shapes a failure into the text a model receives.
    /// </summary>
    /// <remarks>
    ///     The first line states the code, the location and the message; an excerpt of the source
    ///     line with a caret follows when the location is in the source the context holds; then
    ///     the hint, the notes, and a help topic when the context names a help catalog that has
    ///     one. Every line passes through <see cref="Scrub"/>.
    /// </remarks>
    /// <param name="failure">The failure to shape. Must not be null.</param>
    /// <param name="context">What the caller knows about the render. Must not be null.</param>
    /// <returns>The shaped text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    internal static string Shape(RenderFailure failure, ShapeContext context)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(context);

        var text = new StringBuilder();

        // The headline: what failed, where, and the library's own words for it.
        text.Append("Render failed (").Append(failure.Code);
        if (failure.Detail is not null && !string.Equals(failure.Detail, failure.Code, StringComparison.Ordinal))
        {
            text.Append('/').Append(failure.Detail);
        }

        text.Append(')');
        if (failure.Line is { } line)
        {
            text.Append(" at line ").Append(line.ToString(CultureInfo.InvariantCulture));
            if (failure.Column is { } column)
            {
                text.Append(", column ").Append(column.ToString(CultureInfo.InvariantCulture));
            }

            if (failure.Pointer is not null)
            {
                text.Append(" (").Append(failure.Pointer).Append(')');
            }
        }

        text.Append(": ").Append(Rewrite(failure.Message)).Append('\n');

        // The source line with a caret under the column, when the location is in that source.
        AppendExcerpt(text, failure, context);

        // Advice the model can follow, never advice addressed to a host or a command line.
        var hint = HintFor(failure);
        if (hint is not null)
        {
            text.Append("Hint: ").Append(hint).Append('\n');
        }

        if (failure.Notes.Count > 0)
        {
            text.Append("Notes: ").Append(string.Join("; ", failure.Notes.Select(Rewrite))).Append('\n');
        }

        // The help line only names a topic the help tool will actually serve.
        if (context.Help is { } help)
        {
            var topic = help.Tag + "/diagnostics";
            if (help.TopicExists(topic))
            {
                text.Append("Help: ").Append(help.ToolName).Append(" topic '").Append(topic).Append("'.\n");
            }
        }

        return Scrub(text.ToString().TrimEnd('\n'), context.Policy, context.InputFolder);
    }

    /// <summary>
    ///     Removes host paths from text bound for a model.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The working directory, every grant root and the input's folder are replaced, longest
    ///     first, by their form relative to the working directory, or by <c>&lt;outside&gt;</c>
    ///     when they lie outside it. A folder is matched only whole — where a path can start and
    ///     up to the end of its last name — so a sibling such as <c>ws-backup</c> is not mistaken
    ///     for <c>ws</c>. It is matched in every spelling a document or the library may use:
    ///     either separator, a <c>\\?\</c> or <c>//?/</c> device prefix, a <c>file:</c> URI
    ///     scheme, and percent-encoded names; the prefix is removed with the folder. Case is
    ///     ignored on Windows and macOS, as <see cref="PathPolicy"/> ignores it, so a host path
    ///     the library reports in another case is still replaced.
    ///     </para>
    ///     <para>
    ///     Any absolute path that remains — one under none of those folders, such as the system's
    ///     temporary folder, a user profile, a cache or a link's real target — is replaced whole
    ///     by <c>&lt;path&gt;</c>: a drive-qualified, network, device or <c>file:</c> URI path
    ///     wherever it appears, and a POSIX path when it is quoted. An unquoted <c>/</c>-led
    ///     token is left alone because it is as likely to be a JSON pointer, and a relative path
    ///     is left alone because it is the model's own words.
    ///     </para>
    /// </remarks>
    /// <param name="text">The text to scrub. Must not be null.</param>
    /// <param name="policy">The access policy whose folders are the host's. Must not be null.</param>
    /// <param name="inputFolder">The folder of the rendered file, or null for inline source.</param>
    /// <returns>The text without host paths.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    internal static string Scrub(string text, PathPolicy policy, string? inputFolder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(policy);

        var comparison = NamesIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var folders = policy.Grants
            .Where(grant => grant.Root is not null)
            .Select(grant => grant.Root!)
            .Append(policy.WorkingDirectory)
            .Append(inputFolder)
            .Where(folder => !string.IsNullOrEmpty(folder))
            .Select(folder => Path.TrimEndingDirectorySeparator(folder!))
            .Where(folder => folder.Length > 1)
            .Distinct(NamesIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderByDescending(folder => folder.Length);

        // Each known folder, in every spelling it may take, becomes the form a model knows.
        foreach (var folder in folders)
        {
            var replacement = RelativeForm(folder, policy.WorkingDirectory, comparison);
            var endsWithSeparator = folder.EndsWith('\\') || folder.EndsWith('/');
            text = Regex.Replace(
                text,
                FolderPattern(folder),
                match =>
                {
                    // A drive root carries its separator, which the relative form must keep.
                    var form = endsWithSeparator ? replacement + Path.DirectorySeparatorChar : replacement;
                    return match.Groups["root"].Value.Contains('\\', StringComparison.Ordinal) ? form : form.Replace('\\', '/');
                },
                RegexOptions.CultureInvariant | (NamesIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None),
                PatternTimeout);
        }

        // Any absolute path still present is not one the model can use: it is replaced whole.
        text = FileUri().Replace(text, PathPlaceholder);
        text = DrivePath().Replace(text, PathPlaceholder);
        text = UncPath().Replace(text, PathPlaceholder);
        return QuotedPosixPath().Replace(text, PathPlaceholder);
    }

    /// <summary>
    ///     Builds the expression matching a host folder in every spelling a message may carry.
    /// </summary>
    /// <remarks>
    ///     The folder may be led by a device prefix (<c>\\?\</c> or <c>//?/</c>) or a
    ///     <c>file:</c> URI scheme, which are matched with it; its separators may be written
    ///     either way; and each name in it may be percent-encoded. The match must start where a
    ///     path can start and end where the folder's name ends, so a sibling such as
    ///     <c>ws-backup</c> is never taken for <c>ws</c>.
    /// </remarks>
    /// <param name="folder">The host folder, without a trailing separator unless it is a root.</param>
    /// <returns>The pattern, whose <c>root</c> group is the folder without its prefix.</returns>
    private static string FolderPattern(string folder)
    {
        var segments = folder.Split(Separators, StringSplitOptions.None).Select(segment =>
        {
            var plain = Regex.Escape(segment);
            var encoded = Regex.Escape(Uri.EscapeDataString(segment));
            return plain == encoded ? plain : "(?:" + plain + "|" + encoded + ")";
        });

        // A folder ending in a separator (a drive root) is complete at that separator.
        var end = folder.EndsWith('\\') || folder.EndsWith('/')
            ? string.Empty
            : @"(?=$|[\\/'""\s:;,)\]>]|\.(?:$|\s))";
        return @"(?<![\w.%~$/\\-])(?:(?i:file:)/{2,3})?(?:\\\\\?\\|//\?/)?(?<root>"
            + string.Join(@"[\\/]", segments) + ")" + end;
    }

    /// <summary>
    ///     Rewrites the library's host-oriented phrases into words about the agent's situation.
    /// </summary>
    /// <param name="text">The text to rewrite. Must not be null.</param>
    /// <returns>The rewritten text.</returns>
    internal static string Rewrite(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // The policy's internal name means nothing to a model; the library's trailing "Hint:" is
        // replaced by this shaper's own hint line.
        text = PolicyName().Replace(text, "by the access policy");
        text = TrailingHint().Replace(text, string.Empty);
        return text.Replace("the allowed roots", "the locations the agent may read", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Chooses the hint a failure is shaped with.
    /// </summary>
    /// <remarks>
    ///     The codes a model can only fix in one way get this shaper's own advice. Otherwise the
    ///     library's hint is used unless it is written for a host or a command line.
    /// </remarks>
    /// <param name="failure">The failure.</param>
    /// <returns>The hint, or null when there is none worth giving.</returns>
    private static string? HintFor(RenderFailure failure)
    {
        var own = failure.Code switch
        {
            ErrorCodes.FileAccessDenied => FileAccessHint,
            ErrorCodes.BudgetExceeded or ErrorCodes.SizeLimit => BudgetHint,
            ErrorCodes.UnsupportedFeature => UnsupportedHint,
            RenderTimeoutCode => TimeoutHint,
            EmptyRenderCode => EmptyHint,
            _ => null
        };

        if (own is not null)
        {
            return own;
        }

        var library = failure.Hint?.Trim();
        return string.IsNullOrEmpty(library)
            || library.Contains("--", StringComparison.Ordinal)
            || library.Contains("allowed roots", StringComparison.OrdinalIgnoreCase)
            || library.Contains("strict", StringComparison.OrdinalIgnoreCase)
            || library.Contains("RenderOptions", StringComparison.Ordinal)
            ? null
            : Rewrite(library);
    }

    /// <summary>
    ///     Appends the source line a failure points at, with a caret under its column.
    /// </summary>
    /// <param name="text">The text being built.</param>
    /// <param name="failure">The failure.</param>
    /// <param name="context">What the caller knows about the source.</param>
    private static void AppendExcerpt(StringBuilder text, RenderFailure failure, ShapeContext context)
    {
        // Only a location inside the source the caller holds can be shown; a failure inside an
        // included file names that file instead.
        if (failure.Line is not { } line
            || context.SourceText is null
            || (failure.File is not null
                && context.SourceFileName is not null
                && !string.Equals(Path.GetFileName(failure.File), context.SourceFileName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var lines = context.SourceText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (line < 1 || line > lines.Length)
        {
            return;
        }

        // A long line (a one-line JSON specification) is shown as a window around the column.
        var source = lines[line - 1].Replace('\t', ' ');
        var caret = Math.Clamp((failure.Column ?? 1) - 1, 0, source.Length);
        var prefix = string.Empty;
        if (source.Length > MaxExcerptLength)
        {
            var start = Math.Clamp(caret - (MaxExcerptLength / 2), 0, source.Length - MaxExcerptLength);
            source = source.Substring(start, MaxExcerptLength);
            caret -= start;
            if (start > 0)
            {
                prefix = "...";
            }
        }

        var number = line.ToString(CultureInfo.InvariantCulture);
        text.Append("  ").Append(number).Append(" | ").Append(prefix).Append(source).Append('\n');
        text.Append("  ").Append(' ', number.Length).Append(" | ").Append(' ', prefix.Length + caret).Append("^\n");
    }

    /// <summary>
    ///     Decides the failure code from the chain of exceptions, most specific first.
    /// </summary>
    /// <param name="chain">The exception and its inner exceptions, outermost first.</param>
    /// <returns>The failure code.</returns>
    private static string ClassifyCode(List<Exception> chain)
    {
        // A refused file anywhere in the chain is the fact the model must act on.
        var reference = chain.OfType<FileReferenceException>().FirstOrDefault();
        if (reference is not null)
        {
            return reference.Kind switch
            {
                FileReferenceFailureKind.TooLarge or FileReferenceFailureKind.TooManyFiles => ErrorCodes.BudgetExceeded,
                FileReferenceFailureKind.NotARegularFile or FileReferenceFailureKind.CannotRead => ErrorCodes.InputUnreadable,
                _ => ErrorCodes.FileAccessDenied
            };
        }

        // A code the viewer assigned itself is already the stable answer.
        var viewer = chain.OfType<ViewerException>().FirstOrDefault(exception => exception.Code != ErrorCodes.OperationFailed);
        if (viewer is not null)
        {
            return viewer.Code;
        }

        if (chain.Any(exception => exception is ImageSourceBudgetException or RenderBudgetExceededException))
        {
            return ErrorCodes.BudgetExceeded;
        }

        if (chain.Any(exception => exception is UnsupportedImageFeatureException
            || exception.GetType().Name.EndsWith("UnsupportedFeatureException", StringComparison.Ordinal)))
        {
            return ErrorCodes.UnsupportedFeature;
        }

        // The format library the exception belongs to, by the namespace of its type.
        foreach (var exception in chain)
        {
            var code = exception.GetType().Namespace switch
            {
                "DemaConsulting.CanvasNet.Mermaid" => ErrorCodes.MermaidError,
                "DemaConsulting.CanvasNet.PlantUml" => ErrorCodes.PlantUmlError,
                "DemaConsulting.CanvasNet.VegaLite" => ErrorCodes.VegaLiteError,
                "DemaConsulting.CanvasNet.Scad" => ErrorCodes.ScadError,
                _ => null
            };

            if (code is not null)
            {
                return code;
            }
        }

        return chain.Any(exception => exception is InvalidDataException or XmlException)
            ? ErrorCodes.CorruptInput
            : ErrorCodes.OperationFailed;
    }

    /// <summary>
    ///     Reads the location a format library's typed exception carries.
    /// </summary>
    /// <param name="chain">The exception and its inner exceptions, outermost first.</param>
    /// <param name="file">The file the location refers to, or null.</param>
    /// <param name="line">The 1-based line.</param>
    /// <param name="column">The 1-based column.</param>
    /// <param name="pointer">The JSON pointer, or null.</param>
    /// <returns>True when a known location was found.</returns>
    private static bool TryTypedLocation(
        List<Exception> chain,
        out string? file,
        out int? line,
        out int? column,
        out string? pointer)
    {
        foreach (var exception in chain)
        {
            (string? File, int Line, int Column, string? Pointer, bool Known) location = exception switch
            {
                MermaidSyntaxException e => (e.Location.File, e.Location.Line, e.Location.Column, null, e.Location.IsKnown),
                MermaidRenderException e => (e.Location.File, e.Location.Line, e.Location.Column, null, e.Location.IsKnown),
                MermaidUnsupportedFeatureException e => (e.Location.File, e.Location.Line, e.Location.Column, null, e.Location.IsKnown),
                PlantUmlSyntaxException e => (e.Location.File, e.Location.Line, e.Location.Column, null, e.Location.IsKnown),
                PlantUmlRenderException e => (e.Location.File, e.Location.Line, e.Location.Column, null, e.Location.IsKnown),
                PlantUmlUnsupportedFeatureException e => (e.Location.File, e.Location.Line, e.Location.Column, null, e.Location.IsKnown),
                VegaLiteSpecException e => (e.Location.File, e.Location.Line, e.Location.Column, e.Location.Pointer, e.Location.IsKnown),
                VegaLiteRenderException e => (e.Location.File, e.Location.Line, e.Location.Column, e.Location.Pointer, e.Location.IsKnown),
                VegaLiteUnsupportedFeatureException e => (e.Location.File, e.Location.Line, e.Location.Column, e.Location.Pointer, e.Location.IsKnown),
                ScadSyntaxException e => (e.Location.File, e.Location.Line, e.Location.Column, null, e.Location.Line > 0),
                ScadEvaluationException e => (e.Location.File, e.Location.Line, e.Location.Column, null, e.Location.Line > 0),
                ScadUnsupportedFeatureException e => (e.Location.File, e.Location.Line, e.Location.Column, null, e.Location.Line > 0),
                _ => (null, 0, 0, null, false)
            };

            if (location.Known && location.Line > 0)
            {
                file = location.File;
                line = location.Line;
                column = location.Column > 0 ? location.Column : null;
                pointer = string.IsNullOrEmpty(location.Pointer) ? null : location.Pointer;
                return true;
            }
        }

        file = null;
        line = null;
        column = null;
        pointer = null;
        return false;
    }

    /// <summary>
    ///     Gives the form a host folder is shown to a model in.
    /// </summary>
    /// <param name="folder">The host folder.</param>
    /// <param name="workingDirectory">The policy's working directory.</param>
    /// <param name="comparison">The platform's path comparison.</param>
    /// <returns>The relative form, <c>.</c>, or <c>&lt;outside&gt;</c>.</returns>
    private static string RelativeForm(string folder, string workingDirectory, StringComparison comparison)
    {
        var root = Path.TrimEndingDirectorySeparator(workingDirectory);
        if (string.Equals(folder, root, comparison))
        {
            return ".";
        }

        // A drive root keeps its separator when trimmed; another must be given one, so that a
        // sibling such as "ws-backup" is never taken to lie inside "ws".
        var prefix = root.EndsWith('\\') || root.EndsWith('/') ? root : root + Path.DirectorySeparatorChar;
        return folder.StartsWith(prefix, comparison)
            ? Path.GetRelativePath(root, folder)
            : OutsidePlaceholder;
    }

    /// <summary>
    ///     Matches the library's "could not be drawn" framing.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"^The image '[^']*' could not be drawn by the '[^']*' source: ", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Framing();

    /// <summary>
    ///     Matches the uniform <c>file:line:column[ pointer]: </c> location prefix, including a
    ///     file named by a drive-qualified path.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^:\r\n]+?):(?<line>\d+):(?<column>\d+)(?: (?<pointer>/[^:]*))?: ", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex LocationPrefix();

    /// <summary>
    ///     Matches the position an XML reader states inside its message.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"\(line (?<line>\d+), column (?<column>\d+)\)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex XmlPosition();

    /// <summary>
    ///     Matches a detail code leading a description, such as <c>mermaid-syntax-error: </c>.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"^(?<code>[a-z]+(?:-[a-z0-9]+)+): ", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex DetailCode();

    /// <summary>
    ///     Matches a text that is exactly one code in the library's form.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"^[a-z]+(?:-[a-z0-9]+)+$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CodeOnly();

    /// <summary>
    ///     Matches the library's naming of the file-access policy that refused a file.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"by the file-access policy '[^']*'", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PolicyName();

    /// <summary>
    ///     Matches a trailing "Hint: ..." the library appends to a message.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"\.?\s*Hint: [^\r\n]*$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TrailingHint();

    /// <summary>
    ///     Matches a <c>file:</c> URI with an authority, whatever path it names.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"(?<![\w.])(?i:file:)/{2,3}[^\s'""<>|]*?(?=[,;.:)\]]*(?:$|[\s'""<>|]))", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex FileUri();

    /// <summary>
    ///     Matches a drive-qualified absolute path, with or without a device prefix.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"(?<![\w.%~$/\\-])(?:\\\\\?\\|//\?/)?[A-Za-z]:[\\/][^\s'""<>|]*?(?=[,;.:)\]]*(?:$|[\s'""<>|]))", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex DrivePath();

    /// <summary>
    ///     Matches a network or device path written with back slashes, such as
    ///     <c>\\server\share\file</c> or <c>\\?\UNC\server\share</c>.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"(?<![\w\\])\\\\[^\\\s'""<>|]+\\[^\s'""<>|]*?(?=[,;.:)\]]*(?:$|[\s'""<>|]))", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex UncPath();

    /// <summary>
    ///     Matches a quoted POSIX absolute path of at least two names.
    /// </summary>
    /// <remarks>
    ///     Only a quoted path is matched: an unquoted <c>/</c>-led token is as likely to be a
    ///     JSON pointer such as <c>/data/url</c>, which locates the failure for the model.
    /// </remarks>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"(?<=['""])/{1,2}(?:[^/\s'""]+/)+[^\s'""]*(?=['""])", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex QuotedPosixPath();
}

/// <summary>
///     What a caller knows about a render that the shaper needs: the policy whose folders are the
///     host's, the source the location points into, and the help catalog a topic can be named
///     from.
/// </summary>
/// <param name="Policy">The access policy the tool observes.</param>
/// <param name="InputFolder">The folder of the rendered file, or null for inline source.</param>
/// <param name="SourceText">The source text, when the caller holds it as text, or null.</param>
/// <param name="SourceFileName">The file name the source was drawn as, or null.</param>
/// <param name="Help">The help catalog a topic may be named from, or null for none.</param>
internal sealed record ShapeContext(
    PathPolicy Policy,
    string? InputFolder,
    string? SourceText,
    string? SourceFileName,
    HelpReference? Help);

/// <summary>
///     A help tool a shaped failure may point the model to.
/// </summary>
/// <param name="ToolName">The name of the help tool.</param>
/// <param name="Tag">The help tag of the format that failed, such as <c>mermaid</c>.</param>
/// <param name="TopicExists">Answers whether the help tool serves a topic.</param>
internal sealed record HelpReference(string ToolName, string Tag, Func<string, bool> TopicExists);

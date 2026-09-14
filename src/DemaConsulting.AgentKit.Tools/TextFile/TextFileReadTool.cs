using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Text;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_read</c> tool: returns the text of one file the access policy permits the
///     agent to read, optionally paged to a range of lines and always prefixed with line numbers.
/// </summary>
/// <remarks>
///     <para>
///     <b>The tool takes its policy at construction, and there is no other way to build it.</b>
///     <see cref="Create"/> is the only factory, it requires a <see cref="PathPolicy"/>, and it
///     builds the tool through <see cref="GuardedToolFactory"/>. An unguarded read tool is
///     therefore unrepresentable rather than merely discouraged.
///     </para>
///     <para>
///     <b>A read is line-numbered and can be paged.</b> The result opens with a header of the form
///     <c>path lines A-B of N</c> — the returned range and the file's true total — and each body
///     line carries its 1-based line number, a <c>| </c> delimiter, then the raw content. A model
///     handed a fifteen-hundred-line file can therefore read one window, learn from the header how
///     many lines the file really has, and page directly to the region it needs in a single further
///     call. The <c>startLine</c> and <c>lineCount</c> arguments select the window; omitting both
///     reads from the first line to the end, subject to the result ceiling.
///     </para>
///     <para>
///     <b>The line-number prefix is not part of the file's content.</b> The number and the <c>| </c>
///     delimiter are display furniture the tool adds, so a model must not copy them into
///     <see cref="TextFileReplaceTool"/>, which matches the file's raw text exactly. The delimiter is
///     chosen to be visually unambiguous for exactly this reason.
///     </para>
///     <para>
///     <b>A range past the end of the file is an honest empty window, not a refusal.</b> A
///     <c>startLine</c> beyond the last line returns no body and a header naming the true total, so a
///     model learns it paged too far rather than being told its request was malformed. An empty file
///     reads as <c>path lines 0-0 of 0</c> for the same reason: it is a real, if empty, result.
///     </para>
///     <para>
///     <b>A large file is paged, not refused; only an oversized result or an unranged large read
///     is a denial, and the denial always carries recourse.</b> The file is streamed so that only
///     the requested window is held in memory: <see cref="ToolLimits.MaxReadBytes"/> bounds the
///     bytes materialized to satisfy that window rather than gating the whole file, so a model can
///     page to any line of a file far larger than the read ceiling. Reading such a file with no
///     <c>startLine</c>/<c>lineCount</c> asks for the whole file as one window and is refused —
///     but the refusal names the file's true total line count and directs the model to page with
///     <c>startLine</c> and <c>lineCount</c>, so there is always a way forward. A rendered window
///     that still exceeds <see cref="ToolLimits.MaxResultCharacters"/> is likewise refused with
///     the ceiling named, never truncated.
///     </para>
///     <para>
///     <b>A binary file is refused before it is ever decoded, never returned as garbled text.</b>
///     The tool sniffs a leading window and refuses a binary file as
///     <see cref="DenialReason.UnsupportedMediaType"/>. Detection is content-based — a recognized
///     byte-order mark means text, otherwise a NUL byte or a failed strict UTF-8 validation means
///     binary — because the honest text-versus-binary signal is in the bytes, not the name. The
///     byte-order-mark check comes first so a legitimately encoded UTF-16 or UTF-32 file, which
///     contains NUL bytes yet decodes correctly, is not misclassified. The <em>redirect</em> a
///     refusal offers is chosen from the extension by reusing
///     <see cref="ImageMediaTypes.TryResolveMediaType"/>, so the file is offered to <c>image_read</c>
///     when that resolver recognizes its type.
///     </para>
///     <para>
///     Every refusal is returned rather than thrown. A refusal the access policy produces states what
///     was requested, how a relative request was interpreted, and which locations are permitted; a
///     refusal this tool composes itself names only a ceiling, a sibling tool, or the shape of the
///     request. A directory or a missing file is refused with a plain statement of what was wrong,
///     naming no other tool.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class TextFileReadTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant on the unit that defines the tool so that a caller naming it
    ///     cannot drift from the name actually registered.
    /// </remarks>
    public const string ToolName = "text_file_read";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Reads the text content of a file the agent is permitted to read. Paths are relative to the "
        + "workspace root. The result opens with a 'path lines A-B of N' header and prefixes each "
        + "line with its 1-based number and a '| ' delimiter, which are not part of the file's "
        + "content. Optionally page with startLine and lineCount. Returns the numbered text, or a "
        + "denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, "
        + "for example 'notes.txt'.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound = "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when the file exists and is permitted but cannot be read.
    /// </summary>
    private const string FileUnreadable = "The requested file could not be read.";

    /// <summary>
    ///     The refusal used when a binary file's type is one the image tool can read.
    /// </summary>
    /// <remarks>
    ///     The tool the model should reach for instead is supplied to <see cref="ToolResult.Denied"/>
    ///     as the redirect argument, so the rendered sentence stays consistent with every other
    ///     redirect and the name cannot drift from the one the sibling tool actually publishes.
    /// </remarks>
    private const string BinaryImageContent =
        "The requested file is binary content, not text. Its type is one an image tool can read.";

    /// <summary>
    ///     The refusal used when a binary file's type has no better tool to name.
    /// </summary>
    private const string BinaryContent =
        "The requested file is binary content, not text, and cannot be read as text.";

    /// <summary>
    ///     The refusal used when a paging argument is not a positive number.
    /// </summary>
    private const string InvalidRange =
        "The startLine must be 1 or greater and lineCount must be 0 or greater. Line numbers are "
        + "1-based.";

    /// <summary>
    ///     The delimiter placed between a line's number and its content.
    /// </summary>
    /// <remarks>
    ///     A pipe followed by a space, chosen to read unambiguously and to make plain that the number
    ///     and the delimiter are display furniture rather than part of the file's raw text.
    /// </remarks>
    private const string LineNumberDelimiter = "| ";

    /// <summary>
    ///     The number of bytes buffered per read while streaming a file's lines.
    /// </summary>
    private const int StreamBufferSize = 16 * 1024;

    /// <summary>
    ///     Creates the <c>text_file_read</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every read this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application, not something a
        // model supplied, so it is surfaced rather than converted into a denial.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return Task<object> on purpose; see the type remarks before changing this.
        // Every parameter carries a default so that an omitted argument reaches the tool body as a
        // request this tool answers, rather than a framework error raised before the body.
        var read = (
                [Description(
                    "The path of the file to read, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null,
                [Description(
                    "The 1-based line to begin reading at. Optional: omit it to read from the "
                    + "first line.")]
                int? startLine = null,
                [Description(
                    "The number of lines to read. Optional: omit it to read to the end of the "
                    + "file, subject to the result limit.")]
                int? lineCount = null,
                CancellationToken cancellationToken = default) =>
            ReadAsync(policy, path, startLine, lineCount, cancellationToken);

        return GuardedToolFactory.Create(read, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Reads one file, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The order of the checks is the contract: the policy decision comes before anything is
    ///     learned about the file, so a refused path never reveals whether it exists.
    /// </remarks>
    /// <param name="policy">The access policy governing the read.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="startLine">The 1-based line to begin at, or null for the first line.</param>
    /// <param name="lineCount">The number of lines to read, or null for the rest of the file.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The numbered text window, or a refusal naming its reason.</returns>
    private static async Task<object> ReadAsync(
        PathPolicy policy,
        string? path,
        int? startLine,
        int? lineCount,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown: the model supplied it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // A paging argument outside its domain is a malformed request, refused before any file work.
        if (startLine is < 1 || lineCount is < 0)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, InvalidRange);
        }

        // The single read decision. Resolution and containment both happen inside the policy, so a
        // path outside the permitted location, or one a deny pattern excludes, is refused here
        // without this tool making any path judgment of its own.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A directory is not a file; the refusal states only that.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        // A missing file is reported as a plain fact, naming no other tool.
        if (!System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, FileNotFound);
        }

        return await ReadPermittedFileAsync(policy, path, realPath, startLine, lineCount, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads a file already known to exist and to be permitted, observing the policy's ceilings
    ///     and paging to the requested window.
    /// </summary>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="requested">The path the model requested, used to mirror its dialect.</param>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="startLine">The 1-based line to begin at, or null for the first line.</param>
    /// <param name="lineCount">The number of lines to read, or null for the rest of the file.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The numbered text window, or a refusal naming the ceiling it exceeded.</returns>
    private static async Task<object> ReadPermittedFileAsync(
        PathPolicy policy,
        string requested,
        string realPath,
        int? startLine,
        int? lineCount,
        CancellationToken cancellationToken)
    {
        try
        {
            // The length is a stat, not a read; it feeds the binary sniff's end-of-file decision and
            // is never used to gate the whole file, so a file larger than the read ceiling can still
            // be paged one window at a time.
            var length = new FileInfo(realPath).Length;

            // A binary file is refused before its bytes are ever decoded into text, from a leading
            // sniff that never materializes the whole file.
            if (await TextFileBinaryGuard.IsBinaryAsync(realPath, length, cancellationToken)
                .ConfigureAwait(false))
            {
                return ImageMediaTypes.TryResolveMediaType(realPath, out _)
                    ? ToolResult.Denied(
                        DenialReason.UnsupportedMediaType,
                        BinaryImageContent,
                        ImageReadTool.ToolName)
                    : ToolResult.Denied(
                        DenialReason.UnsupportedMediaType,
                        BinaryContent);
            }

            var maxReadBytes = policy.Limits.MaxReadBytes;
            var start = startLine ?? 1;

            // A null lineCount reads to the end; a long guards against overflow when a model sends a
            // very large lineCount close to int.MaxValue.
            var windowEnd = lineCount is { } count ? (long)start + count - 1 : long.MaxValue;

            var windowLines = new List<string>();
            long materializedBytes = 0;
            var overflow = false;
            var total = 0;

            await using var stream = new FileStream(
                realPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: StreamBufferSize,
                useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            // Stream every line so the true total is exact, but materialize only the requested
            // window; MaxReadBytes bounds the window's bytes rather than the file's.
            foreach (var line in TextLines.EnumerateLines(reader))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total++;

                if (overflow || total < start || total > windowEnd)
                {
                    continue;
                }

                materializedBytes += Encoding.UTF8.GetByteCount(line);
                if (materializedBytes > maxReadBytes)
                {
                    // The window itself exceeds the read ceiling — an unranged read of a large file
                    // always does. Stop materializing but keep streaming so the total stays exact.
                    overflow = true;
                    windowLines.Clear();
                    continue;
                }

                windowLines.Add(line);
            }

            if (overflow)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The file is " + total.ToString(CultureInfo.InvariantCulture)
                    + " lines and too large to read at once (the "
                    + maxReadBytes.ToString(CultureInfo.InvariantCulture)
                    + "-byte read limit). Read a range by supplying startLine and lineCount, for "
                    + "example startLine 1 with a small lineCount, then page forward.");
            }

            var rendered = RenderWindow(policy, requested, realPath, windowLines, start, total);

            // The return budget is separate and tighter; a window within the read ceiling can still
            // exceed it, and returning a truncated document would be worse than refusing.
            if (rendered.Length > policy.Limits.MaxResultCharacters)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The requested text exceeds the "
                    + policy.Limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                    + "-character result limit.");
            }

            return ToolResult.Text(rendered);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnreadable);
        }
    }

    /// <summary>
    ///     Renders the requested window of a file's text as a numbered listing with a range header.
    /// </summary>
    /// <remarks>
    ///     The header names the returned range and the file's true total, so a model learns how far
    ///     the file extends beyond the window it received. A window past the end of the file renders
    ///     as an honest empty body, and an empty file renders as <c>lines 0-0 of 0</c>.
    /// </remarks>
    /// <param name="policy">The access policy whose dialect the reported path mirrors.</param>
    /// <param name="requested">The path the model requested.</param>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="windowLines">The materialized lines of the requested window, in order.</param>
    /// <param name="start">The 1-based line the window begins at.</param>
    /// <param name="total">The file's true total line count, streamed to the end.</param>
    /// <returns>The rendered window.</returns>
    private static string RenderWindow(
        PathPolicy policy,
        string requested,
        string realPath,
        IReadOnlyList<string> windowLines,
        int start,
        int total)
    {
        var reportedPath = ReportedPath(policy, requested, realPath);

        // An empty file is a real, if empty, result rather than a refusal.
        if (total == 0)
        {
            return reportedPath + " lines 0-0 of 0";
        }

        // A window that begins past the last line, or a zero-count window, materializes no lines and
        // is an honest empty result naming the true total, not an error.
        if (start > total || windowLines.Count == 0)
        {
            return reportedPath + " lines " + start.ToString(CultureInfo.InvariantCulture)
                + "-" + (start - 1).ToString(CultureInfo.InvariantCulture)
                + " of " + total.ToString(CultureInfo.InvariantCulture);
        }

        var end = start + windowLines.Count - 1;
        var width = total.ToString(CultureInfo.InvariantCulture).Length;

        var builder = new StringBuilder();
        builder.Append(reportedPath)
            .Append(" lines ")
            .Append(start.ToString(CultureInfo.InvariantCulture))
            .Append('-')
            .Append(end.ToString(CultureInfo.InvariantCulture))
            .Append(" of ")
            .Append(total.ToString(CultureInfo.InvariantCulture));

        for (var index = 0; index < windowLines.Count; index++)
        {
            var number = (start + index).ToString(CultureInfo.InvariantCulture).PadLeft(width);
            builder.Append('\n')
                .Append(number)
                .Append(LineNumberDelimiter)
                .Append(TextLines.Content(windowLines[index]));
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Reports the file's path in the caller's dialect: relative when the caller addressed it
    ///     relatively and the policy permits, otherwise the absolute path.
    /// </summary>
    /// <param name="policy">The access policy whose dialect is mirrored.</param>
    /// <param name="requested">The path the model requested.</param>
    /// <param name="realPath">The resolved real location of the permitted file.</param>
    /// <returns>The path reported in the header.</returns>
    private static string ReportedPath(PathPolicy policy, string requested, string realPath)
    {
        return policy.EmitRelative(realPath, requested)
            ? TextLines.ToForwardSlash(Path.GetRelativePath(policy.WorkingDirectory, realPath))
            : TextLines.ToForwardSlash(realPath);
    }

    /// <summary>
    ///     Determines whether an exception represents a failure to reach or read a path rather than
    ///     a defect that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be reached or read;
    ///     otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }
}

using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_write</c> tool: sets the whole content of a text file the access policy
///     permits the agent to write, creating the file when it is absent and capturing the previous
///     content before replacing it.
/// </summary>
/// <remarks>
///     <para>
///     <b>This tool makes the file's content be the supplied text, and nothing else.</b> There is
///     deliberately no <c>overwrite</c> flag: a mode switch would make one name mean two different
///     things and force the model to reason about which mode it is in before it can predict what a
///     call will do. The family expresses the distinction with three separate names instead —
///     <see cref="TextFileCreateTool"/> brings a new file into existence and refuses to replace one,
///     <see cref="TextFileReplaceTool"/> changes part of an existing file's content by exact match,
///     and this tool sets all of it. The description names both siblings, because an agent that
///     reaches for this tool to make a small edit clobbers the file.
///     </para>
///     <para>
///     <b>The previous content is captured before it is replaced.</b> The family's invariant is that
///     nothing it destroys <em>sequentially</em> is unrecoverable through
///     <see cref="TextFileLineBuffers"/>, and a wholesale
///     overwrite destroys more than any other operation in the family. So the file's entire previous
///     text is captured first, exactly as <see cref="TextFileCutLinesTool"/> captures the slice it
///     removes, and it can be restored with <see cref="TextFilePasteLinesTool"/>.
///     </para>
///     <para>
///     <b>The capture goes to <see cref="TextFileLineBuffers.OverwrittenSlot"/>, never to the default
///     slot, and this tool takes no slot name.</b> The default slot is the model's working clipboard:
///     a model that has cut a fragment and is about to paste it holds that fragment there, and a
///     capture landing on top of it would turn a safety net into a second act of destruction. A
///     model-supplied name would not help either — the model did not ask for this capture and by
///     construction did not anticipate needing it, and a name it chose could be the default one. A
///     fixed name is also the only way the description can state unconditionally where the displaced
///     content went.
///     </para>
///     <para>
///     <b>Only the most recent overwrite is recoverable, and the tool says so.</b> One fixed slot
///     means a second overwrite replaces the first capture — ordinary clipboard semantics this family
///     already documents. That limit is stated in the description rather than implied away, so a
///     model does not read the capture as an unbounded undo history.
///     </para>
///     <para>
///     <b>The capture is a sequential guarantee, and that limit is stated too.</b> Reading the
///     previous content, capturing it and writing the new content are three steps and are not
///     serialized against another writer. If two callers write the same file concurrently, the
///     buffer holds the content that was there before whichever write read it, and the content
///     an interleaved write produced can be destroyed without ever having been captured. No
///     cross-tool lock is taken to close that: the library coordinates concurrent access
///     nowhere else, an agent's tool calls are sequential, and a delegated sub-agent composes
///     its own buffers — so a locking regime for one tool would cost far more than the exposure
///     it removes. The promise is therefore scoped rather than quietly excepted, exactly as the
///     one-slot limit above is: <em>every sequential overwrite is recoverable</em>.
///     </para>
///     <para>
///     <b>Nothing is captured when there is nothing to lose, and the slot is released.</b> An
///     absent file has no previous content, and an existing but empty file has none either.
///     Capturing an empty string in either case would leave a slot that pastes nothing while
///     reporting success, so the tool captures nothing — and releases the slot rather than
///     leaving an earlier capture standing, which would have the confirmation report that
///     nothing was captured while a later paste handed back content displaced by an older
///     write. The confirmation distinguishes a file that was created from one that was
///     replaced, and says whether anything was captured.
///     </para>
///     <para>
///     <b>A non-text file is refused rather than captured and destroyed.</b> Binary content does not
///     survive a text round trip, so a captured binary file would not restore — the recoverability
///     promise would be false in exactly the case where the destruction is total. The shared
///     <see cref="TextFileBinaryGuard"/> makes that decision from a leading window, the same way the
///     read and search tools do.
///     </para>
///     <para>
///     <b>No ceiling is enforced, on the written content or on the capture, and that is a decision
///     rather than an omission.</b> Every ceiling in <see cref="ToolLimits"/> reasons from the
///     model's context budget and is applied to content flowing <em>to</em> the model. The
///     <c>content</c> argument arrives <em>from</em> the model and is already in the transcript, so
///     refusing it would spend a turn rejecting text the provider has already accepted and would save
///     no context; the capture goes into the buffer and never into a result, so it spends none
///     either. This matches every other write path in the family.
///     </para>
///     <para>
///     <b>This tool consults <see cref="PathPolicy.TryResolveWrite"/> and nothing else.</b> A path an
///     agent may read is refused unless a read-write grant permits it too, which is what keeps a
///     read-wide, write-narrow configuration meaningful. The tool creates no directory: a missing
///     parent is refused rather than materialized, and a path naming a directory is refused outright.
///     </para>
///     <para>
///     The delegate is declared to return <c>Task&lt;object&gt;</c> deliberately — see the remarks on
///     <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown. Empty
///     content is a legitimate empty file; only a missing content argument is refused. A refusal
///     states a fact and prescribes no remedy; it is the description that names the siblings.
///     </para>
///     <para>
///     The class is stateless and holds no buffer of its own; the buffer is supplied by the pack and
///     guards its own access, so the tool is safe for concurrent use from any number of threads. That
///     safety is about the tool's own state, not about the file: the capture guarantee is the
///     sequential one described above.
///     </para>
/// </remarks>
public static class TextFileWriteTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "text_file_write";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    /// <remarks>
    ///     The description carries the work of steering a model to the right tool, because a model
    ///     that reaches for this one to make a small edit destroys the rest of the file. It therefore
    ///     names both siblings, and states the recovery path and its two limits plainly.
    /// </remarks>
    private const string ToolDescription =
        "Sets the whole content of a text file the agent is permitted to write to the given text, "
        + "creating the file when it does not exist and replacing everything in it when it does. "
        + "Paths are relative to the workspace root. Use " + TextFileCreateTool.ToolName
        + " to add a new file without risking an existing one, and " + TextFileReplaceTool.ToolName
        + " to change part of a file while leaving the rest alone. Before replacing, the file's "
        + "previous content is captured into the buffer named '"
        + TextFileLineBuffers.OverwrittenSlot + "', so it can be restored with "
        + TextFilePasteLinesTool.ToolName + "; only the most recent overwrite is kept there, and "
        + "only a write that was not interleaved with another write of the same file is captured "
        + "at all. "
        + "Returns a confirmation, or a denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, "
        + "for example 'notes.txt'.";

    /// <summary>
    ///     The refusal used when the request carries no content at all.
    /// </summary>
    /// <remarks>
    ///     Absent content is a malformed request; empty content is not. Emptying a file is a real
    ///     outcome an agent may legitimately want, so only <see langword="null"/> is refused.
    /// </remarks>
    private const string ContentRequired =
        "File content is required. Supply the text the file is to contain, which may be an empty "
        + "string when an empty file is what is wanted.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file. Name the file to write within it.";

    /// <summary>
    ///     The refusal used when the parent directory of the requested path does not exist.
    /// </summary>
    private const string ParentMissing =
        "The parent directory of the requested path does not exist.";

    /// <summary>
    ///     The refusal used when the existing file at the requested path is not text.
    /// </summary>
    /// <remarks>
    ///     Stated as a fact and nothing more. Binary content cannot be captured into the recovery
    ///     buffer and restored from it, so replacing it would destroy content the family promises is
    ///     recoverable.
    /// </remarks>
    private const string BinaryContent =
        "The file at the requested path holds binary content, not text. Its content could not be "
        + "captured for recovery, so it was left unchanged.";

    /// <summary>
    ///     The refusal used when a permitted path cannot be written.
    /// </summary>
    private const string FileUnwritable = "The requested file could not be written.";

    /// <summary>
    ///     Creates the <c>text_file_write</c> tool governed by an access policy, sharing the supplied
    ///     buffer with the paste tool so an overwrite can be undone.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>, which allocates one buffer per
    ///     composition and hands it to this tool alongside the cut, copy and paste tools. The policy
    ///     and the buffer are captured by the returned tool's delegate, so the tool cannot later be
    ///     pointed at a different policy or a different buffer.
    /// </remarks>
    /// <param name="policy">The access policy governing every write this tool performs.</param>
    /// <param name="buffers">The recovery buffer shared with the paste tool.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> or <paramref name="buffers"/> is
    ///     <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy, TextFileLineBuffers buffers)
    {
        // A missing policy or buffer is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(buffers);

        // Declared to return Task<object> on purpose; see the type remarks before changing this.
        // Both parameters carry a default so that an omitted argument becomes a refusal this tool
        // composes, rather than a framework error raised before the body is reached.
        var write = (
                [Description(
                    "The path of the file to write, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null,
                [Description(
                    "The text the file is to contain, replacing anything already in it. May be an "
                    + "empty string.")]
                string? content = null,
                CancellationToken cancellationToken = default) =>
            WriteAsync(policy, buffers, path, content, cancellationToken);

        return GuardedToolFactory.Create(write, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Sets one file's content, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <param name="policy">The access policy governing the write.</param>
    /// <param name="buffers">The recovery buffer the previous content is captured into.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="content">The text the model wishes the file to hold, or null when it supplied
    ///     none.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A confirmation naming what was written and what was captured, or a refusal naming
    ///     its reason.</returns>
    private static async Task<object> WriteAsync(
        PathPolicy policy,
        TextFileLineBuffers buffers,
        string? path,
        string? content,
        CancellationToken cancellationToken)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        if (content is null)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, ContentRequired);
        }

        // The write decision alone. A path a read-only grant permits is not thereby writable.
        if (!policy.TryResolveWrite(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // Writing over a directory is not a meaningful operation.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        return await WritePermittedFileAsync(buffers, realPath, content, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Captures whatever a permitted file already holds and then sets its content.
    /// </summary>
    /// <param name="buffers">The recovery buffer the previous content is captured into.</param>
    /// <param name="realPath">The real location the policy permitted.</param>
    /// <param name="content">The text the file is to hold.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> WritePermittedFileAsync(
        TextFileLineBuffers buffers,
        string realPath,
        string content,
        CancellationToken cancellationToken)
    {
        try
        {
            // A missing parent is refused, never created.
            var parent = Path.GetDirectoryName(realPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return ToolResult.Denied(DenialReason.TargetNotFound, ParentMissing);
            }

            var existed = System.IO.File.Exists(realPath);
            var previous = string.Empty;

            if (existed)
            {
                // A file whose content could not be captured and restored must not be destroyed;
                // the sniff reads only a leading window, never the whole file.
                var length = new FileInfo(realPath).Length;
                if (await TextFileBinaryGuard.IsBinaryAsync(realPath, length, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return ToolResult.Denied(DenialReason.UnsupportedMediaType, BinaryContent);
                }

                previous = await System.IO.File.ReadAllTextAsync(realPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Capture before writing, and only when there is content to lose. A write that
            // displaces nothing releases the slot rather than leaving it alone: the family promises
            // that only the most recent overwrite is recoverable, so an earlier capture left
            // standing would have the confirmation say nothing was captured while the paste tool
            // handed back content displaced by some older write. Read, capture and write are three
            // steps and are deliberately not serialized against another writer; see the type
            // remarks for why the guarantee is scoped to sequential writes rather than defended
            // with a lock.
            var captured = previous.Length > 0;
            if (captured)
            {
                buffers.Capture(TextFileLineBuffers.OverwrittenSlot, previous);
            }
            else
            {
                buffers.Release(TextFileLineBuffers.OverwrittenSlot);
            }

            await System.IO.File.WriteAllTextAsync(realPath, content, cancellationToken)
                .ConfigureAwait(false);

            return ToolResult.Text(Confirm(content, existed, captured, previous.Length));
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnwritable);
        }
    }

    /// <summary>
    ///     Composes the confirmation, which says what the file now holds and what became of what it
    ///     held before.
    /// </summary>
    /// <remarks>
    ///     A model cannot see the file, so "created" and "replaced" must be distinguished for it, and
    ///     a replacement must say whether anything was captured — otherwise a model told only that a
    ///     write succeeded has no way to know whether a recovery is available. The resulting line
    ///     count is reported for the same reason the create tool reports it: so a model that goes on
    ///     to address the file by line number needs no exploratory read first.
    /// </remarks>
    /// <param name="content">The text that was written.</param>
    /// <param name="existed">Whether a file was already present at the path.</param>
    /// <param name="captured">Whether previous content was captured into the buffer.</param>
    /// <param name="previousLength">The number of characters the file previously held.</param>
    /// <returns>The confirmation text.</returns>
    private static string Confirm(string content, bool existed, bool captured, int previousLength)
    {
        var wrote = "Wrote "
            + content.Length.ToString(CultureInfo.InvariantCulture)
            + " characters in "
            + TextLines.Split(content).Count.ToString(CultureInfo.InvariantCulture)
            + " lines.";

        if (!existed)
        {
            return wrote + " The file did not exist and was created, so nothing was captured.";
        }

        if (!captured)
        {
            return wrote + " The file was already empty, so nothing was captured.";
        }

        return wrote
            + " The file's previous content ("
            + previousLength.ToString(CultureInfo.InvariantCulture)
            + " characters) was captured into buffer '"
            + TextFileLineBuffers.OverwrittenSlot
            + "', replacing whatever that buffer held before.";
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be written; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool IsAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }
}

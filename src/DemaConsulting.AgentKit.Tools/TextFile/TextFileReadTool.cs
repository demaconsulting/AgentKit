using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Text;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_read</c> tool: returns the text of one file the access policy permits
///     the agent to read.
/// </summary>
/// <remarks>
///     <para>
///     <b>The tool takes its policy at construction, and there is no other way to build it.</b>
///     <see cref="Create"/> is the only factory, it requires a <see cref="PathPolicy"/>, and it
///     builds the tool through <see cref="GuardedToolFactory"/>. An unguarded read tool, or one
///     governed by no policy, is therefore unrepresentable rather than merely discouraged.
///     </para>
///     <para>
///     <b>The delegate is declared <c>Task&lt;object&gt;</c> deliberately.</b> A tool returns a
///     union — a refusal, or text — and <see cref="object"/> is the only type that expresses it.
///     That is also exactly the declared shape the underlying function factory would serialize
///     into JSON, which is why the guarded factory exists and why this declaration must not be
///     "tidied up" into a strongly-typed one; see the remarks on
///     <see cref="GuardedToolFactory"/>.
///     </para>
///     <para>
///     <b>A path the model supplies is read relative to the workspace root.</b> A model asks for
///     <c>notes.txt</c>, and that is the request this tool answers; the access policy holds the
///     workspace the name is resolved against. An absolute path remains expressible and remains
///     subject to the same containment decision.
///     </para>
///     <para>
///     <b>A missing path argument is a refusal, never a framework error.</b> The path parameter
///     carries a default so that an omitted argument reaches the tool body. A file path really
///     is required here — unlike a listing, a read has no sensible default target — so the tool
///     refuses and says what to supply instead.
///     </para>
///     <para>
///     <b>An oversized file is a denial naming the ceiling, never a truncation.</b> Silently
///     returning the first part of a file would hand the model an incomplete document it has no
///     way to detect, and it would then reason confidently about content it never saw. A denial
///     that names the ceiling lets the model narrow its request instead.
///     </para>
///     <para>
///     <b>A binary file is refused before it is ever decoded, never returned as garbled text.</b>
///     Decoding an image or other binary content as text produces a wall of
///     replacement characters that tells the model nothing true, wastes a large slice of its
///     context window, and invites it to hallucinate about content it cannot actually see. The
///     tool therefore sniffs a leading window and refuses a binary file as
///     <see cref="DenialReason.UnsupportedMediaType"/>. Detection is content-based — a
///     recognized byte-order mark means text, otherwise a NUL byte or a failed strict UTF-8
///     validation means binary — because the honest text-versus-binary signal is in the bytes,
///     not the name. The byte-order-mark check comes first precisely so that a legitimately
///     encoded UTF-16 or UTF-32 file, which contains NUL bytes yet decodes correctly today, is
///     not misclassified by the NUL rule. The <em>redirect</em> that a refusal offers is, by
///     contrast, chosen from the extension: the file is offered to <c>image_read</c> when
///     <see cref="ImageMediaTypes.TryResolveMediaType"/> resolves it, reusing the one unit that
///     owns media-type knowledge rather than building a second, parallel table here. This is the
///     mirror image of <see cref="ImageMediaTypes.DenyUnsupportedType"/>, which already redirects
///     an <c>.svg</c> to this tool; the reference between the two units is therefore mutual and
///     deliberate, a leaf reference to published constants within one assembly rather than a
///     layering violation.
///     </para>
///     <para>
///     Every refusal is returned rather than thrown, and carries no host detail: no absolute
///     path, no permitted location, no directory separator. Each nevertheless states what the
///     model should do instead, because an agent told only "no" retries the same request. The
///     refusal text reaches a model and the resulting transcript leaves this process, so the
///     messages here are constants, and the only interpolated values are integers naming a
///     ceiling.
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
    ///     — most importantly a sibling tool redirecting a model to it — cannot drift from the
    ///     name actually registered.
    /// </remarks>
    public const string ToolName = "text_file_read";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Reads the text content of a file the agent is permitted to read. Paths are relative to "
        + "the workspace root. Returns the file's text, or a denial explaining why the request "
        + "was refused.";

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
        "The requested path is a directory, not a file. List it to discover the file names it "
        + "holds, then read one of those.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound =
        "The requested file does not exist. List the workspace to discover the names that do.";

    /// <summary>
    ///     The refusal used when the file exists and is permitted but cannot be read.
    /// </summary>
    private const string FileUnreadable = "The requested file could not be read.";

    /// <summary>
    ///     The refusal used when a binary file's type is one the image tool can read.
    /// </summary>
    /// <remarks>
    ///     The tool the model should reach for instead is not named in the text: it is supplied
    ///     to <see cref="ToolResult.Denied"/> as the redirect argument, so the rendered sentence
    ///     stays consistent with every other redirect and the name cannot drift from the one the
    ///     sibling tool actually publishes.
    /// </remarks>
    private const string BinaryImageContent =
        "The requested file is binary content, not text. Its type is one an image tool can read.";

    /// <summary>
    ///     The refusal used when a binary file's type has no better tool to name.
    /// </summary>
    private const string BinaryContent =
        "The requested file is binary content, not text, and cannot be read as text.";

    /// <summary>
    ///     The number of leading bytes sniffed to decide whether a file is text or binary.
    /// </summary>
    /// <remarks>
    ///     A leading window is enough to classify a file without reading it twice: a binary file
    ///     reveals itself within the first bytes, and a text file is validated over the window
    ///     with the boundary mitigation the sniff applies. Four kilobytes is the customary sniff
    ///     size and comfortably spans any byte-order mark plus enough content to judge.
    /// </remarks>
    private const int SniffByteCount = 4096;

    /// <summary>
    ///     Creates the <c>text_file_read</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>, which is the only place
    ///     the <c>text_file</c> family prefix is claimed. The policy is captured by the returned
    ///     tool's delegate, so the tool cannot later be pointed at a different policy.
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
        // The path carries a default so that an omitted argument becomes a refusal this tool
        // composes, rather than a framework error raised before the body is reached.
        var read = (
                [Description(
                    "The path of the file to read, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null,
                CancellationToken cancellationToken = default) =>
            ReadAsync(policy, path, cancellationToken);

        return GuardedToolFactory.Create(read, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Reads one file, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The order of the checks is the contract: the policy decision comes before anything is
    ///     learned about the file, so a refused path never reveals whether it exists. The
    ///     ceilings are then checked before any content is returned, so a model is refused
    ///     rather than handed a partial document.
    /// </remarks>
    /// <param name="policy">The access policy governing the read.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The file's text, or a refusal naming its reason.</returns>
    private static async Task<object> ReadAsync(
        PathPolicy policy,
        string? path,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown: the model supplied it, so the model
        // is the one that must be told how to correct it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // The single read decision. Resolution happens inside the policy, so a path that reaches
        // outside the permitted location through a link is refused here without this tool having
        // to know that links exist.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A directory has an obvious better tool, so the refusal names it rather than leaving
        // the model to guess.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                PathIsDirectory,
                TextFileListTool.ToolName);
        }

        // A missing file has the same better tool: listing is how a model discovers the name it
        // should have asked for.
        if (!File.Exists(realPath))
        {
            return ToolResult.Denied(
                DenialReason.TargetNotFound,
                FileNotFound,
                TextFileListTool.ToolName);
        }

        return await ReadPermittedFileAsync(policy, realPath, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads a file already known to exist and to be permitted, observing the policy's
    ///     ceilings.
    /// </summary>
    /// <remarks>
    ///     Two distinct ceilings apply and both are checked. <see cref="ToolLimits.MaxReadBytes"/>
    ///     bounds what may be read from the file system; <see cref="ToolLimits.MaxResultCharacters"/>
    ///     is the deliberately tighter budget for what may be returned to the model. Checking
    ///     only the first would let a file within the read ceiling still overrun the published
    ///     return budget without anybody noticing.
    ///     <para>
    ///     The binary sniff sits between the two ceilings by design. It comes <em>after</em> the
    ///     read-size check because that check is the cheaper, containment-independent gate and
    ///     reading a window from an oversized file would waste work the size ceiling already
    ///     forbids. It comes <em>before</em> the decode — and therefore before the result-size
    ///     check — because binary detection decides whether decoding is meaningful at all: a
    ///     binary file must be reported as <see cref="DenialReason.UnsupportedMediaType"/>, not
    ///     as a size problem, and must never be decoded into garbled replacement characters
    ///     first. This preserves the
    ///     existing "policy → existence → size → return budget" ordering and touches neither the
    ///     containment nor the limit logic. A file access failure during the sniff falls to the
    ///     same <see cref="IsAccessFailure"/> classification as the decode, becoming the ordinary
    ///     unreadable-file refusal rather than a thrown exception.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The file's text, or a refusal naming the ceiling it exceeded.</returns>
    private static async Task<object> ReadPermittedFileAsync(
        PathPolicy policy,
        string realPath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Size is judged before the file is opened, so an oversized file is never read into
            // memory merely to discover it was oversized.
            var length = new FileInfo(realPath).Length;
            if (length > policy.Limits.MaxReadBytes)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The file exceeds the "
                    + policy.Limits.MaxReadBytes.ToString(CultureInfo.InvariantCulture)
                    + "-byte read limit.");
            }

            // A binary file is refused before its bytes are ever decoded into text: it sits
            // after the size gate above and before the decode below, so the model is told its
            // content is binary rather than handed a screen of replacement characters. The
            // redirect is chosen from the extension by reusing the image family's own resolver,
            // so no media-type knowledge is duplicated here.
            if (await IsBinaryAsync(realPath, length, cancellationToken).ConfigureAwait(false))
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

            var text = await File.ReadAllTextAsync(realPath, cancellationToken)
                .ConfigureAwait(false);

            // The return budget is separate and tighter; a file within the read ceiling can
            // still exceed it, and returning a truncated document would be worse than refusing.
            if (text.Length > policy.Limits.MaxResultCharacters)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The file's text exceeds the "
                    + policy.Limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                    + "-character result limit.");
            }

            return ToolResult.Text(text);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // The same explicit classification the policy uses: a file system failure becomes a
            // refusal the model can act on, while a genuine defect still surfaces.
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnreadable);
        }
    }

    /// <summary>
    ///     Determines whether a permitted file's content is binary rather than text, by sniffing
    ///     a leading window.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A recognized byte-order mark is decisive evidence of text and is checked first: the
    ///     existing decode path handles UTF-8, UTF-16 and UTF-32 correctly, and a UTF-16 or
    ///     UTF-32 file legitimately contains NUL bytes, so refusing it on the NUL rule below
    ///     would be a regression. The four-byte UTF-32 marks are tested before the two-byte
    ///     UTF-16 marks because <c>FF FE</c> (UTF-16 LE) is a prefix of <c>FF FE 00 00</c>
    ///     (UTF-32 LE); testing the shorter one first would misread a UTF-32 file as UTF-16.
    ///     </para>
    ///     <para>
    ///     With no byte-order mark, a NUL byte anywhere in the window means binary, and otherwise
    ///     the window is validated as strict UTF-8. The UTF-8 check uses a <see cref="Decoder"/>
    ///     flushed only when the window reached end of file: when more bytes remain, a multi-byte
    ///     sequence the window boundary split in half is buffered rather than reported invalid,
    ///     so a valid UTF-8 file is never misclassified merely because a character straddled the
    ///     window edge. When the window is the whole file, the decoder is flushed, so a genuinely
    ///     truncated trailing sequence is correctly reported invalid. An empty file yields an
    ///     empty window with no mark and no NUL that validates as UTF-8, so it is text.
    ///     </para>
    /// </remarks>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="length">
    ///     The file's already-known length, used to decide whether the window reaches the end of
    ///     the file so the file is not read a second time to learn it.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the sniff read.</param>
    /// <returns>
    ///     <see langword="true"/> when the leading window indicates binary content; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static async Task<bool> IsBinaryAsync(
        string realPath,
        long length,
        CancellationToken cancellationToken)
    {
        var window = new byte[SniffByteCount];
        int count;

        // The window is read once; the full decode, if it happens, reads the file again. The
        // file is never read twice merely to classify it, because a binary file is refused
        // before the decode is reached.
        await using (var stream = new FileStream(
            realPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: SniffByteCount,
            useAsync: true))
        {
            count = await stream
                .ReadAtLeastAsync(window, window.Length, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
        }

        // A recognized byte-order mark is text; this must precede the NUL rule, which UTF-16 and
        // UTF-32 text would otherwise trip.
        if (HasRecognizedBom(window, count))
        {
            return false;
        }

        // A NUL byte in a mark-less window is the classic binary signal.
        if (Array.IndexOf<byte>(window, 0, 0, count) >= 0)
        {
            return true;
        }

        // Otherwise the window must be valid UTF-8 to be treated as text.
        var reachedEof = length <= SniffByteCount;
        return !IsValidUtf8(window, count, reachedEof);
    }

    /// <summary>
    ///     Determines whether the leading bytes of a window begin with a byte-order mark the
    ///     decode path recognizes.
    /// </summary>
    /// <remarks>
    ///     The four-byte UTF-32 marks are tested before the two-byte UTF-16 marks because the
    ///     UTF-16 little-endian mark is a prefix of the UTF-32 little-endian mark; the longer
    ///     match must win.
    /// </remarks>
    /// <param name="window">The sniffed leading bytes.</param>
    /// <param name="count">The number of valid bytes in <paramref name="window"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when a recognized mark is present; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool HasRecognizedBom(byte[] window, int count)
    {
        // UTF-32 LE (FF FE 00 00) and UTF-32 BE (00 00 FE FF) — tested before the two-byte marks.
        if (count >= 4 && window[0] == 0xFF && window[1] == 0xFE && window[2] == 0x00 && window[3] == 0x00)
        {
            return true;
        }

        if (count >= 4 && window[0] == 0x00 && window[1] == 0x00 && window[2] == 0xFE && window[3] == 0xFF)
        {
            return true;
        }

        // UTF-16 LE (FF FE) and UTF-16 BE (FE FF).
        if (count >= 2 && window[0] == 0xFF && window[1] == 0xFE)
        {
            return true;
        }

        if (count >= 2 && window[0] == 0xFE && window[1] == 0xFF)
        {
            return true;
        }

        // UTF-8 (EF BB BF).
        return count >= 3 && window[0] == 0xEF && window[1] == 0xBB && window[2] == 0xBF;
    }

    /// <summary>
    ///     Determines whether a window of bytes is valid UTF-8, tolerating a multi-byte sequence
    ///     split by the window boundary when the window has not reached end of file.
    /// </summary>
    /// <remarks>
    ///     A throwing decoder reports invalid bytes as a <see cref="DecoderFallbackException"/>.
    ///     Flushing is tied to <paramref name="reachedEof"/> so that a sequence the window merely
    ///     truncated is buffered rather than rejected, while a sequence truncated by the actual
    ///     end of the file is rejected.
    /// </remarks>
    /// <param name="window">The sniffed leading bytes.</param>
    /// <param name="count">The number of valid bytes in <paramref name="window"/>.</param>
    /// <param name="reachedEof">
    ///     <see langword="true"/> when the window spans the whole file, so the decoder is flushed.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the window is valid UTF-8; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool IsValidUtf8(byte[] window, int count, bool reachedEof)
    {
        var decoder = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetDecoder();

        try
        {
            decoder.GetCharCount(window, 0, count, flush: reachedEof);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Determines whether an exception represents a failure to reach or read a path rather
    ///     than a defect that should be allowed to propagate.
    /// </summary>
    /// <remarks>
    ///     Enumerated explicitly rather than catching everything, so that a null reference or an
    ///     out-of-memory condition still fails loudly during development instead of being
    ///     reported to a model as an unreadable file.
    /// </remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be reached or
    ///     read; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }
}


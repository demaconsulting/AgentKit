using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
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
///     <b>An oversized file is a denial naming the ceiling, never a truncation.</b> Silently
///     returning the first part of a file would hand the model an incomplete document it has no
///     way to detect, and it would then reason confidently about content it never saw. A denial
///     that names the ceiling lets the model narrow its request instead.
///     </para>
///     <para>
///     Every refusal is returned rather than thrown, and carries no host detail: no absolute
///     path, no permitted location, no directory separator. The refusal text reaches a model and
///     the resulting transcript leaves this process, so the messages here are constants, and the
///     only interpolated values are integers naming a ceiling.
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
        "Reads the text content of a file the agent is permitted to read. "
        + "Returns the file's text, or a denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired = "A file path is required.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    private const string PathIsDirectory = "The requested path is a directory, not a file.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound = "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when the file exists and is permitted but cannot be read.
    /// </summary>
    private const string FileUnreadable = "The requested file could not be read.";

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

        // Declared Task<object> on purpose; see the type remarks before changing this.
        return GuardedToolFactory.Create(
            (Func<string, CancellationToken, Task<object>>)(
                ([Description("The path of the file to read.")] string path,
                    CancellationToken cancellationToken) =>
                    ReadAsync(policy, path, cancellationToken)),
            ToolName,
            ToolDescription);
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
    /// <param name="path">The path the model requested.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The file's text, or a refusal naming its reason.</returns>
    private static async Task<object> ReadAsync(
        PathPolicy policy,
        string path,
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


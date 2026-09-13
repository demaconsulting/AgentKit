using System.ComponentModel;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The <c>file_copy</c> tool: copies one file the access policy permits the agent to read to a
///     destination the policy permits it to write.
/// </summary>
/// <remarks>
///     <para>
///     <b>Two policy decisions, one per endpoint.</b> The source is judged by the read decision and
///     the destination by the write decision, because a copy reads from one place and writes to
///     another. A file an agent may read but not write can still be copied <em>into</em> a location
///     it may write; a file it may not read cannot be copied at all. Consulting the decision that
///     matches each endpoint is what keeps a read-wide, write-narrow configuration meaningful for a
///     copy exactly as it is for a plain read or write.
///     </para>
///     <para>
///     <b>An existing destination is refused unless the request explicitly permits the overwrite.</b>
///     Silently replacing a file an agent did not mean to touch is the kind of irreversible side
///     effect this library exists to make impossible to express by accident. The overwrite flag
///     defaults to refusing, so a model must state its intent to replace before the tool will,
///     and the refusal names the flag so the model learns how to proceed when the replacement was
///     intended.
///     </para>
///     <para>
///     <b>The tool copies a single file and creates no directory.</b> A source that is a directory
///     is refused rather than copied recursively, and a missing destination parent is refused rather
///     than materialized, because each would be a side effect an operator never granted. Recovery
///     from a mistaken copy is the same as for any other change in a governed workspace: source
///     control, which is why an agent should be run in a repository.
///     </para>
///     <para>
///     The delegate is declared to return <c>Task&lt;object&gt;</c> deliberately — see the remarks
///     on <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown. A
///     refusal the tool composes itself names only the flag or the shape of the request; a refusal
///     the access policy composes discloses the request, its interpretation and the permitted
///     locations, so a confined model learns where it may work.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class FileCopyTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "file_copy";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Copies a file the agent may read to a destination it may write. Paths are relative to the "
        + "workspace root. Refuses to overwrite an existing destination unless overwrite is true. "
        + "Returns a confirmation, or a denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no source path.
    /// </summary>
    private const string SourceRequired =
        "A source path is required. Supply the path of the file to copy, relative to the workspace "
        + "root, for example 'notes.txt'.";

    /// <summary>
    ///     The refusal used when the request carries no destination path.
    /// </summary>
    private const string DestinationRequired =
        "A destination path is required. Supply the path to copy the file to, relative to the "
        + "workspace root, for example 'backup/notes.txt'.";

    /// <summary>
    ///     The refusal used when the source names a directory rather than a file.
    /// </summary>
    private const string SourceIsDirectory =
        "The source path is a directory, not a file. This tool copies a single file; name the file "
        + "to copy.";

    /// <summary>
    ///     The refusal used when the source file does not exist.
    /// </summary>
    private const string SourceNotFound = "The source file does not exist.";

    /// <summary>
    ///     The refusal used when the destination names an existing directory.
    /// </summary>
    private const string DestinationIsDirectory =
        "The destination path is a directory, not a file. Name the file to write, including its "
        + "file name.";

    /// <summary>
    ///     The refusal used when the destination already exists and the overwrite was not permitted.
    /// </summary>
    private const string DestinationExists = "The destination already exists.";

    /// <summary>
    ///     The refusal used when the destination's parent directory does not exist.
    /// </summary>
    private const string ParentMissing =
        "The parent directory of the destination does not exist.";

    /// <summary>
    ///     The refusal used when a permitted copy cannot be completed for a file-system reason.
    /// </summary>
    private const string CopyFailed = "The file could not be copied.";

    /// <summary>
    ///     Creates the <c>file_copy</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="FilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every copy this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return object on purpose; see the type remarks before changing this.
        // Every parameter carries a default so that an omitted argument becomes a refusal this
        // tool composes, rather than a framework error raised before the body is reached.
        var copy = (
                [Description(
                    "The path of the file to copy, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? source = null,
                [Description(
                    "The path to copy the file to, relative to the workspace root, "
                    + "for example 'backup/notes.txt'.")]
                string? destination = null,
                [Description(
                    "Whether to overwrite the destination when it already exists. "
                    + "Defaults to false, refusing to replace an existing file.")]
                bool overwrite = false) =>
            Copy(policy, source, destination, overwrite);

        return GuardedToolFactory.Create(copy, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Copies one file, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The two policy decisions are taken first and before anything is learned about the file
    ///     system, so a refused source or destination discloses nothing about what exists beyond the
    ///     permitted locations.
    /// </remarks>
    /// <param name="policy">The access policy governing the copy.</param>
    /// <param name="source">The path the model wishes to copy, or null when it supplied none.</param>
    /// <param name="destination">The path to copy to, or null when it supplied none.</param>
    /// <param name="overwrite">Whether an existing destination may be replaced.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object Copy(
        PathPolicy policy,
        string? source,
        string? destination,
        bool overwrite)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(source))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, SourceRequired);
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationRequired);
        }

        // The source is judged by the read decision, the destination by the write decision.
        if (!policy.TryResolveRead(source, out var realSource, out var sourceDenial))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, sourceDenial);
        }

        if (!policy.TryResolveWrite(destination, out var realDestination, out var destinationDenial))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, destinationDenial);
        }

        // A directory source has no single file to copy; the tool never recurses.
        if (Directory.Exists(realSource))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, SourceIsDirectory);
        }

        // A missing source is a target-not-found refusal stating only what was wrong.
        if (!System.IO.File.Exists(realSource))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, SourceNotFound);
        }

        // Writing over a directory is not a meaningful file copy.
        if (Directory.Exists(realDestination))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationIsDirectory);
        }

        // An existing destination is refused unless the overwrite was explicitly permitted, so a
        // clobber can never happen by accident.
        if (System.IO.File.Exists(realDestination) && !overwrite)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationExists);
        }

        try
        {
            // A missing parent is refused, never created.
            var parent = Path.GetDirectoryName(realDestination);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return ToolResult.Denied(DenialReason.TargetNotFound, ParentMissing);
            }

            System.IO.File.Copy(realSource, realDestination, overwrite);

            return ToolResult.Text("Copied the file.");
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, CopyFailed);
        }
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the copy could not be completed;
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

using System.ComponentModel;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The <c>file_create_directory</c> tool: creates a directory, and any missing directories
///     above it, in a location the access policy permits the agent to write.
/// </summary>
/// <remarks>
///     <para>
///     <b>Creation is judged by the write decision.</b> Materializing a directory changes the file
///     system, so a location an agent may read but not write cannot receive one. Write access is
///     never inferred from read access.
///     </para>
///     <para>
///     <b>Missing intermediate directories are created, but never above the granted location.</b>
///     "Ensure this path exists, then write into it" is what a model actually wants, and refusing
///     because a middle component does not exist yet would force it into a
///     create-one-level-at-a-time loop that buys no safety — <em>provided</em> every level created
///     lies inside a location the policy permits writing. That proviso is not automatic: a grant
///     root need not exist, because <see cref="RealPathResolver"/> is lexical and requires no
///     component to be present, so a grant rooted at a location whose own parents are missing
///     would have had those parents materialized too. Every directory this tool would create is
///     therefore judged by the write decision, not only the one the request names, and a request
///     that would have to create a directory no grant permits is refused outright — nothing is
///     created, including the part that was permitted.
///     </para>
///     <para>
///     <b>Why refusing beats creating the permitted part.</b> The two candidate rules coincide
///     wherever they can differ: a directory cannot be created inside a parent that does not
///     exist, so a tool that "creates only at or below the grant root" still has to refuse the
///     moment an ancestor above that root is missing. Refusing up front says so with one decision
///     instead of a half-finished tree, and it keeps this unit's promise identical to the rest of
///     the family's — a refusal means nothing happened.
///     </para>
///     <para>
///     <b>A directory that already exists is a success, reported in its own words.</b> The
///     family's no-silent-overwrite instinct is about <em>destruction</em>, and creating a
///     directory that is already there destroys nothing — so refusing would borrow a rule from a
///     situation that does not apply. What does carry over is that the model must never be left
///     guessing which thing happened, so the two outcomes carry two distinct confirmations: one
///     says the directory was created, the other says it already existed and that nothing beneath
///     it was changed.
///     </para>
///     <para>
///     <b>An existing file at the named path is refused.</b> A file is never replaced by a
///     directory: that would be a destructive act wearing a creating verb, which is exactly the
///     kind of thing this library exists to make impossible to express.
///     </para>
///     <para>
///     The delegate is declared to return <c>Task&lt;object&gt;</c> deliberately — see the remarks
///     on <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class FileCreateDirectoryTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "file_create_directory";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Creates a directory in a location the agent is permitted to write, creating any missing "
        + "parent directories along the way, provided every one of them is itself in a permitted "
        + "location. Paths are relative to the workspace root. Reports "
        + "separately whether the directory was created or already existed. Refuses when the name "
        + "is already a file, and refuses — creating nothing at all — when a missing parent lies "
        + "outside every permitted location. Returns a confirmation, or a denial explaining why "
        + "the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A directory path is required. Supply the path of the directory to create, relative to "
        + "the workspace root, for example 'reports/drafts'.";

    /// <summary>
    ///     The refusal used when the request names an existing file.
    /// </summary>
    private const string PathIsFile =
        "The requested path is an existing file, not a directory. A file is never replaced by a "
        + "directory; name a path that is free, or a directory that already exists.";

    /// <summary>
    ///     The refusal used when a missing parent directory lies outside every permitted location.
    /// </summary>
    /// <remarks>
    ///     The offending ancestor is described rather than named. It lies above every permitted
    ///     location, so its own name is a host location the policy never disclosed, and naming it
    ///     would leak exactly what the check is protecting.
    /// </remarks>
    private const string ParentNotPermitted =
        "The requested directory cannot be created because a directory above every permitted "
        + "location would have to be created as well. Nothing was created. Name a path whose "
        + "parent directories already exist inside a permitted location.";

    /// <summary>
    ///     The refusal used when a permitted directory cannot be created for a file-system reason.
    /// </summary>
    private const string CreateFailed = "The requested directory could not be created.";

    /// <summary>
    ///     The confirmation used when the directory was created.
    /// </summary>
    private const string Created =
        "Created the directory, including any missing parent directories.";

    /// <summary>
    ///     The confirmation used when the directory was already there.
    /// </summary>
    private const string AlreadyExisted =
        "The directory already exists. Nothing was created and nothing beneath it was changed.";

    /// <summary>
    ///     Creates the <c>file_create_directory</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="FilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every creation this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return object on purpose; see the type remarks before changing this.
        // The path carries a default so an omitted argument becomes a refusal this tool composes,
        // rather than a framework error raised before the body is reached.
        var create = (
                [Description(
                    "The path of the directory to create, relative to the workspace root, "
                    + "for example 'reports/drafts'.")]
                string? path = null) =>
            CreateDirectory(policy, path);

        return GuardedToolFactory.Create(create, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Creates one directory, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <remarks>
    ///     The write decision is taken before anything is learned about the file system, so a
    ///     refused path discloses nothing about what does or does not exist beyond the permitted
    ///     write location.
    /// </remarks>
    /// <param name="policy">The access policy governing the creation.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object CreateDirectory(PathPolicy policy, string? path)
    {
        // A malformed request is refused rather than thrown; the model supplied it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // Creating a directory changes the file system, so the write decision alone governs it.
        if (!policy.TryResolveWrite(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // The write decision judged the named path alone, but the creation below materializes
        // every missing directory above it too. Each of those is a write in its own right, so
        // each is judged before anything is created.
        if (!EveryMissingParentIsPermitted(policy, realPath))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, ParentNotPermitted);
        }

        // A file is never replaced by a directory, so the file check precedes everything else.
        if (System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsFile);
        }

        // Observed before the creation, because afterwards the two outcomes are indistinguishable
        // and the model would be told only that the path now exists.
        var existed = Directory.Exists(realPath);

        try
        {
            Directory.CreateDirectory(realPath);

            return ToolResult.Text(existed ? AlreadyExisted : Created);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, CreateFailed);
        }
    }

    /// <summary>
    ///     Determines whether every directory above a target that would have to be created is
    ///     itself in a location the policy permits writing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <see cref="Directory.CreateDirectory(string)"/> materializes every missing component
    ///     of the path it is given, so the set of writes a single call performs is the target plus
    ///     each absent ancestor. The target has already been judged by the caller; this judges the
    ///     rest, by the same decision and against the same grants.
    ///     </para>
    ///     <para>
    ///     The walk stops at the first ancestor that already exists, because an existing directory
    ///     is not created and everything above it exists by construction. A grant is consulted
    ///     directly rather than through <see cref="PathPolicy.TryResolveWrite"/> because the
    ///     ancestors are already resolved real locations; resolving them again would re-interpret
    ///     absolute paths the tool produced rather than paths a model supplied.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy governing the creation.</param>
    /// <param name="realPath">The resolved location the request named.</param>
    /// <returns>
    ///     <see langword="true"/> when no ancestor would be created, or every ancestor that would
    ///     be is permitted; otherwise <see langword="false"/>.
    /// </returns>
    private static bool EveryMissingParentIsPermitted(PathPolicy policy, string realPath)
    {
        var ancestor = Path.GetDirectoryName(realPath);

        while (!string.IsNullOrEmpty(ancestor) && !Directory.Exists(ancestor))
        {
            // Captured into a local because the grant test is a closure over it, and the loop
            // variable advances on every iteration.
            var candidate = ancestor;

            if (!policy.Grants.Any(grant =>
                    grant.Access == AccessLevel.ReadWrite && grant.Allows(candidate)))
            {
                return false;
            }

            ancestor = Path.GetDirectoryName(ancestor);
        }

        return true;
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the directory could not be created;
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

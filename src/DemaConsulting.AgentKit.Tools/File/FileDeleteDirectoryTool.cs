using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The <c>file_delete_directory</c> tool: removes a directory, and everything beneath it, in a
///     location the access policy permits the agent to write.
/// </summary>
/// <remarks>
///     <para>
///     <b>Removal is judged by the write decision.</b> Taking away a directory is a write in the
///     strongest sense, so a tree an agent may read but not write cannot be removed. Write access
///     is never inferred from read access.
///     </para>
///     <para>
///     <b>The removal happens in two phases, and the first phase changes nothing.</b> The tool
///     first walks the tree and builds the complete list of entries the removal would take; only
///     if that whole list is acceptable does it remove anything. Deciding before acting is what
///     makes both guards below enforceable, and it is what avoids the worst outcome of all: a
///     half-destroyed tree that neither the model nor the operator can reason about afterwards.
///     </para>
///     <para>
///     <b>The walk never follows a link, and a link found inside the tree refuses the whole
///     request.</b> A link — a symbolic link or a directory junction — can point anywhere,
///     including outside everything the policy granted. Following one would remove content the
///     request never named; removing the link entry itself would destroy a connection the request
///     never mentioned. Neither is something a request to delete a directory asked for, so the
///     tool refuses, names the offending entry as the model spelled the request, and removes
///     nothing. The refusal names the entry but never the link's target: the target is the host
///     location the guard exists to protect, and disclosing it would leak exactly what the refusal
///     is defending.
///     </para>
///     <para>
///     <b>A directory that is itself a link is removed as the link alone.</b> Without that rule
///     the refusal above would be a dead end: a workspace containing a link would be permanently
///     undeletable, because <see cref="FileDeleteTool"/> refuses a directory and a link is a
///     directory. So a link the request names directly is removed, unfollowed, and the result says
///     plainly that what it pointed at was not touched. That gives a model met with the refusal a
///     concrete way forward — name the link, then ask for the parent again.
///     </para>
///     <para>
///     <b>A recursive framework delete is never issued.</b> Measured on Windows, deleting a tree
///     recursively throws when the tree contains a junction and leaves the tree <em>partly
///     deleted</em>, which is both unsafe and the one outcome this design exists to avoid. The
///     walk is therefore hand-rolled, and the removal takes files first and then directories
///     deepest-first, one entry at a time.
///     </para>
///     <para>
///     <b>The entry ceiling bounds a mistake, not an intention.</b> A tree holding more entries
///     than <see cref="ToolLimits.MaxDeleteEntries"/> is refused, naming the real count and the
///     configured limit. This is <em>not</em> protection against a determined agent: one that
///     means to destroy a tree can remove it a file at a time with <see cref="FileDeleteTool"/>
///     and this ceiling will not stop it. What it buys is that a mistake — a wrong path, a model
///     confusion, an off-by-one in a constructed path — is survivable and observable rather than
///     total in one call. The count is exact rather than approximate, because "more than a
///     thousand" tells a model nothing about whether one subdivision would suffice while a real
///     figure in the tens of thousands tells it to take a different approach; the cost is one
///     metadata-only enumeration of a tree that was about to be enumerated anyway.
///     </para>
///     <para>
///     <b>A path the model <em>names</em> that traverses a link is resolved lexically and
///     permitted</b>, exactly as every other tool in this library already behaves. This tool
///     guards what the walk <em>discovers</em>; it does not reopen the question of what the caller
///     may spell. See <see cref="RealPathResolver"/>, whose resolution is documented as lexical.
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
public static class FileDeleteDirectoryTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     The name spells out the directory rather than adding a flag to <c>file_delete</c>,
    ///     because the separation between removing one named file and removing a tree is the
    ///     safety property: it is what stops "delete this" ever meaning "delete this tree".
    /// </remarks>
    public const string ToolName = "file_delete_directory";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Deletes a directory and everything beneath it, in a location the agent is permitted to "
        + "write. Paths are relative to the workspace root. Never follows a link out of the "
        + "directory; a directory containing such a link is refused rather than partly removed. "
        + "Refuses a directory holding more entries than the configured limit, naming the count "
        + "and the limit. Returns a confirmation reporting how many entries were removed, or a "
        + "denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A directory path is required. Supply the path of the directory to delete, relative to "
        + "the workspace root, for example 'build/output'.";

    /// <summary>
    ///     The refusal used when the request names a file rather than a directory.
    /// </summary>
    private const string PathIsFile =
        "The requested path is a file, not a directory. This tool deletes a directory and "
        + "everything beneath it; name a directory.";

    /// <summary>
    ///     The refusal used when the requested directory does not exist.
    /// </summary>
    private const string DirectoryNotFound = "The requested directory does not exist.";

    /// <summary>
    ///     The refusal used when the walk finds a link beneath the named directory.
    /// </summary>
    /// <remarks>
    ///     The placeholder receives the path the model supplied plus the relative sub-path the
    ///     walk reached — never the resolved host path, and never the link's target.
    /// </remarks>
    private const string LinkWithinTheTree =
        "The directory contains a link, at '{0}', that leads outside it. Following it would "
        + "remove content this request never named, and removing the link itself would destroy a "
        + "connection the request did not mention, so nothing was deleted. Name that link "
        + "directly to remove the link alone, then ask again.";

    /// <summary>
    ///     The refusal used when the tree holds more entries than the configured ceiling.
    /// </summary>
    /// <remarks>
    ///     The placeholders receive the exact entry count and the host's configured ceiling, in
    ///     that order, so the model can judge whether subdividing the request would help.
    /// </remarks>
    private const string TooManyEntries =
        "The directory holds {0} entries and this tool removes at most {1} in one call, so "
        + "nothing was deleted. Delete part of it first, or have the application raise the limit.";

    /// <summary>
    ///     The refusal used when a permitted removal cannot be completed for a file-system reason.
    /// </summary>
    private const string DeleteFailed = "The directory could not be deleted.";

    /// <summary>
    ///     The confirmation used when the named path was itself a link.
    /// </summary>
    private const string LinkRemoved = "Removed the link. What it pointed at was not touched.";

    /// <summary>
    ///     The confirmation used when a whole tree was removed, reporting the entry count.
    /// </summary>
    private const string TreeRemoved =
        "Deleted the directory and everything beneath it: {0} entries removed.";

    /// <summary>
    ///     Creates the <c>file_delete_directory</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="FilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy,
    ///     and the removal ceiling is read from that policy's limits on every call.
    /// </remarks>
    /// <param name="policy">The access policy governing every removal this tool performs.</param>
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
        var delete = (
                [Description(
                    "The path of the directory to delete, relative to the workspace root, "
                    + "for example 'build/output'.")]
                string? path = null) =>
            Delete(policy, path);

        return GuardedToolFactory.Create(delete, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Removes one directory tree, refusing rather than throwing whenever the request cannot
    ///     be honored.
    /// </summary>
    /// <remarks>
    ///     The write decision is taken before anything is learned about the file system, so a
    ///     refused path discloses nothing about what does or does not exist beyond the permitted
    ///     write location.
    /// </remarks>
    /// <param name="policy">The access policy governing the removal.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object Delete(PathPolicy policy, string? path)
    {
        // A malformed request is refused rather than thrown; the model supplied it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // Removal is a write, so the write decision alone governs it. Read access never implies
        // permission to take something away.
        if (!policy.TryResolveWrite(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A file is never removed by the directory tool; keeping the single-file and whole-tree
        // capabilities apart is what bounds each one's blast radius.
        if (System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsFile);
        }

        // A missing directory is reported rather than treated as a silent success, so a mistyped
        // path is never mistaken for an accomplished removal.
        if (!Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, DirectoryNotFound);
        }

        // A link the request named directly is removed as the link alone, never followed. This is
        // what keeps the link refusal below from leaving a workspace permanently undeletable.
        if (IsLink(new DirectoryInfo(realPath)))
        {
            return DeleteLink(realPath);
        }

        // Phase one: plan the whole removal, touching nothing. The walk refuses on the first link
        // it meets rather than descending into it.
        List<string> directories = [];
        List<string> files = [];
        var offendingEntry = Plan(realPath, directories, files);
        if (offendingEntry is not null)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                string.Format(
                    CultureInfo.InvariantCulture,
                    LinkWithinTheTree,
                    DescribeEntry(path, realPath, offendingEntry)));
        }

        // The named directory counts as an entry, so an empty directory costs one and a ceiling
        // of zero forbids recursive removal entirely.
        var entryCount = directories.Count + files.Count;
        if (entryCount > policy.Limits.MaxDeleteEntries)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                string.Format(
                    CultureInfo.InvariantCulture,
                    TooManyEntries,
                    entryCount,
                    policy.Limits.MaxDeleteEntries));
        }

        // Phase two: carry out exactly the plan phase one approved.
        return Execute(directories, files, entryCount);
    }

    /// <summary>
    ///     Removes a link entry without following it.
    /// </summary>
    /// <remarks>
    ///     A non-recursive delete of a link removes the link and leaves the target's contents
    ///     intact — measured, not assumed — which is what makes naming a link directly a safe and
    ///     useful thing for a model to do.
    /// </remarks>
    /// <param name="realPath">The resolved path of the link.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object DeleteLink(string realPath)
    {
        try
        {
            Directory.Delete(realPath, recursive: false);

            return ToolResult.Text(LinkRemoved);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DeleteFailed);
        }
    }

    /// <summary>
    ///     Removes every entry in an approved plan, files first and directories deepest-first.
    /// </summary>
    /// <remarks>
    ///     Directories are removed in reverse collection order, and the collection is built
    ///     shallowest-first, so every directory is empty by the time it is removed. No recursive
    ///     delete is ever issued, so no call can walk into something the plan did not approve.
    /// </remarks>
    /// <param name="directories">The planned directories, shallowest first.</param>
    /// <param name="files">The planned files.</param>
    /// <param name="entryCount">The number of entries the plan covers.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object Execute(List<string> directories, List<string> files, int entryCount)
    {
        try
        {
            foreach (var file in files)
            {
                System.IO.File.Delete(file);
            }

            for (var index = directories.Count - 1; index >= 0; index--)
            {
                Directory.Delete(directories[index], recursive: false);
            }

            return ToolResult.Text(
                string.Format(CultureInfo.InvariantCulture, TreeRemoved, entryCount));
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DeleteFailed);
        }
    }

    /// <summary>
    ///     Walks the tree, recording every entry the removal would take, and stops at the first
    ///     link it meets.
    /// </summary>
    /// <remarks>
    ///     Nothing is mutated here: the walk is what makes deciding before acting affordable,
    ///     which is why both the link guard and the entry ceiling can act before any removal.
    /// </remarks>
    /// <param name="directory">The directory to walk. Never itself a link.</param>
    /// <param name="directories">The directories collected so far, shallowest first.</param>
    /// <param name="files">The files collected so far.</param>
    /// <returns>
    ///     The resolved path of the first link found beneath the directory, or
    ///     <see langword="null"/> when the tree holds none.
    /// </returns>
    private static string? Plan(string directory, List<string> directories, List<string> files)
    {
        directories.Add(directory);

        // A file may be a link too, and removing one would destroy a connection the request never
        // named, so files are judged by the same rule as directories.
        foreach (var file in Directory.GetFiles(directory))
        {
            if (IsLink(new FileInfo(file)))
            {
                return file;
            }

            files.Add(file);
        }

        // A link is never descended into; it is reported and the walk stops.
        foreach (var child in Directory.GetDirectories(directory))
        {
            if (IsLink(new DirectoryInfo(child)))
            {
                return child;
            }

            var offendingEntry = Plan(child, directories, files);
            if (offendingEntry is not null)
            {
                return offendingEntry;
            }
        }

        return null;
    }

    /// <summary>
    ///     Determines whether a file-system entry is a link rather than ordinary content.
    /// </summary>
    /// <remarks>
    ///     A non-null link target is what identifies an entry as a reparse point. It is non-null
    ///     for a POSIX symbolic link and for a Windows directory junction alike — verified, not
    ///     assumed — which matters because a junction needs no privilege to create and is
    ///     therefore the form an escape most easily takes on Windows.
    /// </remarks>
    /// <param name="info">The entry to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the entry is a link; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsLink(FileSystemInfo info)
    {
        return info.LinkTarget is not null;
    }

    /// <summary>
    ///     Describes a discovered entry in the dialect the model used, without disclosing a host
    ///     path.
    /// </summary>
    /// <remarks>
    ///     The refusal must be actionable — the model has to be able to name the offending entry
    ///     in a following request — and must not leak a location outside what the policy already
    ///     disclosed. Composing the requested path with the relative sub-path the walk reached
    ///     satisfies both: every component of the result was either supplied by the model or lies
    ///     inside the tree it named.
    /// </remarks>
    /// <param name="requested">The path exactly as the model supplied it.</param>
    /// <param name="realRoot">The resolved path of the named directory.</param>
    /// <param name="realEntry">The resolved path of the discovered entry.</param>
    /// <returns>The entry named in the caller's dialect.</returns>
    private static string DescribeEntry(string requested, string realRoot, string realEntry)
    {
        // Mirror the separator the model used, so the name can be handed straight back.
        var separator = requested.Contains('\\') && !requested.Contains('/') ? '\\' : '/';

        var relative = Path.GetRelativePath(realRoot, realEntry)
            .Replace(Path.DirectorySeparatorChar, separator)
            .Replace(Path.AltDirectorySeparatorChar, separator);

        return requested.TrimEnd('/', '\\') + separator + relative;
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the directory could not be deleted;
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

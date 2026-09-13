using System.ComponentModel;
using System.Globalization;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The <c>file_list</c> tool: lists the files beneath a directory that the access policy
///     permits the agent to read, of any type.
/// </summary>
/// <remarks>
///     <para>
///     <b>The file family treats a file as an entity, not as a kind of content.</b> Listing,
///     copying, moving and deleting a file are the same operations whether the file is text, an
///     image, or anything else, so they belong to a type-agnostic family rather than to the text
///     family. This tool is the family's listing operation and is the one an agent reaches for to
///     discover what exists before it reads, copies, moves or deletes anything.
///     </para>
///     <para>
///     <b>Enumeration goes through <see cref="PathPolicy.EnumerateFiles"/> and never through
///     <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> or
///     <see cref="Directory.GetFiles(string, string)"/>.</b> Recursive enumeration performed by
///     the operating system follows directory junctions and symbolic links, so an implementation
///     that enumerated directly would advertise files lying outside every permitted location even
///     though reading them is refused. Disclosing that such a file exists — and inviting the
///     agent to ask for it — is itself the leak. The policy filters every candidate through the
///     same read decision direct access uses, which is why enumeration and access can never
///     reach different conclusions. This is a design invariant, not an implementation detail:
///     the call must remain a policy call.
///     </para>
///     <para>
///     <b>A listing is grouped under an absolute location header, and names appear beneath it.</b>
///     Each permitted location the listing covers is emitted as an absolute header line followed by
///     the matching names given relative to that header. The header is written once per location —
///     an O(1) cost — rather than repeating a full absolute path on every file, which would quickly
///     trip the result ceiling. A discovery listing (one made with no directory argument) covers
///     every granted location — including one that currently holds no matching file, rendered as its
///     absolute header followed by a marker line naming its access level — so the model learns that
///     locations other than the working directory exist and are addressed by their absolute header.
///     </para>
///     <para>
///     <b>A listing mirrors the caller's dialect.</b> When the working directory is granted and the
///     requested subtree lies within it, a relative request lists bare working-directory-relative
///     names — the form the model can hand straight back to the read, copy, move and delete tools.
///     An absolute request, or a location outside the working directory, lists under its absolute
///     header instead, because a relative name could not truthfully address it.
///     </para>
///     <para>
///     <b>The pattern is a glob whose leading <c>**/</c> is optional.</b> Enumeration is always
///     recursive — the policy's own enumeration walks the whole subtree so that no escaped file can
///     hide beneath a nested directory — so a leading <c>**/</c> (or a bare <c>**</c>) is honored
///     and stripped, and the trailing file-name portion becomes the pattern each name is matched
///     against. A pattern without a recursion prefix matches the same recursive walk, because a
///     policy-filtered enumeration cannot be made shallow without weakening the containment
///     invariant.
///     </para>
///     <para>
///     <b>An empty listing is a fact, not a refusal.</b> A directory that matches nothing is
///     reported as such, because refusing would tell the model its request was wrong when it was
///     merely unproductive. An oversized listing, by contrast, is refused with the ceiling named
///     rather than truncated, for the same reason a partial file is refused.
///     </para>
///     <para>
///     The delegate is synchronous and declared to return <see cref="object"/> deliberately —
///     the guard applies identically to a synchronous tool, and <see cref="object"/> is the only
///     type expressing the refusal-or-listing union; see the remarks on
///     <see cref="GuardedToolFactory"/>.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class FileListTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant on the unit that defines the tool so that a sibling tool naming
    ///     it — most importantly a read tool redirecting a model to it — cannot drift from the name
    ///     actually registered.
    /// </remarks>
    public const string ToolName = "file_list";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Lists the files the agent is permitted to read, of any type. Each permitted location "
        + "appears as an absolute header line followed by the matching file names beneath it. "
        + "Omit the directory to list every permitted location. The pattern is a glob such as "
        + "'*.md' or '**/*.txt'. Returns the listing, or a denial explaining why the request was "
        + "refused.";

    /// <summary>
    ///     The pattern used when the request supplies none.
    /// </summary>
    /// <remarks>
    ///     A missing pattern means "everything here", which is the least surprising reading and
    ///     keeps the tool usable by a model that has not yet learned the directory's contents.
    /// </remarks>
    private const string DefaultSearchPattern = "*";

    /// <summary>
    ///     The recursion prefix a glob may carry, honored and stripped before matching.
    /// </summary>
    /// <remarks>
    ///     Enumeration is always recursive, so a leading <c>**/</c> is accepted for the model's
    ///     convenience and removed, leaving the trailing file-name portion as the match pattern.
    /// </remarks>
    private const string RecursivePrefix = "**/";

    /// <summary>
    ///     The bare recursion glob, treated as "everything, recursively".
    /// </summary>
    private const string RecursiveEverything = "**";

    /// <summary>
    ///     The separator between listed names.
    /// </summary>
    /// <remarks>
    ///     A newline rather than a comma, because file names may legitimately contain a comma
    ///     and a model would then have no way to tell one name from two.
    /// </remarks>
    private const string NameSeparator = "\n";

    /// <summary>
    ///     The directory separator used in reported names.
    /// </summary>
    /// <remarks>
    ///     Normalized to a forward slash on every platform. A backslash would make an otherwise
    ///     identical listing differ between a developer machine and a build agent, and a forward
    ///     slash is the form a model most readily hands back as a relative path.
    /// </remarks>
    private const char ReportedSeparator = '/';

    /// <summary>
    ///     The blank line separating one location's block from the next in a discovery listing.
    /// </summary>
    private const string BlockSeparator = "\n\n";

    /// <summary>
    ///     The result reported when nothing matched.
    /// </summary>
    private const string NoMatches = "No files matched.";

    /// <summary>
    ///     The marker line placed beneath the header of a read-write location that a discovery
    ///     listing covers but that currently holds no matching file.
    /// </summary>
    private const string EmptyReadWriteMarker = "(no files - read-write)";

    /// <summary>
    ///     The marker line placed beneath the header of a read-only location that a discovery
    ///     listing covers but that currently holds no matching file.
    /// </summary>
    private const string EmptyReadOnlyMarker = "(no files - read-only)";

    /// <summary>
    ///     Creates the <c>file_list</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="FilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every listing this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return object on purpose; see the type remarks before changing this. Both
        // parameters carry a default so that a model omitting either supplies a request this
        // tool answers, rather than one the function factory rejects before the body is reached.
        var list = (
                [Description(
                    "The directory to list, relative to the workspace or an absolute path. "
                    + "Optional: omit it to list every location the agent may read.")]
                string? directory = null,
                [Description(
                    "The file-name glob to match, for example '*.md' or '**/*.txt'. "
                    + "Leave empty to list every file.")]
                string? pattern = null) =>
            List(policy, directory, pattern);

        return GuardedToolFactory.Create(list, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Lists the permitted files, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <param name="policy">The access policy governing the listing.</param>
    /// <param name="directory">The directory the model requested, or null for every permitted location.</param>
    /// <param name="pattern">The glob the model requested, or null for everything.</param>
    /// <returns>The listing, or a refusal naming its reason.</returns>
    private static object List(PathPolicy policy, string? directory, string? pattern)
    {
        // An absent pattern means everything; a recursion prefix is honored and stripped, because
        // the policy enumeration walks the whole subtree already.
        var searchPattern = NormalizePattern(pattern);

        string text;
        if (PathPolicy.IsDiscoveryRequest(directory))
        {
            text = ListEveryLocation(policy, searchPattern);
        }
        else
        {
            // Resolve the named directory itself so that a refusal is reported, and so the resolved
            // location decides the header and anchor.
            if (!policy.TryResolveRead(directory, out var realDirectory, out var denialMessage))
            {
                return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
            }

            text = ListOneDirectory(policy, directory, realDirectory, searchPattern);
        }

        // Nothing matched is an answer, not a refusal.
        if (text.Length == 0)
        {
            return ToolResult.Text(NoMatches);
        }

        // The return budget applies to a listing exactly as it applies to a file's text: a
        // truncated listing would silently hide files the model would then never ask for.
        if (text.Length > policy.Limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The listing exceeds the "
                + policy.Limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit.");
        }

        return ToolResult.Text(text);
    }

    /// <summary>
    ///     Reduces a caller's glob to the .NET file-name pattern the policy enumeration accepts.
    /// </summary>
    /// <remarks>
    ///     Enumeration is always recursive, so a leading <c>**/</c> (or a bare <c>**</c>) is honored
    ///     for the model's convenience and stripped, leaving the trailing file-name portion. A null,
    ///     empty or whitespace pattern becomes "everything".
    /// </remarks>
    /// <param name="pattern">The glob the caller supplied, or null.</param>
    /// <returns>The .NET file-name search pattern to enumerate with.</returns>
    private static string NormalizePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return DefaultSearchPattern;
        }

        var trimmed = pattern.Trim();
        if (string.Equals(trimmed, RecursiveEverything, StringComparison.Ordinal))
        {
            return DefaultSearchPattern;
        }

        if (trimmed.StartsWith(RecursivePrefix, StringComparison.Ordinal))
        {
            trimmed = trimmed[RecursivePrefix.Length..];
        }

        // Any remaining directory portion is discarded: enumeration already walks every
        // subdirectory, and the trailing file-name portion is what a name is matched against.
        var lastSeparator = trimmed.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
        {
            trimmed = trimmed[(lastSeparator + 1)..];
        }

        return string.IsNullOrEmpty(trimmed) ? DefaultSearchPattern : trimmed;
    }

    /// <summary>
    ///     Builds the discovery listing: one block per permitted location, each an absolute header
    ///     followed by its matching names, or a marker naming its access level when it is empty.
    /// </summary>
    /// <param name="policy">The access policy governing the listing.</param>
    /// <param name="pattern">The resolved search pattern.</param>
    /// <returns>The joined discovery listing, empty only when there are no grants.</returns>
    private static string ListEveryLocation(PathPolicy policy, string pattern)
    {
        var blocks = new List<string>();

        foreach (var root in policy.DiscoveryRoots().OrderBy(root => root, StringComparer.Ordinal))
        {
            var names = policy.EnumerateFiles(root, pattern)
                .Select(file => ToReportedName(root, file))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Every permitted location is reported, including an empty one: a discovery listing
            // that hid a granted location would let a model invent a path and write to the wrong
            // target, the exact failure this library exists to prevent.
            blocks.Add(names.Count > 0
                ? RenderBlock(root, names)
                : RenderEmptyLocation(policy, root));
        }

        return string.Join(BlockSeparator, blocks);
    }

    /// <summary>
    ///     Renders a permitted-but-empty location's block: its absolute header followed by a marker
    ///     line naming the access level, so discovery reports the location without inventing a name.
    /// </summary>
    /// <param name="policy">The access policy governing the listing.</param>
    /// <param name="root">The permitted location that currently holds no matching file.</param>
    /// <returns>The rendered empty-location block.</returns>
    private static string RenderEmptyLocation(PathPolicy policy, string root)
    {
        var marker = policy.TryResolveWrite(root, out _, out _)
            ? EmptyReadWriteMarker
            : EmptyReadOnlyMarker;

        return ToForwardSlash(root) + NameSeparator + marker;
    }

    /// <summary>
    ///     Builds the listing for a single named directory, mirroring the caller's dialect.
    /// </summary>
    /// <param name="policy">The access policy governing the listing.</param>
    /// <param name="directory">The directory the model requested.</param>
    /// <param name="realDirectory">The resolved real location of the requested directory.</param>
    /// <param name="pattern">The resolved search pattern.</param>
    /// <returns>The listing block, empty when nothing matched.</returns>
    private static string ListOneDirectory(
        PathPolicy policy,
        string? directory,
        string realDirectory,
        string pattern)
    {
        // Mirror the caller: a relative request within the granted working directory lists under
        // the working directory; any other request lists under the resolved directory's own header.
        var anchor = policy.EmitRelative(realDirectory, directory)
            ? policy.WorkingDirectory
            : realDirectory;

        var names = policy.EnumerateFiles(directory, pattern)
            .Select(file => ToReportedName(anchor, file))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return names.Count == 0 ? string.Empty : RenderBlock(anchor, names);
    }

    /// <summary>
    ///     Renders one location block: the absolute header line followed by its names.
    /// </summary>
    /// <param name="header">The absolute location the names are given relative to.</param>
    /// <param name="names">The already-relative, already-sorted names beneath the header.</param>
    /// <returns>The rendered block.</returns>
    private static string RenderBlock(string header, IEnumerable<string> names)
    {
        return ToForwardSlash(header) + NameSeparator + string.Join(NameSeparator, names);
    }

    /// <summary>
    ///     Converts a real file location into the relative, platform-neutral name reported beneath a
    ///     location header.
    /// </summary>
    /// <param name="anchor">The location the reported name is made relative to.</param>
    /// <param name="file">The real location of one permitted file.</param>
    /// <returns>The name reported to the model.</returns>
    private static string ToReportedName(string anchor, string file)
    {
        return ToForwardSlash(Path.GetRelativePath(anchor, file));
    }

    /// <summary>
    ///     Normalizes a path's separators to a forward slash on every platform.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The path with every separator rendered as a forward slash.</returns>
    private static string ToForwardSlash(string path)
    {
        return path
            .Replace(Path.DirectorySeparatorChar, ReportedSeparator)
            .Replace(Path.AltDirectorySeparatorChar, ReportedSeparator);
    }
}

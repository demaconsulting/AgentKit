using System.ComponentModel;
using System.Globalization;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_list</c> tool: lists the files beneath a directory that the access
///     policy permits the agent to read.
/// </summary>
/// <remarks>
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
///     an O(1) cost — rather than repeating a full absolute path on every file, which would be some
///     ninety characters each and would quickly trip the result ceiling. A discovery listing (one
///     made with no directory argument) covers every granted location, so the model learns that
///     locations other than the working directory are addressed by their absolute header.
///     </para>
///     <para>
///     <b>A listing mirrors the caller's dialect.</b> The output is a demonstration the model
///     imitates. When the working directory is granted and the requested subtree lies within it, a
///     relative request lists bare working-directory-relative names — the form the model can hand
///     straight back to <see cref="TextFileReadTool"/>. An absolute request, or a location outside
///     the working directory, lists under its absolute header instead, because a relative name could
///     not truthfully address it. A discovery request establishes the dialect: a single granted
///     working directory establishes the relative form, while an ungranted working directory can only
///     be addressed absolutely.
///     </para>
///     <para>
///     <b>An empty listing is a fact, not a refusal.</b> A directory that matches nothing is
///     reported as such, because refusing would tell the model its request was wrong when it was
///     merely unproductive. An oversized listing, by contrast, is refused with the ceiling named
///     rather than truncated, for the same reason a partial file is refused.
///     </para>
///     <para>
///     <b>An omitted directory means every permitted location.</b> A model that has not yet seen the
///     workspace has no directory name to give, and what it sends instead — no argument, an empty
///     one, or its own runtime's word for absence — is a question this tool can answer by listing
///     what it may read. Both parameters therefore carry a default, so a missing argument reaches the
///     tool body rather than failing inside the function factory as a framework error the model
///     cannot interpret. No caller-supplied directory causes this tool to raise an exception.
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
public static class TextFileListTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant on the unit that defines the tool so that the sibling tools
    ///     redirecting a model here cannot drift from the name actually registered.
    /// </remarks>
    public const string ToolName = "text_file_list";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Lists the files the agent is permitted to read. Each permitted location appears as an "
        + "absolute header line followed by the matching file names beneath it. Omit the directory "
        + "to list every permitted location. Returns the listing, or a denial explaining why the "
        + "request was refused.";

    /// <summary>
    ///     The search pattern used when the request supplies none.
    /// </summary>
    /// <remarks>
    ///     A missing pattern means "everything here", which is the least surprising reading and
    ///     keeps the tool usable by a model that has not yet learned the directory's contents.
    /// </remarks>
    private const string DefaultSearchPattern = "*";

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
    ///     Creates the <c>text_file_list</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>. The policy is captured by
    ///     the returned tool's delegate, so the tool cannot later be pointed at a different
    ///     policy.
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
                    "The file-name search pattern to match, for example '*.txt'. "
                    + "Leave empty to list every file.")]
                string? searchPattern = null) =>
            List(policy, directory, searchPattern);

        return GuardedToolFactory.Create(list, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Lists the permitted files, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A request naming a directory resolves it first, for two reasons: a refused directory then
    ///     produces a denial rather than the empty sequence enumeration alone would return, and the
    ///     resolved location decides the header and the anchor every name is made relative to.
    ///     </para>
    ///     <para>
    ///     <b>A request naming no directory lists every permitted location.</b> A model exploring a
    ///     workspace for the first time has no directory name to supply, and the request it makes
    ///     instead — no argument, an empty one, or its runtime's own word for absence — is answered by
    ///     listing what it may read, each location under its own absolute header. This establishes the
    ///     dialect: a single granted working directory yields bare relative names, while an ungranted
    ///     working directory yields absolute headers the model must address by.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy governing the listing.</param>
    /// <param name="directory">The directory the model requested, or null for every permitted location.</param>
    /// <param name="searchPattern">The pattern the model requested, or null for everything.</param>
    /// <returns>The listing, or a refusal naming its reason.</returns>
    private static object List(PathPolicy policy, string? directory, string? searchPattern)
    {
        // An absent pattern means everything, which is what a model asking "what is here?" means.
        var pattern = string.IsNullOrWhiteSpace(searchPattern)
            ? DefaultSearchPattern
            : searchPattern;

        string text;
        if (PathPolicy.IsDiscoveryRequest(directory))
        {
            text = ListEveryLocation(policy, pattern);
        }
        else
        {
            // Resolve the named directory itself so that a refusal is reported, and so the resolved
            // location decides the header and anchor.
            if (!policy.TryResolveRead(directory, out var realDirectory, out var denialMessage))
            {
                return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
            }

            text = ListOneDirectory(policy, directory, realDirectory, pattern);
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
                + "-character result limit. Narrow the search pattern.");
        }

        return ToolResult.Text(text);
    }

    /// <summary>
    ///     Builds the discovery listing: one block per permitted location, each an absolute header
    ///     followed by its matching names.
    /// </summary>
    /// <remarks>
    ///     The header is written once per location — an O(1) cost — and names are given relative to
    ///     it rather than as full absolute paths, so a location with a long path does not spend the
    ///     result budget on repetition. A location with no match contributes no block, so an empty
    ///     result is an empty string the caller reports as "no files matched".
    /// </remarks>
    /// <param name="policy">The access policy governing the listing.</param>
    /// <param name="pattern">The resolved search pattern.</param>
    /// <returns>The joined discovery listing, empty when nothing matched anywhere.</returns>
    private static string ListEveryLocation(PathPolicy policy, string pattern)
    {
        var blocks = new List<string>();

        foreach (var root in policy.DiscoveryRoots().OrderBy(root => root, StringComparer.Ordinal))
        {
            var names = policy.EnumerateFiles(root, pattern)
                .Select(file => ToReportedName(root, file))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            if (names.Count > 0)
            {
                blocks.Add(RenderBlock(root, names));
            }
        }

        return string.Join(BlockSeparator, blocks);
    }

    /// <summary>
    ///     Builds the listing for a single named directory, mirroring the caller's dialect.
    /// </summary>
    /// <remarks>
    ///     When the working directory is granted and the resolved directory lies within it, a
    ///     relative request lists bare working-directory-relative names beneath the working-directory
    ///     header — the form the model handed in and can hand straight back. An absolute request, or a
    ///     directory outside the working directory, lists beneath its own absolute header instead,
    ///     because a relative name could not truthfully address it.
    /// </remarks>
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
    /// <remarks>
    ///     Names are reported relative to their location header so that a listing never repeats a full
    ///     absolute path per file. The header discloses the location once; the names beneath it stay
    ///     short and, for the working-directory header, are directly usable as relative inputs.
    /// </remarks>
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


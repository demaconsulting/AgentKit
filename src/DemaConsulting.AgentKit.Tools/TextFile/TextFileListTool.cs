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
///     that enumerated directly would advertise files lying outside the permitted location even
///     though reading them is refused. Disclosing that such a file exists — and inviting the
///     agent to ask for it — is itself the leak. The policy filters every candidate through the
///     same read decision direct access uses, which is why enumeration and access can never
///     reach different conclusions. This is a design invariant, not an implementation detail:
///     the call must remain a policy call.
///     </para>
///     <para>
///     <b>Names are reported relative to the requested directory.</b> An absolute listing would
///     disclose exactly the host layout every denial withholds, and the transcript leaves this
///     process. A relative name is also directly usable: the model can hand it back to
///     <see cref="TextFileReadTool"/> against the directory it just asked about.
///     </para>
///     <para>
///     <b>An empty listing is a fact, not a refusal.</b> A directory that matches nothing is
///     reported as such, because refusing would tell the model its request was wrong when it was
///     merely unproductive. An oversized listing, by contrast, is refused with the ceiling named
///     rather than truncated, for the same reason a partial file is refused.
///     </para>
///     <para>
///     <b>An omitted directory means the workspace root.</b> A model that has not yet seen the
///     workspace has no directory name to give, and what it sends instead — no argument, an
///     empty one, or its own runtime's word for absence — is a question this tool can answer.
///     Both parameters therefore carry a default, so a missing argument reaches the tool body
///     rather than failing inside the function factory as a framework error the model cannot
///     interpret. No caller-supplied directory causes this tool to raise an exception.
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
        "Lists the files beneath a directory the agent is permitted to read, as names relative "
        + "to that directory. Paths are relative to the workspace root. Returns the listing, or "
        + "a denial explaining why the request was refused.";

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
    ///     Normalized to a forward slash on every platform. A backslash would disclose the host
    ///     platform to a transcript that leaves the process, and would make an otherwise
    ///     identical listing differ between a developer machine and a build agent.
    /// </remarks>
    private const char ReportedSeparator = '/';

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
                    "The directory to list, relative to the workspace root. Optional: omit it "
                    + "to list the workspace root itself.")]
                string? directory = null,
                [Description(
                    "The file-name search pattern to match, for example '*.txt'. "
                    + "Leave empty to list every file.")]
                string? searchPattern = null) =>
            List(policy, directory, searchPattern);

        return GuardedToolFactory.Create(list, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Lists the permitted files beneath a directory, refusing rather than throwing whenever
    ///     the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The directory is resolved before it is enumerated, for two reasons: a refused
    ///     directory then produces a denial rather than the empty sequence enumeration alone
    ///     would return, and the resolved location is what every reported name is made relative
    ///     to.
    ///     </para>
    ///     <para>
    ///     <b>An omitted directory means the workspace root.</b> A model exploring a workspace
    ///     for the first time has no directory name to supply, and the request it makes instead
    ///     — no argument, an empty one, or its runtime's own word for absence — is a question
    ///     this tool can answer. Refusing it merely sends the model guessing at locations it has
    ///     no business exploring. The reading is not invented here: the access policy applies it
    ///     to every path, so listing and reading agree.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy governing the listing.</param>
    /// <param name="directory">The directory the model requested, or null for the workspace root.</param>
    /// <param name="searchPattern">The pattern the model requested, or null for everything.</param>
    /// <returns>The listing, or a refusal naming its reason.</returns>
    private static object List(PathPolicy policy, string? directory, string? searchPattern)
    {
        // An absent pattern means everything, which is what a model asking "what is here?" means.
        var pattern = string.IsNullOrWhiteSpace(searchPattern)
            ? DefaultSearchPattern
            : searchPattern;

        // Resolve the directory itself so that a refusal is reported, and so that reported names
        // can be made relative to the real location.
        if (!policy.TryResolveRead(directory, out var realDirectory, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // The policy enumeration, never a direct one. See the type remarks: a direct enumeration
        // would surface files reachable only by following a junction out of the permitted
        // location.
        var names = policy.EnumerateFiles(directory, pattern)
            .Select(file => ToReportedName(realDirectory, file))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Nothing matched is an answer, not a refusal.
        if (names.Count == 0)
        {
            return ToolResult.Text(NoMatches);
        }

        var listing = string.Join(NameSeparator, names);

        // The return budget applies to a listing exactly as it applies to a file's text: a
        // truncated listing would silently hide files the model would then never ask for.
        if (listing.Length > policy.Limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The listing exceeds the "
                + policy.Limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit. Narrow the search pattern.");
        }

        return ToolResult.Text(listing);
    }

    /// <summary>
    ///     Converts a real file location into the relative, platform-neutral name reported to
    ///     the model.
    /// </summary>
    /// <remarks>
    ///     Making a name relative is the redaction step: it removes the permitted location, the drive or
    ///     mount point and every directory above the one the model asked about, leaving a name
    ///     that is both useful and free of host layout.
    /// </remarks>
    /// <param name="realDirectory">The resolved location of the requested directory.</param>
    /// <param name="file">The real location of one permitted file.</param>
    /// <returns>The name reported to the model.</returns>
    private static string ToReportedName(string realDirectory, string file)
    {
        return Path.GetRelativePath(realDirectory, file)
            .Replace(Path.DirectorySeparatorChar, ReportedSeparator)
            .Replace(Path.AltDirectorySeparatorChar, ReportedSeparator);
    }
}


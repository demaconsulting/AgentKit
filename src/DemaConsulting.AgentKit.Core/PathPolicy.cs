using System.Diagnostics.CodeAnalysis;
using System.Security;
using System.Text;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Holds the one working directory relative paths are anchored to and the zero-or-more access
///     grants that permit locations, and provides the single containment decision used both by
///     direct path access and by directory enumeration.
/// </summary>
/// <remarks>
///     <para>
///     <b>Working directory and grants are orthogonal.</b> A policy separates two ideas that are
///     easy to conflate. The <see cref="WorkingDirectory"/> is the single anchor a relative path
///     is resolved against, and nothing more — it carries no permission of its own. A grant (see
///     <see cref="PathRule"/>) is a permitted location carrying an <see cref="AccessLevel"/>, and
///     nothing more — it says a location may be read, or read and written, but says nothing about
///     addressing. The working directory is often also granted, but it need not be: a valid policy
///     may anchor relative paths at an application folder it grants nothing, while granting three
///     absolute locations elsewhere; another may grant the working directory read-only. The
///     application decides what permission the working directory should have by granting it, exactly
///     as it grants any other location. There is no implicit access.
///     </para>
///     <para>
///     <b>The working directory is required and explicit.</b> There is no fallback to the process
///     working directory. That value is a process-global any code can change and every agent in the
///     process shares, yet one application may run several agents or sub-agents each needing a
///     different anchor. A missing working directory is therefore a programming error: the
///     constructor throws rather than guessing an anchor no caller asked for.
///     </para>
///     <para>
///     <b>An ungranted working directory is coherent, not a special case.</b> When the working
///     directory is granted nothing, a bare relative name still resolves against it correctly — and
///     is then correctly denied, because no grant permits that location. The denial names the
///     locations that are permitted, so the model can re-address its request.
///     </para>
///     <para>
///     <b>The resolution order is part of the contract.</b> A requested path is first made absolute
///     against the working directory, then normalized through
///     <see cref="RealPathResolver.Resolve"/>, and only then tested for containment. Making the
///     path absolute first is what lets a relative path be expressed at all; normalizing before
///     the containment test is what ensures a relative path climbing out of a granted location
///     with <c>..</c> is refused exactly as an absolute one is.
///     </para>
///     <para>
///     <b>Containment is judged on normalized paths.</b> A symbolic link, junction or mount
///     inside a granted location is neither followed nor detected, so a path that leaves a
///     granted location through one is judged on the location it is spelled as. Links are not a
///     protection boundary here; see the note in the README.
///     </para>
///     <para>
///     <b>Denial is a return value, not an exception.</b> A refused path is reported by returning
///     <see langword="false"/> with a denial message. An exception thrown at a model's tool call
///     ends the agent's turn and strands it with no way forward, whereas a returned denial lets the
///     model read the reason and choose a different path. No path a caller supplies — including none
///     at all — is reported as an exception; only programming errors such as a null grant are.
///     </para>
///     <para>
///     <b>Denials state what was asked, how it was interpreted, and what is permitted.</b> A denial
///     echoes the caller's input verbatim; when the input was a relative path it also states the
///     absolute location that input was interpreted as, so a silent wrong-target is impossible to
///     miss; and it enumerates the granted locations with their access levels, so the model learns
///     the complete, truthful map of where it may go instead of guessing. A denial deliberately does
///     disclose host locations: the value of telling a confined model where it may work outweighs
///     concealing paths it is already confined to.
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     The common application shape: one working directory to anchor relative paths, and two
///     granted locations with different access. The workspace is granted read-only while a
///     separate session location is granted read-write, so the agent can read the user's documents
///     and write its artifacts somewhere else entirely.
///     </para>
///     <para>
///     <b>Note the dialect consequence, and read the transition hazard on the constructor
///     (<see cref="PathPolicy(string, IEnumerable{PathRule}, ToolLimits)"/>) before adding a second
///     location to an existing single-location application.</b> Because the working directory is
///     granted, results inside it are still reported as relative names; results in the session
///     location lie outside the anchor and are therefore reported as absolute paths. An application
///     that previously granted only its working directory will see that switch happen silently the
///     moment a second location is added, and nothing else warns it.
///     </para>
///     <code>
///     var workspace = Path.GetFullPath("workspace");
///     var session = Path.GetFullPath("session");
///
///     // The anchor grants nothing on its own; the read-only grant below is what permits reads.
///     var policy = new PathPolicy(
///         workingDirectory: workspace,
///         grants:
///         [
///             PathRule.ReadOnly(workspace),
///             PathRule.ReadWrite(session)
///         ]);
///
///     // Relative names anchor at the workspace whether or not the workspace is granted.
///     policy.TryResolveRead("notes.txt", out var readable, out var readDenial);
///
///     // A write into the read-only workspace is denied; the denial enumerates the writable
///     // session location so the model can re-address the request rather than guess.
///     policy.TryResolveWrite("notes.txt", out _, out var writeDenial);
///
///     // Relative output is emitted only when the anchor is granted and the result lies within it.
///     var emitsRelative = policy.EmitRelative(readable ?? workspace, "notes.txt");
///     </code>
/// </example>
public sealed class PathPolicy
{
    /// <summary>
    ///     The string comparison used when comparing paths, chosen to match the case sensitivity
    ///     of the platform's default file system, so containment and aliasing agree with the
    ///     comparison a grant performs.
    /// </summary>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    ///     The equality comparer matching <see cref="PathComparison"/>, used where an
    ///     <see cref="IEqualityComparer{T}"/> is needed rather than a <see cref="StringComparison"/>.
    /// </summary>
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    ///     The literal strings a model supplies when it means "no path at all", which are
    ///     treated exactly as an omitted path is.
    /// </summary>
    /// <remarks>
    ///     A model whose tool schema marks an argument optional frequently sends the word its
    ///     own runtime uses for absence — observed in practice as the literal <c>"None"</c> —
    ///     rather than omitting the argument. Reading that as a file name refuses a request that
    ///     was well formed in every way the model could tell, so the recognized spellings are
    ///     named here once and treated as absence.
    /// </remarks>
    private static readonly string[] PlaceholderPaths = ["None", "null"];

    /// <summary>
    ///     The enumeration options used when listing candidate files.
    /// </summary>
    /// <remarks>
    ///     Recursion is required because a caller asks for a subtree, and inaccessible entries
    ///     are ignored so that one unreadable directory does not turn a listing into a failure.
    ///     Note that recursion deliberately does <em>not</em> imply trust: every candidate the
    ///     enumeration surfaces is filtered afterwards by the same decision a direct access
    ///     makes.
    /// </remarks>
    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true
    };

    /// <summary>
    ///     The grants, stored as an array so the exposed list cannot be mutated by a caller.
    /// </summary>
    private readonly PathRule[] _grants;

    /// <summary>
    ///     <see cref="WorkingDirectory"/> with a trailing directory separator, precomputed so the
    ///     "within the working directory" test performs no string arithmetic per request.
    /// </summary>
    private readonly string _workingDirectoryPrefix;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PathPolicy"/> class with the library's
    ///     documented resource ceilings.
    /// </summary>
    /// <remarks>
    ///     Delegates to the three-argument constructor with <see cref="ToolLimits.Default"/>, so a
    ///     host that has no opinion about ceilings still gets bounded ones.
    /// </remarks>
    /// <param name="workingDirectory">
    ///     The single location relative paths are anchored to. Required and non-empty; it carries
    ///     no permission of its own. Need not exist.
    /// </param>
    /// <param name="grants">
    ///     The permitted locations, each carrying its own access level. May be empty; individual
    ///     grants must be non-null.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="workingDirectory"/> is <see langword="null"/> or empty — a
    ///     programming error, because the application must supply the anchor and the library will
    ///     not guess one.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="grants"/> is <see langword="null"/> or contains a
    ///     <see langword="null"/> grant.
    /// </exception>
    public PathPolicy(string workingDirectory, IEnumerable<PathRule> grants)
        : this(workingDirectory, grants, ToolLimits.Default)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PathPolicy"/> class with explicit resource
    ///     ceilings.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The working directory is required rather than defaulted. Anchoring a relative path at
    ///     the process working directory — a process-global value any code can mutate and every
    ///     agent in the process shares — would refuse legitimate requests while looking like a
    ///     containment decision, and would break the moment one application ran two agents needing
    ///     different anchors. A missing anchor is a programming error, so the constructor throws.
    ///     </para>
    ///     <para>
    ///     Grants may be empty. An empty grant set is a valid, fully-confined policy that permits
    ///     nothing; its denials say so plainly. The ceilings ride with the policy rather than being
    ///     passed per call so that every tool a host governs observes the same budget.
    ///     </para>
    ///     <para>
    ///     <b>Transition hazard.</b> An application that starts with a single granted working
    ///     directory gets the relative path dialect from its tools: a listing reports bare relative
    ///     names and the model imitates them. If that application later adds a second granted
    ///     location, paths under the second location can no longer be named relative to the working
    ///     directory, so tool output for them switches to the absolute dialect — and nothing else
    ///     warns the application that the move happened. This is by design (see the dialect rules on
    ///     the tools), but it is the one behavioral change an otherwise additive configuration
    ///     change causes, so it is called out here.
    ///     </para>
    /// </remarks>
    /// <param name="workingDirectory">
    ///     The single location relative paths are anchored to. Required and non-empty; it carries
    ///     no permission of its own. Need not exist.
    /// </param>
    /// <param name="grants">
    ///     The permitted locations, each carrying its own access level. May be empty; individual
    ///     grants must be non-null.
    /// </param>
    /// <param name="limits">
    ///     The ceilings every tool governed by this policy observes. Must not be null.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="workingDirectory"/> is <see langword="null"/> or empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="grants"/> or <paramref name="limits"/> is
    ///     <see langword="null"/>, or when <paramref name="grants"/> contains a
    ///     <see langword="null"/> grant.
    /// </exception>
    public PathPolicy(string workingDirectory, IEnumerable<PathRule> grants, ToolLimits limits)
    {
        // A missing working directory is a programming error: the application must supply the
        // anchor, and there is no safe process-global value to assume in its place.
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(limits);

        // Copy the grants into private storage and reject a null grant, so a half-built policy
        // cannot exist and a caller cannot mutate the set after it is granted.
        _grants = grants.ToArray();
        if (Array.Exists(_grants, grant => grant is null))
        {
            throw new ArgumentNullException(nameof(grants), "Grants must not contain a null entry.");
        }

        Limits = limits;

        // Resolve the working directory once, here, so every later request pays only for
        // resolving its own path, and every comparison is real location against real location.
        WorkingDirectory = RealPathResolver.Resolve(workingDirectory);
        _workingDirectoryPrefix = WorkingDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? WorkingDirectory
            : WorkingDirectory + Path.DirectorySeparatorChar;

        // Whether the working directory is itself a permitted read location decides the output
        // dialect: relative names are emitted only when the anchor is granted. Computed once.
        WorkingDirectoryIsGranted = Array.Exists(_grants, grant => grant.Allows(WorkingDirectory));
    }

    /// <summary>
    ///     Gets the single location a relative request is anchored to.
    /// </summary>
    /// <remarks>
    ///     The sole relative anchor, and nothing else: it carries no permission. A relative path
    ///     resolves against this location whether or not this location is granted, and is then
    ///     subject to the grants like any other path. Exposed so a host can report the anchor to a
    ///     user and tests can state what a bare name resolves to. Resolved to its real location at
    ///     construction. Never null.
    /// </remarks>
    public string WorkingDirectory { get; }

    /// <summary>
    ///     Gets the permitted locations, each carrying its own access level.
    /// </summary>
    /// <remarks>
    ///     Zero or more grants. A read is permitted when any grant permits it; a write only when a
    ///     <see cref="AccessLevel.ReadWrite"/> grant permits it. The returned list is a read-only
    ///     view over internal storage and never changes for the lifetime of the policy.
    /// </remarks>
    public IReadOnlyList<PathRule> Grants => _grants;

    /// <summary>
    ///     Gets the ceilings every tool governed by this policy observes.
    /// </summary>
    /// <remarks>
    ///     Carried with the policy rather than supplied per call, so that every pack a host
    ///     attaches observes one budget rather than each inventing its own. Never null.
    /// </remarks>
    public ToolLimits Limits { get; }

    /// <summary>
    ///     Gets whether the working directory is itself a permitted read location.
    /// </summary>
    /// <remarks>
    ///     Decides the output dialect: a tool emits a name relative to the working directory only
    ///     when the anchor is granted and the result lies within it. When the anchor is not granted,
    ///     a relative name would name a location the model cannot actually reach, so output is
    ///     absolute instead. It is the standalone precondition <see cref="EmitRelative"/> applies
    ///     internally, and — unlike the other three helpers — it has no built-in consumer of its own
    ///     today: the built-in <c>file_list</c> tool reaches this same fact through
    ///     <see cref="EmitRelative"/>. It stays public as part of that same extensibility contract so
    ///     a third-party tool pack can inspect up front — before it has any result path to test —
    ///     whether relative addressing is meaningful at all in the current configuration, for example
    ///     to shape its tool description or to decide whether to offer the model relative examples.
    ///     That the built-in tools cross a package boundary to consume the rest of this contract is
    ///     what puts these members on the public surface; the exposure is not for any test or build
    ///     reason.
    /// </remarks>
    public bool WorkingDirectoryIsGranted { get; }

    /// <summary>
    ///     Attempts to resolve a path for reading and to confirm some grant permits it.
    /// </summary>
    /// <remarks>
    ///     This is the single read decision in the library. Directory enumeration filters every
    ///     candidate through this same method, so a file that direct access would refuse can
    ///     never appear in a listing. A grant of either access level permits a read.
    /// </remarks>
    /// <param name="path">
    ///     The requested path. A relative path is interpreted against <see cref="WorkingDirectory"/>;
    ///     an absolute path is taken as given and remains subject to containment. An omitted, empty,
    ///     whitespace or placeholder path denotes the working directory itself. Need not exist.
    /// </param>
    /// <param name="realPath">
    ///     On success, the real location the caller may read; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="denialMessage">
    ///     On refusal, a message stating what was asked for, how it was interpreted when it was
    ///     relative, and which locations are permitted; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the read is permitted; otherwise <see langword="false"/>.
    /// </returns>
    public bool TryResolveRead(
        string? path,
        [NotNullWhen(true)] out string? realPath,
        [NotNullWhen(false)] out string? denialMessage)
    {
        return TryResolve(path, requireWrite: false, out realPath, out denialMessage);
    }

    /// <summary>
    ///     Attempts to resolve a path for writing and to confirm a read-write grant permits it.
    /// </summary>
    /// <remarks>
    ///     Consults only <see cref="AccessLevel.ReadWrite"/> grants. A path that is readable is not
    ///     thereby writable — the grants are independent, which is what makes a read-wide,
    ///     write-narrow configuration meaningful.
    /// </remarks>
    /// <param name="path">
    ///     The requested path, interpreted exactly as <see cref="TryResolveRead"/> interprets it.
    ///     Need not exist.
    /// </param>
    /// <param name="realPath">
    ///     On success, the real location the caller may write; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="denialMessage">
    ///     On refusal, a message stating what was asked for, how it was interpreted when it was
    ///     relative, and which locations are permitted — including any read-only locations, shown
    ///     as such so the model learns why the write is refused there; otherwise
    ///     <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the write is permitted; otherwise <see langword="false"/>.
    /// </returns>
    public bool TryResolveWrite(
        string? path,
        [NotNullWhen(true)] out string? realPath,
        [NotNullWhen(false)] out string? denialMessage)
    {
        return TryResolve(path, requireWrite: true, out realPath, out denialMessage);
    }

    /// <summary>
    ///     Lists the real locations of the files beneath a directory that this policy permits the
    ///     caller to read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Enumeration and access share one decision.</b> Recursive enumeration by the operating
    ///     system surfaces every file beneath the directory, including files that lie outside every
    ///     permitted location. Every candidate is passed through
    ///     <see cref="TryResolveRead"/> — the very method used for direct access — so a listing can
    ///     never advertise a file that a read would refuse. This is a design invariant: the
    ///     filtering must remain the same code path as direct access, never a parallel
    ///     re-implementation.
    ///     </para>
    ///     <para>
    ///     Nothing here throws for a policy or file system reason. A denied directory, a missing
    ///     directory or an unreadable tree all yield an empty sequence.
    ///     </para>
    ///     <para>
    ///     The argument is itself subject to the read decision, and is interpreted exactly as
    ///     <see cref="TryResolveRead"/> interprets a path — including the omitted case, which lists
    ///     the working directory.
    ///     </para>
    /// </remarks>
    /// <param name="directory">
    ///     The directory to list. A relative directory is interpreted against
    ///     <see cref="WorkingDirectory"/>; an omitted, empty, whitespace or placeholder directory
    ///     denotes the working directory itself.
    /// </param>
    /// <param name="searchPattern">
    ///     The file-name search pattern to match, for example <c>*</c> or <c>*.txt</c>. Must be
    ///     non-null and non-empty.
    /// </param>
    /// <returns>
    ///     The real locations of the permitted files, which is empty when the directory itself is
    ///     refused or cannot be listed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="searchPattern"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="searchPattern"/> is an empty string.
    /// </exception>
    public IEnumerable<string> EnumerateFiles(string? directory, string searchPattern)
    {
        // A missing pattern is a programming error in the tool, not something a model supplied,
        // so it is surfaced. The directory is not checked here: an omitted directory is a
        // request the model can legitimately make, and it means the working directory.
        ArgumentException.ThrowIfNullOrEmpty(searchPattern);

        // The directory itself is subject to the same read decision as any other path; a
        // refused directory yields nothing rather than an error, so a caller can list freely.
        if (!TryResolveRead(directory, out var realDirectory, out _))
        {
            return [];
        }

        // Filter every candidate through the single read decision, so that a listing can never
        // advertise a file a direct read would refuse.
        return ListCandidates(realDirectory, searchPattern)
            .Select(candidate => TryResolveRead(candidate, out var real, out _) ? real : null)
            .Where(real => real is not null)
            .Select(real => real!);
    }

    /// <summary>
    ///     Lists the distinct locations a discovery listing (one made with no directory argument)
    ///     enumerates, one per grant.
    /// </summary>
    /// <remarks>
    ///     A rooted grant contributes its resolved location; an unrestricted grant, having no
    ///     bounded location to walk, contributes the working directory instead, so a discovery
    ///     listing against an unrestricted read grant still shows the anchor's contents. Duplicates
    ///     are removed so two grants over the same location produce one block. It is public because a
    ///     package boundary already crosses here: the built-in <c>file_list</c> tool
    ///     (<c>FileListTool</c>) consumes it from the separate Tools package to group a discovery
    ///     listing under one absolute header per location, and a third-party tool pack sits in
    ///     exactly the same position and needs the same grouping. Tools is only the first consumer,
    ///     not a privileged one, so the exposure follows from that design fact — not from any test or
    ///     build reason. See <see cref="EmitRelative"/> for the full extensibility contract.
    /// </remarks>
    /// <returns>The distinct real locations to enumerate for a discovery listing.</returns>
    public IReadOnlyList<string> DiscoveryRoots()
    {
        return _grants
            .Select(grant => grant.Root ?? WorkingDirectory)
            .Distinct(PathComparer)
            .ToList();
    }

    /// <summary>
    ///     Determines whether a tool should emit a result path relative to the working directory
    ///     rather than as an absolute path, mirroring the caller's dialect.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Part of the extensibility contract — a package boundary already crosses it.</b>
    ///     AgentKit's premise is that applications and third parties write their own tools, and any
    ///     tool that accepts or returns a path must follow the same dialect rules as the built-in
    ///     ones: mirror the caller, emit a relative name only when the working directory is granted
    ///     and the result lies within it, and treat a no-argument request as discovery. The evidence
    ///     that these rules belong on the public surface is that a package boundary already crosses
    ///     here: the built-in <c>file_list</c> tool (<c>FileListTool</c>) lives in the
    ///     separate Tools package and consumes Core's dialect rules from outside Core. Tools is not
    ///     privileged — it is simply the first consumer, and a third-party tool pack sits in exactly
    ///     the same position, needing exactly the same rules to stay consistent with the built-in
    ///     tools. Only tools that report locations need this contract: a tool that returns content
    ///     rather than paths — the built-in image reader, for one — has no dialect to mirror and
    ///     correctly consumes none of these members. The public exposure follows from that design
    ///     fact alone, not from any test or build reason.
    ///     </para>
    ///     <para>
    ///     A tool's output is a demonstration the model imitates, so a result is named in the
    ///     dialect of the request that produced it. A relative name is emitted only when three
    ///     things hold: the caller did not supply an absolute path (a relative or absent request),
    ///     the working directory is itself granted (so a relative name refers to a location the
    ///     model can actually reach), and the result lies within the working directory (so a
    ///     relative name can truthfully name it). When any fails, the absolute path is the only
    ///     truthful answer and the switch is visible.
    ///     </para>
    ///     <para>
    ///     The two edges are explicit rather than guessed. A request with no path at all establishes
    ///     the dialect — it is relative-eligible, so a single granted working directory establishes
    ///     the relative dialect. A result outside the anchor cannot be named relatively at all, so it
    ///     is emitted absolute even for a relative-style request.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     A third-party tool emits paths in the same dialect as the built-in tools:
    ///     <code>
    ///     // A custom tool a host application composes into its own pack.
    ///     public string DescribeResult(PathPolicy policy, string? requested, string realResult)
    ///     {
    ///         // Treat a no-argument request as discovery, exactly as the built-in list tool does.
    ///         if (PathPolicy.IsDiscoveryRequest(requested))
    ///         {
    ///             return string.Join(", ", policy.DiscoveryRoots());
    ///         }
    ///
    ///         // Mirror the caller's dialect: a relative name only when it truthfully names a
    ///         // reachable location, otherwise the absolute path.
    ///         return policy.EmitRelative(realResult, requested)
    ///             ? Path.GetRelativePath(policy.WorkingDirectory, realResult)
    ///             : realResult;
    ///     }
    ///     </code>
    /// </example>
    /// <param name="resultRealPath">The resolved real location a tool is about to report.</param>
    /// <param name="callerInput">The path the caller supplied, or null when none was supplied.</param>
    /// <returns>
    ///     <see langword="true"/> to emit a working-directory-relative name; otherwise
    ///     <see langword="false"/> to emit an absolute path.
    /// </returns>
    public bool EmitRelative(string resultRealPath, string? callerInput)
    {
        return !IsAbsoluteRequest(callerInput)
               && WorkingDirectoryIsGranted
               && IsWithinWorkingDirectory(resultRealPath);
    }

    /// <summary>
    ///     Determines whether a request names an absolute path, which fixes the output dialect to
    ///     absolute regardless of what is granted.
    /// </summary>
    /// <remarks>
    ///     Absence (no path, or a placeholder) is deliberately <em>not</em> absolute: it is the
    ///     discovery case that establishes the dialect, and is relative-eligible.
    /// </remarks>
    /// <param name="callerInput">The path the caller supplied, or null when none was supplied.</param>
    /// <returns><see langword="true"/> when the request is an absolute path; otherwise <see langword="false"/>.</returns>
    private static bool IsAbsoluteRequest(string? callerInput)
    {
        return !IsAbsence(callerInput) && Path.IsPathRooted(callerInput!.Trim());
    }

    /// <summary>
    ///     Determines whether a resolved real path lies at or within the working directory.
    /// </summary>
    /// <param name="realPath">The resolved real location to test.</param>
    /// <returns><see langword="true"/> when the path is the working directory or beneath it.</returns>
    private bool IsWithinWorkingDirectory(string realPath)
    {
        return string.Equals(realPath, WorkingDirectory, PathComparison)
               || realPath.StartsWith(_workingDirectoryPrefix, PathComparison);
    }

    /// <summary>
    ///     Lists the raw candidate files beneath a directory, converting any file system failure
    ///     into an empty result.
    /// </summary>
    /// <remarks>
    ///     The candidates are materialized rather than streamed because a lazily-enumerated listing
    ///     raises its exceptions during iteration, where the caller would see them escape.
    ///     Materializing inside this guarded method keeps the promise that enumeration never throws
    ///     for a file system reason.
    /// </remarks>
    /// <param name="realDirectory">The real location of the directory to list.</param>
    /// <param name="searchPattern">The file-name search pattern to match.</param>
    /// <returns>The raw candidate paths, unfiltered by policy, or an empty array on failure.</returns>
    private static string[] ListCandidates(string realDirectory, string searchPattern)
    {
        try
        {
            return Directory.GetFiles(realDirectory, searchPattern, RecursiveEnumeration);
        }
        catch (Exception exception) when (IsResolutionFailure(exception))
        {
            // A missing, unreadable or malformed directory is not an error the agent can act
            // on; reporting nothing is both fail-safe and actionable.
            return [];
        }
    }

    /// <summary>
    ///     Determines whether an exception represents a failure to resolve or reach a path, rather
    ///     than a defect that should be allowed to propagate.
    /// </summary>
    /// <remarks>
    ///     Enumerated explicitly rather than catching every exception so that genuine defects — a
    ///     null reference, an out-of-memory condition — still surface during development instead of
    ///     being silently reported to a model as a denied path.
    /// </remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the path could not be determined or
    ///     reached; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsResolutionFailure(Exception exception)
    {
        return exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }

    /// <summary>
    ///     Resolves a path to its real location and confirms a grant of the required access permits
    ///     it, building a constructed denial when none does.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Both the read and write entry points funnel through this method so that resolution,
    ///     failure handling and denial construction exist in exactly one place. Sharing the
    ///     implementation is what guarantees that reads and writes differ only in which grants they
    ///     consult, and that read, write and list agree on aliasing and interpretation.
    ///     </para>
    ///     <para>
    ///     <b>The order of the steps is the contract.</b> The request is turned into an absolute
    ///     candidate (an absent request denotes the working directory; every relative request is
    ///     interpreted against the working directory first, with a bare segment matching one
    ///     grant's last path segment aliased to that grant only as a fallback, when the
    ///     working-directory interpretation does not name an existing path), normalized into an
    ///     absolute location with <c>.</c> and <c>..</c> collapsed, and only then tested for
    ///     containment. Making the path absolute before normalizing is what ensures a relative
    ///     path climbing out of a granted location with <c>..</c> is refused exactly as an
    ///     absolute one is.
    ///     </para>
    /// </remarks>
    /// <param name="path">The requested path; may be null, which denotes the working directory.</param>
    /// <param name="requireWrite">Whether a read-write grant is required; false consults every grant.</param>
    /// <param name="realPath">On success, the real location; otherwise <see langword="null"/>.</param>
    /// <param name="denialMessage">On refusal, the constructed reason; otherwise <see langword="null"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when a grant of the required access permits the resolved path;
    ///     otherwise <see langword="false"/>.
    /// </returns>
    private bool TryResolve(
        string? path,
        bool requireWrite,
        [NotNullWhen(true)] out string? realPath,
        [NotNullWhen(false)] out string? denialMessage)
    {
        // Build the absolute candidate and remember how it was interpreted, so a denial can echo
        // the input and, when it was relative, state what it was interpreted as.
        var candidate = BuildCandidate(path, out var wasRelative, out var interpretedAbsolute);

        string resolved;
        try
        {
            // Normalize the absolute form so that containment is judged between two paths
            // spelled the same way; see RealPathResolver for what this does and does not do.
            resolved = RealPathResolver.Resolve(candidate);
        }
        catch (Exception exception) when (IsResolutionFailure(exception))
        {
            // A caller-supplied path the platform cannot express as a location - malformed
            // text, or a result longer than the platform permits - is refused. Denying the
            // unknown is the fail-safe reading, and it keeps the promise that no exception
            // escapes.
            realPath = null;
            denialMessage = BuildDenial(
                "could not be resolved to a real location",
                path,
                wasRelative,
                interpretedAbsolute);
            return false;
        }

        // Consult the grants: a read is permitted by any grant, a write only by a read-write one.
        if (Applicable(requireWrite).Any(grant => grant.Allows(resolved)))
        {
            realPath = resolved;
            denialMessage = null;
            return true;
        }

        // No grant of the required access permits it. Classify the reason for the message, then
        // enumerate what is permitted so the model can re-address rather than guess.
        realPath = null;
        var reason = ClassifyDenial(resolved, requireWrite);
        denialMessage = BuildDenial(reason, path, wasRelative, interpretedAbsolute);
        return false;
    }

    /// <summary>
    ///     Selects the grants a decision consults: every grant for a read, only read-write grants
    ///     for a write.
    /// </summary>
    /// <param name="requireWrite">Whether a read-write grant is required.</param>
    /// <returns>The grants of the required access.</returns>
    private IEnumerable<PathRule> Applicable(bool requireWrite)
    {
        return _grants.Where(grant => !requireWrite || grant.Access == AccessLevel.ReadWrite);
    }

    /// <summary>
    ///     Chooses the reason phrase for a denial: a protected-pattern hit, or being outside every
    ///     permitted location.
    /// </summary>
    /// <remarks>
    ///     A deny-pattern hit is reported as such because it is actionable in a different way — the
    ///     file is withheld however it is spelled — whereas an out-of-location denial invites the
    ///     model to re-address. Only grants of the required access are consulted, matching which
    ///     grants the decision itself consulted.
    /// </remarks>
    /// <param name="resolved">The resolved real path that was refused.</param>
    /// <param name="requireWrite">Whether the decision required a read-write grant.</param>
    /// <returns>The reason phrase for the denial.</returns>
    private string ClassifyDenial(string resolved, bool requireWrite)
    {
        return Applicable(requireWrite).Any(grant => grant.MatchesDenyPattern(resolved))
            ? "matches a protected pattern"
            : "outside every permitted location";
    }

    /// <summary>
    ///     Builds the absolute candidate path from the caller's request and records how it was
    ///     interpreted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     An absent request denotes the working directory; an absolute request is taken as given.
    ///     Every relative request is first interpreted against the working directory — the
    ///     documented mechanism for interpreting a relative path — and that interpretation wins
    ///     whenever it resolves to an existing path. The bare-segment grant alias (a segment with
    ///     no separator that equals the last path segment of exactly one grant's location) is only
    ///     a fallback: it is considered solely for a bare segment whose working-directory
    ///     interpretation does not resolve to an existing path, so a model can still name a granted
    ///     location by its final folder name for input that could not have been a real relative
    ///     path. The alias is an ergonomic courtesy and must never override the documented
    ///     mechanism; demoting it eliminates the silent wrong-target case where a request for
    ///     <c>docs</c> — meaning the working directory's own <c>docs</c> folder — returned a
    ///     different granted <c>docs</c> location instead. An ambiguous bare segment matching two
    ///     or more grants is deliberately <em>not</em> aliased: <see cref="AliasGrant"/> returns
    ///     null, and the request falls through to the working-directory interpretation and is then
    ///     denied with every same-named location enumerated.
    ///     </para>
    ///     <para>
    ///     <paramref name="wasRelative"/> records whether an interpretation actually occurred — a
    ///     relative request joined to the working directory — so a denial reports the interpretation
    ///     only then, never for an absolute request and never for an alias that needs no
    ///     interpretation.
    ///     </para>
    /// </remarks>
    /// <param name="path">The caller's requested path; may be null.</param>
    /// <param name="wasRelative">
    ///     On return, true when the request was joined to the working directory (an interpretation
    ///     occurred); otherwise false.
    /// </param>
    /// <param name="interpretedAbsolute">
    ///     On return, the absolute location the request was interpreted as, used by a denial.
    /// </param>
    /// <returns>The absolute candidate path to resolve.</returns>
    private string BuildCandidate(string? path, out bool wasRelative, out string interpretedAbsolute)
    {
        // Absence, in every spelling: the working directory itself. No interpretation to report.
        if (IsAbsence(path))
        {
            wasRelative = false;
            interpretedAbsolute = WorkingDirectory;
            return WorkingDirectory;
        }

        var trimmed = path!.Trim();

        // An absolute request is taken as given; no interpretation occurred.
        if (Path.IsPathRooted(trimmed))
        {
            wasRelative = false;
            interpretedAbsolute = trimmed;
            return trimmed;
        }

        // Every relative request is FIRST interpreted against the working directory — the
        // documented mechanism for interpreting a relative path. This is the single "make
        // absolute against the anchor" step that lets a model say "notes.txt".
        var combined = Path.Combine(WorkingDirectory, trimmed);

        // The bare-segment grant alias is a FALLBACK only. The working-directory interpretation
        // is the documented mechanism, so it must win whenever it names a real path; a courtesy
        // for input a model guessed at must never override it. The alias is therefore considered
        // only for a bare segment whose working-directory interpretation does not resolve to an
        // existing path. An ambiguous alias (two or more matches) still returns null and falls
        // through to the working-directory interpretation and its enumerated denial.
        if (!ContainsSeparator(trimmed) && !CombinedPathExists(combined))
        {
            var aliased = AliasGrant(trimmed);
            if (aliased is not null)
            {
                // The alias resolves to a granted location; no working-directory interpretation.
                wasRelative = false;
                interpretedAbsolute = aliased;
                return aliased;
            }
        }

        // The working-directory interpretation stands: it named a real path, the request was not
        // a bare segment, or no single grant matched the alias.
        wasRelative = true;
        interpretedAbsolute = NormalizeInterpretedPath(combined);   // report a navigable location
        return combined;                                            // candidate to resolve is unchanged
    }

    /// <summary>
    ///     Lexically normalizes the absolute location a relative request was interpreted as, so a
    ///     denial reports a navigable path rather than one still carrying "." or ".." segments.
    /// </summary>
    /// <remarks>
    ///     Normalization is lexical only — it collapses "." and ".." against the already-absolute
    ///     input and does not touch the file system. Only the reported value is normalized; the
    ///     candidate handed to the resolver is unchanged, so containment is unaffected. Because
    ///     the path is caller-controlled, normalization can throw on malformed input (invalid
    ///     characters, an over-long result); a denial must never throw, so the same
    ///     resolution-class failures <see cref="IsResolutionFailure"/> recognizes fall back to
    ///     the un-normalized value.
    /// </remarks>
    /// <param name="interpreted">The working-directory-combined absolute path to normalize for a denial.</param>
    /// <returns>The lexically normalized path, or the original value when normalization fails.</returns>
    private static string NormalizeInterpretedPath(string interpreted)
    {
        try
        {
            return Path.GetFullPath(interpreted);
        }
        catch (Exception exception) when (IsResolutionFailure(exception))
        {
            // A caller-supplied path that cannot be normalized still yields a denial; reporting the
            // un-normalized interpretation is the fail-safe reading and keeps the non-throwing contract.
            return interpreted;
        }
    }

    /// <summary>
    ///     Reports whether a working-directory-combined candidate names an existing file or
    ///     directory, the existence probe that decides whether a bare-segment alias is even
    ///     considered.
    /// </summary>
    /// <remarks>
    ///     The probe is an existence check only — it never opens or reads the path — and it
    ///     decides candidate selection only, never containment: a probed path that exists is still
    ///     subject to the unchanged normalization and grant test. Any failure to
    ///     determine existence is treated as "does not exist" so the probe can never throw out of
    ///     <see cref="BuildCandidate"/>; falling back to the alias for an indeterminate path is the
    ///     same fail-safe reading used for a genuinely missing one.
    /// </remarks>
    /// <param name="combined">The working-directory-combined candidate to probe.</param>
    /// <returns><see langword="true"/> when the path exists; otherwise <see langword="false"/>.</returns>
    private static bool CombinedPathExists(string combined)
    {
        try
        {
            return Path.Exists(combined);
        }
        catch (Exception exception) when (IsResolutionFailure(exception))
        {
            return false;
        }
    }

    /// <summary>
    ///     Returns the resolved location of the one grant whose final path segment equals the given
    ///     bare segment, or <see langword="null"/> when none or more than one match.
    /// </summary>
    /// <remarks>
    ///     Matching exactly one grant is deliberate. Aliasing an ambiguous segment to one of several
    ///     same-named locations is the silent wrong-target failure this model exists to prevent, so
    ///     ambiguity returns null and the request falls through to the ordinary reading and its
    ///     enumerated denial, which lists every same-named location. Only rooted grants have a final
    ///     segment to match.
    /// </remarks>
    /// <param name="segment">The bare input segment to match against grant locations.</param>
    /// <returns>The single matching grant's resolved location, or null when not exactly one matches.</returns>
    private string? AliasGrant(string segment)
    {
        // Match at most two distinct locations: exactly one is an alias, two or more is ambiguous.
        var matches = _grants
            .Where(grant => grant.Root is not null)
            .Select(grant => grant.Root!)
            .Where(root => string.Equals(LastSegment(root), segment, PathComparison))
            .Distinct(PathComparer)
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    ///     Returns the final path segment of a resolved location, ignoring any trailing separator.
    /// </summary>
    /// <param name="root">The resolved location.</param>
    /// <returns>The location's last path segment.</returns>
    private static string LastSegment(string root)
    {
        return Path.GetFileName(
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    /// <summary>
    ///     Builds a denial message stating what was asked for, how it was interpreted when that
    ///     happened, and which locations are permitted.
    /// </summary>
    /// <remarks>
    ///     The three parts appear in order: the requested input echoed verbatim; the interpreted
    ///     absolute location, present only when a relative request was joined to the working
    ///     directory, so an interpretation is never reported that did not occur and the
    ///     interpretation is never reported on its own; and the permitted locations, each with
    ///     its access level, or a statement that none are permitted.
    /// </remarks>
    /// <param name="reason">The reason phrase for the denial.</param>
    /// <param name="path">The caller's requested path; may be null.</param>
    /// <param name="wasRelative">Whether the request was interpreted against the working directory.</param>
    /// <param name="interpretedAbsolute">The absolute location the request was interpreted as.</param>
    /// <returns>The constructed denial message.</returns>
    private string BuildDenial(
        string reason,
        string? path,
        bool wasRelative,
        string interpretedAbsolute)
    {
        var builder = new StringBuilder();
        builder.Append("Denied: ").Append(reason).Append('.');

        // (a) What was asked for, echoed verbatim; a stand-in when nothing was supplied.
        var echo = IsAbsence(path) ? "(no path — the working directory)" : $"\"{path}\"";
        builder.Append('\n').Append("Requested: ").Append(echo);

        // (b) What it was interpreted as, only when a relative request was actually interpreted.
        if (wasRelative)
        {
            builder.Append('\n').Append("Interpreted as: ").Append(interpretedAbsolute);
        }

        // (c) What is actually permitted, enumerated with access levels.
        if (_grants.Length == 0)
        {
            builder.Append('\n').Append("No locations are permitted.");
        }
        else
        {
            builder.Append('\n').Append("Permitted locations:");
            foreach (var grant in _grants)
            {
                builder.Append('\n').Append("  - ").Append(grant.Describe());
            }
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Determines whether a directory request means "no directory": absent, empty, whitespace,
    ///     or a placeholder spelling. Such a request lists every permitted location rather than one.
    /// </summary>
    /// <remarks>
    ///     It is public because a package boundary already crosses here: the built-in
    ///     <c>file_list</c> tool (<c>FileListTool</c>) consumes it from the separate Tools
    ///     package so that a custom tool and the policy never disagree about what "no argument"
    ///     means, and a third-party tool pack sits in exactly the same position and needs the same
    ///     agreement. Tools is only the first consumer, not a privileged one, so the exposure follows
    ///     from that design fact — not from any test or build reason. See <see cref="EmitRelative"/>
    ///     for the full extensibility contract.
    /// </remarks>
    /// <param name="directory">The directory the caller supplied; may be null.</param>
    /// <returns><see langword="true"/> when the request denotes no directory; otherwise <see langword="false"/>.</returns>
    public static bool IsDiscoveryRequest(string? directory)
    {
        return IsAbsence(directory);
    }

    /// <summary>
    ///     Determines whether a request means "no path": absent, empty, whitespace, or one of the
    ///     spellings a model's own runtime prints for absence.
    /// </summary>
    /// <remarks>
    ///     A tool call carrying no path, an empty path, or the literal word its runtime prints for
    ///     absence all mean the working directory. Recognizing them here — once, in the single
    ///     decision point — means every entry point agrees, and no tool has to invent its own
    ///     reading.
    /// </remarks>
    /// <param name="path">The path the caller supplied; may be null.</param>
    /// <returns><see langword="true"/> when the request denotes no path; otherwise <see langword="false"/>.</returns>
    private static bool IsAbsence(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var trimmed = path.Trim();
        return PlaceholderPaths.Any(
            placeholder => string.Equals(trimmed, placeholder, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Determines whether a request contains a directory separator, which disqualifies it from
    ///     being read as a bare-segment grant alias.
    /// </summary>
    /// <param name="path">The trimmed request text.</param>
    /// <returns><see langword="true"/> when the text contains either separator character.</returns>
    private static bool ContainsSeparator(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar);
    }
}

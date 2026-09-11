using System.Diagnostics.CodeAnalysis;
using System.Security;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Pairs an independent read rule and write rule, carries the workspace location relative
///     paths are interpreted against, and provides the single containment decision used both by
///     direct path access and by directory enumeration.
/// </summary>
/// <remarks>
///     <para>
///     A policy exists so that a tool never has to decide for itself whether a path is
///     acceptable. It resolves the requested path to its real location and consults exactly one
///     rule — the read rule for a read, the write rule for a write — so read access and write
///     access stay independent of each other.
///     </para>
///     <para>
///     <b>A relative path is interpreted against <see cref="BaseDirectory"/>, never against the
///     process working directory.</b> A model writes the paths a person would write: it asks
///     for <c>notes.txt</c>, not for the absolute location of <c>notes.txt</c>. Resolving that
///     against wherever the host process happened to start refuses every legitimate request
///     while looking, from the outside, like a containment decision. The base belongs to the
///     policy rather than to a rule because reads may be unrestricted while writes are confined,
///     and both directions must interpret a relative path the same way.
///     </para>
///     <para>
///     <b>The resolution order is part of the contract.</b> A requested path is first made
///     absolute against the base, then resolved through
///     <see cref="RealPathResolver.Resolve"/>'s per-component reparse-point walk, and only then
///     tested for containment. Making the path absolute first is what lets a relative path be
///     expressed at all; doing it before the walk is what ensures a relative path that reaches
///     outside through a link is refused exactly as an absolute one is.
///     </para>
///     <para>
///     <b>Denial is a return value, not an exception.</b> A refused path is reported by
///     returning <see langword="false"/> with a denial message. An exception thrown at a model's
///     tool call ends the agent's turn and strands it with no way forward, whereas a returned
///     denial lets the model read the reason and choose a different path. No path a caller
///     supplies — including none at all — is reported as an exception; only programming errors
///     such as a null rule are.
///     </para>
///     <para>
///     <b>Denial messages guide recovery and still carry no host detail.</b> They are fixed
///     constants with no interpolation, because the message is handed back to a model and the
///     resulting transcript leaves this process; interpolating the requested path or the
///     permitted location would disclose host layout to a third party. Each message
///     nevertheless states what the model should do instead, because a model told only "no"
///     retries the same path until it gives up. The guidance is deliberately written without a
///     directory separator, so that "contains no separator" remains a usable test for "contains
///     no host location".
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
public sealed class PathPolicy
{
    /// <summary>
    ///     The fixed sentence every location denial ends with, telling the model how to phrase a
    ///     request that could succeed.
    /// </summary>
    /// <remarks>
    ///     A denial that only refuses leaves a model guessing, and an agent that guesses spends
    ///     its turns re-submitting variations of the same path. The example is a bare file name
    ///     on purpose: it demonstrates the workspace-relative form while containing no directory
    ///     separator, so it cannot weaken the redaction property the denial messages exist to
    ///     hold.
    /// </remarks>
    private const string RecoveryGuidance =
        " Paths are interpreted relative to the workspace root, so request a path such as"
        + " 'notes.txt'.";

    /// <summary>
    ///     The denial message used when a read request resolves outside the permitted read location.
    /// </summary>
    private const string ReadLocationDenied =
        "Access denied: the requested path is outside the permitted read location."
        + RecoveryGuidance;

    /// <summary>
    ///     The denial message used when a write request resolves outside the permitted write location.
    /// </summary>
    private const string WriteLocationDenied =
        "Access denied: the requested path is outside the permitted write location."
        + RecoveryGuidance;

    /// <summary>
    ///     The denial message used when a request matches one of the rule's denied patterns.
    /// </summary>
    private const string PatternDenied =
        "Access denied: the requested path matches a protected pattern. Request a different file"
        + " instead; this one is withheld regardless of how it is spelled.";

    /// <summary>
    ///     The denial message used when the real location of a request cannot be determined.
    /// </summary>
    private const string UnresolvableDenied =
        "Access denied: the requested path could not be resolved." + RecoveryGuidance;

    /// <summary>
    ///     The path used in place of an omitted or placeholder request, denoting the base
    ///     directory itself.
    /// </summary>
    /// <remarks>
    ///     Expressed as the current-directory token rather than as the base directory's text so
    ///     that the single "make absolute against the base" step below handles the omitted case
    ///     with no branch of its own.
    /// </remarks>
    private const string BasePath = ".";

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
    ///     Note that recursion deliberately does <em>not</em> imply trust: the operating system
    ///     will happily descend through a link that leaves the permitted location, which is
    ///     precisely why every candidate is filtered afterwards.
    /// </remarks>
    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true
    };

    /// <summary>
    ///     Initializes a new instance of the <see cref="PathPolicy"/> class with the library's
    ///     documented resource ceilings.
    /// </summary>
    /// <remarks>
    ///     There is no default, parameterless or single-rule constructor. Requiring both rules
    ///     makes an unguarded policy unrepresentable, so no code path can accidentally hand a
    ///     tool a policy that permits everything. This overload delegates to the three-argument
    ///     constructor with <see cref="ToolLimits.Default"/>, so a host that has no opinion
    ///     about ceilings still gets bounded ones.
    /// </remarks>
    /// <param name="readRule">The rule governing read access. Must not be null.</param>
    /// <param name="writeRule">The rule governing write access. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="readRule"/> or <paramref name="writeRule"/> is
    ///     <see langword="null"/>.
    /// </exception>
    public PathPolicy(PathRule readRule, PathRule writeRule)
        : this(readRule, writeRule, ToolLimits.Default)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PathPolicy"/> class with explicit
    ///     resource ceilings.
    /// </summary>
    /// <remarks>
    ///     The ceilings ride with the policy rather than being passed per call so that every
    ///     tool a host governs observes the same budget. A tool receives one object and cannot
    ///     end up observing a different budget from its neighbor.
    /// </remarks>
    /// <param name="readRule">The rule governing read access. Must not be null.</param>
    /// <param name="writeRule">The rule governing write access. Must not be null.</param>
    /// <param name="limits">
    ///     The ceilings every tool governed by this policy observes. Must not be null.
    /// </param>
    /// <param name="baseDirectory">
    ///     The location a relative request is interpreted against, or <see langword="null"/> to
    ///     use the read rule's location, failing that the write rule's, failing that the process
    ///     working directory. Need not exist.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="readRule"/>, <paramref name="writeRule"/> or
    ///     <paramref name="limits"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="baseDirectory"/> is supplied but is empty or is not a
    ///     valid path.
    /// </exception>
    public PathPolicy(
        PathRule readRule,
        PathRule writeRule,
        ToolLimits limits,
        string? baseDirectory = null)
    {
        // Reject missing rules at construction: an absent rule is a programming error, and
        // defaulting it to anything would silently grant access nobody asked for. An absent set
        // of ceilings is the same kind of error — unbounded is not a sensible default.
        ArgumentNullException.ThrowIfNull(readRule);
        ArgumentNullException.ThrowIfNull(writeRule);
        ArgumentNullException.ThrowIfNull(limits);

        ReadRule = readRule;
        WriteRule = writeRule;
        Limits = limits;

        // Resolve the base once, here, so that every later request pays only for resolving its
        // own path. Defaulting to the read rule's location means a policy built the ordinary
        // rooted way interprets relative paths against the location it was confined to, which is
        // what a host building such a policy already believes it asked for. The process working
        // directory is the last resort, reached only when neither rule names a location and
        // there is therefore nothing else for a relative path to be relative to.
        BaseDirectory = RealPathResolver.Resolve(
            baseDirectory ?? readRule.Root ?? writeRule.Root ?? Environment.CurrentDirectory);
    }

    /// <summary>
    ///     Creates a policy confining both reads and writes to one workspace location and
    ///     interpreting relative requests against it, with the library's documented resource
    ///     ceilings.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the shape almost every host wants, and it exists because the general
    ///     constructor makes the wrong configuration the easy one: a caller who supplies two
    ///     rooted rules and no base has said nothing about how a relative path should be read,
    ///     and a caller who supplies a base that disagrees with the rules has built a policy
    ///     that denies its own relative requests. Naming the workspace once removes both
    ///     mistakes.
    ///     </para>
    ///     <para>
    ///     Absolute requests remain expressible and remain subject to containment; naming a
    ///     workspace narrows how a bare name is read, it does not widen or narrow what is
    ///     permitted.
    ///     </para>
    /// </remarks>
    /// <param name="root">
    ///     The workspace location. Must be non-null and non-empty. Need not exist.
    /// </param>
    /// <returns>
    ///     A policy whose read rule, write rule and base directory are all the workspace.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="root"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="root"/> is empty or is not a valid path.
    /// </exception>
    public static PathPolicy ForWorkspace(string root)
    {
        return ForWorkspace(root, ToolLimits.Default);
    }

    /// <summary>
    ///     Creates a policy confining both reads and writes to one workspace location and
    ///     interpreting relative requests against it, with explicit resource ceilings.
    /// </summary>
    /// <remarks>
    ///     The same contract as the single-argument overload, for a host that has an opinion
    ///     about the budget its tools observe. The ceilings are required rather than nullable
    ///     here for the same reason the general constructor requires them: "unbounded" is not a
    ///     sensible default.
    /// </remarks>
    /// <param name="root">
    ///     The workspace location. Must be non-null and non-empty. Need not exist.
    /// </param>
    /// <param name="limits">
    ///     The ceilings every tool governed by this policy observes. Must not be null.
    /// </param>
    /// <returns>
    ///     A policy whose read rule, write rule and base directory are all the workspace.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="root"/> or <paramref name="limits"/> is
    ///     <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="root"/> is empty or is not a valid path.
    /// </exception>
    public static PathPolicy ForWorkspace(string root, ToolLimits limits)
    {
        // Reject a missing workspace before any rule is built: a workspace policy with no
        // workspace is a programming error, and there is no safe location to assume instead.
        ArgumentException.ThrowIfNullOrEmpty(root);

        return new PathPolicy(PathRule.Rooted(root), PathRule.Rooted(root), limits, root);
    }

    /// <summary>
    ///     Gets the rule governing read access.
    /// </summary>
    /// <remarks>
    ///     Exposed so that a host can report the configured access to a user, and so that
    ///     tests can build request paths relative to the rule's resolved location.
    /// </remarks>
    public PathRule ReadRule { get; }

    /// <summary>
    ///     Gets the rule governing write access.
    /// </summary>
    /// <remarks>
    ///     Exposed for the same reasons as <see cref="ReadRule"/>. It is a separate rule, not a
    ///     narrowing of the read rule, so that reading widely while writing narrowly is
    ///     expressible.
    /// </remarks>
    public PathRule WriteRule { get; }

    /// <summary>
    ///     Gets the ceilings every tool governed by this policy observes.
    /// </summary>
    /// <remarks>
    ///     Carried with the policy rather than supplied per call, so that every pack a host
    ///     attaches observes one budget rather than each inventing its own. Never null.
    /// </remarks>
    public ToolLimits Limits { get; }

    /// <summary>
    ///     Gets the real location a relative request is interpreted against.
    /// </summary>
    /// <remarks>
    ///     Exposed so that a host can report the workspace to a user and so that tests can state
    ///     what a bare file name is expected to resolve to. It is resolved to its real location
    ///     at construction, for the same reason a rule's location is: every later comparison is
    ///     then real location against real location. Never null.
    /// </remarks>
    public string BaseDirectory { get; }

    /// <summary>
    ///     Attempts to resolve a path for reading and to confirm the read rule permits it.
    /// </summary>
    /// <remarks>
    ///     This is the single read decision in the library. Directory enumeration filters every
    ///     candidate through this same method, so a file that direct access would refuse can
    ///     never appear in a listing.
    /// </remarks>
    /// <param name="path">
    ///     The requested path. A relative path is interpreted against
    ///     <see cref="BaseDirectory"/>; an absolute path is taken as given and remains subject
    ///     to containment. An omitted, empty, whitespace or placeholder path denotes the base
    ///     directory itself. Need not exist.
    /// </param>
    /// <param name="realPath">
    ///     On success, the real location the caller may read; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="denialMessage">
    ///     On refusal, a fixed message stating why and what to do instead, containing no host
    ///     detail; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the read is permitted; otherwise <see langword="false"/>.
    /// </returns>
    public bool TryResolveRead(
        string? path,
        [NotNullWhen(true)] out string? realPath,
        [NotNullWhen(false)] out string? denialMessage)
    {
        return TryResolve(path, ReadRule, ReadLocationDenied, out realPath, out denialMessage);
    }

    /// <summary>
    ///     Attempts to resolve a path for writing and to confirm the write rule permits it.
    /// </summary>
    /// <remarks>
    ///     Consults the write rule alone. A path that is readable is not thereby writable — the
    ///     two rules are independent, which is what makes a read-wide, write-narrow
    ///     configuration meaningful.
    /// </remarks>
    /// <param name="path">
    ///     The requested path, interpreted exactly as <see cref="TryResolveRead"/> interprets
    ///     it. Need not exist.
    /// </param>
    /// <param name="realPath">
    ///     On success, the real location the caller may write; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="denialMessage">
    ///     On refusal, a fixed message stating why and what to do instead, containing no host
    ///     detail; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the write is permitted; otherwise <see langword="false"/>.
    /// </returns>
    public bool TryResolveWrite(
        string? path,
        [NotNullWhen(true)] out string? realPath,
        [NotNullWhen(false)] out string? denialMessage)
    {
        return TryResolve(path, WriteRule, WriteLocationDenied, out realPath, out denialMessage);
    }

    /// <summary>
    ///     Lists the real locations of the files beneath a directory that this policy permits
    ///     the caller to read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Enumeration and access share one decision.</b> Recursive enumeration by the
    ///     operating system follows directory junctions and symbolic links, and will therefore
    ///     surface files that lie outside the permitted location. Every candidate is passed
    ///     through <see cref="TryResolveRead"/> — the very method used for direct access — so a
    ///     listing can never advertise a file that a read would refuse. This is a design
    ///     invariant: the filtering must remain the same code path as direct access, never a
    ///     parallel re-implementation.
    ///     </para>
    ///     <para>
    ///     Nothing here throws for a policy or file system reason. A denied directory, a
    ///     missing directory or an unreadable tree all yield an empty sequence.
    ///     </para>
    ///     <para>
    ///     The first parameter is named <c>directory</c> rather than describing a relative
    ///     location because a read rule may be unrestricted, in which case there is no root for
    ///     a path to be relative to. The argument is itself subject to the read decision, and is
    ///     interpreted exactly as <see cref="TryResolveRead"/> interprets a path — including
    ///     the omitted case, which lists the base directory.
    ///     </para>
    /// </remarks>
    /// <param name="directory">
    ///     The directory to list. A relative directory is interpreted against
    ///     <see cref="BaseDirectory"/>; an omitted, empty, whitespace or placeholder directory
    ///     denotes the base directory itself.
    /// </param>
    /// <param name="searchPattern">
    ///     The file-name search pattern to match, for example <c>*</c> or <c>*.txt</c>. Must be
    ///     non-null and non-empty.
    /// </param>
    /// <returns>
    ///     The real locations of the permitted files, which is empty when the directory itself
    ///     is refused or cannot be listed.
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
        // request the model can legitimately make, and it means the base.
        ArgumentException.ThrowIfNullOrEmpty(searchPattern);

        // The directory itself is subject to the same read decision as any other path; a
        // refused directory yields nothing rather than an error, so a caller can list freely.
        if (!TryResolveRead(directory, out var realDirectory, out _))
        {
            return [];
        }

        // Filter every candidate through the single read decision. The enumeration itself may
        // have crossed a link out of the permitted location; this is where that is undone.
        return ListCandidates(realDirectory, searchPattern)
            .Select(candidate => TryResolveRead(candidate, out var real, out _) ? real : null)
            .Where(real => real is not null)
            .Select(real => real!);
    }

    /// <summary>
    ///     Lists the raw candidate files beneath a directory, converting any file system
    ///     failure into an empty result.
    /// </summary>
    /// <remarks>
    ///     The candidates are materialized rather than streamed because a lazily-enumerated
    ///     listing raises its exceptions during iteration, where the caller would see them
    ///     escape. Materializing inside this guarded method keeps the promise that enumeration
    ///     never throws for a file system reason.
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
    ///     Determines whether an exception represents a failure to resolve or reach a path,
    ///     rather than a defect that should be allowed to propagate.
    /// </summary>
    /// <remarks>
    ///     Enumerated explicitly rather than catching every exception so that genuine defects —
    ///     a null reference, an out-of-memory condition — still surface during development
    ///     instead of being silently reported to a model as a denied path.
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
    ///     Resolves a path to its real location and applies a single rule to it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Both the read and write entry points funnel through this method so that resolution,
    ///     failure handling and denial-message selection exist in exactly one place. Sharing
    ///     the implementation is what guarantees that reads and writes differ only in which
    ///     rule they consult.
    ///     </para>
    ///     <para>
    ///     <b>The order of the four steps is the contract.</b> The request is normalized, made
    ///     absolute against <see cref="BaseDirectory"/>, resolved through the per-component
    ///     reparse-point walk, and only then tested for containment. Making the path absolute
    ///     before the walk rather than after it is what ensures a relative path that reaches
    ///     outside the permitted location through a link is refused exactly as an absolute one
    ///     is — the walk sees the same fully-qualified path either way.
    ///     </para>
    ///     <para>
    ///     Every step that touches the caller's text runs inside the guarded region, so no
    ///     spelling a model can produce escapes as an exception.
    ///     </para>
    /// </remarks>
    /// <param name="path">The requested path; may be null, which denotes the base directory.</param>
    /// <param name="rule">The rule to apply.</param>
    /// <param name="locationDenialMessage">The message to report when the location is refused.</param>
    /// <param name="realPath">On success, the real location; otherwise <see langword="null"/>.</param>
    /// <param name="denialMessage">On refusal, the reason; otherwise <see langword="null"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when the rule permits the resolved path; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private bool TryResolve(
        string? path,
        PathRule rule,
        string locationDenialMessage,
        [NotNullWhen(true)] out string? realPath,
        [NotNullWhen(false)] out string? denialMessage)
    {
        // Resolve first: every decision below is made about the real location, never about the
        // string the caller supplied. Normalization and base-relative combination sit inside
        // the guarded region with the walk, because they too operate on model-supplied text.
        string resolved;
        try
        {
            // An omitted or placeholder request means the workspace itself.
            var candidate = Normalize(path);

            // Make the request absolute against the workspace, never against the process
            // working directory. This single step is what lets a model say "notes.txt".
            if (!Path.IsPathRooted(candidate))
            {
                candidate = Path.Combine(BaseDirectory, candidate);
            }

            // The unchanged per-component reparse-point walk. It must run on the absolute form,
            // and it must not be replaced by a cheaper resolution; see RealPathResolver.
            resolved = RealPathResolver.Resolve(candidate);
        }
        catch (Exception exception) when (IsResolutionFailure(exception))
        {
            // A path whose real location cannot be determined is refused. Denying the unknown
            // is the fail-safe reading, and it keeps the promise that no exception escapes.
            realPath = null;
            denialMessage = UnresolvableDenied;
            return false;
        }

        // Consult the rule. Only this single rule is consulted, keeping read and write
        // decisions independent of one another.
        if (rule.Allows(resolved))
        {
            realPath = resolved;
            denialMessage = null;
            return true;
        }

        // Report why, choosing between the two refusal reasons without revealing either the
        // requested path or the permitted location.
        realPath = null;
        denialMessage = rule.MatchesDenyPattern(resolved) ? PatternDenied : locationDenialMessage;
        return false;
    }

    /// <summary>
    ///     Converts the spellings a model uses for "no path" into the token denoting the base
    ///     directory, and leaves every other request untouched.
    /// </summary>
    /// <remarks>
    ///     A tool call carrying no directory, an empty directory, or the literal word its
    ///     runtime prints for absence all mean the same thing: the workspace. Reading any of
    ///     them as a file name refuses a request that was well formed in every way the model
    ///     could tell, and leaves it guessing at locations it has no business exploring.
    ///     Recognizing them here — once, in the single decision point — means every entry point
    ///     agrees, and no tool has to invent its own reading.
    /// </remarks>
    /// <param name="path">The path the caller supplied; may be null.</param>
    /// <returns>
    ///     The base-directory token when the request denotes no path; otherwise
    ///     <paramref name="path"/> unchanged.
    /// </returns>
    private static string Normalize(string? path)
    {
        // Absence, in every spelling that reaches a tool as whitespace or nothing at all.
        if (string.IsNullOrWhiteSpace(path))
        {
            return BasePath;
        }

        // Absence, in the spellings a model's own runtime produces. Compared case-insensitively
        // because the word arrives capitalized or not depending on the model.
        var trimmed = path.Trim();
        return PlaceholderPaths.Any(
            placeholder => string.Equals(trimmed, placeholder, StringComparison.OrdinalIgnoreCase))
            ? BasePath
            : path;
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Security;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Pairs an independent read rule and write rule, and provides the single containment
///     decision used both by direct path access and by directory enumeration.
/// </summary>
/// <remarks>
///     <para>
///     A policy exists so that a tool never has to decide for itself whether a path is
///     acceptable. It resolves the requested path to its real location and consults exactly one
///     rule — the read rule for a read, the write rule for a write — so read access and write
///     access stay independent of each other.
///     </para>
///     <para>
///     <b>Denial is a return value, not an exception.</b> A refused path is reported by
///     returning <see langword="false"/> with a denial message. An exception thrown at a model's
///     tool call ends the agent's turn and strands it with no way forward, whereas a returned
///     denial lets the model read the reason and choose a different path. Only programming
///     errors — a null rule, a null or empty path — are reported as exceptions.
///     </para>
///     <para>
///     <b>Denial messages carry no host detail.</b> They are fixed constants with no
///     interpolation, because the message is handed back to a model and the resulting
///     transcript leaves this process; interpolating the requested path or the permitted
///     location would disclose host layout to a third party.
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
public sealed class PathPolicy
{
    /// <summary>
    ///     The denial message used when a read request resolves outside the permitted read location.
    /// </summary>
    private const string ReadLocationDenied =
        "Access denied: the requested path is outside the permitted read location.";

    /// <summary>
    ///     The denial message used when a write request resolves outside the permitted write location.
    /// </summary>
    private const string WriteLocationDenied =
        "Access denied: the requested path is outside the permitted write location.";

    /// <summary>
    ///     The denial message used when a request matches one of the rule's denied patterns.
    /// </summary>
    private const string PatternDenied =
        "Access denied: the requested path matches a protected pattern.";

    /// <summary>
    ///     The denial message used when the real location of a request cannot be determined.
    /// </summary>
    private const string UnresolvableDenied =
        "Access denied: the requested path could not be resolved.";

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
    ///     Initializes a new instance of the <see cref="PathPolicy"/> class.
    /// </summary>
    /// <remarks>
    ///     There is no default, parameterless or single-rule constructor. Requiring both rules
    ///     makes an unguarded policy unrepresentable, so no code path can accidentally hand a
    ///     tool a policy that permits everything.
    /// </remarks>
    /// <param name="readRule">The rule governing read access. Must not be null.</param>
    /// <param name="writeRule">The rule governing write access. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="readRule"/> or <paramref name="writeRule"/> is
    ///     <see langword="null"/>.
    /// </exception>
    public PathPolicy(PathRule readRule, PathRule writeRule)
    {
        // Reject missing rules at construction: an absent rule is a programming error, and
        // defaulting it to anything would silently grant access nobody asked for.
        ArgumentNullException.ThrowIfNull(readRule);
        ArgumentNullException.ThrowIfNull(writeRule);

        ReadRule = readRule;
        WriteRule = writeRule;
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
    ///     Attempts to resolve a path for reading and to confirm the read rule permits it.
    /// </summary>
    /// <remarks>
    ///     This is the single read decision in the library. Directory enumeration filters every
    ///     candidate through this same method, so a file that direct access would refuse can
    ///     never appear in a listing.
    /// </remarks>
    /// <param name="path">
    ///     The requested path, absolute or relative to the current working directory. Must be
    ///     non-null and non-empty.
    /// </param>
    /// <param name="realPath">
    ///     On success, the real location the caller may read; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="denialMessage">
    ///     On refusal, a fixed message stating why, containing no host detail; otherwise
    ///     <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the read is permitted; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="path"/> is an empty string.
    /// </exception>
    public bool TryResolveRead(
        string path,
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
    ///     The requested path, absolute or relative to the current working directory. Must be
    ///     non-null and non-empty. Need not exist.
    /// </param>
    /// <param name="realPath">
    ///     On success, the real location the caller may write; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="denialMessage">
    ///     On refusal, a fixed message stating why, containing no host detail; otherwise
    ///     <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the write is permitted; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="path"/> is an empty string.
    /// </exception>
    public bool TryResolveWrite(
        string path,
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
    ///     a path to be relative to. The argument is itself subject to the read decision.
    ///     </para>
    /// </remarks>
    /// <param name="directory">
    ///     The directory to list, absolute or relative to the current working directory. Must
    ///     be non-null and non-empty.
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
    ///     Thrown when <paramref name="directory"/> or <paramref name="searchPattern"/> is
    ///     <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="directory"/> or <paramref name="searchPattern"/> is an
    ///     empty string.
    /// </exception>
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern)
    {
        // Missing arguments are programming errors and are reported as such.
        ArgumentException.ThrowIfNullOrEmpty(directory);
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
    ///     Both the read and write entry points funnel through this method so that resolution,
    ///     failure handling and denial-message selection exist in exactly one place. Sharing
    ///     the implementation is what guarantees that reads and writes differ only in which
    ///     rule they consult.
    /// </remarks>
    /// <param name="path">The requested path. Must be non-null and non-empty.</param>
    /// <param name="rule">The rule to apply.</param>
    /// <param name="locationDenialMessage">The message to report when the location is refused.</param>
    /// <param name="realPath">On success, the real location; otherwise <see langword="null"/>.</param>
    /// <param name="denialMessage">On refusal, the reason; otherwise <see langword="null"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when the rule permits the resolved path; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="path"/> is an empty string.
    /// </exception>
    private static bool TryResolve(
        string path,
        PathRule rule,
        string locationDenialMessage,
        [NotNullWhen(true)] out string? realPath,
        [NotNullWhen(false)] out string? denialMessage)
    {
        // A missing path is a programming error in the caller, not something a model supplied,
        // so it is surfaced rather than converted into a denial.
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Resolve first: every decision below is made about the real location, never about the
        // string the caller supplied.
        string resolved;
        try
        {
            resolved = RealPathResolver.Resolve(path);
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
}

using System.IO.Enumeration;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     The permission an access grant carries: whether it may be read only, or read and written.
/// </summary>
/// <remarks>
///     <para>
///     An access level is <b>permission only</b>. It says what may be done at a location; it says
///     nothing about addressing — nothing about how a relative path is resolved, nor about which
///     location a bare name refers to. Addressing is the sole concern of
///     <see cref="PathPolicy.WorkingDirectory"/>, and the two ideas are deliberately orthogonal:
///     a location may be granted read-write yet never be an addressing anchor, and the anchor may
///     be granted nothing at all.
///     </para>
///     <para>
///     There are exactly two levels and no more. A grant is either <see cref="ReadOnly"/> or
///     <see cref="ReadWrite"/>; there is no append-only or create-only level, because a tool set
///     that can express "read" and "read and write" can express every access an agent file tool
///     needs, and a third level would add a state to reason about at every decision point without
///     enabling a capability the two do not already cover.
///     </para>
/// </remarks>
public enum AccessLevel
{
    /// <summary>
    ///     The location and its contents may be read but never written.
    /// </summary>
    ReadOnly,

    /// <summary>
    ///     The location and its contents may be both read and written.
    /// </summary>
    ReadWrite
}

/// <summary>
///     One access grant: a location an agent is permitted to reach, carrying the
///     <see cref="AccessLevel"/> that permission grants and its own set of denied patterns. A
///     grant may instead be unrestricted, permitting every location its access level allows
///     except those its patterns exclude.
/// </summary>
/// <remarks>
///     <para>
///     A grant expresses <b>permission only</b>. It answers "may this location be read, or read
///     and written?" and nothing else. In particular it carries no addressing meaning: granting a
///     location does not make it the anchor a bare relative name resolves against, and being that
///     anchor grants nothing. That anchor is <see cref="PathPolicy.WorkingDirectory"/>, and the
///     separation is deliberate — a policy may grant several locations while anchoring relative
///     paths at exactly one, or anchor at a location it grants nothing.
///     </para>
///     <para>
///     Grants are independent of one another. A policy may hold a read-only grant over one
///     location and a read-write grant over another; a read is permitted when any grant permits
///     it, and a write only when a <see cref="AccessLevel.ReadWrite"/> grant permits it, so
///     reading widely while writing narrowly is expressed as two grants rather than one combined
///     setting. Each grant keeps its own denied patterns, so credential material can be excluded
///     from a wide read grant without affecting what a separate write grant permits.
///     </para>
///     <para>
///     A grant is constructed only through <see cref="ReadOnly"/>, <see cref="ReadWrite"/> or
///     <see cref="Unrestricted"/>, so a partially-configured grant cannot exist. Instances are
///     immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Asymmetric grants: read widely, write narrowly. A read-only grant never authorizes a write,
///     no matter how many other grants exist, because a write consults only
///     <see cref="AccessLevel.ReadWrite"/> grants. Deny patterns belong to the individual grant, so
///     credential material can be excluded from the wide read grant without affecting the write
///     grant at all.
///     </para>
///     <code>
///     var source = Path.GetFullPath("source");
///     var output = Path.GetFullPath("output");
///
///     // Read the source tree, but never write to it — and never read its secrets at all.
///     var readSource = PathRule.ReadOnly(source, ["*.pem", "*.key", ".env"]);
///
///     // Write only here.
///     var writeOutput = PathRule.ReadWrite(output);
///
///     var policy = new PathPolicy(source, [readSource, writeOutput]);
///
///     // Permitted: the read-only grant covers it.
///     var canRead = policy.TryResolveRead("report.txt", out _, out _);
///
///     // Refused: no read-write grant covers the source tree. The denial enumerates every
///     // permitted location with its access level, so the output location is discoverable.
///     var canWrite = policy.TryResolveWrite("report.txt", out _, out var denial);
///     </code>
/// </example>
public sealed class PathRule
{
    /// <summary>
    ///     The string comparison used for containment, chosen to match the case sensitivity of
    ///     the platform's default file system.
    /// </summary>
    /// <remarks>
    ///     The comparison must match the platform's file system, and getting it wrong is unsafe
    ///     in one direction rather than merely inconvenient in both.
    ///     <para>
    ///     Windows and macOS default file systems are case-insensitive. Comparing
    ///     case-sensitively there would let a differently-cased spelling of a denied location
    ///     slip past containment while still reaching the same file.
    ///     </para>
    ///     <para>
    ///     Linux file systems are case-sensitive, so two paths differing only in case are
    ///     genuinely different locations. Comparing case-insensitively there would judge a path
    ///     contained by a root it does not actually lie beneath — a permitted access to a
    ///     location the rule was meant to exclude. This direction is the dangerous one, which is
    ///     why the comparison follows the platform rather than defaulting to ignoring case
    ///     everywhere.
    ///     </para>
    ///     <para>
    ///     This is a platform default rather than a probe of the file system actually in use.
    ///     Windows supports per-directory case sensitivity and macOS can be configured with a
    ///     case-sensitive volume; in those unusual configurations containment is judged by the
    ///     platform convention rather than by observed behavior.
    ///     </para>
    /// </remarks>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    ///     Whether pattern matching ignores case, kept consistent with <see cref="PathComparison"/>.
    /// </summary>
    private static readonly bool IgnoreCase = PathComparison == StringComparison.OrdinalIgnoreCase;

    /// <summary>
    ///     The denied patterns, stored as an array so the exposed list cannot be mutated by a caller.
    /// </summary>
    private readonly string[] _denyPatterns;

    /// <summary>
    ///     The prefix a path must start with to be contained by <see cref="Root"/>, including
    ///     the trailing directory separator; <see langword="null"/> when unrestricted.
    /// </summary>
    /// <remarks>
    ///     Precomputed at construction because the trailing separator is what prevents a
    ///     sibling location with a shared prefix — <c>/allowed-root-evil</c> against an allowed
    ///     <c>/allowed-root</c> — from being treated as contained.
    /// </remarks>
    private readonly string? _containmentPrefix;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PathRule"/> class.
    /// </summary>
    /// <remarks>
    ///     Private so that <see cref="ReadOnly"/>, <see cref="ReadWrite"/> and
    ///     <see cref="Unrestricted"/> are the only construction paths; this keeps the access
    ///     level and the "rooted or unrestricted" choice explicit at every call site instead of
    ///     hiding either behind a nullable or defaulted constructor argument.
    /// </remarks>
    /// <param name="root">The already-resolved real root, or <see langword="null"/> when unrestricted.</param>
    /// <param name="access">The permission this grant carries.</param>
    /// <param name="denyPatterns">The validated denied patterns.</param>
    private PathRule(string? root, AccessLevel access, string[] denyPatterns)
    {
        Root = root;
        Access = access;
        _denyPatterns = denyPatterns;

        // Precompute the containment prefix so that Allows performs no string arithmetic, and
        // tolerate a root that is itself a volume root and already ends with a separator.
        _containmentPrefix = BuildContainmentPrefix(root);
    }

    /// <summary>
    ///     Builds the prefix a real path must start with to be contained by the given root.
    /// </summary>
    /// <remarks>
    ///     The trailing separator is the whole point of this helper: without it a sibling
    ///     location whose name merely starts with the root's name would be treated as
    ///     contained. A volume root such as <c>C:\</c> already ends with a separator, so
    ///     appending another would produce a prefix that matches nothing.
    /// </remarks>
    /// <param name="root">The resolved root, or <see langword="null"/> when unrestricted.</param>
    /// <returns>The containment prefix, or <see langword="null"/> when unrestricted.</returns>
    private static string? BuildContainmentPrefix(string? root)
    {
        if (root is null)
        {
            return null;
        }

        return root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
    }

    /// <summary>
    ///     Gets the real location this rule confines access to, or <see langword="null"/> when
    ///     the rule is unrestricted.
    /// </summary>
    /// <remarks>
    ///     Exposed so that callers and tests can see which location a rule actually resolved
    ///     to, which may differ from the location supplied at construction when that location
    ///     was relative or carried <c>.</c> or <c>..</c> segments.
    /// </remarks>
    public string? Root { get; }

    /// <summary>
    ///     Gets the permission this grant carries.
    /// </summary>
    /// <remarks>
    ///     The access level is permission only and carries no addressing meaning. It decides
    ///     whether a permitted location may be written as well as read; it never decides how a
    ///     relative path is resolved or which location a bare name refers to. A read is permitted
    ///     by a grant of either level, while a write is permitted only by a
    ///     <see cref="AccessLevel.ReadWrite"/> grant.
    /// </remarks>
    public AccessLevel Access { get; }

    /// <summary>
    ///     Gets the patterns whose match denies a path regardless of the rule's location.
    /// </summary>
    /// <remarks>
    ///     Exposed for inspection and diagnostics. The returned list is a read-only view over
    ///     internal storage and never changes for the lifetime of the rule.
    /// </remarks>
    public IReadOnlyList<string> DenyPatterns => _denyPatterns;

    /// <summary>
    ///     Creates an unrestricted grant of the given access level, permitting any location
    ///     except those matching its denied patterns.
    /// </summary>
    /// <remarks>
    ///     An unrestricted grant is how "may read anything on this machine" — or, less commonly,
    ///     "may read and write anything" — is expressed. The access level is explicit because an
    ///     unrestricted grant carries the same permission-only meaning as a rooted one: an
    ///     unrestricted <see cref="AccessLevel.ReadOnly"/> grant permits reads everywhere and
    ///     writes nowhere. An unrestricted grant is deliberately still able to carry denied
    ///     patterns, because the common case for wide read access is nevertheless to exclude
    ///     credential material.
    /// </remarks>
    /// <param name="access">The permission the grant carries.</param>
    /// <param name="denyPatterns">
    ///     Patterns denying any path whose file name or enclosing directory name matches.
    ///     May be <see langword="null"/> or empty; individual entries must be non-null and non-empty.
    /// </param>
    /// <returns>A new unrestricted <see cref="PathRule"/>.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when any entry of <paramref name="denyPatterns"/> is <see langword="null"/> or empty.
    /// </exception>
    public static PathRule Unrestricted(AccessLevel access, IEnumerable<string>? denyPatterns = null)
    {
        return new PathRule(null, access, ValidatePatterns(denyPatterns));
    }

    /// <summary>
    ///     Creates a read-only grant over the given location and its contents, except those
    ///     matching its denied patterns.
    /// </summary>
    /// <remarks>
    ///     The location is resolved to its real location at construction time, exactly as
    ///     <see cref="ReadWrite"/> resolves it. Granting read only says the location may be read
    ///     but never written; pairing a read-only grant over one location with a read-write grant
    ///     over another is how "read here, write there" is expressed.
    /// </remarks>
    /// <param name="root">
    ///     The location to grant read access to. Must be non-null and non-empty. Need not exist.
    /// </param>
    /// <param name="denyPatterns">
    ///     Patterns denying any path whose file name or enclosing directory name matches.
    ///     May be <see langword="null"/> or empty; individual entries must be non-null and non-empty.
    /// </param>
    /// <returns>A new read-only <see cref="PathRule"/>.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="root"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="root"/> is empty or is not a valid path, or when any
    ///     entry of <paramref name="denyPatterns"/> is <see langword="null"/> or empty.
    /// </exception>
    public static PathRule ReadOnly(string root, IEnumerable<string>? denyPatterns = null)
    {
        return Rooted(root, AccessLevel.ReadOnly, denyPatterns);
    }

    /// <summary>
    ///     Creates a read-write grant over the given location and its contents, except those
    ///     matching its denied patterns.
    /// </summary>
    /// <remarks>
    ///     The location is resolved to its real location at construction time. Resolving once,
    ///     up front, means every request is compared against one settled spelling of the
    ///     permitted location, and it keeps the per-request cost to a single resolution
    ///     of the candidate path.
    /// </remarks>
    /// <param name="root">
    ///     The location to grant read-write access to. Must be non-null and non-empty. Need not exist.
    /// </param>
    /// <param name="denyPatterns">
    ///     Patterns denying any path whose file name or enclosing directory name matches.
    ///     May be <see langword="null"/> or empty; individual entries must be non-null and non-empty.
    /// </param>
    /// <returns>A new read-write <see cref="PathRule"/>.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="root"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="root"/> is empty or is not a valid path, or when any
    ///     entry of <paramref name="denyPatterns"/> is <see langword="null"/> or empty.
    /// </exception>
    public static PathRule ReadWrite(string root, IEnumerable<string>? denyPatterns = null)
    {
        return Rooted(root, AccessLevel.ReadWrite, denyPatterns);
    }

    /// <summary>
    ///     Creates a grant confined to the given location, carrying the supplied access level.
    /// </summary>
    /// <remarks>
    ///     Shared by <see cref="ReadOnly"/> and <see cref="ReadWrite"/> so that resolving the
    ///     location and validating the patterns exists in exactly one place, with the two public
    ///     factories differing only in the access level they name.
    /// </remarks>
    /// <param name="root">The location to confine access to. Must be non-null and non-empty.</param>
    /// <param name="access">The permission the grant carries.</param>
    /// <param name="denyPatterns">The caller-supplied denied patterns; may be null.</param>
    /// <returns>A new rooted <see cref="PathRule"/>.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="root"/> is null or empty, or when any entry of
    ///     <paramref name="denyPatterns"/> is <see langword="null"/> or empty.
    /// </exception>
    private static PathRule Rooted(string root, AccessLevel access, IEnumerable<string>? denyPatterns)
    {
        // Reject a missing root before doing any work: a grant with no location is a
        // programming error, and silently treating it as unrestricted would widen access.
        ArgumentException.ThrowIfNullOrEmpty(root);

        // Resolve the root now so that every later containment test compares real location
        // against real location rather than string against string.
        return new PathRule(RealPathResolver.Resolve(root), access, ValidatePatterns(denyPatterns));
    }

    /// <summary>
    ///     Determines whether a path that has already been resolved to its real location is
    ///     permitted by this rule.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The argument must already be a real location. This method deliberately does not
    ///     resolve it: resolution belongs to the single decision point in
    ///     <see cref="PathPolicy"/>, so that direct access and directory enumeration cannot
    ///     drift apart into two different notions of containment.
    ///     </para>
    ///     <para>
    ///     Denied patterns are evaluated first and override the location entirely, so a denied
    ///     name inside an otherwise permitted location is still refused.
    ///     </para>
    ///     <para>
    ///     This method never throws for a policy reason — a refused path is reported by
    ///     returning <see langword="false"/>.
    ///     </para>
    /// </remarks>
    /// <param name="realPath">
    ///     The real, absolute location to test. Must be non-null and non-empty.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the rule permits <paramref name="realPath"/>;
    ///     otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="realPath"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="realPath"/> is an empty string.
    /// </exception>
    public bool Allows(string realPath)
    {
        // A missing path is a programming error in the caller, not a policy denial.
        ArgumentException.ThrowIfNullOrEmpty(realPath);

        // Denied patterns win over the location, so evaluate them first and stop on a match.
        if (MatchesDenyPattern(realPath))
        {
            return false;
        }

        // An unrestricted rule imposes no location constraint once the patterns have passed.
        if (_containmentPrefix is null)
        {
            return true;
        }

        // The root itself is permitted, as is anything strictly beneath it. Requiring the
        // separator for the "beneath" case is what stops a sibling sharing the root's name
        // prefix from being treated as contained.
        return string.Equals(realPath, Root, PathComparison) ||
               realPath.StartsWith(_containmentPrefix, PathComparison);
    }

    /// <summary>
    ///     Determines whether any denied pattern matches the file name or an enclosing
    ///     directory name of the given real location.
    /// </summary>
    /// <remarks>
    ///     Exposed to the assembly so that <see cref="PathPolicy"/> can report <em>why</em> a
    ///     path was refused without duplicating the matching logic, while keeping the public
    ///     surface of this type to the single <see cref="Allows"/> decision.
    /// </remarks>
    /// <param name="realPath">The real, absolute location to test. Must be non-null and non-empty.</param>
    /// <returns>
    ///     <see langword="true"/> when a denied pattern matches a segment of
    ///     <paramref name="realPath"/>; otherwise <see langword="false"/>.
    /// </returns>
    internal bool MatchesDenyPattern(string realPath)
    {
        // Nothing to match against is the common case; skip splitting the path entirely.
        if (_denyPatterns.Length == 0)
        {
            return false;
        }

        // Patterns apply to every name in the path, not just the last one, so that excluding a
        // directory such as ".git" also excludes everything inside it.
        var segments = realPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment => _denyPatterns.Any(
            pattern => FileSystemName.MatchesSimpleExpression(pattern, segment, IgnoreCase)));
    }

    /// <summary>
    ///     Produces a short human- and model-readable description of this grant, naming its
    ///     location and access level, for enumeration in a denial message.
    /// </summary>
    /// <remarks>
    ///     Used only to build the "permitted locations" list a denial enumerates, so that a
    ///     refused request is answered with the complete, truthful map of what is actually
    ///     permitted. The location is the resolved real root, or the word <c>anywhere</c> for an
    ///     unrestricted grant, followed by the access level. Deny patterns are omitted because
    ///     they narrow a location rather than name one, and listing them would make the map
    ///     harder to read without telling the model where it may go instead.
    /// </remarks>
    /// <returns>A description such as <c>C:\work (read-only)</c> or <c>anywhere (read-write)</c>.</returns>
    internal string Describe()
    {
        var level = Access == AccessLevel.ReadWrite ? "read-write" : "read-only";
        var location = Root ?? "anywhere";
        return $"{location} ({level})";
    }

    /// <summary>
    ///     Validates caller-supplied denied patterns and copies them into private storage.
    /// </summary>
    /// <remarks>
    ///     Copying defends against a caller mutating the collection after construction, which
    ///     would otherwise silently change an access rule after it had been granted. A null or
    ///     empty pattern is rejected because it can never express a meaningful exclusion and is
    ///     far more likely to be a configuration mistake than an intent.
    /// </remarks>
    /// <param name="denyPatterns">The caller-supplied patterns; may be <see langword="null"/>.</param>
    /// <returns>A private array of validated patterns, empty when none were supplied.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when any entry is <see langword="null"/> or an empty string.
    /// </exception>
    private static string[] ValidatePatterns(IEnumerable<string>? denyPatterns)
    {
        // Treat "no patterns supplied" and "an empty collection" identically.
        if (denyPatterns is null)
        {
            return [];
        }

        var patterns = denyPatterns.ToArray();

        // Validate every entry before the rule exists, so an invalid rule can never be used.
        if (patterns.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException(
                "Deny patterns must be non-null and non-empty.",
                nameof(denyPatterns));
        }

        return patterns;
    }
}

using System.IO.Enumeration;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     One path access rule: either unrestricted or confined to a single location, and in both
///     cases carrying its own set of denied patterns.
/// </summary>
/// <remarks>
///     <para>
///     A rule exists so that read access and write access can be described independently of one
///     another. A development assistant that may read widely across a machine but write only
///     into one working directory is expressed as two rules, not as one combined setting, and
///     each rule keeps its own denied patterns so that, for example, credential files can be
///     excluded from reads without affecting what may be written.
///     </para>
///     <para>
///     A rule is constructed only through <see cref="Unrestricted"/> or <see cref="Rooted"/>,
///     so a partially-configured rule cannot exist. Instances are immutable after construction
///     and are safe for concurrent use.
///     </para>
/// </remarks>
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
    ///     Private so that <see cref="Unrestricted"/> and <see cref="Rooted"/> are the only
    ///     construction paths; this keeps the "unrestricted or rooted" choice explicit at every
    ///     call site instead of hiding it behind a nullable constructor argument.
    /// </remarks>
    /// <param name="root">The already-resolved real root, or <see langword="null"/> when unrestricted.</param>
    /// <param name="denyPatterns">The validated denied patterns.</param>
    private PathRule(string? root, string[] denyPatterns)
    {
        Root = root;
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
    ///     was itself reached through a link.
    /// </remarks>
    public string? Root { get; }

    /// <summary>
    ///     Gets the patterns whose match denies a path regardless of the rule's location.
    /// </summary>
    /// <remarks>
    ///     Exposed for inspection and diagnostics. The returned list is a read-only view over
    ///     internal storage and never changes for the lifetime of the rule.
    /// </remarks>
    public IReadOnlyList<string> DenyPatterns => _denyPatterns;

    /// <summary>
    ///     Creates a rule that permits any location except those matching its denied patterns.
    /// </summary>
    /// <remarks>
    ///     An unrestricted rule is how "may read anything on this machine" is expressed. It is
    ///     deliberately still able to carry denied patterns, because the common case for wide
    ///     read access is nevertheless to exclude credential material.
    /// </remarks>
    /// <param name="denyPatterns">
    ///     Patterns denying any path whose file name or enclosing directory name matches.
    ///     May be <see langword="null"/> or empty; individual entries must be non-null and non-empty.
    /// </param>
    /// <returns>A new unrestricted <see cref="PathRule"/>.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when any entry of <paramref name="denyPatterns"/> is <see langword="null"/> or empty.
    /// </exception>
    public static PathRule Unrestricted(IEnumerable<string>? denyPatterns = null)
    {
        return new PathRule(null, ValidatePatterns(denyPatterns));
    }

    /// <summary>
    ///     Creates a rule that permits only the given location and its contents, except those
    ///     matching its denied patterns.
    /// </summary>
    /// <remarks>
    ///     The location is resolved to its real location at construction time. Resolving once,
    ///     up front, means a permitted location that is itself reached through a link still
    ///     permits its own contents, and it keeps the per-request cost to a single resolution
    ///     of the candidate path.
    /// </remarks>
    /// <param name="root">
    ///     The location to confine access to. Must be non-null and non-empty. Need not exist.
    /// </param>
    /// <param name="denyPatterns">
    ///     Patterns denying any path whose file name or enclosing directory name matches.
    ///     May be <see langword="null"/> or empty; individual entries must be non-null and non-empty.
    /// </param>
    /// <returns>A new rooted <see cref="PathRule"/>.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="root"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="root"/> is empty or is not a valid path, or when any
    ///     entry of <paramref name="denyPatterns"/> is <see langword="null"/> or empty.
    /// </exception>
    public static PathRule Rooted(string root, IEnumerable<string>? denyPatterns = null)
    {
        // Reject a missing root before doing any work: a rule with no location is a
        // programming error, and silently treating it as unrestricted would widen access.
        ArgumentException.ThrowIfNullOrEmpty(root);

        // Resolve the root now so that every later containment test compares real location
        // against real location rather than string against string.
        return new PathRule(RealPathResolver.Resolve(root), ValidatePatterns(denyPatterns));
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

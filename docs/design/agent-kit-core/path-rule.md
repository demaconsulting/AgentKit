## PathRule

![AgentKit Core Structure](AgentKitCoreView.svg)

The `PathRule` class expresses one access grant — unrestricted or confined to a single location —
carrying the access level that permission grants and its own denied patterns.

### Purpose

`PathRule` exists so that permitted locations and their permissions can be stated independently
of addressing. A grant answers only whether a real location may be read, or read and written. It
does not say how a relative path is resolved, and it does not make a bare name refer to the
grant's location; those addressing decisions belong to `PathPolicy.WorkingDirectory`.

Each grant carries an `AccessLevel`. `ReadOnly` permits reads and never writes. `ReadWrite`
permits both reads and writes, so write access cannot be granted without read access. A policy
that can read one location and write another is expressed as two grants of different levels.

Each grant also carries its own denied patterns, because the exclusions that matter for a wide
read grant, such as credential material or repository metadata, need not match the exclusions on a
separate write grant. Keeping permission, location and deny patterns together makes each grant
small enough to reason about at a glance, which matters for a type whose misconfiguration is a
security defect.

A rule is immutable after construction and is safe for concurrent use.

### Data Model

| Member               | Type                    | Description                                                    |
|----------------------|-------------------------|----------------------------------------------------------------|
| `Access`             | `AccessLevel`           | Permission this grant carries: read-only or read-write.        |
| `Root`               | `string?`               | Real location access is confined to; `null` when unrestricted. |
| `DenyPatterns`       | `IReadOnlyList<string>` | Patterns whose match denies a path regardless of location.     |
| `_denyPatterns`      | `string[]`              | Private copy of the validated patterns; never changes.         |
| `_containmentPrefix` | `string?`               | `Root` plus a trailing directory delimiter; null if unbounded. |
| `PathComparison`     | `StringComparison`      | Ordinal; ignores case on Windows and macOS only.               |
| `IgnoreCase`         | `bool`                  | Pattern-matching case sensitivity; matches `PathComparison`.   |

Invariants:

- `Access` is always an `AccessLevel` value and never carries addressing meaning.
- `Root` is always a real location when present: the constructor never stores a path that has not
  been resolved.
- `_containmentPrefix` is non-null exactly when `Root` is non-null.
- `_denyPatterns` contains no null or empty entry, and never changes after construction.

`AccessLevel` has exactly two values:

- **ReadOnly** — the location and its contents may be read but never written.
- **ReadWrite** — the location and its contents may be both read and written.

There is no append-only or create-only level. The two levels express the access an agent file tool
needs, and adding another level would add a state to reason about at every decision point without
enabling a capability the two do not already cover.

**Case sensitivity.** Containment comparison is ordinal and ignores case on Windows and macOS,
and is ordinal on Linux, matching the default file system semantics of each platform. Pattern
matching follows the same choice.

Always ignoring case would be **unsafe on Linux**. There, `/allowed/foo` and `/allowed/Foo` are
genuinely different directories, so a case-insensitive containment test would judge a path under
`/allowed/Foo` to be contained by a rule rooted at `/allowed/foo` and permit access to a location
the operator never granted. That is a **false allow** — a security defect, not an inconvenience.

Always comparing case-sensitively has the opposite failure. On Windows and macOS a
differently-cased spelling reaches the same file, so a case-sensitive comparison would refuse a
path the operator did grant. That is a **false denial**: an unnecessary refusal the caller can
see and correct. The asymmetry between the two — a false allow is a breach, a false denial is an
annoyance — is why the comparison follows the platform rather than picking one setting for
everywhere.

**Known limitation.** This is a platform heuristic, not a file system probe. NTFS supports
per-directory case sensitivity, and APFS can be configured case-sensitive, so a volume configured
against its platform default is judged by the default rather than by its actual behavior. On such
a volume the Linux reasoning above would apply on Windows or macOS, and the comparison would be
wrong in the unsafe direction. This is recorded as a known and accepted limitation; closing it
would require probing the containing volume at grant construction, which is an owner decision and
out of scope for the current design.

**The trailing directory delimiter.** `_containmentPrefix` is precomputed with a trailing
directory delimiter because that delimiter is what distinguishes containment from a shared name
prefix. Without it a grant rooted at `allowed-root` would treat `allowed-root-evil` — a directory
an attacker can simply create — as contained. A location that already ends with the delimiter,
such as a volume root, is left as is.

### Key Methods

#### Unrestricted(AccessLevel access, IEnumerable&lt;string&gt;? denyPatterns)

Creates a grant with no location constraint and the supplied access level. Validates and copies
the denied patterns.

An unrestricted `ReadOnly` grant permits reads anywhere except locations matching its denied
patterns and permits writes nowhere. An unrestricted `ReadWrite` grant permits both reads and
writes anywhere except matching locations. The access level is explicit so a wide grant never
silently becomes writable.

**Returns:** a grant whose `Root` is `null`.

**Throws:** `ArgumentException` when any pattern is null or empty.

#### ReadOnly(string root, IEnumerable&lt;string&gt;? denyPatterns)

Creates a read-only grant confined to one location.

**Preconditions:** `root` is non-null and non-empty. It need not exist.

**Algorithm:** reject a missing location, resolve `root` to its real location through
`RealPathResolver`, validate and copy the denied patterns.

Granting read only says the location may be read but never written. Pairing a read-only grant
over one location with a read-write grant over another is how "read here, write there" is
expressed.

**Throws:** `ArgumentNullException` for a null location; `ArgumentException` for an empty or
invalid location, or for a null or empty pattern.

#### ReadWrite(string root, IEnumerable&lt;string&gt;? denyPatterns)

Creates a read-write grant confined to one location.

**Preconditions:** `root` is non-null and non-empty. It need not exist.

**Algorithm:** reject a missing location, resolve `root` to its real location through
`RealPathResolver`, validate and copy the denied patterns.

Resolving the location once, at construction, means a permitted location that is itself reached
through a link still permits its own contents — operators routinely point an agent at a checkout
reached through a convenience alias. It also keeps the per-request cost to a single resolution of
the candidate path.

**Throws:** `ArgumentNullException` for a null location; `ArgumentException` for an empty or
invalid location, or for a null or empty pattern.

#### Allows(string realPath)

Decides whether a location already resolved to its real form is permitted by this grant.

**Preconditions:** `realPath` is non-null, non-empty, and **already resolved**. The method
deliberately does not resolve it; resolution belongs to the single decision point in `PathPolicy`
so that direct access and enumeration cannot drift into two different notions of containment.

**Algorithm:** denied patterns are evaluated first and override the location entirely; an
unrestricted grant then permits anything remaining; a rooted grant permits the root itself and
any location whose text begins with the containment prefix.

`Allows` does not inspect `Access`. It answers whether the grant covers the resolved location.
`PathPolicy` decides whether that covered location is enough for the requested operation by
consulting every grant for reads and only `ReadWrite` grants for writes.

**Postconditions:** never throws for a policy reason — a refusal is the return value `false`.

**Throws:** `ArgumentNullException` or `ArgumentException` for a missing argument only, which is
a programming error rather than a policy outcome.

#### MatchesDenyPattern(string realPath)

Internal helper reporting whether any denied pattern matches the file name or an enclosing
directory name. It is internal rather than private so that `PathPolicy` can state *why* a path
was refused without duplicating the matching logic, and internal rather than public so that the
type's decision surface remains the single `Allows` method.

Patterns are matched against every name in the path, not only the last, so that excluding a
directory such as `.git` also excludes everything inside it. Matching uses the base class
library's simple-expression matcher, which keeps the unit free of any package dependency.

#### Describe()

Internal helper producing the location-and-access text used when `PathPolicy` enumerates
permitted locations in a denial. A rooted grant is described as its resolved real root followed by
its access level, for example `C:\work (read-only)`. An unrestricted grant is described as
`anywhere (read-write)` or `anywhere (read-only)`.

Deny patterns are omitted from this short description because they narrow a location rather than
name one, and listing them would make the permission map harder to read without telling the model
where it may go instead.

#### ValidatePatterns(IEnumerable&lt;string&gt;? denyPatterns)

Private helper that copies the caller's patterns into private storage after rejecting null and
empty entries. The copy defends against a caller mutating the collection after the grant has been
constructed, which would otherwise change an access grant after the fact.

### Error Handling

| Condition                          | Handling                                       |
|------------------------------------|------------------------------------------------|
| Null location supplied to factory  | `ArgumentNullException` propagates             |
| Empty or invalid location          | `ArgumentException` propagates                 |
| Null or empty denied pattern       | `ArgumentException` propagates                 |
| Null or empty argument to `Allows` | `ArgumentNullException` or `ArgumentException` |
| Location outside the root          | Handled locally; `Allows` returns `false`      |
| Location matching a denied pattern | Handled locally; `Allows` returns `false`      |

Construction-time failures are propagated because a malformed grant is a defect in the calling
code, and accepting one would produce an access grant that looks configured while permitting more
than intended. Runtime refusals are never exceptions — they are the ordinary `false` result.

### Dependencies

- **RealPathResolver** — used by rooted factories to resolve the confined location once at
  construction; see *RealPathResolver Unit Design*.

### Callers

`PathRule` is a public API entry point: hosts construct grants to describe the access they are
granting. Within this system it is consumed by `PathPolicy`, which holds zero or more grants and
selects the applicable grants for each read or write decision.

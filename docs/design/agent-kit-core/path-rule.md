## PathRule

![AgentKit Core Structure](AgentKitCoreView.svg)

The `PathRule` class expresses one access rule — unrestricted or confined to a single location —
together with the patterns that rule denies.

### Purpose

`PathRule` exists so that read access and write access can be described independently of one
another. An assistant that must consult documentation and dependencies across a machine while
modifying only its working directory is expressed as two rules, not as one combined setting.

Each rule carries its own denied patterns, because the exclusions that matter for reading
(credential material, repository metadata) are not the same as those that matter for writing.
Keeping a rule to a single access direction also keeps it small enough to reason about at a
glance, which matters for a type whose misconfiguration is a security defect.

A rule is immutable after construction and is safe for concurrent use.

### Data Model

| Member               | Type                    | Description                                                    |
|----------------------|-------------------------|----------------------------------------------------------------|
| `Root`               | `string?`               | Real location access is confined to; `null` when unrestricted. |
| `DenyPatterns`       | `IReadOnlyList<string>` | Patterns whose match denies a path regardless of location.     |
| `_denyPatterns`      | `string[]`              | Private copy of the validated patterns; never changes.         |
| `_containmentPrefix` | `string?`               | `Root` plus a trailing separator; `null` when unrestricted.    |
| `PathComparison`     | `StringComparison`      | Ordinal, ignoring case on Windows and macOS.                   |
| `IgnoreCase`         | `bool`                  | Pattern-matching case sensitivity; matches `PathComparison`.   |

Invariants:

- `Root` is always a real location: the constructor never stores a path that has not been
  resolved.
- `_containmentPrefix` is non-null exactly when `Root` is non-null.
- `_denyPatterns` contains no null or empty entry, and never changes after construction.

**Case sensitivity.** Comparison is ordinal and ignores case on Windows and macOS, and is ordinal
on Linux, matching the default file system semantics of each platform. On a case-insensitive file
system a case-sensitive comparison would let a differently-cased spelling of a location pass
containment while still reaching the same file; on a case-sensitive file system an
insensitive comparison would refuse two genuinely different locations as if they were one.

**The trailing separator.** `_containmentPrefix` is precomputed with a trailing separator because
that separator is what distinguishes containment from a shared name prefix. Without it a rule
rooted at `allowed-root` would treat `allowed-root-evil` — a directory an attacker can simply
create — as contained. A location that already ends with a separator, such as a volume root, is
left as is.

### Key Methods

#### Unrestricted(IEnumerable&lt;string&gt;? denyPatterns)

Creates a rule with no location constraint. Validates and copies the denied patterns.

**Returns:** a rule whose `Root` is `null`.

**Throws:** `ArgumentException` when any pattern is null or empty.

#### Rooted(string root, IEnumerable&lt;string&gt;? denyPatterns)

Creates a rule confined to one location.

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

Decides whether a location already resolved to its real form is permitted.

**Preconditions:** `realPath` is non-null, non-empty, and **already resolved**. The method
deliberately does not resolve it; resolution belongs to the single decision point in `PathPolicy`
so that direct access and enumeration cannot drift into two different notions of containment.

**Algorithm:** denied patterns are evaluated first and override the location entirely; an
unrestricted rule then permits anything remaining; a rooted rule permits the root itself and any
location whose text begins with the containment prefix.

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

#### ValidatePatterns(IEnumerable&lt;string&gt;? denyPatterns)

Private helper that copies the caller's patterns into private storage after rejecting null and
empty entries. The copy defends against a caller mutating the collection after the rule has been
granted, which would otherwise change an access rule after the fact.

### Error Handling

| Condition                          | Handling                                       |
|------------------------------------|------------------------------------------------|
| Null location supplied to `Rooted` | `ArgumentNullException` propagates             |
| Empty or invalid location          | `ArgumentException` propagates                 |
| Null or empty denied pattern       | `ArgumentException` propagates                 |
| Null or empty argument to `Allows` | `ArgumentNullException` or `ArgumentException` |
| Location outside the root          | Handled locally; `Allows` returns `false`      |
| Location matching a denied pattern | Handled locally; `Allows` returns `false`      |

Construction-time failures are propagated because a malformed rule is a defect in the calling
code, and accepting one would produce an access rule that looks configured while permitting more
than intended. Runtime refusals are never exceptions — they are the ordinary `false` result.

### Dependencies

- **RealPathResolver** — used by `Rooted` to resolve the confined location once at construction;
  see *RealPathResolver Unit Design*.

### Callers

`PathRule` is a public API entry point: hosts construct rules to describe the access they are
granting. Within this system it is consumed by `PathPolicy`, which holds one rule for reading and
one for writing.

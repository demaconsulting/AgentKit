## PathPolicy

![AgentKit Core Structure](AgentKitCoreView.svg)

The `PathPolicy` class pairs an independent read rule and write rule, carries the workspace
location a relative request is interpreted against, and provides the single containment decision
used by both direct path access and directory enumeration.

### Purpose

`PathPolicy` exists so that a tool never has to decide for itself whether a path is acceptable.
It resolves a requested path to its real location and consults exactly one rule — the read rule
for a read, the write rule for a write — so the two access directions stay independent.

Five properties shape the whole design of this unit:

- **A relative path means a path relative to the workspace.** A model states the paths a person
  states: it asks for `notes.txt`, not for the absolute location of `notes.txt`. Resolving that
  against the location the host process happens to be running from refuses every legitimate
  request while presenting, from the outside, as a containment decision — an agent was observed
  to be refused an existing file nine consecutive times before abandoning its task. The base
  belongs to the policy rather than to a rule because reads may be unrestricted while writes are
  confined, and both directions must read one name the same way.
- **An omitted path means the workspace.** A model that has not yet seen the workspace has no
  name to give, and what it sends instead is a question the policy can answer.
- **A refusal is a return value, not an exception.** An exception raised while a model is calling
  a tool ends the agent's turn and strands it with no way forward. A returned denial lets the
  model read the reason and choose a permitted path instead, which turns a refusal into a
  recoverable step. No path a caller supplies — including none at all — is an exception; only
  programming errors such as a missing rule are.
- **Enumeration and access share one decision.** See _Design Constraints_ below; this is an
  invariant, not an implementation detail.
- **Denial messages guide recovery and carry no host detail.** They are fixed constants with no
  interpolation, because the message is handed to a model and the resulting transcript leaves the
  process. Each nevertheless states the form a permitted request takes, because a model told only
  "no" retries the same path until it gives up.

A policy is immutable after construction and is safe for concurrent use.

### Data Model

| Member                 | Type                 | Description                                                          |
|------------------------|----------------------|----------------------------------------------------------------------|
| `ReadRule`             | `PathRule`           | The rule governing read access. Never null.                          |
| `WriteRule`            | `PathRule`           | The rule governing write access. Never null.                         |
| `Limits`               | `ToolLimits`         | Ceilings every tool governed by this policy observes. Never null.    |
| `BaseDirectory`        | `string`             | The real location a relative request resolves against. Never null.   |
| `RecoveryGuidance`     | `const string`       | The fixed sentence stating the form a permitted request takes.       |
| `ReadLocationDenied`   | `const string`       | Message used when a read resolves outside the permitted location.    |
| `WriteLocationDenied`  | `const string`       | Message used when a write resolves outside the permitted location.   |
| `PatternDenied`        | `const string`       | Message used when a request matches a denied pattern.                |
| `UnresolvableDenied`   | `const string`       | Message used when the real location cannot be determined.            |
| `BasePath`             | `const string`       | The token an omitted or placeholder request is rewritten to.         |
| `PlaceholderPaths`     | `string[]`           | The literal words a model sends in place of an absent path.          |
| `RecursiveEnumeration` | `EnumerationOptions` | Recursive listing that ignores inaccessible entries.                 |

Invariants:

- Both rules are non-null for the lifetime of the policy; there is no way to construct an
  instance without them.
- Limits are non-null for the lifetime of the policy.
- `BaseDirectory` is non-null, absolute and already resolved to its real location.
- Every denial message is a compile-time constant containing no path, no root, no host detail and
  no directory separator.

### Key Methods

#### PathPolicy(PathRule readRule, PathRule writeRule)

There is no default, no-argument or single-rule overload, because an unguarded policy must be
unrepresentable: a policy that can exist without its rules invites a caller to create one and
forget to restrict it, producing an agent with unbounded file access that nevertheless looks
governed.

This overload delegates to the four-argument constructor with `ToolLimits.Default` and no stated
base, so a host that has no opinion about resource ceilings still receives bounded ones and a host
that states no base still receives a sensible one.

**Throws:** `ArgumentNullException` when either rule is null.

#### PathPolicy(PathRule readRule, PathRule writeRule, ToolLimits limits, string? baseDirectory)

The same contract, with the resource ceilings stated explicitly and, optionally, the location a
relative request is interpreted against. The first three arguments are required; an absent set of
ceilings is the same kind of programming error as an absent rule, because "unbounded" is not a
sensible default.

**The base defaults to `readRule.Root`, failing that `writeRule.Root`, failing that
`Environment.CurrentDirectory`,** and is resolved once here through `RealPathResolver`. The
default is chosen so that a policy built the ordinary rooted way behaves as the host already
believes it asked for rather than silently preserving a defect; the process working directory is
reached only when neither rule names a location, in which case there is nothing else for a
relative path to be relative to.

**Throws:** `ArgumentNullException` when any of the first three arguments is null;
`ArgumentException` when a supplied base is empty or is not a valid path.

#### ForWorkspace(string root) and ForWorkspace(string root, ToolLimits limits)

Creates a policy whose read rule, write rule and base are all `root`.

This exists because the general constructor makes the wrong configuration the shorter one to
write: a caller who supplies two rooted rules and no base has said nothing about how a bare file
name should be read, and a caller who supplies a base that disagrees with the rules has built a
policy that denies its own relative requests. Naming the workspace once removes both mistakes.

Naming a workspace narrows how a bare name is read; it neither widens nor narrows what is
permitted, and an absolute request remains expressible and remains subject to containment.

**Throws:** `ArgumentNullException` / `ArgumentException` for a missing or empty workspace;
`ArgumentNullException` for missing ceilings on the two-argument overload.

#### TryResolveRead(string? path, out string? realPath, out string? denialMessage)

Resolves a path for reading and confirms the read rule permits it.

**Preconditions:** none. `path` may be absolute, relative, or absent; it need not exist. A
relative path is interpreted against `BaseDirectory`, and an omitted, empty, whitespace or
placeholder path denotes `BaseDirectory` itself.

**Postconditions:** on success the method returns `true`, `realPath` is the real location the
caller may read, and `denialMessage` is `null`. On refusal it returns `false`, `realPath` is
`null`, and `denialMessage` is one of the fixed constants. No path a caller supplies produces an
exception.

This is the single read decision in the library, and enumeration filters through this very
method.

#### TryResolveWrite(string? path, out string? realPath, out string? denialMessage)

The same contract for writing, consulting `WriteRule` alone. A path that is readable is not
thereby writable; that independence is what makes a read-wide, write-narrow configuration
meaningful. The interpretation of a relative or omitted path is identical to the read case,
because an agent that could read a file under one name and not write it back under the same name
would be unusable.

#### EnumerateFiles(string? directory, string searchPattern)

Lists the real locations of the files beneath a directory that this policy permits the caller to
read.

**Algorithm:** the directory argument is itself subjected to `TryResolveRead`; a refused
directory yields an empty sequence. Candidates are then listed recursively and every candidate is
passed through `TryResolveRead`, with only permitted candidates yielded, as their real locations.

**Postconditions:** never throws for a policy or file system reason. A refused directory, a
missing directory or an unreadable tree all yield an empty sequence.

**Parameter naming.** The first parameter is named `directory` rather than describing a location
relative to a root, because a read rule may be unrestricted, in which case there is no root for a
path to be relative to. The argument is interpreted exactly as `TryResolveRead` interprets a path,
so an omitted directory enumerates `BaseDirectory`. Only `searchPattern` is validated as a
programming error, because a missing pattern comes from the tool while a missing directory comes
from the model.

#### ListCandidates(string realDirectory, string searchPattern)

Private helper producing the raw, unfiltered candidates and converting any file system failure
into an empty result. The candidates are materialized rather than streamed because a lazily
enumerated listing raises its exceptions during iteration, where they would escape past the
guard; materializing inside this guarded helper keeps the promise that enumeration never throws.

#### IsResolutionFailure(Exception exception)

Private helper classifying an exception as "the path could not be determined or reached" rather
than "the software is defective". The set is enumerated explicitly rather than catching every
exception, so that genuine defects still surface during development instead of being quietly
reported to a model as a denied path.

#### TryResolve(string? path, PathRule rule, string locationDenialMessage, out string? realPath, out string? denialMessage)

Private helper through which both public entry points funnel, so that resolution, failure
handling and denial-message selection exist in exactly one place. Sharing this implementation is
what guarantees reads and writes differ only in which rule they consult.

**Algorithm**, in this order and no other — see _Design Constraints_:

1. Normalize the request, rewriting absence and the recognized placeholders to the
   current-directory token.
2. When the result is not rooted, combine it with `BaseDirectory`.
3. Resolve the now-absolute path through the unchanged per-component reparse-point walk.
4. Apply `rule.Allows` to the real location.

Steps 1 through 3 all run inside the guarded region, because all three operate on text a model
supplied and none of them may escape as an exception.

#### Normalize(string?)

Private helper mapping `null`, an empty string, whitespace, and the entries of `PlaceholderPaths`
— compared case-insensitively after trimming — to the current-directory token, and returning
every other request unchanged. Recognizing the placeholders here rather than in each tool is what
keeps every entry point in agreement about what "no path" means.

### Error Handling

| Condition                                       | Handling                                                    |
|-------------------------------------------------|-------------------------------------------------------------|
| Null or empty rule at construction              | `ArgumentNullException` propagates                          |
| Null limits at construction                     | `ArgumentNullException` propagates                          |
| Invalid base directory at construction          | `ArgumentException` propagates                              |
| Missing or empty workspace at `ForWorkspace`    | `ArgumentNullException` / `ArgumentException` propagates    |
| Null or empty `searchPattern`                   | `ArgumentNullException` / `ArgumentException` propagates    |
| Omitted or placeholder `path` / `directory`     | Handled locally; denotes the base directory                 |
| Real location cannot be determined              | Handled locally; denial with the unresolvable message       |
| Location outside the permitted location         | Handled locally; denial with the read or write message      |
| Location matches a denied pattern               | Handled locally; denial with the pattern message            |
| Directory cannot be listed                      | Handled locally; empty sequence                             |

The dividing line is deliberate and is stated here so a reviewer can check it: **programming
errors propagate; anything a model can produce is returned.** A path is the one argument a model
controls, so no spelling of a path — absent, empty, malformed or hostile — is an exception.
Resolution failures are caught by type — argument, I/O, unauthorized access, unsupported operation
and security exceptions — and converted into a denial, because denying what cannot be understood
is the fail-safe reading for input a model controls.

### Design Constraints

**The four resolution steps must stay in this order.** Normalize, then make absolute against the
base, then run the per-component reparse-point walk, then test containment. Making the path
absolute before the walk rather than after it is what ensures a relative path that reaches outside
through a link is refused exactly as an absolute one is: the walk sees the same fully-qualified
path either way. **The walk itself must not be weakened or replaced** — a leaf-only or
deepest-existing-ancestor resolution has been proven not to detect an escape; see
_RealPathResolver Unit Design_. `RealPathResolver` is deliberately untouched by the introduction
of the base, and its regression tests must continue to pass unmodified.

**Enumeration and access must remain one decision.** Recursive enumeration provided by the
operating system follows directory junctions and symbolic links and will surface files outside
the permitted location; this was established experimentally. `EnumerateFiles` therefore filters
every candidate through `TryResolveRead` — the same method used for direct access, not a parallel
re-implementation of containment. A future change must not introduce a second containment check
for listings: a listing that is broader than what access permits discloses files the operator
withheld.

**Recovery guidance must contain no directory separator.** "Contains no separator" is the usable
test for "contains no host location", and it is asserted by several scenarios. The guidance
therefore names a bare file name as its example rather than a nested path; an example containing
a slash would silently retire that check while looking like an improvement.

**How a denial is expressed.** A refusal is reported as `false` with an `out string?
denialMessage`. This introduces no new public type, so nothing has to be renamed or deleted when
richer tool results arrive. Alternatives were rejected: a bespoke decision struct would be a
second result type that later code must map from; returning the real path or `null` loses the
reason entirely, leaving the redaction requirement with nowhere to live.

**Resource ceilings ride with the policy.** `ToolLimits` — the ceilings on bytes read, characters
returned to the model, bytes of binary content returned and attachments per turn — is carried as
a `Limits` property rather than being passed per call. A tool therefore receives one object and
cannot end up observing a different budget from its neighbor, which is what "every pack observes
the same budget" means in practice. The four-argument constructor states the ceilings
explicitly; the two-argument constructor delegates to it with `ToolLimits.Default`, so the change
is additive and no existing call site is affected. See _ToolLimits Unit Design_ for the reasoning
behind the values.

### Dependencies

- **RealPathResolver** — used to resolve every requested path before a rule is consulted; see
  _RealPathResolver Unit Design_.
- **PathRule** — holds the read and write decisions; see _PathRule Unit Design_.
- **ToolLimits** — the resource ceilings carried with the policy; see _ToolLimits Unit Design_.

### Callers

`PathPolicy` is a public API entry point: a host constructs one and hands it to the tools it
builds. Within this system nothing else calls it; the tool units that consume it are introduced
in later increments.

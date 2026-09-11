## PathPolicy

![AgentKit Core Structure](AgentKitCoreView.svg)

The `PathPolicy` class pairs an independent read rule and write rule and provides the single
containment decision used by both direct path access and directory enumeration.

### Purpose

`PathPolicy` exists so that a tool never has to decide for itself whether a path is acceptable.
It resolves a requested path to its real location and consults exactly one rule — the read rule
for a read, the write rule for a write — so the two access directions stay independent.

Three properties shape the whole design of this unit:

- **A refusal is a return value, not an exception.** An exception raised while a model is calling
  a tool ends the agent's turn and strands it with no way forward. A returned denial lets the
  model read the reason and choose a permitted path instead, which turns a refusal into a
  recoverable step. Only programming errors — a missing rule, a missing path — are exceptions.
- **Enumeration and access share one decision.** See _Design Constraints_ below; this is an
  invariant, not an implementation detail.
- **Denial messages carry no host detail.** They are fixed constants with no interpolation,
  because the message is handed to a model and the resulting transcript leaves the process.

A policy is immutable after construction and is safe for concurrent use.

### Data Model

| Member                 | Type                 | Description                                                          |
|------------------------|----------------------|----------------------------------------------------------------------|
| `ReadRule`             | `PathRule`           | The rule governing read access. Never null.                          |
| `WriteRule`            | `PathRule`           | The rule governing write access. Never null.                         |
| `Limits`               | `ToolLimits`         | Ceilings every tool governed by this policy observes. Never null.    |
| `ReadLocationDenied`   | `const string`       | Message used when a read resolves outside the permitted location.    |
| `WriteLocationDenied`  | `const string`       | Message used when a write resolves outside the permitted location.   |
| `PatternDenied`        | `const string`       | Message used when a request matches a denied pattern.                |
| `UnresolvableDenied`   | `const string`       | Message used when the real location cannot be determined.            |
| `RecursiveEnumeration` | `EnumerationOptions` | Recursive listing that ignores inaccessible entries.                 |

Invariants:

- Both rules are non-null for the lifetime of the policy; there is no way to construct an
  instance without them.
- Limits are non-null for the lifetime of the policy.
- Every denial message is a compile-time constant containing no path, no root and no host detail.

### Key Methods

#### PathPolicy(PathRule readRule, PathRule writeRule)

There is no default, no-argument or single-rule overload, because an unguarded policy must be
unrepresentable: a policy that can exist without its rules invites a caller to create one and
forget to restrict it, producing an agent with unbounded file access that nevertheless looks
governed.

This overload delegates to the three-argument constructor with `ToolLimits.Default`, so a host
that has no opinion about resource ceilings still receives bounded ones.

**Throws:** `ArgumentNullException` when either rule is null.

#### PathPolicy(PathRule readRule, PathRule writeRule, ToolLimits limits)

The same contract, with the resource ceilings stated explicitly. All three arguments are
required; an absent set of ceilings is the same kind of programming error as an absent rule,
because "unbounded" is not a sensible default.

**Throws:** `ArgumentNullException` when any argument is null.

#### TryResolveRead(string path, out string? realPath, out string? denialMessage)

Resolves a path for reading and confirms the read rule permits it.

**Preconditions:** `path` is non-null and non-empty; it may be absolute or relative to the
current working directory, and need not exist.

**Postconditions:** on success the method returns `true`, `realPath` is the real location the
caller may read, and `denialMessage` is `null`. On refusal it returns `false`, `realPath` is
`null`, and `denialMessage` is one of the fixed constants.

This is the single read decision in the library, and enumeration filters through this very
method.

#### TryResolveWrite(string path, out string? realPath, out string? denialMessage)

The same contract for writing, consulting `WriteRule` alone. A path that is readable is not
thereby writable; that independence is what makes a read-wide, write-narrow configuration
meaningful.

#### EnumerateFiles(string directory, string searchPattern)

Lists the real locations of the files beneath a directory that this policy permits the caller to
read.

**Algorithm:** the directory argument is itself subjected to `TryResolveRead`; a refused
directory yields an empty sequence. Candidates are then listed recursively and every candidate is
passed through `TryResolveRead`, with only permitted candidates yielded, as their real locations.

**Postconditions:** never throws for a policy or file system reason. A refused directory, a
missing directory or an unreadable tree all yield an empty sequence.

**Parameter naming.** The first parameter is named `directory` rather than describing a location
relative to a root, because a read rule may be unrestricted, in which case there is no root for a
path to be relative to. The argument accepts an absolute path or one relative to the process
working directory.

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

#### TryResolve(string path, PathRule rule, string locationDenialMessage, out string? realPath, out string? denialMessage)

Private helper through which both public entry points funnel, so that resolution, failure
handling and denial-message selection exist in exactly one place. Sharing this implementation is
what guarantees reads and writes differ only in which rule they consult.

### Error Handling

| Condition                                       | Handling                                                    |
|-------------------------------------------------|-------------------------------------------------------------|
| Null or empty rule at construction              | `ArgumentNullException` propagates                          |
| Null limits at construction                     | `ArgumentNullException` propagates                          |
| Null or empty `path` / `directory` / pattern    | `ArgumentNullException` / `ArgumentException` propagates    |
| Real location cannot be determined              | Handled locally; denial with the unresolvable message       |
| Location outside the permitted location         | Handled locally; denial with the read or write message      |
| Location matches a denied pattern               | Handled locally; denial with the pattern message            |
| Directory cannot be listed                      | Handled locally; empty sequence                             |

The dividing line is deliberate and is stated here so a reviewer can check it: **programming
errors propagate; runtime policy outcomes are returned.** Resolution failures are caught by type
— argument, I/O, unauthorized access, unsupported operation and security exceptions — and
converted into a denial, because denying what cannot be understood is the fail-safe reading for
input a model controls.

### Design Constraints

**Enumeration and access must remain one decision.** Recursive enumeration provided by the
operating system follows directory junctions and symbolic links and will surface files outside
the permitted location; this was established experimentally. `EnumerateFiles` therefore filters
every candidate through `TryResolveRead` — the same method used for direct access, not a parallel
re-implementation of containment. A future change must not introduce a second containment check
for listings: a listing that is broader than what access permits discloses files the operator
withheld.

**How a denial is expressed.** A refusal is reported as `false` with an `out string?
denialMessage`. This introduces no new public type, so nothing has to be renamed or deleted when
richer tool results arrive. Alternatives were rejected: a bespoke decision struct would be a
second result type that later code must map from; returning the real path or `null` loses the
reason entirely, leaving the redaction requirement with nowhere to live.

**Resource ceilings ride with the policy.** `ToolLimits` — the ceilings on bytes read, characters
returned to the model, bytes of binary content returned and attachments per turn — is carried as
a `Limits` property rather than being passed per call. A tool therefore receives one object and
cannot end up observing a different budget from its neighbor, which is what "every pack observes
the same budget" means in practice. The three-argument constructor states the ceilings
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

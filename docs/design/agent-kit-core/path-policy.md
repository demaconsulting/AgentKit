## PathPolicy

![AgentKit Core Structure](AgentKitCoreView.svg)

The `PathPolicy` class holds the one working directory relative paths are anchored to and the
zero-or-more access grants that permit locations, keeping addressing and permission orthogonal.
It provides the single containment decision used by both direct path access and directory
enumeration.

### Purpose

`PathPolicy` exists so that a tool never has to decide for itself whether a path is acceptable.
It resolves a requested path to its real location, consults the grants the application supplied,
and returns either the permitted real location or a denial the model can act on.

Seven properties shape the whole design of this unit:

- **Working directory and grants are orthogonal.** The working directory is the single anchor a
  relative path resolves against, and nothing else. It carries zero permission. Grants are the
  permitted locations, each carrying an `AccessLevel`, and nothing else. The working directory is
  often also granted, but need not be; the application grants it whatever permission it should
  have, exactly like any other location.
- **A missing working directory is a programming error.** There is no fallback to
  `Environment.CurrentDirectory`. The process working directory is a process-global value any
  code can mutate and every agent in the process shares, which is unsafe when one application
  runs several agents or sub-agents with different anchors.
- **An ungranted working directory is coherent.** A bare relative name still resolves beneath the
  working directory, and is then correctly denied if no grant permits that location. The denial
  names the locations that are permitted, so the model can re-address the request.
- **Permission is grant-based.** A read is permitted when any grant allows the resolved path. A
  write is permitted only when a `ReadWrite` grant allows it. `ReadWrite` therefore implies read,
  a read-only grant never authorizes a write, and "read here, write there" is expressed as two
  grants of different levels.
- **A refusal is a return value, not an exception.** An exception raised while a model is calling
  a tool ends the agent's turn and strands it with no way forward. A returned denial lets the
  model read the reason and choose a permitted path instead, which turns a refusal into a
  recoverable step. No path a caller supplies — including none at all — is an exception; only
  programming errors such as a missing grant collection are.
- **Denials disclose the truthful permission map.** A denial states, in order, what was asked for,
  how a relative request was interpreted when it was joined to the working directory, and the
  permitted locations with their access levels. If no locations are permitted, it says so. This
  deliberately discloses host locations because telling a confined model where it may work is
  more useful than concealing paths it is already confined to.
- **Tool output mirrors the caller's dialect.** A tool result is a demonstration the model
  imitates. Absolute input produces absolute output. Relative input, including the discovery case
  where no path was supplied, may produce relative output only when the working directory itself
  is granted and the result lies within it; otherwise the result is absolute so a wrong target is
  visible.

**Transition hazard.** An application that starts with a single granted working directory gets the
relative dialect: listings report bare relative names and the model imitates them. If the
application later adds a second granted location, paths under that second location switch to the
absolute dialect, and nothing else warns the application that the move happened.

A policy is immutable after construction and is safe for concurrent use.

### Data Model

| Member | Type | Description |
| --- | --- | --- |
| `WorkingDirectory` | `string` | Real anchor a relative request resolves against; grants nothing. |
| `Grants` | `IReadOnlyList<PathRule>` | Permitted locations, each carrying its own access level. |
| `Limits` | `ToolLimits` | Ceilings every tool governed by this policy observes. |
| `WorkingDirectoryIsGranted` | `bool` | Whether the anchor is itself permitted for reading. |
| `PlaceholderPaths` | `string[]` | Literal words a model sends in place of an absent path. |
| `RecursiveEnumeration` | `EnumerationOptions` | Recursive listing that ignores inaccessible entries. |
| `_grants` | `PathRule[]` | Private copy of the grant collection; never changes. |
| `_workingDirectoryPrefix` | `string` | Anchor with a trailing directory delimiter for containment. |

Invariants:

- `WorkingDirectory` is non-null, non-empty, absolute and already resolved to its real location.
- `WorkingDirectory` is only an addressing anchor. It never grants read or write access.
- `Grants` is non-null for the lifetime of the policy, may be empty, and contains no null entry.
- Every grant is permission only. It never changes how a relative path is resolved.
- Limits are non-null for the lifetime of the policy.
- `WorkingDirectoryIsGranted` is true only when some grant allows the working directory to be
  read.

### Key Methods

#### PathPolicy(string workingDirectory, IEnumerable&lt;PathRule&gt; grants)

Creates a policy with the library's documented resource ceilings.

The working directory is required, resolved once through `RealPathResolver`, and carries no
permission of its own. The grant collection may be empty, producing a valid policy that permits
nothing. This is useful for a fully confined agent that must receive denials with recovery
information rather than accidentally inheriting process-wide file access.

A single granted working directory is written explicitly as
`new PathPolicy(root, [PathRule.ReadWrite(root)])`. That spelling is intentionally longer than a
factory because it makes the permission grant visible at the call site.

**Throws:** `ArgumentException` when `workingDirectory` is missing or empty;
`ArgumentNullException` when `grants` is null or contains a null entry.

#### PathPolicy(string workingDirectory, IEnumerable&lt;PathRule&gt; grants, ToolLimits limits)

The same contract, with resource ceilings stated explicitly. The ceilings ride with the policy
rather than being passed per call so that every tool a host governs observes one budget.

This constructor is also where the dialect transition is called out: adding a second granted
location changes how paths under that second location are reported. The second location cannot be
named relative to the working directory, so tool output for those paths is absolute even when the
single-grant configuration previously produced relative names.

**Throws:** `ArgumentException` when `workingDirectory` is missing or empty;
`ArgumentNullException` when `grants` or `limits` is null, or when `grants` contains a null entry.

#### TryResolveRead(string? path, out string? realPath, out string? denialMessage)

Resolves a path for reading and confirms that some grant permits it.

**Preconditions:** none. `path` may be absolute, relative, or absent; it need not exist. A
relative path is interpreted against `WorkingDirectory`. An omitted, empty, whitespace or
placeholder path denotes the working directory itself.

**Postconditions:** on success the method returns `true`, `realPath` is the real location the
caller may read, and `denialMessage` is `null`. On refusal it returns `false`, `realPath` is
`null`, and `denialMessage` states the reason, the request, any working-directory interpretation,
and the permitted locations. No path a caller supplies produces an exception.

This is the single read decision in the library, and enumeration filters through this very method.

#### TryResolveWrite(string? path, out string? realPath, out string? denialMessage)

The same contract for writing, consulting only grants whose access level is `ReadWrite`. A path
that is readable is not thereby writable; a read-only grant never authorizes a write. The
interpretation of a relative or omitted path is identical to the read case, because an agent that
could read a file under one name and not write it back under the same name would be unusable.

Denials for write requests enumerate all grants with their access levels, including read-only
locations, so the model can distinguish "wrong place" from "right place, wrong permission."

#### EnumerateFiles(string? directory, string searchPattern)

Lists the real locations of the files beneath a directory that this policy permits the caller to
read.

**Algorithm:** the directory argument is itself subjected to `TryResolveRead`; a refused
directory yields an empty sequence. Candidates are then listed recursively and every candidate is
passed through `TryResolveRead`, with only permitted candidates yielded, as their real locations.

**Postconditions:** never throws for a policy or file system reason. A refused directory, a
missing directory or an unreadable tree all yield an empty sequence.

**Parameter naming.** The first parameter is named `directory` because it is interpreted exactly
as `TryResolveRead` interprets a path. An omitted directory denotes the working directory for this
method; tools that need a discovery listing use `IsDiscoveryRequest` and `DiscoveryRoots` to list
all locations that should be shown to the model.

#### DiscoveryRoots()

Lists the distinct locations a discovery listing should enumerate, one per grant. A rooted grant
contributes its resolved location. An unrestricted grant contributes the working directory,
because there is no bounded location to walk. Duplicates are removed so overlapping grants over
the same location produce one block.

This helper is public because a package boundary already crosses here: the built-in `file_list`
tool consumes it from the separate Tools package to group discovery output under one absolute header per
location, and any third-party tool pack sits in the same position and needs the same grouping. Tools is
only the first consumer, not a privileged one. Exposing where discovery begins does not change the access
decision: each listed file still flows through `TryResolveRead`.

#### EmitRelative(string resultRealPath, string? callerInput)

Determines whether a tool should report a result relative to the working directory or as an
absolute path.

A result is emitted relative only when all three conditions hold: the caller did not supply an
absolute path, the working directory is itself granted, and the result lies within the working
directory. A request with no path at all establishes the relative-eligible dialect, so a discovery
listing in a single granted working directory can report bare relative names. A result outside the
anchor is always absolute because a relative name cannot truthfully name it.

#### IsDiscoveryRequest(string? directory)

Reports whether a directory argument means "no directory": null, empty, whitespace, or a
recognized placeholder. It is public because a package boundary already crosses here: the built-in
`file_list` tool consumes it from the separate Tools package so a tool and the policy never
disagree about what "no argument" means, and any third-party tool pack — the first consumer being no
more privileged than the rest — needs the same agreement.

#### ListCandidates(string realDirectory, string searchPattern)

Private helper producing the raw, unfiltered candidates and converting any file system failure
into an empty result. The candidates are materialized rather than streamed because a lazily
enumerated listing raises its exceptions during iteration, where they would escape past the
guard; materializing inside this guarded helper keeps the promise that enumeration never throws.

#### IsResolutionFailure(Exception exception)

Private helper classifying an exception as "the path could not be determined or reached" rather
than "the software is defective." The set is enumerated explicitly rather than catching every
exception, so that genuine defects still surface during development instead of being quietly
reported to a model as a denied path.

#### TryResolve(string? path, bool requireWrite, out string? realPath, out string? denialMessage)

Private helper through which both public entry points funnel, so that resolution, failure
handling and denial-message construction exist in exactly one place. Sharing this implementation
is what guarantees reads and writes differ only in which grants they consult.

**Algorithm**, in this order and no other — see _Design Constraints_:

1. Build the absolute candidate: absence denotes `WorkingDirectory`; an absolute request is taken
   as given; every relative request is interpreted against `WorkingDirectory` first, and a unique
   bare-segment alias denotes the matching grant's root only as a fallback, when that
   working-directory interpretation does not resolve to an existing path.
2. Resolve the candidate through the unchanged per-component reparse-point walk.
3. For a read, test the resolved location against every grant. For a write, test it only against
   read-write grants.
4. On refusal, build a denial that echoes the request, states any working-directory
   interpretation, and enumerates the permitted locations with access levels.

#### BuildCandidate(string? path, out bool wasRelative, out string interpretedAbsolute)

Private helper that turns caller input into the absolute candidate that will be resolved. Every
relative request is interpreted against the working directory first — the documented mechanism for
interpreting a relative path — and that interpretation wins whenever it names an existing path. The
bare-segment alias is only a fallback, considered for a bare segment whose working-directory
interpretation does not resolve to an existing path, so an ergonomic courtesy for input a model
guessed at never overrides the documented mechanism; this eliminates the silent wrong-target case
where a request for a folder inside the working directory returned a different granted location
sharing the final folder name. It also records whether a relative request was joined to the working
directory, because only that case is reported as an interpretation in a denial. Absolute inputs and
successful aliases are not reported as "interpreted as" values. The absolute location reported for a
working-directory interpretation is lexically normalized before it is handed to a denial (`.` and
`..` collapsed), while the candidate handed to the resolver is the un-normalized combined path, so
containment is unchanged. The normalization is lexical only — links are not resolved, because a
denial must not disclose a link target the caller did not name.

#### NormalizeInterpretedPath(string interpreted)

Private helper that lexically normalizes the absolute location a relative request was interpreted
as, so a denial reports a navigable path rather than one still carrying `.` or `..` segments. It
collapses those segments against the already-absolute input via `Path.GetFullPath` and touches
neither the file system nor any link: the per-component reparse walk in `RealPathResolver` exists for
the containment decision, and a link-resolved real path in a denial would disclose a link target the
caller never named. Only the reported value is normalized; the candidate handed to the resolver is
unchanged. Because the path is caller-controlled, normalization can throw on malformed input; a
denial must never throw, so the same resolution-class failures `IsResolutionFailure` recognizes fall
back to the un-normalized value.

#### CombinedPathExists(string combined)

Private helper that probes whether a working-directory-combined candidate names an existing file or
directory — the existence check that decides whether the bare-segment alias is even considered. It
is an existence check only; it never opens or reads the path, and it governs candidate selection
only, never containment: a probed path that exists is still subject to the unchanged per-component
reparse walk and grant test. The probe runs unconditionally, on the working directory whether or not
it is granted, so selection stays purely about addressing and never re-couples addressing to
permission. Any failure to determine existence is treated as "does not exist" (the same
resolution-class exceptions `IsResolutionFailure` recognizes), so the probe can never throw out of
`BuildCandidate` and an indeterminate path falls back to the alias exactly as a genuinely missing
one does.

#### AliasGrant(string segment)

Private helper implementing the last-segment alias. A bare input segment — no directory
delimiter, not rooted — that equals the last path segment of exactly one rooted grant resolves to
that grant's real location, but only as a fallback after the working-directory interpretation, and
only when nothing by that name exists in the working directory (see `CombinedPathExists`). If the
same bare segment matches two or more distinct grants, it is ambiguous and is not aliased. It falls
through to the working-directory-relative interpretation and is then denied, with the same-named
locations enumerated, rather than silently guessing the wrong target.

#### BuildDenial(string reason, string? path, bool wasRelative, string interpretedAbsolute)

Private helper constructing a denial in the order the model needs to recover:

1. The request, echoed verbatim, or an explicit marker for no path.
2. The absolute location a relative request was interpreted as, only when the request was joined
   to the working directory, reported in canonical (lexically normalized) form with `.` and `..`
   collapsed.
3. The permitted locations, each with its access level, or a statement that no locations are
   permitted.

### Error Handling

| Condition                                   | Handling                                                   |
| ------------------------------------------- | ---------------------------------------------------------- |
| Missing or empty working directory          | `ArgumentException` propagates                             |
| Null grant collection                       | `ArgumentNullException` propagates                         |
| Null grant entry                            | `ArgumentNullException` propagates                         |
| Null limits at construction                 | `ArgumentNullException` propagates                         |
| Null or empty `searchPattern`               | `ArgumentNullException` / `ArgumentException` propagates   |
| Omitted or placeholder `path` / `directory` | Handled locally; denotes the working directory             |
| Real location cannot be determined          | Handled locally; denial with the resolution reason         |
| Location outside every permitted location   | Handled locally; denial with the location reason           |
| Location matches a denied pattern           | Handled locally; denial with the pattern reason            |
| Directory cannot be listed                  | Handled locally; empty sequence                            |
| Empty grant collection                      | Accepted; the policy permits nothing                       |
| Bare segment exists under working directory | Handled locally; working-directory reading wins            |
| Existence probe cannot determine existence  | Handled locally; treated as missing; alias used            |
| Ambiguous last-segment alias                | Handled locally; interpreted beneath the working directory |
| Interpreted path cannot be normalized       | Handled locally; interpretation falls back un-normalized   |

The dividing line is deliberate and is stated here so a reviewer can check it: **programming
errors propagate; anything a model can produce is returned.** A path is the one argument a model
controls, so no spelling of a path — absent, empty, malformed or hostile — is an exception.
Resolution failures are caught by type — argument, I/O, unauthorized access, unsupported operation
and security exceptions — and converted into a denial, because denying what cannot be understood
is the fail-safe reading for input a model controls.

### Design Constraints

**Working directory and grants must remain orthogonal.** The working directory is addressing
only, and grants are permission only. A policy with an application-folder working directory and no
grant over that folder is valid: a relative request resolves there and is denied because no grant
permits it. A policy with a read-only working directory is also valid. There is no implicit access
and no special case that treats the anchor differently from any other location.

**The resolution steps must stay in this order.** Build an absolute candidate, run the
per-component reparse-point walk, then test containment. Making the path absolute before the walk
rather than after it is what ensures a relative path that reaches outside through a link is
refused exactly as an absolute one is: the walk sees the same fully-qualified path either way.
**The walk itself must not be weakened or replaced** — a leaf-only or deepest-existing-ancestor
resolution has been proven not to detect an escape; see _RealPathResolver Unit Design_.
`RealPathResolver` is deliberately untouched by the path model change, and its regression tests
must continue to pass unmodified.

**Enumeration and access must remain one decision.** Recursive enumeration provided by the
operating system follows directory junctions and symbolic links and will surface files outside
the permitted location; this was established experimentally. `EnumerateFiles` therefore filters
every candidate through `TryResolveRead` — the same method used for direct access, not a parallel
re-implementation of containment. A future change must not introduce a second containment check
for listings: a listing that is broader than what access permits discloses files the operator
withheld.

**Denials must enumerate the truthful map.** A refusal is reported as `false` with an `out
string? denialMessage`. The message deliberately includes permitted locations and access levels,
because a confined model needs the map of where it may work. It must not report an
interpretation-only denial, and it must not add an interpretation line for absolute input or for a
successful last-segment alias. The interpreted location is reported in canonical form — lexical
normalization only, with `.` and `..` collapsed and no link resolution — and normalizing the
reported value must not change the candidate that is resolved.

**Output dialect must mirror input dialect without lying.** Absolute input fixes absolute output.
Relative or discovery input is only relative-eligible. A relative result is allowed only when the
working directory is granted and the result lies within it; otherwise the absolute path must be
shown so the model sees that it has moved outside the anchor. This constraint prevents silent
wrong-target behavior.

**The working-directory interpretation must win; the alias is a fallback.** A relative request is
interpreted against the working directory first, because the working directory is the documented
mechanism for interpreting a relative path. The bare-segment grant alias is an ergonomic courtesy
and must never override that mechanism, so it is considered only for a bare segment whose
working-directory interpretation does not resolve to an existing path. This eliminates the silent
wrong-target case where a request for a folder inside the working directory would have returned a
different granted location sharing the final folder name. The deciding existence probe
(`CombinedPathExists`) governs candidate **selection** only, never containment: every candidate,
aliased or not, still passes the unchanged reparse walk and grant test. The probe runs
unconditionally — on the working directory whether or not it is granted — so selection stays purely
about addressing, and it must never throw out of `BuildCandidate`: any failure to determine
existence is treated as "does not exist" and falls back to the alias, the same fail-safe reading
used for a genuinely missing path.

**The last-segment alias must refuse ambiguity by enumeration.** A bare input segment equal to
exactly one grant's last path segment may name that grant. If two or more grants share the same
last segment, the request is not aliased. It is interpreted beneath the working directory and
denied with the permitted locations listed, because guessing would be worse than refusing.

**Resource ceilings ride with the policy.** `ToolLimits` — the ceilings on bytes read, characters
returned to the model, bytes of binary content returned and attachments per turn — is carried as
a `Limits` property rather than being passed per call. A tool therefore receives one object and
cannot end up observing a different budget from its neighbor, which is what "every pack observes
the same budget" means in practice. The three-argument constructor states the ceilings
explicitly; the two-argument constructor delegates to it with `ToolLimits.Default`. See
_ToolLimits Unit Design_ for the reasoning behind the values.

### Dependencies

- **RealPathResolver** — used to resolve the working directory at construction and every requested
  path before a grant is consulted; see _RealPathResolver Unit Design_.
- **PathRule** — holds each permission grant; see _PathRule Unit Design_.
- **ToolLimits** — the resource ceilings carried with the policy; see _ToolLimits Unit Design_.

### Callers

`PathPolicy` is a public API entry point: a host constructs one and hands it to the tools it
builds. Within this system nothing else calls it directly; the tool units that consume it are
introduced in later increments.

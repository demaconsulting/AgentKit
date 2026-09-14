## RealPathResolver

![AgentKit Core Structure](AgentKitCoreView.svg)

The `RealPathResolver` class resolves a path to the location the file system will actually
reach, so that every access decision in the library is made about a real location rather than
about the text a caller supplied.

### Purpose

`RealPathResolver` has one responsibility: given any path — absolute or relative, existing or
not — report the real, absolute, normalized location it denotes, with every symbolic link and
directory junction along the path followed to its final target.

It exists because a path string and the location it reaches are not the same thing. A reparse
point placed anywhere along a path silently redirects the operation elsewhere while the path
text continues to look contained within a permitted location. Separating resolution into its own
unit means containment logic never has to think about links, and the resolution algorithm has a
single place where its correctness can be reviewed and tested.

The class is static and stateless and is therefore safe for concurrent use. It reads file system
metadata but never creates, modifies or deletes anything.

### Data Model

`RealPathResolver` holds no state. Its only data is local to a single `Resolve` call:

| Local         | Type       | Description                                                         |
|---------------|------------|---------------------------------------------------------------------|
| `full`        | `string`   | Requested path made absolute, with relative segments collapsed.     |
| `root`        | `string?`  | Volume root the walk starts from; empty only for a rootless path.   |
| `components`  | `string[]` | Names below the volume root, in order, with empty entries removed.  |
| `accumulated` | `string`   | Prefix resolved so far; replaced outright whenever a link is found. |
| `depth`       | `int`      | Link substitutions already made on the way to this resolution.      |

The invariant maintained through the loop is that `accumulated` is always the real location of
the components consumed so far. `depth` is zero for a caller's request and is incremented once
per substituted link target, which is what bounds the recursion described below.

### Key Methods

#### Resolve(string path)

Reports the real, absolute location of `path`.

**Preconditions:** `path` is non-null and non-empty. It need not exist, and it need not be
absolute.

**Algorithm:**

1. Reject a null or empty path.
2. Refuse a path that has already been redirected more times than the platform's own link
   resolution depth, reporting it as an unresolvable path.
3. Make the path absolute and collapse `.` and `..` segments.
4. Take the volume root. A path with no root cannot be walked; the normalized form is returned
   as a defensive fallback.
5. Split the portion below the volume root into components, accepting either directory
   separator.
6. Starting at the volume root, append one component at a time. At each accumulated prefix,
   probe for a directory and then for a file; if the prefix exists, ask for its final link
   target. When a target is reported, resolve that target by this same algorithm, replace the
   accumulated prefix with the result, and continue with the remaining components beneath it.
7. Normalize once more and return, because a link target may itself be recorded relatively.

**Postconditions:** the returned location is absolute, fully qualified, contains no relative
segments, and is not itself reachable through any unresolved reparse point.

**Why the walk visits every component.** This is the load-bearing design decision of the unit and
must not be simplified away:

- Making the path absolute does **not** follow reparse points. A junction inside a permitted
  location that points outside it produces a fully-qualified path string that passes any
  containment check while the operation lands elsewhere.
- Resolving only the **leaf** is insufficient. The platform reports a link target only for an
  entry that is *itself* a reparse point; an ordinary file sitting beneath a junction reports
  nothing, so the escape goes undetected. This was established experimentally and is pinned by
  the regression test named in *RealPathResolver Unit Verification Design*.
- Resolving only the **deepest existing ancestor** is insufficient for the same reason: that
  ancestor is usually an ordinary directory, not the junction.

**Why a link target is resolved rather than trusted.** The platform follows a chain of links to
its end, but it does **not** canonicalize the *ancestors* of the target it reports. A target may
therefore name a location that is itself reached through a further reparse point, and accepting
it verbatim would leave an unresolved link inside the value the containment decision is made
from. Two links, both created inside a permitted location, are enough to exploit that: the first
points out of the permitted location, the second records a target spelled through the first. The
result then reads as contained while the file reached is outside — a defeat of the containment
risk control. The substituted target is consequently resolved by the same algorithm before the
walk continues beneath it, which makes the returned location real all the way down. The same
incompleteness is what made a platform whose temporary directory is itself reached through a
link disagree with itself about where a location was; with the target resolved, both spellings
report the same real location.

**Why resolution is bounded.** Resolving a target recursively means a cyclic arrangement of
links — one whose cycle is spread across two or more links, so that each individual chain
terminates and the platform's own chain following cannot see it — would otherwise recurse
without end. The number of substitutions is therefore limited to the resolution depth POSIX
platforms apply before refusing to follow any more links, and exceeding it raises the same error
a cyclic chain raises. Failing is the fail-safe outcome: callers treat an unresolvable path as a
denial.

**Cost.** At most one metadata probe per existing component, served from the operating system's
directory cache. Resolving a substituted target costs further probes only where a reparse point
is actually encountered, and the nesting is bounded as above, so a path containing no links pays
exactly what it did before. Components that do not yet exist cannot be reparse points and are
appended unchanged, which is what allows a write target to be judged before it is created.

#### ResolveBounded(string path, int depth)

Private helper carrying out the walk described above while counting the link substitutions made
on the way to it. The public entry point calls it at depth zero, and each substituted target is
resolved through it at one greater depth, which is what makes a link target's own ancestors real
and what keeps a cycle spread across several links from recursing without end. The depth is
deliberately not part of the public contract: a caller has no way to express a partly-resolved
path, so exposing it would offer only a way to weaken the guarantee.

#### Probe(string path)

Private helper returning file system metadata for an existing path, or nothing when the path does
not exist. Directories are probed first because a junction and a directory symbolic link both
present as directories, and those are the escape vectors that matter most; a file is probed
second because a POSIX file symbolic link can equally leave a permitted location.

### Error Handling

| Condition                            | Handling                                   |
|--------------------------------------|--------------------------------------------|
| `path` is null                       | `ArgumentNullException` propagates         |
| `path` is empty or not a valid path  | `ArgumentException` propagates             |
| A link chain is cyclic or too deep   | `IOException` propagates                   |
| Links form a cycle across each other | `IOException` raised once the bound is hit |
| A component does not exist           | Not an error; component appended unchanged |
| A path has no volume root            | Not an error; normalized path returned     |

Nothing is handled locally. A missing path is a programming error in the caller and is surfaced
immediately. A cyclic link chain is deliberately **not** converted into a result here, because
whether an unresolvable path is a denial or a failure is a policy question, not a resolution
question; see *PathPolicy Unit Design*, which catches it and denies.

### Dependencies

N/A - `RealPathResolver` depends only on the .NET base class library.

### Callers

`RealPathResolver` is a public API entry point, callable by consumers of the AgentKit Core
package. Within this system it is used by `PathRule`, which resolves its confined location once
at construction, and by `PathPolicy`, which resolves every requested path before consulting a
rule.

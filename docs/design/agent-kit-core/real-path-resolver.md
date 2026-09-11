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

The invariant maintained through the loop is that `accumulated` is always the real location of
the components consumed so far.

### Key Methods

#### Resolve(string path)

Reports the real, absolute location of `path`.

**Preconditions:** `path` is non-null and non-empty. It need not exist, and it need not be
absolute.

**Algorithm:**

1. Reject a null or empty path.
2. Make the path absolute and collapse `.` and `..` segments.
3. Take the volume root. A path with no root cannot be walked; the normalized form is returned
   as a defensive fallback.
4. Split the portion below the volume root into components, accepting either directory
   separator.
5. Starting at the volume root, append one component at a time. At each accumulated prefix,
   probe for a directory and then for a file; if the prefix exists, ask for its final link
   target. When a target is reported, replace the accumulated prefix with that target and
   continue with the remaining components beneath it.
6. Normalize once more and return, because a link target may itself be recorded relatively.

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

**Cost.** At most one metadata probe per existing component, served from the operating system's
directory cache. Components that do not yet exist cannot be reparse points and are appended
unchanged, which is what allows a write target to be judged before it is created.

#### Probe(string path)

Private helper returning file system metadata for an existing path, or nothing when the path does
not exist. Directories are probed first because a junction and a directory symbolic link both
present as directories, and those are the escape vectors that matter most; a file is probed
second because a POSIX file symbolic link can equally leave a permitted location.

### Error Handling

| Condition                           | Handling                                   |
|-------------------------------------|--------------------------------------------|
| `path` is null                      | `ArgumentNullException` propagates         |
| `path` is empty or not a valid path | `ArgumentException` propagates             |
| A link chain is cyclic or too deep  | `IOException` propagates                   |
| A component does not exist          | Not an error; component appended unchanged |
| A path has no volume root           | Not an error; normalized path returned     |

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

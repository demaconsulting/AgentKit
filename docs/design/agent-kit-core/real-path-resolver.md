## RealPathResolver

![AgentKit Core Structure](AgentKitCoreView.svg)

The `RealPathResolver` class puts any path a caller supplies into a single form — absolute, with
relative segments collapsed — so that every access decision in the library compares two locations
that are spelled the same way.

### Purpose

`RealPathResolver` has one responsibility: given any path — absolute or relative, existing or
not — report the absolute, normalized location it denotes, either as spelled (`Resolve`) or as
reached once every link on it is followed (`ResolveLinks`).

It exists because containment is a comparison, and a comparison is only meaningful between two
values expressed the same way. A model supplies a bare name, a relative name, or a name carrying
`..` segments; a grant supplies a location of its own. Normalizing both in one place means the
containment logic never has to think about spelling, and the normalization has a single place
where its behavior can be reviewed and tested.

**Two resolutions.** `Resolve` is lexical: symbolic links, directory junctions and other reparse
points are neither followed nor detected, so a path that leaves a granted location through a link
is reported as it is spelled. That is the resolution the lexical decisions use, and the README
states its consequence for users. `ResolveLinks` follows every link on the path, so a decision
that must not be steered through a link can judge where the path really leads; see
*PathPolicy Unit Design* for the decisions built on it.

The class is static and stateless and is therefore safe for concurrent use. `Resolve` reads
nothing from the file system; `ResolveLinks` reads link metadata only.

### Data Model

`RealPathResolver` holds no state and keeps no data between calls.

### Key Methods

#### Resolve(string path)

Reports the absolute, normalized location of `path`.

**Preconditions:** `path` is non-null and non-empty. It need not exist, and it need not be
absolute.

**Algorithm:**

1. Reject a null or empty path.
2. Make the path absolute against the current working directory and collapse `.` and `..`
   segments.

**Postconditions:** the returned location is absolute, fully qualified, and contains no relative
segments.

**Why normalization is the whole of it.** Collapsing `..` is what makes a spelled escape from a
granted location fail the containment test, and making the path absolute is what allows a
relative request to be expressed at all. Nothing else is required for a decision between two
paths, and nothing else is claimed: a location reached through a link is reported as it is
spelled.

**Why the path need not exist.** A write decision has to be made before the file is created, so
resolution never probes for existence and a non-existent path resolves exactly as an existing one
does.

**Cost.** One lexical normalization per call, with no file system access.

#### ResolveLinks(string path)

Reports the absolute, normalized location `path` reaches once every symbolic link and directory
junction on it has been followed.

**Preconditions:** `path` is non-null and non-empty. It need not exist, and it need not be
absolute.

**Algorithm:**

1. Resolve the path lexically with `Resolve`.
2. Walk the result one component at a time from the volume root, reading each component's link
   target (`FileSystemInfo.LinkTarget`), which reports a POSIX symbolic link and a Windows
   junction alike and reports nothing for a missing entry.
3. At the first component that is a link, resolve its target against the folder holding the link
   (a relative target such as macOS `/var -> private/var` is relative to that folder), rewrite a
   Windows device-form target (`\\?\`, `\??\`, `\\?\UNC\`) into its ordinary form, append the
   components not yet walked, and start the walk again from the root, so links in the target's own
   ancestry are followed too. A dangling link is followed like any other.
4. When a walk meets no link, return the location without a trailing separator (a volume root
   keeps its separator).
5. If more than 40 links are met (the limit Linux applies), throw `IOException`: the links form a
   cycle.

**Postconditions:** the returned location is absolute, normalized, and no component of it was a
link when it was walked. A component that does not exist is not a link, so the rest of the path is
kept as spelled: a file not yet created beneath an existing linked folder resolves to that
folder's real location followed by its name, which is what a write decision needs.

**What it does not do.** Case is kept as stored, and a Windows short (8.3) name is not expanded.
A hard link cannot be told apart from an ordinary file. A reparse point that reports no link
target, such as a cloud-file placeholder, is treated as an ordinary entry. macOS firm links need
nothing, because their spelled path is already their real path.

**The answer describes one moment.** The file system can change as soon as the method returns, so
a decision built on it is a pre-flight check; *PathPolicy Unit Design* states the residual window.

**Cost.** One link-target read per component per walk, and one further walk per link followed.

### Error Handling

| Condition | Handling |
| --- | --- |
| `path` is null | `ArgumentNullException` propagates |
| `path` is empty or not a valid path | `ArgumentException` propagates |
| The normalized result is too long | `IOException` propagates |
| A path does not exist | Not an error; the location is stated |
| `ResolveLinks` meets more than 40 links | `IOException` propagates (a link cycle) |
| `ResolveLinks` meets a link target that is not a valid path | `ArgumentException` propagates |
| A link target cannot be read | Not an error; the component is treated as an ordinary entry |

Nothing is handled locally. A missing path is a programming error in the caller and is surfaced
immediately. A path the platform cannot express as a location is deliberately **not** converted
into a result here, because whether an unresolvable path is a denial or a failure is a policy
question, not a resolution question; see *PathPolicy Unit Design*, which catches it and denies.

### Dependencies

N/A - `RealPathResolver` depends only on the .NET base class library.

### Callers

`RealPathResolver` is a public API entry point, callable by consumers of the AgentKit Core
package. Within this system it is used by `PathRule`, which normalizes its confined location once
at construction, and by `PathPolicy`, which normalizes every requested path before consulting a
rule and, in its link-following decisions, resolves the permitted path and each grant root through
`ResolveLinks`.

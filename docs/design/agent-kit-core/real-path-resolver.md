## RealPathResolver

![AgentKit Core Structure](AgentKitCoreView.svg)

The `RealPathResolver` class puts any path a caller supplies into a single form — absolute, with
relative segments collapsed — so that every access decision in the library compares two locations
that are spelled the same way.

### Purpose

`RealPathResolver` has one responsibility: given any path — absolute or relative, existing or
not — report the absolute, normalized location it denotes.

It exists because containment is a comparison, and a comparison is only meaningful between two
values expressed the same way. A model supplies a bare name, a relative name, or a name carrying
`..` segments; a grant supplies a location of its own. Normalizing both in one place means the
containment logic never has to think about spelling, and the normalization has a single place
where its behavior can be reviewed and tested.

**Links are out of scope.** Symbolic links, directory junctions and other reparse points are
neither followed nor detected. A path that leaves a granted location through a link is judged on
the location it is spelled as, so a link is not a protection boundary; this is stated for users
in the README.

The class is static and stateless and is therefore safe for concurrent use. Resolution is lexical
and reads nothing from the file system.

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

### Error Handling

| Condition                           | Handling                             |
|-------------------------------------|--------------------------------------|
| `path` is null                      | `ArgumentNullException` propagates   |
| `path` is empty or not a valid path | `ArgumentException` propagates       |
| The normalized result is too long   | `IOException` propagates             |
| A path does not exist               | Not an error; the location is stated |

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
rule.

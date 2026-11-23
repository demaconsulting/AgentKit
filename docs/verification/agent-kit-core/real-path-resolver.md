## RealPathResolver Unit Verification Design

This document describes the unit-level verification strategy for the `RealPathResolver` class.

### Verification Approach

`RealPathResolver` is verified through unit tests that resolve real paths on the real file system.
Nothing is mocked or stubbed: `Resolve` is lexical, so its scenarios state the normalization
contract directly, and `ResolveLinks` is verified against genuine links, so its scenarios compare
the location a linked path reaches with the location it must equal.

Each test creates its own temporary tree through `TempDirectoryFixture`, which provides an allowed
root and a sibling directory outside it. The link scenarios create a real link with
`DirectoryLink`: an NTFS junction on Windows, which needs no privilege, and a symbolic link
elsewhere. A link that cannot be created fails the test rather than skipping it, because a skipped
test leaves no passing result and the requirement would appear covered without evidence.

Unit tests reside in `RealPathResolverTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **File system**: A temporary directory tree created per test beneath the platform temporary
  path, named with a fresh identifier so concurrent runs cannot collide
- **Links**: Created per test inside the temporary tree and removed, unfollowed, before the tree
  itself is deleted
- **Mocking**: None; introducing any would invalidate the verification
- **Isolation**: Each test constructs and disposes its own fixture; no state is shared

### Acceptance Criteria

A unit test run passes when all nine scenarios below pass without error or unexpected exception.
Any unexpected exception type, and any resolved location that differs from the one specified,
constitutes a failure.

### Test Scenarios

#### AgentKitCore-RealPathResolver-Normalization: Relative Segments Resolve to a Normalized Absolute Location

**Test**: `RealPathResolver_Resolve_RelativeSegments_ReturnsNormalizedAbsolutePath`

Resolves the same file through a redundant parent-directory detour and through a path relative to
the current working directory, and asserts both agree with the direct spelling, that the result
is fully qualified, and that it contains no relative segment. This is the property every
containment decision rests on: two spellings of one location resolve to one value, and a `..`
segment cannot survive into that value.

#### AgentKitCore-RealPathResolver-Normalization: An Already-Normalized Path Resolves Unchanged

**Test**: `RealPathResolver_Resolve_NormalizedPath_ReturnsSamePath`

Resolves an ordinary file directly inside the root. Asserts the result is exactly the resolved
root combined with the file's name, and that resolving the result again returns the same
location, so resolution is stable and adds nothing where there is nothing to collapse.

#### AgentKitCore-RealPathResolver-NonExistentPath: A File That Does Not Yet Exist Resolves to Its Location

**Test**: `RealPathResolver_Resolve_NonExistentPath_ReturnsLocationItWouldOccupy`

Resolves a file name that has never been created, beneath a directory that has never been
created. Asserts the resolved location does not exist, preserves the requested file name, and is
the location beneath the resolved root the request named. This is the boundary case that makes a
write decision possible before the file exists.

#### AgentKitCore-RealPathResolver-RejectInvalidPath: Null Path Throws ArgumentNullException

**Test**: `RealPathResolver_Resolve_NullPath_ThrowsArgumentNullException`

Asserts that a null path raises `ArgumentNullException` rather than being resolved or denied,
because a missing path is a defect in the calling code.

#### AgentKitCore-RealPathResolver-RejectInvalidPath: Empty Path Throws ArgumentException

**Test**: `RealPathResolver_Resolve_EmptyPath_ThrowsArgumentException`

Asserts that an empty path raises `ArgumentException` and not `ArgumentNullException`. This is
the empty-string boundary, distinct from the null case.

#### AgentKitCore-RealPathResolver-RejectInvalidPath: Null or Empty Path Throws When Following Links

**Test**: `RealPathResolver_ResolveLinks_NullOrEmpty_Throws`

Asserts that `ResolveLinks` raises `ArgumentNullException` for a null path and `ArgumentException`
for an empty one, the same programming-error contract `Resolve` keeps.

#### AgentKitCore-RealPathResolver-FollowsLinks: A Path Through a Linked Directory Resolves to the Target

**Test**: `RealPathResolver_ResolveLinks_LinkedDirectory_ReturnsTargetLocation`

Creates a link inside the root pointing at the sibling directory outside it, and resolves a file
spelled through the link. Asserts the result is the file's location beneath the outside
directory's resolved location, so a path that escapes through a link is reported where it leads.

#### AgentKitCore-RealPathResolver-FollowsLinks: A File Not Yet Created Under a Linked Folder Resolves to the Target

**Test**: `RealPathResolver_ResolveLinks_NonExistentFileUnderLinkedParent_ReturnsTargetParentAndName`

Resolves a file name that has never been created beneath the same kind of link. Asserts the result
does not exist and is the outside directory's resolved location followed by the requested name.
This is the boundary case a write decision needs before the file exists.

#### AgentKitCore-RealPathResolver-FollowsLinks: A Path Without Links Resolves Stably

**Test**: `RealPathResolver_ResolveLinks_NoLinks_IsIdempotent`

Resolves an ordinary file directly inside the root and asserts the result is the root's resolved
location followed by the file's name, and that resolving it again returns the same value. The
expected value is built from the resolved root rather than the raw temporary path, because on
macOS the temporary folder lies beneath the `/var` link.

## RealPathResolver Unit Verification Design

This document describes the unit-level verification strategy for the `RealPathResolver` class.

### Verification Approach

`RealPathResolver` is verified through unit tests that exercise **real reparse points on the real
file system**. Nothing is mocked or stubbed, and nothing may be: the behavior under test is
precisely the behavior of the operating system's link resolution, so a simulated file system
would verify the simulation rather than the control.

Each test creates its own temporary tree through `ReparsePointFixture`, which provides an allowed
root, a sibling directory outside it, and the ability to create a genuine directory link between
them.

Unit tests reside in `RealPathResolverTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **File system**: A temporary directory tree created per test beneath the platform temporary
  path, named with a fresh identifier so concurrent runs cannot collide
- **Reparse points**: On Windows, a directory junction created by `cmd.exe /c mklink /J`; on
  Linux and macOS, a directory symbolic link. A Windows symbolic link is deliberately **not**
  used: it requires a privilege an unelevated developer session does not hold, which would make
  the test pass on the elevated CI runner and fail on every workstation
- **Failure policy**: When link creation fails the fixture fails the test with the operating
  system's error text. It never skips, because a skipped test produces no evidence in the test
  results while the requirement continues to appear covered
- **Mocking**: None; introducing any would invalidate the verification
- **Isolation**: Each test constructs and disposes its own fixture; no state is shared

### Acceptance Criteria

A unit test run passes when all eight scenarios below pass without error or unexpected exception.
Any unexpected exception type, any resolved location that differs from the one specified, and any
failure to create or tear down the reparse point constitutes a failure.

### Test Scenarios

#### AgentKitCore-RealPathResolver-ComponentWalk: File Beneath a Directory Link Resolves Outside the Root

**Test**: `RealPathResolver_Resolve_PathBeneathDirectoryLink_ReturnsRealTargetOutsideRoot`

The central junction-escape scenario. Writes a file into the outside directory, creates a
directory link inside the root pointing at that directory, and resolves the path that reaches the
file through the link. Asserts that the resolved location reads back the outside file's content,
that its file name is preserved, and that it is **not** at or beneath the real root. Verifies that
an escape through a link anywhere in the path is detected.

#### AgentKitCore-RealPathResolver-ComponentWalk: Leaf Resolution Alone Does Not Detect the Escape

**Test**: `RealPathResolver_Resolve_LeafResolutionAlone_DoesNotDetectEscape`

The regression guard that locks in why the component walk exists. Sets up the same escape, then
asserts three things together: resolving only the leaf reports no link target, because a real
file beneath a junction is not itself a reparse point; the requested path text *is* contained by
the real root, so a text-based check would accept it; and the resolver nonetheless reports a
location outside the root. If a future change replaces the walk with leaf-only or
deepest-ancestor resolution, this scenario fails and states the reason.

#### AgentKitCore-RealPathResolver-LinkTarget: A Directory Link Resolves to Its Target

**Test**: `RealPathResolver_Resolve_DirectoryLinkItself_ReturnsLinkTarget`

Resolves the link itself rather than something beneath it. Asserts the resolved location is an
existing directory containing the marker file written outside the root, and that it is not at or
beneath the root. Verifies that a path naming a link is judged by its target.

#### AgentKitCore-RealPathResolver-NonExistentPath: A File That Does Not Yet Exist Resolves to Its Real Location

**Test**: `RealPathResolver_Resolve_NonExistentFileBeneathLink_ReturnsRealTargetLocation`

Resolves a file name that has never been created, beneath a directory link. Asserts the resolved
location does not exist, preserves the requested file name, lies beneath the link's real target,
and is not beneath the root. This is the boundary case that makes a write decision possible
before the file exists.

#### AgentKitCore-RealPathResolver-Normalization: Relative Segments Resolve to a Normalized Absolute Location

**Test**: `RealPathResolver_Resolve_RelativeSegments_ReturnsNormalizedAbsolutePath`

Resolves the same file through a redundant parent-directory detour and through a path relative to
the current working directory, and asserts both agree with the direct spelling, that the result
is fully qualified, and that it contains no relative segment.

#### AgentKitCore-RealPathResolver-Normalization: A Path With No Links Resolves Unchanged

**Test**: `RealPathResolver_Resolve_PathWithNoLinks_ReturnsSamePath`

Resolves an ordinary file directly inside the root. Asserts the result is exactly the real root
combined with the file's name, and that resolving the result again returns the same location.
The expectation is expressed relative to the resolved root rather than to the raw temporary path
because on macOS the temporary directory is itself reached through a symbolic link; the property
under test is that no *further* redirection occurs.

#### AgentKitCore-RealPathResolver-RejectInvalidPath: Null Path Throws ArgumentNullException

**Test**: `RealPathResolver_Resolve_NullPath_ThrowsArgumentNullException`

Asserts that a null path raises `ArgumentNullException` rather than being resolved or denied,
because a missing path is a defect in the calling code.

#### AgentKitCore-RealPathResolver-RejectInvalidPath: Empty Path Throws ArgumentException

**Test**: `RealPathResolver_Resolve_EmptyPath_ThrowsArgumentException`

Asserts that an empty path raises `ArgumentException` and not `ArgumentNullException`. This is
the empty-string boundary, distinct from the null case.

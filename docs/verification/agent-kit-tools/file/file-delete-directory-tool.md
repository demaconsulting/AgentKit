### FileDeleteDirectoryTool Unit Verification Design

This document describes the unit-level verification strategy for the `FileDeleteDirectoryTool`
class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses a real `PathPolicy`, a real temporary directory
tree and — where the link guard is under test — a **real reparse point**. Path containment is a
security control, so the scenarios that verify it must exercise a genuine link rather than a
simulated one; a substitute would only prove that the substitute behaves consistently with itself.

Returned results and file system state are both checked. A successful response must correspond to
the real state change, and every refusal scenario has a companion assertion that the protected
state is unchanged — for the two guards, a dedicated scenario each proving that **nothing** was
removed, because a half-destroyed tree is the outcome the two-phase design exists to prevent.

**The three link scenarios were authored first, and they are listed first for that reason.** They
were run against an implementation whose walk recursed unconditionally and observed to fail, with
the file beyond the link genuinely destroyed, before the guard that stops it was written. The
guard is therefore known to be load-bearing rather than assumed to be.

**Link creation fails the test; it never skips it.** The `DirectoryLink` helper creates an NTFS
junction through `cmd.exe /c mklink /J` on Windows and a symbolic link through the managed API
elsewhere, and calls `Assert.Fail` with the operating system's own message when either fails. A
symbolic-link-based fixture on Windows would be green on an elevated continuous-integration runner
and red on every unelevated workstation, and a *skipped* test produces no entry in the results at
all — so the security requirement would appear covered while no evidence existed behind it. A
junction needs no privilege, is the same class of reparse point an attacker would use, and is
detected by the same non-null link target the guard reads.

**No recursive framework delete is used anywhere**, in the unit or in the helper's cleanup. On
Windows a recursive delete over a tree containing a junction throws and leaves the tree partly
deleted, so the helper removes the link entry alone before any surrounding fixture tears its tree
down.

Unit tests reside in `File/FileDeleteDirectoryToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project; the link helper is `File/DirectoryLink.cs` in the
same project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **Reparse points**: A platform able to create a directory junction (Windows, no privilege
  required) or a directory symbolic link (Linux, macOS). A platform where neither succeeds fails
  the link scenarios loudly, which is the designed behavior
- **State**: Each scenario creates the temporary file tree, links and host limits it needs
- **Isolation**: Each test constructs its own policy, tool, link and temporary state; no state is
  shared

#### Acceptance Criteria

A unit test run passes when all 9 requirement scenarios below, covering 13 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, an ignored policy decision, a link followed out of
the tree, a link's target disturbed, a link's target named in a refusal, a tree partly removed
after a refusal, a ceiling not observed, a removal count misreported, a leaked path, a malformed
request thrown as a framework error, or a link scenario that is skipped rather than run
constitutes a failure.

#### Test Scenarios

##### AgentKitTools-File-DeleteDirectoryTool-NeverFollowsALink: Never Follows a Link

**Test**: `FileDeleteDirectoryTool_Delete_TreeContainingALinkOutsideTheGrant_ReturnsDenialAndLeavesTheLinkTargetIntact`

**Test**: `FileDeleteDirectoryTool_Delete_TreeContainingALinkOutsideTheGrant_RemovesNothingFromTheTree`

Security control, and the pair authored first. A real link is created inside a permitted tree
pointing at an ungranted sibling that holds a file. The first test asserts the request is refused,
that the refusal names the offending entry as the model spelled it — `build/escape`, composed from
the requested path and the relative sub-path — that the refusal does **not** contain the link's
target path, and that the file beyond the link is confirmed byte-identical afterwards. The second
asserts the tree's own directory, its ordinary file and the link entry are all still present, so
the refusal is proven to be taken before anything is removed rather than part way through.

##### AgentKitTools-File-DeleteDirectoryTool-RemovesALinkNamedDirectly: Removes a Link Named Directly

**Test**: `FileDeleteDirectoryTool_Delete_DirectoryThatIsItselfALink_RemovesTheLinkAndNotItsTarget`

Security control, and the complement of the scenario above. A link named directly is removed and
the target directory and the file inside it are confirmed intact. Without this behavior the
refusal above would be a dead end — a workspace containing a link would be permanently undeletable
by the agent, since the single-file deletion refuses a directory and a link is a directory — so
this scenario is what proves the denial teaches rather than merely stops.

##### AgentKitTools-File-DeleteDirectoryTool-ToolName: Tool Name

**Test**: `FileDeleteDirectoryTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed test proves the published tool name is the family-qualified name the pack claims, and
that it is a valid tool name.

##### AgentKitTools-File-DeleteDirectoryTool-GuardedConstruction: Guarded Construction

**Test**: `FileDeleteDirectoryTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed test proves a missing policy is a programming error rather than a denial.

##### AgentKitTools-File-DeleteDirectoryTool-DeletesTree: Deletes Tree

**Test**: `FileDeleteDirectoryTool_Delete_PermittedTree_RemovesItAndReportsTheEntryCount`

**Test**: `FileDeleteDirectoryTool_Delete_EmptyDirectory_RemovesIt`

The two listed tests prove a permitted tree of four entries — the named directory, a nested
directory and two files — is removed whole and that the result reports the count, so a reader of
the transcript can see the scale of what was done; and that an empty directory, the smallest tree
the tool handles, is removed as well.

##### AgentKitTools-File-DeleteDirectoryTool-EntryCeiling: Entry Ceiling

**Test**: `FileDeleteDirectoryTool_Delete_TreeExceedingTheEntryCeiling_ReturnsDenialNamingTheCountAndTheLimit`

**Test**: `FileDeleteDirectoryTool_Delete_TreeExceedingTheEntryCeiling_RemovesNothing`

**Test**: `FileDeleteDirectoryTool_Delete_ZeroEntryCeiling_RefusesEvenAnEmptyDirectory`

Security control. A four-entry tree is offered to a tool whose host configured a ceiling of two.
The first test asserts the refusal is `ResourceTooLarge` and names both the real count and the
host's ceiling, so the model is told a number it can act on rather than "more than the limit". The
second asserts every entry survives, pinning the same before-acting property as the link guard.
The third asserts that a ceiling of zero refuses even an empty directory, which is the behavior
that makes zero the expressible way for a host to withhold the capability entirely — and which
only holds because the named directory itself counts as an entry.

##### AgentKitTools-File-DeleteDirectoryTool-PolicyGoverned: Policy Governed

**Test**: `FileDeleteDirectoryTool_Delete_ReadOnlyLocation_ReturnsDenialAndLeavesTheTree`

Security control: the location is granted read-only, so the read decision would permit the path
and the write decision must not. The request is refused as `PathNotPermitted` and both the tree and
its file are confirmed present, so write access is proven not to be inferred from read access.

##### AgentKitTools-File-DeleteDirectoryTool-MissingDirectory: Missing Directory

**Test**: `FileDeleteDirectoryTool_Delete_MissingDirectory_ReturnsDenialNamingNoTool`

The listed test proves a missing directory is reported as `TargetNotFound` rather than treated as a
silent success, with a plain fact that names no other tool.

##### AgentKitTools-File-DeleteDirectoryTool-RefusesFile: Refuses File

**Test**: `FileDeleteDirectoryTool_Delete_PathIsAFile_ReturnsDenialAndLeavesTheFile`

The listed test proves a path naming a file is refused and the file is left in place, so the
whole-tree tool is not a second route to removing one named file.

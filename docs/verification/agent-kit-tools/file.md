## File Subsystem Verification Design

This document describes the subsystem-level verification strategy for the File tool family.

### Verification Approach

The subsystem is verified through integration tests that exercise the family the way an application
does: composed through the AgentKitCore `ToolPackBuilder` under one access policy, then invoked
through the published tool list by name and argument dictionary, exactly as an agent runtime invokes
it. Nothing is mocked. The access policy and file system are real, because the properties under
verification belong to the real host boundary.

The scenarios here assert what belongs to the family as a whole: the list, copy, move, delete,
create-directory, move-directory, delete-directory and pack units. They verify that files are
handled as entities regardless of type, that the directories holding them can be managed as a
distinct capability, and that both are done with safe mutation defaults. The algorithm of any
single tool is verified in that unit's own document.

The boundary the subsystem is exercised at is deliberately the composed tool list rather than the
units' internal factories, because that list is what an application actually attaches. A tool that
could not be reached that way would not be reachable by an agent either. That matters most for the
recursive-deletion guard: what an application relies on is that no tool it *received* can remove
content the request never named, so that property is verified at the composed boundary as well as
inside the unit.

Subsystem tests reside in `File/FileTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project; the real-link helper they share with the unit tests is `File/DirectoryLink.cs`.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: Each scenario creates and deletes its own temporary directory tree when files are
  needed
- **Reparse points**: The recursive-deletion scenario creates a real directory junction on Windows
  or a directory symbolic link elsewhere, and fails rather than skips when neither can be created
- **Isolation**: Each test constructs its own policy, composition and temporary state; no state is
  shared

### Acceptance Criteria

A subsystem test run passes when all 8 requirement scenarios below, covering 9 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing tool, a wrong
family prefix, an ignored policy decision, a containment escape, a link followed out of a tree
being removed, a thrown refusal, incorrect relative-path
behavior, unsafe mutation, or a ceiling violation returned as truncated content constitutes a
failure.

### Test Scenarios

#### AgentKitTools-File-FamilyComposition: Family Composition

**Test**: `File_Family_ComposedThroughBuilder_PublishesTheWholeFamily`

**Test**: `File_Family_HostDeclaringNoCapability_StillReceivesTheFamily`

The listed tests prove a composition attaching the family publishes every tool it has — listing,
copying, moving and deleting a file, and creating, moving and deleting a directory — under a policy
that permits writing; a host declaring no capability still receives the family.

#### AgentKitTools-File-DirectoryManagement: Directory Management

**Test**: `File_Family_DirectoryLifecycle_CreateMoveDelete_WorksEndToEnd`

The listed test proves an agent can perform the whole directory lifecycle through the composed
family and by bare relative names: it creates a nested directory including its missing parent, has
a file placed inside it, renames it to a name beside itself and confirms the file arrived, then
removes the whole tree and confirms the reported entry count. The rename leg is exercised as a
move with a destination in the same parent, which is the only form the capability takes.

#### AgentKitTools-File-RecursiveDeletionIsBounded: Recursive Deletion Is Bounded

**Test**: `File_Family_RecursiveDelete_LinkOutOfTheGrant_IsNeverFollowed`

Security control at the composed boundary. A real link is created inside a permitted tree pointing
at an ungranted sibling holding a file, and the composed family is asked to remove the tree. The
request is refused, the tree is confirmed still present, and the file beyond the link is confirmed
byte-identical — so an application that attached the pack can rely on no tool it received removing
content the request never named. The link is real rather than simulated, and its creation fails
the test rather than skipping it, because a skipped test leaves no evidence behind a security
requirement. The complementary half — the entry ceiling, and the link that is itself the named
path — is verified against the unit in *FileDeleteDirectoryTool Unit Verification Design*.

#### AgentKitTools-File-WriteToolsRequireAWriteGrant: Only What the Policy Can Permit Is Offered

**Test**: `File_Family_ComposedThroughBuilder_PublishesTheWholeFamily`

The listed test proves at family level that a composition governed by a write-permitting policy
receives every tool, which is the half of the rule that guards against a filter suppressing
unconditionally. The complementary half — that a policy permitting no writing anywhere receives
only `file_list` — is verified against the pack in *FilePack Unit Verification Design*, and across
all seven families in *AgentKitTools System Verification Design*.

#### AgentKitTools-File-GuardedConstruction: Guarded Construction

**Test**: `File_Family_ComposedThroughBuilder_PublishesTheWholeFamily`

The listed test proves a composition attaching the family publishes every tool through the guarded
construction path, each governed by the one policy the builder was given.

#### AgentKitTools-File-PolicyGoverned: Policy Governed

**Test**: `File_Family_PathOutsideRoot_IsNeverReachableByAnyTool`

Security control at the composed boundary: the grant covers only a workspace subdirectory and the
bait file and bait directory are written into its ungranted parent — the very directory the listing
request names — so each leg is refused by a decision rather than by path arithmetic. The listing of
the ungranted parent is refused as `PathNotPermitted` and never names the bait; the copy, the move,
the delete, the directory creation, the directory move and the directory removal are each refused
as `PathNotPermitted`; and the bait file is confirmed byte-identical, the bait directory still
present and the directory the creation named confirmed absent, so no tool in the family reached
outside the grant.

#### AgentKitTools-File-DenialsAreResults: Denials Are Results

**Test**: `File_Family_DeniedRequest_ReturnsAResultWithoutThrowing`

The listed test proves a refused request returns a result rather than throwing.

#### AgentKitTools-File-SafeMutation: Safe Mutation

**Test**: `File_Family_CopyThenDelete_ByRelativeNames_WorksEndToEnd`

The listed test proves a copy-then-delete round-trip a model performs by bare relative names works
end to end, with each step naming what it changes.

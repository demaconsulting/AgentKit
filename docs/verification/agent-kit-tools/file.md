## File Subsystem Verification Design

This document describes the subsystem-level verification strategy for the File tool family.

### Verification Approach

The subsystem is verified through integration tests that exercise the family the way an application
does: composed through the AgentKitCore `ToolPackBuilder` under one access policy, then invoked
through the published tool list by name and argument dictionary, exactly as an agent runtime invokes
it. Nothing is mocked. The access policy and file system are real, because the properties under
verification belong to the real host boundary.

The scenarios here assert what belongs to the family as a whole: the list, copy, move, delete and
pack units. They verify that files are handled as entities regardless of type, with safe mutation
defaults. The algorithm of any single tool is verified in that unit's own document.

The boundary the subsystem is exercised at is deliberately the composed tool list rather than the
units' internal factories, because that list is what an application actually attaches. A tool that
could not be reached that way would not be reachable by an agent either.

Subsystem tests reside in `File/FileTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: Each scenario creates and deletes its own temporary directory tree when files are
  needed
- **Isolation**: Each test constructs its own policy, composition and temporary state; no state is
  shared

### Acceptance Criteria

A subsystem test run passes when all 5 requirement scenarios below, covering 6 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing tool, a wrong
family prefix, an ignored policy decision, a containment escape, a thrown refusal, incorrect relative-path
behavior, unsafe mutation, or a ceiling violation returned as truncated content constitutes a
failure.

### Test Scenarios

#### AgentKitTools-File-FamilyComposition: Family Composition

**Test**: `File_Family_ComposedThroughBuilder_PublishesListCopyMoveDelete`

**Test**: `File_Family_HostDeclaringNoCapability_StillReceivesTheFamily`

The listed tests prove a composition attaching the family publishes list, copy, move and delete
tools; a host declaring no capability still receives the family.

#### AgentKitTools-File-GuardedConstruction: Guarded Construction

**Test**: `File_Family_ComposedThroughBuilder_PublishesListCopyMoveDelete`

The listed tests prove a composition attaching the family publishes list, copy, move and delete
tools.

#### AgentKitTools-File-PolicyGoverned: Policy Governed

**Test**: `File_Family_PathOutsideRoot_IsNeverReachableByAnyTool`

Security control at the composed boundary: the grant covers only a workspace subdirectory and the
bait file is written into its ungranted parent — the very directory the listing request names — so
each leg is refused by a decision rather than by path arithmetic. The listing of the ungranted parent
is refused as `PathNotPermitted` and never names the bait; the copy, the move and the delete of the
bait are each refused as `PathNotPermitted`; and the bait is confirmed byte-identical afterwards, so
no tool in the family reached it.

#### AgentKitTools-File-DenialsAreResults: Denials Are Results

**Test**: `File_Family_DeniedRequest_ReturnsAResultWithoutThrowing`

The listed tests prove a refused request returns a result rather than throwing.

#### AgentKitTools-File-SafeMutation: Safe Mutation

**Test**: `File_Family_CopyThenDelete_ByRelativeNames_WorksEndToEnd`

The listed tests prove a copy-then-delete round-trip a model performs by bare relative names works
end to end.

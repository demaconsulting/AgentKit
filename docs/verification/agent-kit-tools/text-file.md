## TextFile Subsystem Verification Design

This document describes the subsystem-level verification strategy for the TextFile tool family.

### Verification Approach

The subsystem is verified through integration tests that exercise the family the way an application
does: composed through the AgentKitCore `ToolPackBuilder` under one access policy, then invoked
through the published tool list by name and argument dictionary, exactly as an agent runtime invokes
it. Nothing is mocked. The access policy and file system are real, and reparse-point scenarios use
real links, because the properties under verification belong to the real host boundary.

The scenarios here assert what belongs to the family as a whole: seven content tools, the shared line
buffer helper and `TextFilePack`. They verify that search, read, create, replace, cut-lines,
copy-lines and paste-lines use one policy and the same workspace names. The algorithm of any single
tool is verified in that unit's own document.

The boundary the subsystem is exercised at is deliberately the composed tool list rather than the
units' internal factories, because that list is what an application actually attaches. A tool that
could not be reached that way would not be reachable by an agent either.

Subsystem tests reside in `TextFile/TextFileTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: Each scenario creates and deletes its own temporary directory tree when files are
  needed
- **Reparse points**: Link-escape scenarios use the shared real reparse-point fixture and fail if
  the link cannot be created
- **Isolation**: Each test constructs its own policy, composition and temporary state; no state is
  shared

### Acceptance Criteria

A subsystem test run passes when all 7 requirement scenarios below, covering 11 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing tool, a wrong
family prefix, an ignored policy decision, a link escape, a thrown refusal, incorrect relative-path
behavior, unsafe mutation, or a ceiling violation returned as truncated content constitutes a
failure.

### Test Scenarios

#### AgentKitTools-TextFile-FamilyComposition: Family Composition

**Test**: `TextFile_Family_ComposedThroughBuilder_PublishesTheSevenContentTools`

**Test**: `TextFile_Family_HostDeclaringNoCapability_StillReceivesTheFamily`

The listed tests prove a composition attaching the family publishes the seven content tools in order;
a host declaring no capability still receives the family.

#### AgentKitTools-TextFile-GuardedConstruction: Guarded Construction

**Test**: `TextFile_Family_EveryTool_CarriesAValidatedNameAndDescription`

**Test**: `TextFile_Family_ToolResult_ReachesTheCallerUnserialized`

The listed tests prove every tool in the family carries a valid name and a description; a family
tool's result reaches the caller in the form the tool returned it.

#### AgentKitTools-TextFile-PolicyGoverned: Policy Governed

**Test**: `TextFile_Family_ReadWideWriteNarrow_PermitsTheReadAndRefusesTheEdit`

**Test**: `TextFile_Family_PathBeneathLinkOutsideRoot_IsRefusedByEveryTool`

The listed tests prove a read-wide, write-narrow policy permits the read and refuses the edit of one
path; a path reaching outside the permitted location through a link is refused by every editing tool
and never surfaced by search.

#### AgentKitTools-TextFile-NavigationAndPaging: Navigation And Paging

**Test**: `TextFile_Family_SearchReadEditLoop_WorksByRelativeNames`

The listed tests prove the search-read-edit loop works end to end by the bare relative names a model
sends, sharing one workspace across all seven tools.

#### AgentKitTools-TextFile-ContentEditing: Content Editing

**Test**: `TextFile_Family_SearchReadEditLoop_WorksByRelativeNames`

**Test**: `TextFile_Family_LargeBlockDuplication_CopiesCreatesAndPastes`

The listed tests prove the search-read-edit loop works end to end by the bare relative names a model
sends, sharing one workspace across all seven tools; and the large-block duplication scenario copies
a few hundred lines, creates a new file, pastes the block into it and confirms it, leaving the source
byte-identical.

#### AgentKitTools-TextFile-DenialsAreResults: Denials Are Results

**Test**: `TextFile_Family_DeniedRequest_ReturnsAResultWithoutThrowing`

The listed tests prove a refused request returns a result rather than throwing.

#### AgentKitTools-TextFile-ObservesPolicyLimits: Observes Policy Limits

**Test**: `TextFile_Family_ToolResult_ReachesTheCallerUnserialized`

The listed tests prove a family tool's result reaches the caller in the form the tool returned it.

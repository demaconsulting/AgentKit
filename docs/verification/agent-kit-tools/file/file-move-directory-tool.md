### FileMoveDirectoryTool Unit Verification Design

This document describes the unit-level verification strategy for the `FileMoveDirectoryTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses a real `PathPolicy` and a real temporary
directory tree; the move, the rename, the unconditional destination refusal, the move-into-itself
refusal and both policy refusals are checked through `InvokeAsync`. This keeps verification at the
same boundary the runtime or composing application uses, rather than proving a substitute behaves
consistently with itself.

Returned results and file system state are both checked where the unit mutates the file system. A
successful response must correspond to the real state change — a moved tree is verified by reading
a nested file at its new location, not merely by the directory existing — and a refusal must leave
the protected state unchanged.

The policy-governed scenarios are written as a **pair**, one refusing a read-only source and one
refusing a read-only destination. A single scenario would pass even if only one endpoint were
being judged, which is precisely the defect the pair exists to catch.

There is no overwrite scenario, because there is no overwrite: any existing destination is refused.
The absence is deliberate and is recorded here so that a later reader does not take it for a gap.

Unit tests reside in `File/FileMoveDirectoryToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 8 requirement scenarios below, covering 10 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, an endpoint judged by the wrong decision, a
destination replaced, a directory moved into itself, a rename the description does not declare, a
leaked path, a malformed request thrown as a framework error, or returned content that violates a
configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-File-MoveDirectoryTool-ToolName: Tool Name

**Test**: `FileMoveDirectoryTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed test proves the published tool name is the family-qualified name the pack claims, and
that it is a valid tool name.

##### AgentKitTools-File-MoveDirectoryTool-GuardedConstruction: Guarded Construction

**Test**: `FileMoveDirectoryTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed test proves a missing policy is a programming error rather than a denial.

##### AgentKitTools-File-MoveDirectoryTool-MovesDirectory: Moves Directory

**Test**: `FileMoveDirectoryTool_Move_PermittedTree_MovesItWithEverythingBeneathIt`

The listed test proves a permitted tree is moved with everything beneath it, addressed by bare
relative names. A file nested two levels down is read at its new location and confirmed
byte-identical, so the whole tree is proven to have moved rather than only its top level.

##### AgentKitTools-File-MoveDirectoryTool-Renames: Renames

**Test**: `FileMoveDirectoryTool_Move_DestinationInTheSameParent_RenamesTheDirectory`

The listed test proves a destination in the same parent renames the directory, and asserts that
the description a model reads names the rename. The description assertion is part of the scenario
rather than a separate one because a rename a model cannot discover is a capability the library
does not really offer.

##### AgentKitTools-File-MoveDirectoryTool-NoClobber: No Clobber

**Test**: `FileMoveDirectoryTool_Move_ExistingDestination_ReturnsDenialAndLeavesBoth`

The listed test proves an existing destination is refused unconditionally — there is no overwrite
argument that could permit it — and that both trees still hold their own distinguishable content
afterwards, so nothing beneath either was destroyed.

##### AgentKitTools-File-MoveDirectoryTool-RefusesMoveIntoItself: Refuses Move Into Itself

**Test**: `FileMoveDirectoryTool_Move_DestinationInsideTheSource_ReturnsDenialAndLeavesTheTree`

The listed test proves a destination nested inside the source is refused, the tree is intact, and
no partial destination was materialized.

##### AgentKitTools-File-MoveDirectoryTool-PolicyGoverned: Policy Governed

**Test**: `FileMoveDirectoryTool_Move_ReadOnlySource_ReturnsDenialAndLeavesItInPlace`

**Test**: `FileMoveDirectoryTool_Move_ReadOnlyDestination_ReturnsDenialAndLeavesItInPlace`

Security control at both endpoints. The first grants the tree's location read-only, so the read
decision would permit it and the write decision must not; the second grants a writable workspace
alongside a readable-but-unwritable second location and names that location as the destination.
Each is refused as `PathNotPermitted` and the source is confirmed still in place, so neither
endpoint can have its write permission inferred from a read.

##### AgentKitTools-File-MoveDirectoryTool-MissingSource: Missing Source

**Test**: `FileMoveDirectoryTool_Move_MissingSource_ReturnsDenialNamingNoTool`

**Test**: `FileMoveDirectoryTool_Move_SourceIsAFile_ReturnsDenialAndLeavesTheFile`

The two listed tests prove a missing source is reported as `TargetNotFound` with a plain fact that
names no other tool, and that a source naming a file is refused separately — a different mistake,
reported differently — with the file left in place.

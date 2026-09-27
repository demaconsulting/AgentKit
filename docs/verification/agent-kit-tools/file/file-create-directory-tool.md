### FileCreateDirectoryTool Unit Verification Design

This document describes the unit-level verification strategy for the `FileCreateDirectoryTool`
class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses a real `PathPolicy` and a real temporary
directory tree; creation, parent creation, the already-present outcome, the existing-file refusal
and the read-only refusal are all checked through `InvokeAsync`. This keeps verification at the
same boundary the runtime or composing application uses, rather than proving a substitute behaves
consistently with itself.

Returned results and file system state are both checked where the unit mutates the file system. A
successful response must correspond to the real state change, and a refusal must leave the
protected state unchanged.

The two success paths are verified separately and are asserted to say **different** things. That
is deliberate: the whole justification for treating an existing directory as a success rather than
a refusal is that the model can still tell which outcome occurred, so a test that accepted either
text would verify the wrong property.

The containment scenarios come in pairs for the same reason: one proving the refusal, one proving
the neighboring case it must **not** refuse. A rule that stopped every creation whose ancestors
were missing would pass a refusal test on its own and be useless, so the permitted case is
asserted beside it.

Unit tests reside in `File/FileCreateDirectoryToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 7 requirement scenarios below, covering 12 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, ignored policy decision, a directory created above
every permitted location, a partially created tree left behind by a refusal, a file replaced by a
directory, the two success outcomes reported in the same words, a leaked path, a malformed request
thrown as a framework error, or returned content that violates a configured ceiling constitutes a
failure.

#### Test Scenarios

##### AgentKitTools-File-CreateDirectoryTool-ToolName: Tool Name

**Test**: `FileCreateDirectoryTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed test proves the published tool name is the family-qualified name the pack claims, and
that it is a valid tool name.

##### AgentKitTools-File-CreateDirectoryTool-GuardedConstruction: Guarded Construction

**Test**: `FileCreateDirectoryTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed test proves a missing policy is a programming error rather than a denial.

##### AgentKitTools-File-CreateDirectoryTool-CreatesDirectory: Creates Directory

**Test**: `FileCreateDirectoryTool_CreateDirectory_PermittedPath_CreatesIt`

**Test**: `FileCreateDirectoryTool_CreateDirectory_NestedPath_CreatesEveryMissingParent`

**Test**: `FileCreateDirectoryTool_CreateDirectory_MissingGrantRootWhoseParentExists_CreatesBothLevels`

The three listed tests prove a permitted directory is created; that a path naming a directory
two levels below anything that exists creates every level along the way — so a model need not
create them one at a time; and that a grant root that does not yet exist is itself created, since
the root is a permitted location. The third is the companion the refusal scenario below needs: it
is what shows that rule bites on ancestors the grants do not reach rather than on missing
ancestors as such.

##### AgentKitTools-File-CreateDirectoryTool-NeverCreatesAnUngrantedParent: Never Creates an Ungranted Parent

**Test**: `FileCreateDirectoryTool_CreateDirectory_MissingParentAboveTheGrantRoot_ReturnsDenialAndCreatesNothing`

**Test**: `FileCreateDirectoryTool_Create_Description_DeclaresTheParentBoundary`

Security control, and one a single-path check cannot express: the unit's one creation call
materializes every missing component of the path, so the writes it performs are the named
directory plus each absent directory above it. The scenario roots a read-write grant two levels
below anything that exists — a configuration lexical resolution permits, because a grant root need
not exist — and names a directory inside that grant. The test asserts the refusal is
`PathNotPermitted`, that it discloses no host location, and then asserts all three levels absent:
the ungranted ancestor, the grant root, and the named directory. That last triple is the point;
a refusal that had already created the permitted part would leave the family's promise that a
refusal means nothing happened untrue for this unit alone. Run against a check that asks the
question of the named path instead of each ancestor, the tool answers `Created the directory,
including any missing parent directories` and materializes a directory above every grant, so the
scenario is measured rather than assumed. The second test asserts the description declares the
boundary and that such a refusal creates nothing, since the description is all a model has to go
on when choosing the tool.

##### AgentKitTools-File-CreateDirectoryTool-ExistingDirectory: Existing Directory

**Test**: `FileCreateDirectoryTool_CreateDirectory_ExistingDirectory_ReportsItExistedAndLeavesContents`

**Test**: `FileCreateDirectoryTool_Create_Description_NamesBothOutcomes`

The two listed tests prove an already-present directory produces a success stating that it already
existed — asserted **not** to be the created text — with a file it already held confirmed
byte-identical afterwards; and that the description a model reads declares both outcomes, so a
model can know a repeat request is safe before it makes one.

##### AgentKitTools-File-CreateDirectoryTool-RefusesExistingFile: Refuses Existing File

**Test**: `FileCreateDirectoryTool_CreateDirectory_ExistingFile_ReturnsDenialAndLeavesTheFile`

The listed test proves a path occupied by a file is refused and the file's content is confirmed
unchanged, so a file is never replaced by a directory.

##### AgentKitTools-File-CreateDirectoryTool-PolicyGoverned: Policy Governed

**Test**: `FileCreateDirectoryTool_CreateDirectory_ReadOnlyLocation_ReturnsDenialAndCreatesNothing`

Security control: the location is granted read-only, so the read decision would permit the path
and the write decision must not. The request is refused as `PathNotPermitted` and the directory is
confirmed absent afterwards, so write access is proven not to be inferred from read access.

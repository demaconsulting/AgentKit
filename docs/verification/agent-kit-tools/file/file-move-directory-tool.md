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
being judged, which is precisely the defect the pair exists to catch. The two link-traversal
scenarios are written as the same kind of pair, for the same reason, and — because path
containment is a security control — they exercise a **real reparse point** through the shared
`DirectoryLink` helper, which fails the test rather than skipping when the platform refuses to
create one. A skipped test leaves no entry in the results, so the requirement would appear covered
with no evidence behind it.

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

A unit test run passes when all 10 requirement scenarios below, covering 15 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, an endpoint judged by the wrong decision, a
destination replaced, a directory moved into itself, a real child read as lying outside the
source, an endpoint accepted that is reached through a link out of the permitted location, an
entry the policy withholds relocated as part of a tree, an entry landed where the policy permits
no writing, a
link's target disturbed or named in a refusal, a rename the description does not declare, a
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

**Test**: `FileMoveDirectoryTool_Move_DestinationIsAChildNamedWithLeadingDots_IsTreatedAsInsideTheSource`

The first listed test proves a destination nested inside the source is refused, the tree is
intact, and no partial destination was materialized. The second pins the boundary the first cannot
see: a genuine child named `..foo`, for which the framework's relative-path answer begins with two
dots. It asserts the refusal is the destination-inside-source one rather than the opaque
"could not be moved" a first-two-characters test produces, so a real child is never read as an
escape.

##### AgentKitTools-File-MoveDirectoryTool-EndpointThroughALinkIsRefused: An Endpoint Through a Link Is Refused

**Test**: `FileMoveDirectoryTool_Move_SourceReachedThroughALink_ReturnsDenialAndLeavesTheTargetIntact`

**Test**: `FileMoveDirectoryTool_Move_DestinationReachedThroughALink_ReturnsDenialAndLeavesTheTargetIntact`

Security control at both endpoints, and the pair the lexical path resolution makes necessary. A
real link inside the permitted location points at an ungranted sibling; the first test names a
source *through* that link, the second a destination through it. Each asserts the refusal is
`PathNotPermitted`, that it names the offending component as the model spelled it, that it does
**not** contain the link's target path, and — the assertion that matters — that the tree beyond
the link is untouched and nothing was placed there. Run against an implementation without the
ancestor walk both answer `Moved the directory and everything beneath it`, with a tree taken out
of, or placed into, a location outside every grant.

**What the pair is evidence of.** It verifies *pre-flight* classification: both endpoints are
judged as they stand when the request is judged, before anything is moved. It is deliberately not
evidence of race resistance, and no scenario here claims to be — portable .NET exposes no
handle-relative, no-follow directory move, so a process writing inside a location the operator
already granted can replace a checked component between the classification and the
`Directory.Move`, and a second path check would only move that window. The unit states that limit
rather than testing for a guarantee it does not make.

##### AgentKitTools-File-MoveDirectoryTool-EveryEntryIsPolicyJudged: Every Entry Is Policy Judged

**Test**: `FileMoveDirectoryTool_Move_SourceHoldingAnEntryThePolicyWithholds_ReturnsDenialAndLeavesTheTree`

**Test**: `FileMoveDirectoryTool_Move_DestinationWouldHoldAnEntryThePolicyWithholds_ReturnsDenialAndLeavesTheTree`

Security control, written as a pair for the same reason the link scenarios are: the source
question and the landing question are different questions, and a check on one end alone would
leave the other open. The first grants the workspace read-write while excluding `*.key`, so the
write decision permits both paths the request names and refuses a file inside the directory being
moved; it asserts a `PathNotPermitted` refusal naming `drafts/secret.key` in the model's own
spelling, no host path disclosed, nothing at the destination, and both files still at the source.

The second is the half only a landing check can catch. The policy grants the workspace without
exclusions and a second location *with* one, and the request moves a directory holding key
material into that second location. Every entry is writable where it stands, and the destination
directory itself is permitted — only the place the file would land is refused, and that is a
location no request ever named. The test asserts a `PathNotPermitted` refusal naming
`../outside/vault/drafts/secret.key` in the model's own relative spelling, that nothing was
created at the destination, and that the file is still at its source with its content unchanged.

Run against an implementation that pre-flights neither, both scenarios fail and the tool answers
`Moved the directory and everything beneath it.` with the withheld file genuinely relocated — in
the second case into the location the policy excludes it from — so these scenarios are measured
rather than assumed.

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

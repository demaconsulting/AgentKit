### TextFilePasteLinesTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFilePasteLinesTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy`, real files and a real line
buffer; insert locations, appends, named buffers and denials are checked through `InvokeAsync`. This
keeps verification at the same boundary the runtime or composing application uses, rather than
proving a substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `TextFile/TextFilePasteLinesToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 7 requirement scenarios below, covering 10 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-PasteTool-ToolName: Tool Name

**Test**: `TextFilePasteLinesTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims.

##### AgentKitTools-TextFile-PasteTool-GuardedConstruction: Guarded Construction

**Test**: `TextFilePasteLinesTool_Create_NullArguments_ThrowArgumentNullException`

The listed tests prove a missing policy or buffer is a programming error rather than a denial.

##### AgentKitTools-TextFile-PasteTool-RoundTrip: Round Trip

**Test**: `TextFilePasteLines_CutThenPasteAtSameLine_ReproducesTheFileExactly`

**Test**: `TextFilePasteLines_OmittedAtLine_AppendsToEnd`

**Test**: `TextFilePasteLines_NamedBuffer_RelocatesToAnotherFile`

The listed tests prove an omitted atLine appends the captured text to the end of the file; a named
buffer relocates a cut fragment to a different file.

##### AgentKitTools-TextFile-PasteTool-EmptyBuffer: Empty Buffer

**Test**: `TextFilePasteLines_EmptyBuffer_ReturnsDenialNamingTheBuffer`

The listed tests prove pasting from an empty buffer is a refusal naming the buffer, not a silent
no-op.

##### AgentKitTools-TextFile-PasteTool-MissingFile: Missing File

**Test**: `TextFilePasteLines_MissingFile_RefusesNamingNoTool`

The listed test proves a paste into a file that does not exist is refused as `TargetNotFound` with a
plain fact that names no other tool.

##### AgentKitTools-TextFile-PasteTool-PopulatedSlotGuidance: Populated Slot Guidance

**Test**: `TextFilePasteLines_EmptyDefaultSlot_NoOtherSlotPopulated_StatesTheFactNamingNoTool`

**Test**: `TextFilePasteLines_EmptySlot_AnotherSlotPopulated_NamesThatSlot`

The listed tests prove an empty-slot refusal states the bare fact when no slot holds content, and
names the populated slots as a statement of fact when another slot does — so a model that omitted the
name learns which name to pass instead of re-reading the source — while naming no tool to run.

##### AgentKitTools-TextFile-PasteTool-PolicyGoverned: Policy Governed

**Test**: `TextFile_Family_ReadWideWriteNarrow_PermitsTheReadAndRefusesTheEdit`

The listed tests prove a read-wide, write-narrow policy permits the read and refuses the edit of one
path.

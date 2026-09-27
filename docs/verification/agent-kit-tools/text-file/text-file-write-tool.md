### TextFileWriteTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileWriteTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy`, a real `TextFileLineBuffers`
instance and real files; written content, captured content, refused targets and read-only denials
are checked through `InvokeAsync`. This keeps verification at the same boundary the runtime or
composing application uses, rather than proving a substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change, and a refusal must leave the protected
state unchanged.

Three scenarios are load-bearing rather than illustrative, because each pins a safety property the
unit would otherwise lose silently. The verbatim-capture scenario compares the captured text against
the previous file content byte for byte, including a `\r\n` terminator, a blank line and a trailing
newline, so deleting the capture or normalizing the text on the way in fails. The default-buffer
scenario stages a fragment in the default slot before the write and asserts it survives, so
redirecting the capture to the default slot fails. The read-only-location scenario asserts both the
refusal and the unchanged file, so substituting the read decision for the write decision fails.

Unit tests reside in `TextFile/TextFileWriteToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, buffer, tool or helper state; no state is
  shared

#### Acceptance Criteria

A unit test run passes when all 8 requirement scenarios below, covering 18 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, a description that does not name the sibling tools, accepted null construction input,
an ignored policy decision, content destroyed without being captured, a capture landing in the
default slot, a directory created that the model did not ask for, a binary file replaced, a refusal
that prescribes a remedy, or a malformed request thrown as a framework error constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-WriteTool-ToolName: Tool Name

**Test**: `TextFileWriteTool_ToolName_Constant_IsTheFamilyQualifiedName`

**Test**: `TextFileWriteTool_Create_ConstructedTool_CarriesTheToolNameAndADescriptionNamingItsSiblings`

The listed tests prove the published tool name is the family-qualified name the pack claims, and
that the constructed tool carries that name together with a description naming the create and
replace tools, naming the slot displaced content is recoverable from, and stating that only the most
recent overwrite is kept there.

##### AgentKitTools-TextFile-WriteTool-GuardedConstruction: Guarded Construction

**Test**: `TextFileWriteTool_Create_NullArguments_ThrowArgumentNullException`

The listed tests prove a missing policy or a missing recovery buffer is a programming error rather
than a denial, so neither an unguarded write tool nor one that could destroy content without
capturing it can be constructed.

##### AgentKitTools-TextFile-WriteTool-SetsContent: Sets Content

**Test**: `TextFileWriteTool_Write_AbsentFile_CreatesItAndReportsItWasCreated`

**Test**: `TextFileWriteTool_Write_ExistingFile_ReplacesTheContentAndReportsItWasReplaced`

**Test**: `TextFileWriteTool_Write_EmptyContent_EmptiesTheFileAndReportsZeroLines`

The listed tests prove an absent file is brought into existence with the given content and reported
as created; an existing file's content is replaced wholesale and reported as replaced; and empty
content empties the file rather than being refused, reported as zero lines to match the line model
every other tool in the family addresses.

##### AgentKitTools-TextFile-WriteTool-CapturesPreviousContent: Captures Previous Content

**Test**: `TextFileWriteTool_Write_ExistingFile_CapturesThePreviousContentVerbatim`

**Test**: `TextFileWriteTool_Write_ExistingFile_LeavesTheDefaultBufferUntouched`

**Test**: `TextFileWriteTool_Write_AbsentFile_CapturesNothingAndSaysSo`

**Test**: `TextFileWriteTool_Write_PreviouslyEmptyFile_CapturesNothingAndSaysSo`

**Test**: `TextFileWriteTool_Write_SecondOverwrite_ReplacesTheCapturedContent`

The listed tests prove the file's previous content reaches the recovery buffer byte for byte —
carriage returns, blank lines and trailing newline included — before it is destroyed; that a
fragment already staged in the default slot survives an overwrite untouched, so a capture the model
never requested cannot displace one it did; that an absent file and an existing but empty file each
capture nothing and say so; and that a second overwrite replaces the first capture, pinning the
honest limit that only the most recent overwrite is recoverable.

The scenarios are sequential, and that is what the requirement claims: the capture is a sequential
guarantee. Read, capture and write are not serialized against another writer of the same file, so
two concurrent writes can both read the same content and the one that lands second can destroy
content the buffer never held. No scenario asserts otherwise, because the unit promises no more
than the sequential case — the limit is stated in the tool description, the class remarks, the
design chapter and the requirement's justification rather than left for a reader to discover.

##### AgentKitTools-TextFile-WriteTool-PolicyGoverned: Policy Governed

**Test**: `TextFileWriteTool_Write_ReadOnlyLocation_ReturnsDenialAndLeavesTheFileUnchanged`

**Test**: `TextFileWriteTool_Write_PathOutsideThePolicy_ReturnsDenialAndWritesNothing`

The listed tests prove a write into a read-only location is refused and the file there is left
unchanged, and a path outside the permitted location is refused with nothing written there — so
write access is never inferred from read access.

##### AgentKitTools-TextFile-WriteTool-CreatesNoDirectory: Creates No Directory

**Test**: `TextFileWriteTool_Write_DirectoryPath_ReturnsDenial`

**Test**: `TextFileWriteTool_Write_MissingParentDirectory_ReturnsDenialAndCreatesNoDirectory`

The listed tests prove a path naming a directory is refused rather than treated as a file, and a
missing parent directory is refused rather than materialized.

##### AgentKitTools-TextFile-WriteTool-RefusesNonTextTarget: Refuses Non-Text Target

**Test**: `TextFileWriteTool_Write_BinaryFile_ReturnsDenialAndLeavesItUnchanged`

**Test**: `TextFileWriteTool_Write_Denial_PrescribesNoRemedy`

The listed tests prove a file holding binary content is refused, left byte-identical and captured
nowhere — because content that cannot be captured for recovery must not be destroyed — and that the
refusal states the fact while opening no redirect sentence and naming no sibling tool.

##### AgentKitTools-TextFile-WriteTool-MalformedRequest: Malformed Request

**Test**: `TextFileWriteTool_Write_MissingPathOrContent_ReturnsDenialWithoutThrowing`

The listed tests prove an absent path argument and an absent content argument are each refused
rather than throwing, with no file written in either case.

### TextFileReadTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileReadTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy` and real files; ranged, numbered
output and binary refusals are checked through `InvokeAsync`. This keeps verification at the same
boundary the runtime or composing application uses, rather than proving a substitute behaves
consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `TextFile/TextFileReadToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 12 requirement scenarios below, covering 23 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-ReadTool-ToolName: Tool Name

**Test**: `TextFileReadTool_ToolName_Constant_IsTheFamilyQualifiedName`

**Test**: `TextFileReadTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

The listed tests prove the published tool name is the family-qualified name the pack claims; the
constructed tool carries the published name and a non-empty description.

##### AgentKitTools-TextFile-ReadTool-GuardedConstruction: Guarded Construction

**Test**: `TextFileReadTool_Create_NullPolicy_ThrowsArgumentNullException`

**Test**: `TextFileReadTool_Read_PermittedFile_ResultIsPlainTextNotJsonElement`

The listed tests prove a missing policy is a programming error rather than a denial; the result
reaches the caller as plain text rather than serialized JSON.

##### AgentKitTools-TextFile-ReadTool-RelativePath: Relative Path

**Test**: `TextFileReadTool_Read_BareFileName_ReturnsTheFileContents`

The listed tests prove a bare relative name is read from the working directory and the header echoes
it.

##### AgentKitTools-TextFile-ReadTool-DenyOutsideRoot: Deny Outside Root

**Test**: `TextFileReadTool_Read_PathOutsideTheReadRoot_ReturnsDenial`

The listed test proves a path outside the read grant is refused, disclosing the permitted location,
and without disclosing the file's content.

##### AgentKitTools-TextFile-ReadTool-RangedPaging: Ranged Paging

**Test**: `TextFileReadTool_Read_RangedWindow_ReturnsOnlyThoseLines`

**Test**: `TextFileReadTool_Read_LineCountPastEndOfFile_ClampsToTheTotal`

The listed tests prove a ranged read returns only the requested window, with a header naming it and
the file's true total; a lineCount that runs past the end of the file is clamped to the true total.

##### AgentKitTools-TextFile-ReadTool-LineNumbered: Line Numbered

**Test**: `TextFileReadTool_Read_PermittedFile_ReturnsNumberedContentWithHeader`

The listed tests prove a whole-file read carries the range header and the numbered content.

##### AgentKitTools-TextFile-ReadTool-PastEndOfFile: Past End Of File

**Test**: `TextFileReadTool_Read_StartLinePastEndOfFile_ReturnsEmptyWindowNamingTotal`

**Test**: `TextFileReadTool_Read_EmptyFile_ReturnsZeroLineHeaderNotDenial`

The listed tests prove a start line past the end of the file is an honest empty window naming the
true total, not a refusal; an empty file reads as an honest empty window rather than a refusal.

##### AgentKitTools-TextFile-ReadTool-MissingOrDirectory: Missing Or Directory

**Test**: `TextFileReadTool_Read_MissingFile_ReturnsDenialNamingNoTool`

**Test**: `TextFileReadTool_Read_DirectoryPath_ReturnsDenialNamingNoTool`

The listed tests prove a missing file is refused with a plain fact that names no other tool; a
directory is refused the same way.

##### AgentKitTools-TextFile-ReadTool-MalformedRequest: Malformed Request

**Test**: `TextFileReadTool_Read_MissingPathArgument_ReturnsDenialWithoutThrowing`

**Test**: `TextFileReadTool_Read_InvalidStartLine_ReturnsDenialWithoutThrowing`

The listed tests prove a missing path argument is a refusal, not a framework error; an invalid
paging argument is a refusal, not an exception.

##### AgentKitTools-TextFile-ReadTool-BinaryRefused: Binary Refused

**Test**: `TextFileReadTool_Read_BinaryImageFile_ReturnsDenialRedirectingToImageRead`

**Test**: `TextFileReadTool_Read_BinaryNonImageFile_ReturnsDenialWithoutRedirect`

**Test**: `TextFileReadTool_Read_Utf16BomTextFile_ReturnsTheFileContents`

The listed tests prove a binary image is refused as unsupported media and redirected to the image
tool; a non-image binary file is refused without a redirect; a byte-order-marked UTF-16 file still
reads as text, despite its NUL bytes.

##### AgentKitTools-TextFile-ReadTool-ReadCeiling: Read Ceiling

**Test**: `TextFileReadTool_Read_RangedWindowInLargeFile_ReturnsThatWindowNotADenial`

**Test**: `TextFileReadTool_Read_RangedWindowNearEndOfLargeFile_ReturnsTailWindow`

**Test**: `TextFileReadTool_Read_UnrangedLargeFile_ReturnsDenialNamingRecourseAndTotal`

**Test**: `TextFileReadTool_Read_LineLargerThanTheReadCeiling_ReturnsDenialNamingRecourse`

The listed tests prove a file larger than the read ceiling is paged when a window is requested,
retrieving a specific mid-file line and a tail window rather than a denial; an unranged read of such a
file is refused with a message that names the file's total line count and the paging arguments; and a
single line larger than the read ceiling is likewise refused with recourse rather than truncated.

##### AgentKitTools-TextFile-ReadTool-ResultCeiling: Result Ceiling

**Test**: `TextFileReadTool_Read_TextBeyondTheResultCeiling_ReturnsDenialRatherThanTruncated`

The listed tests prove a window beyond the result ceiling is refused rather than truncated.

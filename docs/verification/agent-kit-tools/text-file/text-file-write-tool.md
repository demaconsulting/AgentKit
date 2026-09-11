### TextFileWriteTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileWriteTool` class.

#### Verification Approach

Nothing is mocked or stubbed. A write tool that behaved correctly against a substituted policy and
a substituted file system would prove nothing about the one property that matters — that it refuses
a destination the write rule does not permit and leaves the host unchanged when it does. Each
scenario therefore constructs a real `PathPolicy` over a temporary directory tree it creates,
builds the tool through its internal factory, and invokes it as a runtime does.

Two scenarios assert the state of the file system after a refusal, not merely the text of the
refusal: a refused write that nevertheless wrote would be a refusal in name only. The scenario
proving a readable path is not thereby writable first reads the path successfully through the read
tool, so the refusal cannot be explained away as the path being unreachable.

Unit tests reside in `TextFile/TextFileWriteToolTests.cs`, with the shared reparse-point fixture in
`TextFile/ReparsePointFixture.cs`, within the `DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: Each scenario creates and deletes its own temporary tree containing a permitted
  root and a sibling directory outside it; several scenarios read the tree back to confirm what was
  and was not written
- **Reparse points**: A directory junction on Windows, a symbolic link elsewhere; a failure to
  create one fails the test rather than skipping it
- **Isolation**: Each test constructs its own policy, tool and tree; no state is shared

#### Acceptance Criteria

A unit test run passes when all fifteen scenarios below pass without error or exception beyond those
explicitly asserted. A permitted write that does not reach the file, a refused write that does, a
directory created by the tool, an exception raised at a malformed request, and a refusal containing
a host path each constitute a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-WriteTool-ToolName: The Name Constant Is Family-Qualified

**Test**: `TextFileWriteTool_ToolName_Constant_IsTheFamilyQualifiedName`

Asserts the constant is `text_file_write` and begins with the pack's declared family prefix.

##### AgentKitTools-TextFile-WriteTool-ToolName: The Constructed Tool Carries the Name and a Description

**Test**: `TextFileWriteTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

Normal operation: the tool a model is offered carries the published name and a non-empty
description it can choose by.

##### AgentKitTools-TextFile-WriteTool-GuardedConstruction: A Null Policy Throws

**Test**: `TextFileWriteTool_Create_NullPolicy_ThrowsArgumentNullException`

Error path at construction: a write tool governed by nothing is the most dangerous thing this
library could hand an agent, and this scenario pins that it cannot be built.

##### AgentKitTools-TextFile-WriteTool-GuardedConstruction: The Result Is Plain Text, Not a JsonElement

**Test**: `TextFileWriteTool_Write_PermittedPath_ResultIsPlainTextNotJsonElement`

Asserts both the positive and the negative type, naming the silent failure the guard prevents.

##### AgentKitTools-TextFile-WriteTool-WritePermittedFile: A Permitted Write Reaches the File and Is Confirmed

**Test**: `TextFileWriteTool_Write_PermittedPath_WritesTheContentAndConfirms`

Normal operation: asserts the confirmation text exactly — a character count and no location — and
reads the file back to confirm the content actually arrived.

##### AgentKitTools-TextFile-WriteTool-WritePermittedFile: Empty Content Writes an Empty File

**Test**: `TextFileWriteTool_Write_EmptyContent_WritesAnEmptyFile`

Boundary condition: an empty string is not an absent one. The file is created and is empty.

##### AgentKitTools-TextFile-WriteTool-IndependentWriteRule: A Readable Path Is Not Thereby Writable

**Test**: `TextFileWriteTool_Write_ReadableButNotWritablePath_ReturnsDenial`

The scenario that makes the independent read and write rules observable. The path is read
successfully first, then refused for writing, and the file on disk is confirmed unchanged.

##### AgentKitTools-TextFile-WriteTool-DenyOutsideRoot: A Path Outside the Write Root Is Refused

**Test**: `TextFileWriteTool_Write_PathOutsideTheWriteRoot_ReturnsDenial`

Error path: asserts the refusal reason and that no file was created outside the permitted location.

##### AgentKitTools-TextFile-WriteTool-DenyOutsideRoot: A Path Beneath a Link Outside the Root Is Refused

**Test**: `TextFileWriteTool_Write_PathBeneathLinkOutsideRoot_ReturnsDenial`

Security control: a real reparse point inside the permitted root points at a sibling directory; the
write through it is refused and no file appears at the link's target.

##### AgentKitTools-TextFile-WriteTool-ReplacesExistingContent: An Existing File Is Replaced

**Test**: `TextFileWriteTool_Write_ExistingFile_ReplacesItsContent`

Normal operation: the file afterwards holds exactly the new content, proving replacement rather
than concatenation.

##### AgentKitTools-TextFile-WriteTool-MissingParentDenied: A Missing Parent Is Refused and Nothing Is Created

**Test**: `TextFileWriteTool_Write_MissingParentDirectory_ReturnsDenialAndCreatesNothing`

Error path: asserts the refusal reason and, crucially, that neither the directory nor the file was
materialized — the side effect the design refuses to perform.

##### AgentKitTools-TextFile-WriteTool-DirectoryRefused: A Directory Path Is Refused

**Test**: `TextFileWriteTool_Write_DirectoryPath_ReturnsDenial`

Error path: the directory still exists afterwards, so the refusal is not a destructive no-op.

##### AgentKitTools-TextFile-WriteTool-MalformedRequestDenied: An Empty Path Is Refused

**Test**: `TextFileWriteTool_Write_EmptyPath_ReturnsDenialWithoutThrowing`

Error path: the policy is unrestricted, so only the request itself can be at fault, and the outcome
must be a returned refusal rather than an exception.

##### AgentKitTools-TextFile-WriteTool-MalformedRequestDenied: Absent Content Is Refused

**Test**: `TextFileWriteTool_Write_NullContent_ReturnsDenialWithoutThrowing`

Error path: the request omits its content entirely. The refusal is returned and no file is created,
distinguishing absent content from the legitimate empty-content case above.

##### AgentKitTools-TextFile-WriteTool-DenialRedaction: A Refusal Contains No Host Detail

**Test**: `TextFileWriteTool_Write_DeniedPath_DenialTextContainsNoHostDetail`

Disclosure control: asserts the refusal contains neither the permitted location, the requested
location, the requested file name, nor a directory separator.

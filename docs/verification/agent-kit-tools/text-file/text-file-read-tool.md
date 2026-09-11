### TextFileReadTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileReadTool` class.

#### Verification Approach

Nothing is mocked or stubbed. The unit's whole purpose is to behave correctly against a real access
policy and a real file system, and a substituted policy would verify only the substitute. Each
scenario constructs a real `PathPolicy` over a temporary directory tree it creates, builds the tool
through its own internal factory, and invokes it the way a runtime does — through `InvokeAsync`
with a named argument dictionary — so that the delivery of the result through the guarded factory is
exercised rather than bypassed.

The scenario covering a link that escapes the permitted location uses a genuine reparse point rather
than a simulated one, because containment is a security control and a simulation would prove only
that the simulation was written consistently.

Unit tests reside in `TextFile/TextFileReadToolTests.cs`, with the shared reparse-point fixture in
`TextFile/ReparsePointFixture.cs`, within the `DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: Each scenario creates and deletes its own temporary tree containing a permitted
  root and a sibling directory outside it
- **Reparse points**: A directory junction on Windows, a symbolic link elsewhere; a failure to
  create one fails the test rather than skipping it
- **Isolation**: Each test constructs its own policy, tool and tree; no state is shared

#### Acceptance Criteria

A unit test run passes when all sixteen scenarios below pass without error or exception beyond those
explicitly asserted. A permitted file that does not read, a refused file whose content leaks, a
truncated result where a refusal was required, an exception raised at a malformed request, and a
refusal containing a host path each constitute a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-ReadTool-ToolName: The Name Constant Is Family-Qualified

**Test**: `TextFileReadTool_ToolName_Constant_IsTheFamilyQualifiedName`

Asserts the constant is `text_file_read` and begins with the pack's declared family prefix, so the
name a redirect points at is the name the pack publishes.

##### AgentKitTools-TextFile-ReadTool-ToolName: The Constructed Tool Carries the Name and a Description

**Test**: `TextFileReadTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

Normal operation: the tool a model is offered carries the published name and a non-empty
description it can choose by.

##### AgentKitTools-TextFile-ReadTool-GuardedConstruction: A Null Policy Throws

**Test**: `TextFileReadTool_Create_NullPolicy_ThrowsArgumentNullException`

Error path at construction: a tool governed by no policy cannot be built. This is an exception
rather than a
denial because it is a programming error in the composing application, not something a model can
provoke.

##### AgentKitTools-TextFile-ReadTool-GuardedConstruction: The Result Is Plain Text, Not a JsonElement

**Test**: `TextFileReadTool_Read_PermittedFile_ResultIsPlainTextNotJsonElement`

Asserts both the positive and the negative type. The negative assertion names the failure being
prevented: without the guard the result would arrive serialized and nothing would report an error.

##### AgentKitTools-TextFile-ReadTool-ReadPermittedFile: A Permitted File Returns Its Contents

**Test**: `TextFileReadTool_Read_PermittedFile_ReturnsTheFileContents`

Normal operation: the content asserted is a distinctive marker written by the test, so the scenario
proves the real file was reached rather than a plausible-looking substitute.

##### AgentKitTools-TextFile-ReadTool-ReadPermittedFile: An Empty File Returns Empty Text

**Test**: `TextFileReadTool_Read_EmptyFile_ReturnsEmptyTextNotDenial`

Boundary condition: an empty file is a legitimate result, so reporting a refusal would tell the
model something untrue about the host.

##### AgentKitTools-TextFile-ReadTool-DenyOutsideRoot: A Path Outside the Read Root Is Refused

**Test**: `TextFileReadTool_Read_PathOutsideTheReadRoot_ReturnsDenial`

Error path: asserts the refusal reason and that the file's content does not appear in the result.

##### AgentKitTools-TextFile-ReadTool-DenyOutsideRoot: A File Beneath a Link Outside the Root Is Refused

**Test**: `TextFileReadTool_Read_FileBeneathLinkOutsideRoot_ReturnsDenial`

Security control: a real reparse point bridges the permitted root to a sibling directory. The
escaped file is first read through the link on disk, so a fixture that failed to create the link
fails the scenario rather than making it pass vacuously.

##### AgentKitTools-TextFile-ReadTool-MissingFileDenied: A Missing File Redirects to the Listing Tool

**Test**: `TextFileReadTool_Read_MissingFile_ReturnsDenialRedirectingToList`

Error path: asserts both the reason and that the refusal names `text_file_list`, which is what turns
a dead end into the agent's next step.

##### AgentKitTools-TextFile-ReadTool-DirectoryRefused: A Directory Redirects to the Listing Tool

**Test**: `TextFileReadTool_Read_DirectoryPath_ReturnsDenialRedirectingToList`

Error path: a directory has no text to return, and the operation the model almost certainly intended
has its own tool.

##### AgentKitTools-TextFile-ReadTool-ReadCeiling: A File Beyond the Read Ceiling Is Refused

**Test**: `TextFileReadTool_Read_FileLargerThanTheReadCeiling_ReturnsDenialNamingTheCeiling`

Boundary condition: asserts the ceiling is named in the refusal and that no part of the file's
content is returned, which is what distinguishes a refusal from a truncation.

##### AgentKitTools-TextFile-ReadTool-ReadCeiling: A File Exactly at the Read Ceiling Is Read

**Test**: `TextFileReadTool_Read_FileAtTheReadCeiling_IsRead`

Boundary condition on the other side of the same ceiling, pinning it as inclusive so no caller has
to guess.

##### AgentKitTools-TextFile-ReadTool-ResultCeiling: Text Beyond the Result Ceiling Is Refused

**Test**: `TextFileReadTool_Read_TextBeyondTheResultCeiling_ReturnsDenialRatherThanTruncatedText`

Boundary condition: a file within the read ceiling but beyond the tighter result ceiling is refused
with that ceiling named, proving the two budgets are checked separately.

##### AgentKitTools-TextFile-ReadTool-MalformedRequestDenied: An Empty Path Is Refused

**Test**: `TextFileReadTool_Read_EmptyPath_ReturnsDenialWithoutThrowing`

Error path: the policy is unrestricted, so only the request itself can be at fault, and the outcome
must be a returned refusal rather than an exception that would end the agent's turn.

##### AgentKitTools-TextFile-ReadTool-MalformedRequestDenied: A Whitespace Path Is Refused

**Test**: `TextFileReadTool_Read_WhitespacePath_ReturnsDenialWithoutThrowing`

Error path: a whitespace-only path would otherwise reach the policy, which raises rather than
refuses for a malformed argument.

##### AgentKitTools-TextFile-ReadTool-DenialRedaction: A Refusal Contains No Host Detail

**Test**: `TextFileReadTool_Read_DeniedPath_DenialTextContainsNoHostDetail`

Disclosure control: asserts the refusal contains neither the permitted location, the requested
location, the requested file name, nor a directory separator.

### ImageReadTool Unit Verification Design

This document describes the unit-level verification strategy for the `ImageReadTool` class.

#### Verification Approach

Nothing is mocked or stubbed. The unit's whole purpose is to behave correctly against a real access
policy and a real file system, and a substituted policy would verify only the substitute. Each
scenario constructs a real `PathPolicy` over a temporary directory tree it creates, writes binary
files into it directly, builds the tool through its own internal factory, and invokes it the way a
runtime does — through `InvokeAsync` with a named argument dictionary — so that the delivery of the
content result through the guarded factory is exercised rather than bypassed. Invoking through the
constructed tool is essential: a hand-built delegate would prove nothing about how the tool as
registered marshals its result.

The scenario covering a path outside the permitted location uses real directories, reusing the
fixture defined in the TextFile tests — an internal type in the same assembly — rather than a copy,
because containment is a security control and a simulation would prove only that the simulation was
written consistently. The relative-path and denial scenarios verify the current
policy model as this tool observes it: the working directory anchors bare names, grants carry the
read permission, and policy refusals disclose the refused request and permitted locations.

Unit tests reside in `Image/ImageReadToolTests.cs`, reusing the shared temporary-directory fixture
from `TextFile/TempDirectoryFixture.cs`, within the `DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: Each scenario creates and deletes its own temporary tree containing a permitted
  root and a sibling directory outside it, writing binary files directly
- **Isolation**: Each test constructs its own policy, tool and tree; no state is shared

#### Acceptance Criteria

A unit test run passes when all seventeen scenarios below pass without error or exception beyond
those explicitly asserted. Image content arriving as a `JsonElement`, a permitted file that does not
read, a relative name that is not read from the working directory, a refused file whose content
leaks, a PDF routed through the image result path, a truncated result where a refusal was required,
an exception or framework error raised at a malformed or omitted request, a refusal offering no way
forward, or a policy refusal that omits the request, permitted location, or access level constitutes
a failure.

#### Test Scenarios

##### AgentKitTools-Image-ReadTool-ToolName: The Name Constant Is Family-Qualified

**Test**: `ImageReadTool_ToolName_Constant_IsTheFamilyQualifiedName`

Asserts the constant is `image_read` and begins with the pack's declared family prefix, so the name
a redirect points at is the name the pack publishes.

##### AgentKitTools-Image-ReadTool-ToolName: The Constructed Tool Carries the Name and a Description

**Test**: `ImageReadTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

Normal operation: the tool a model is offered carries the published name and a non-empty description
it can choose by.

##### AgentKitTools-Image-ReadTool-GuardedConstruction: A Null Policy Throws

**Test**: `ImageReadTool_Create_NullPolicy_ThrowsArgumentNullException`

Error path at construction: a tool governed by no policy cannot be built. This is an exception
rather than a denial because it is a programming error in the composing application, not something a
model can provoke.

##### AgentKitTools-Image-ReadTool-GuardedConstruction: The Result Is a Content List, Not a JsonElement

**Test**: `ImageReadTool_Read_SupportedImage_ResultIsContentListNotJsonElement`

The load-bearing proof: asserts both the positive and the negative type — a `List<AIContent>` and
specifically not a `JsonElement`. The negative assertion names the failure being prevented: without
the guard the content would arrive serialized, the provider would never receive the image, and
nothing would report an error.

##### AgentKitTools-Image-ReadTool-ReadPermittedImage: A Permitted Image Returns Its Content

**Test**: `ImageReadTool_Read_SupportedImage_ReturnsDataContentWithMediaType`

Normal operation: asserts a caption followed by data content carrying the `image/png` media type and
the distinctive bytes the test wrote, so the scenario proves the real file was reached rather than a
plausible-looking substitute.

##### AgentKitTools-Image-ReadTool-ReadPermittedPdf: A Permitted PDF Returns Binary Content

**Test**: `ImageReadTool_Read_PermittedPdf_ReturnsBinaryContentWithPdfMediaType`

Normal operation on the other result path: a PDF is returned as data content carrying
`application/pdf`, still unserialized, confirming the dispatch that keeps a non-image media type off
the image result path — which would otherwise throw — while preserving the caption-plus-content
shape.

##### AgentKitTools-Image-ReadTool-DenyOutsideRoot: A Path Outside the Read Grant Is Refused

**Test**: `ImageReadTool_Read_PathOutsideTheReadRoot_ReturnsDenial`

Error path: a file in a sibling directory no read-capable grant permits is refused as
`PathNotPermitted`, before anything is learned about the file.

##### AgentKitTools-Image-ReadTool-UnsupportedTypeDenied: An Unsupported Type Redirects to the Text File Read Tool

**Test**: `ImageReadTool_Read_UnsupportedType_ReturnsDenialRedirectingToTextFileRead`

Error path: a permitted `.svg` is refused as an unsupported type, the refusal states that the file is
text and vector content, and it names `text_file_read` as the reader for that kind of content —
confirming the tool composes the refusal through the media-type map rather than
inventing its own.

##### AgentKitTools-Image-ReadTool-DirectoryRefused: A Directory Is Refused

**Test**: `ImageReadTool_Read_DirectoryPath_ReturnsDenial`

Error path: a directory has no visual content to return, so it is refused as a malformed request with
a plain statement of that fact; the assertion confirms the refusal prescribes no course of action.

##### AgentKitTools-Image-ReadTool-MissingFileDenied: A Missing File Is Refused

**Test**: `ImageReadTool_Read_MissingFile_ReturnsDenial`

Error path: a supported-type file that does not exist is refused as `TargetNotFound` with a plain
statement of that fact and no prescribed course of action, proving the
type is judged before existence and a supported extension that names no file is refused as not found
rather than as an unsupported type.

##### AgentKitTools-Image-ReadTool-BinaryCeiling: A File Beyond the Binary Ceiling Is Refused

**Test**: `ImageReadTool_Read_FileLargerThanTheBinaryCeiling_ReturnsDenialNamingTheCeiling`

Boundary condition: asserts the ceiling is named in the refusal, which is what distinguishes a
refusal from a truncation.

##### AgentKitTools-Image-ReadTool-BinaryCeiling: A File Exactly at the Binary Ceiling Is Read

**Test**: `ImageReadTool_Read_FileAtTheBinaryCeiling_IsRead`

Boundary condition on the other side of the same ceiling, pinning it as inclusive so no caller has
to guess.

##### AgentKitTools-Image-ReadTool-MalformedRequestDenied: An Empty Path Is Refused

**Test**: `ImageReadTool_Read_EmptyPath_ReturnsDenialWithoutThrowing`

Error path: the policy is unrestricted, so only the request itself can be at fault, and the outcome
must be a returned refusal rather than an exception that would end the agent's turn.

##### AgentKitTools-Image-ReadTool-MalformedRequestDenied: A Whitespace Path Is Refused

**Test**: `ImageReadTool_Read_WhitespacePath_ReturnsDenialWithoutThrowing`

Error path: a whitespace-only path would otherwise reach the policy as malformed input. The tool
returns a refusal rather than letting an exception end the turn.

##### AgentKitTools-Image-ReadTool-RelativePath: A Bare File Name Is Read From the Working Directory

**Test**: `ImageReadTool_Read_BareFileName_ReturnsTheImageContent`

Normal operation for the request a model actually makes. Asserts the caption and real bytes come
back for a name stated with no location at all, so a name a text file listing reported is usable
here.

##### AgentKitTools-Image-ReadTool-MalformedRequestDenied: Omitting the Path Argument Is Refused

**Test**: `ImageReadTool_Read_MissingPathArgument_ReturnsDenialWithoutThrowing`

The scenario that pins the parameter as optional. A parameter with no default fails inside the
function factory before the tool body is reached, and the model then receives an opaque framework
error rather than a refusal. Asserts the outcome is an `InvalidRequest` refusal naming the form a
path should take.

##### AgentKitTools-Image-ReadTool-DenialDisclosure: A Refusal Discloses the Permitted Location

**Test**: `ImageReadTool_Read_DeniedPath_DenialDisclosesPermittedLocation`

Disclosure behavior: the refused request is echoed, and the permitted working directory is named
with its `(read-write)` access level so a confined model learns where it may look instead.

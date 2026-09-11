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

The scenario covering a link that escapes the permitted location uses a genuine reparse point,
reusing the fixture defined in the TextFile tests — an internal type in the same assembly — rather
than a copy, because containment is a security control and a simulation would prove only that the
simulation was written consistently.

Unit tests reside in `Image/ImageReadToolTests.cs`, reusing the shared reparse-point fixture from
`TextFile/ReparsePointFixture.cs`, within the `DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: Each scenario creates and deletes its own temporary tree containing a permitted
  root and a sibling directory outside it, writing binary files directly
- **Reparse points**: A directory junction on Windows, a symbolic link elsewhere; a failure to
  create one fails the test rather than skipping it
- **Isolation**: Each test constructs its own policy, tool and tree; no state is shared

#### Acceptance Criteria

A unit test run passes when all sixteen scenarios below pass without error or exception beyond those
explicitly asserted. Image content arriving as a `JsonElement`, a permitted file that does not read,
a refused file whose content leaks, a PDF routed through the image result path, a truncated result
where a refusal was required, an exception raised at a malformed request, and a refusal containing a
host path each constitute a failure.

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

##### AgentKitTools-Image-ReadTool-DenyOutsideRoot: A Path Outside the Read Root Is Refused

**Test**: `ImageReadTool_Read_PathOutsideTheReadRoot_ReturnsDenial`

Error path: a file in a sibling directory the read rule does not permit is refused as
`PathNotPermitted`, before anything is learned about the file.

##### AgentKitTools-Image-ReadTool-DenyOutsideRoot: A File Beneath a Link Outside the Root Is Refused

**Test**: `ImageReadTool_Read_FileBeneathLinkOutsideRoot_ReturnsDenial`

Security control: a real reparse point bridges the permitted root to a sibling directory. The
escaped file is first read through the link on disk, so a fixture that failed to create the link
fails the scenario rather than making it pass vacuously.

##### AgentKitTools-Image-ReadTool-UnsupportedTypeDenied: An Unsupported Type Redirects to the Text File Read Tool

**Test**: `ImageReadTool_Read_UnsupportedType_ReturnsDenialRedirectingToTextFileRead`

Error path: a permitted `.svg` is refused as an unsupported type and the refusal names
`text_file_read`, confirming the tool composes the refusal through the media-type map rather than
inventing its own.

##### AgentKitTools-Image-ReadTool-DirectoryRefused: A Directory Is Refused

**Test**: `ImageReadTool_Read_DirectoryPath_ReturnsDenial`

Error path: a directory has no visual content to return and no listing tool to redirect to, so it is
refused as a malformed request.

##### AgentKitTools-Image-ReadTool-MissingFileDenied: A Missing File Is Refused

**Test**: `ImageReadTool_Read_MissingFile_ReturnsDenial`

Error path: a supported-type file that does not exist is refused as `TargetNotFound`, proving the
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

Error path: a whitespace-only path would otherwise reach the policy, which raises rather than
refuses for a malformed argument.

##### AgentKitTools-Image-ReadTool-DenialRedaction: A Refusal Contains No Host Detail

**Test**: `ImageReadTool_Read_DeniedPath_DenialTextContainsNoHostDetail`

Disclosure control: asserts the refusal contains neither the permitted location, the requested
location, the requested file name, nor a directory separator.

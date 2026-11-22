### ImageMediaTypes Unit Verification Design

This document describes the unit-level verification strategy for the `ImageMediaTypes` class.

#### Verification Approach

Nothing is mocked or stubbed. The unit is a pure function of a path's extension: it reads no state
and touches no file system, so each scenario simply calls a method with a file name and asserts on
the result. What must be verified is which extensions resolve to which media types, and how a file
whose type the family cannot read is refused — including the one case where naming a sibling reader
states what the file is, and the cases where a refusal states the type is unsupported and nothing
more.

Unit tests reside in `Image/ImageMediaTypesTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system or network access required
- **Isolation**: Each test calls the unit directly; no state is shared

#### Acceptance Criteria

A unit test run passes when all scenarios below pass without error or exception beyond those
explicitly asserted. A supported extension that fails to resolve, a case difference that changes the
result, an unsupported extension that resolves anyway, a readable-but-uncroppable extension that
resolves as croppable, a croppable refusal that names a sibling tool, an `.svg` read refused without
its redirect to `image_view`, and an `.svgz` or unknown type refused *with* an invented redirect each
constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Image-MediaTypes-SupportedTypes: Each Supported Extension Resolves

**Test**: `ImageMediaTypes_TryResolveMediaType_SupportedExtension_ReturnsMediaType`

Normal operation, run as a theory over `png`, `jpg`, `jpeg`, `gif`, `webp` and `pdf`, asserting each
resolves to the media type the family reads it as — the four image types to their `image/*` types,
`jpg` and `jpeg` to the one `image/jpeg`, and `pdf` to `application/pdf`.

##### AgentKitToolsVisual-Image-MediaTypes-SupportedTypes: An Extension Is Matched Case-Insensitively

**Test**: `ImageMediaTypes_TryResolveMediaType_ExtensionCaseInsensitive_ReturnsMediaType`

Boundary condition: a capitalized extension names the same content as its lower-case form and
resolves identically, so a caller is not refused for a difference that means nothing.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesUnknownType: An Unsupported Extension Does Not Resolve

**Test**: `ImageMediaTypes_TryResolveMediaType_UnsupportedExtension_ReturnsFalse`

Error path: an extension the family does not read reports as unsupported with no media type, so the
read tool refuses it rather than attempting a read it cannot complete.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesSvgRedirectsToView: An svg Is Stated to Be Vector Content

**Test**: `ImageMediaTypes_DenyUnsupportedType_Svg_RedirectsToImageView`

Error path stating what the file is: an `.svg` is refused as an unsupported type, the refusal states
that it is vector content, and it names `image_view` as the tool that draws that kind of content,
and the text file read tool is not named. Naming the tool survives the denial rule on the same basis
as the text file read tool's own binary-content refusal naming `image_read` — the naming *is* the
classification, not a prescribed way around the refusal.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesSvgzNoTool: An svgz Is Refused Without a Redirect

**Test**: `ImageMediaTypes_DenyUnsupportedType_Svgz_RefusesWithoutRedirect`

Error path without a redirect: an `.svgz` is gzip-compressed content no tool in the family reads, so
the refusal names no tool a retry would only fail at, and the assertion confirms neither the view
tool name nor the redirect sentence appears.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesUnknownType: Any Other Type Is Refused Without a Redirect

**Test**: `ImageMediaTypes_DenyUnsupportedType_UnknownType_RefusesWithoutRedirect`

Error path: an extension with no honest better tool to name is refused as an unsupported type with
no redirect invented, keeping the refusal truthful.

##### AgentKitToolsVisual-Image-MediaTypes-CroppableTypes: Each Croppable Extension Resolves

**Test**: `ImageMediaTypes_TryResolveCroppableMediaType_CroppableExtension_ReturnsMediaType`

Normal operation, run as a theory over `png`, `jpg` and `jpeg`, asserting each resolves to the
media type a region would be taken from it as.

##### AgentKitToolsVisual-Image-MediaTypes-CroppableTypes: A Croppable Extension Is Matched Case-Insensitively

**Test**: `ImageMediaTypes_TryResolveCroppableMediaType_ExtensionCaseInsensitive_ReturnsMediaType`

Boundary condition: a capitalized extension resolves identically, exactly as it does for a readable
type, so the two maps cannot disagree about case.

##### AgentKitToolsVisual-Image-MediaTypes-CroppableTypes: A Non-Croppable Extension Does Not Resolve

**Test**: `ImageMediaTypes_TryResolveCroppableMediaType_NonCroppableExtension_ReturnsFalse`

Error path, run as a theory over `gif`, `webp`, `pdf` and an extension the family cannot read at
all. Asserts the croppable set is genuinely narrower than the readable set, which is what keeps a
region request from becoming an accidental format conversion.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesNonCroppableType: A gif Is Refused Because Its Frame Is Unknowable

**Test**: `ImageMediaTypes_DenyNonCroppableType_Gif_StatesTheFrameIsUnknowableAndNamesTheCroppableTypes`

Error path: the refusal states that the format may hold more than one frame and that this tool
cannot tell how many, so a region of it would silently be a region of the first frame alone. It
names the types a region can be taken from, and names **no sibling tool** — a `.gif` genuinely is
an image, so naming the reader would offer a route to the content the refusal withheld rather than
classify the file. The reason asserted is the unknowable frame rather than animation, because a
single-frame `.gif` is the common case and would make an "animated" refusal false.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesNonCroppableType: A webp Is Refused in Its Own Words

**Test**: `ImageMediaTypes_DenyNonCroppableType_Webp_StatesItIsNotDecodedAndNamesTheCroppableTypes`

Error path with its own reason rather than the `.gif`'s: WebP is a format the family hands to
a provider without decoding, not one whose frame count it cannot establish.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesNonCroppableType: A pdf Names Rasterization

**Test**: `ImageMediaTypes_DenyNonCroppableType_Pdf_StatesRasterizationIsRequired`

Error path for the one admitted type that *looks* croppable and is not. Without the page-and-resolution
reason stated, a model would reasonably retry with different coordinates.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesSvgCropNoTool: An svg Region Request Names No Tool

**Test**: `ImageMediaTypes_DenyNonCroppableType_Svg_ReturnsDenialNamingNoTool`

Error path: the region refusal for an `.svg` states that it is vector content, names the types a
region can be taken from, and names neither the view tool nor any redirect — the drawing tool would
return the whole picture, not the region asked for.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesNonCroppableType: An svgz Is Refused Identically on Both Paths

**Test**: `ImageMediaTypes_DenyNonCroppableType_Svgz_RefusesWithoutRedirect`

The same equality assertion for the compressed vector form, with no tool named.

##### AgentKitToolsVisual-Image-MediaTypes-RefusesNonCroppableType: Any Other Type Is Refused Without a Redirect

**Test**: `ImageMediaTypes_DenyNonCroppableType_UnknownType_RefusesWithoutRedirect`

Error path: an extension the family cannot read at all is refused with no tool invented to send the
model to.

##### AgentKitToolsVisual-Image-MediaTypes-CroppableTypes: A Null Path Is a Programming Error

**Test**: `ImageMediaTypes_TryResolveCroppableMediaType_NullPath_ThrowsArgumentNullException`

Defensive scenario: an absent path is a mistake in the calling tool rather than anything a model
can provoke, so it is surfaced rather than converted into a refusal.

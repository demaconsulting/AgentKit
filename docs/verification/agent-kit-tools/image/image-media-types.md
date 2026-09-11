### ImageMediaTypes Unit Verification Design

This document describes the unit-level verification strategy for the `ImageMediaTypes` class.

#### Verification Approach

Nothing is mocked or stubbed. The unit is a pure function of a path's extension: it reads no state
and touches no file system, so each scenario simply calls a method with a file name and asserts on
the result. What must be verified is which extensions resolve to which media types, and how a file
whose type the family cannot read is refused — including the redirects that turn a refusal into the
model's next step and the deliberate absence of a redirect where none would be honest.

Unit tests reside in `Image/ImageMediaTypesTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system or network access required
- **Isolation**: Each test calls the unit directly; no state is shared

#### Acceptance Criteria

A unit test run passes when all scenarios below pass without error or exception beyond those
explicitly asserted. A supported extension that fails to resolve, a case difference that changes the
result, an unsupported extension that resolves anyway, an `.svg` refused without its redirect, and
an `.svgz` or unknown type refused *with* an invented redirect each constitute a failure.

#### Test Scenarios

##### AgentKitTools-Image-MediaTypes-SupportedTypes: Each Supported Extension Resolves

**Test**: `ImageMediaTypes_TryResolveMediaType_SupportedExtension_ReturnsMediaType`

Normal operation, run as a theory over `png`, `jpg`, `jpeg`, `gif`, `webp` and `pdf`, asserting each
resolves to the media type the family reads it as — the four image types to their `image/*` types,
`jpg` and `jpeg` to the one `image/jpeg`, and `pdf` to `application/pdf`.

##### AgentKitTools-Image-MediaTypes-SupportedTypes: An Extension Is Matched Case-Insensitively

**Test**: `ImageMediaTypes_TryResolveMediaType_ExtensionCaseInsensitive_ReturnsMediaType`

Boundary condition: a capitalized extension names the same content as its lower-case form and
resolves identically, so a caller is not refused for a difference that means nothing.

##### AgentKitTools-Image-MediaTypes-RefusesUnknownType: An Unsupported Extension Does Not Resolve

**Test**: `ImageMediaTypes_TryResolveMediaType_UnsupportedExtension_ReturnsFalse`

Error path: an extension the family does not read reports as unsupported with no media type, so the
read tool refuses it rather than attempting a read it cannot complete.

##### AgentKitTools-Image-MediaTypes-RefusesSvgRedirectsToText: An svg Redirects to the Text File Read Tool

**Test**: `ImageMediaTypes_DenyUnsupportedType_Svg_RedirectsToTextFileRead`

Error path with a redirect: an `.svg` is refused as an unsupported type and the refusal names
`text_file_read`, the tool that can actually read the vector-text content.

##### AgentKitTools-Image-MediaTypes-RefusesSvgzNoTool: An svgz Is Refused Without a Redirect

**Test**: `ImageMediaTypes_DenyUnsupportedType_Svgz_RefusesWithoutRedirect`

Error path without a redirect: an `.svgz` is gzip-compressed content no tool in the family reads, so
the refusal names no tool a retry would only fail at, and the assertion confirms neither the text
tool name nor the redirect sentence appears.

##### AgentKitTools-Image-MediaTypes-RefusesUnknownType: Any Other Type Is Refused Without a Redirect

**Test**: `ImageMediaTypes_DenyUnsupportedType_UnknownType_RefusesWithoutRedirect`

Error path: an extension with no honest better tool to name is refused as an unsupported type with
no redirect invented, keeping the refusal truthful.

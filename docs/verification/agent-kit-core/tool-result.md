## ToolResult Unit Verification Design

This document describes the unit-level verification strategy for the `ToolResult` class and the
`DenialReason` enumeration it defines.

### Verification Approach

`ToolResult` is verified through unit tests that call the result constructors directly and
inspect the concrete runtime shape of what they return. `ToolName` is used as a real dependency
rather than a substitute — it is a documented dependency in _ToolResult Unit Design_, and
substituting it would remove exactly the redirect validation one scenario exists to confirm.

The captioned scenarios assert the **exact** shape — a two-element list with the caption first
and the content second — rather than merely that two parts exist. That precise shape is the one
proven to be flattened into JSON when a tool is built without the result-delivery guard, so
pinning it here is what makes the guard's own scenarios meaningful; see _GuardedToolFactory Unit
Verification Design_.

The refusal scenarios are written as a theory over every defined `DenialReason` member, so a
member added later without thought is caught here rather than at a model's tool call.

Unit tests reside in `ToolResultTests.cs` within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: `Microsoft.Extensions.AI.Abstractions` supplies the content types under
  inspection; no external services, file system access or network access required
- **Mocking**: None
- **Isolation**: Each test builds its own result; no state is shared

### Acceptance Criteria

A unit test run passes when all fourteen scenarios below pass without error or exception beyond
those explicitly asserted. Any result returned in the wrong shape, any caption and content
returned in the wrong order, any refusal expressed by throwing, and any host detail contributed
by the library to a refusal constitutes a failure.

### Test Scenarios

#### AgentKitCore-ToolResult-Text: Text Is Returned as the Supplied String

**Test**: `ToolResult_Text_Content_ReturnsSuppliedString`

Normal operation: the plain string arrives unwrapped, ready for any runtime to present.

#### AgentKitCore-ToolResult-Text: A Missing Text Is Refused

**Test**: `ToolResult_Text_NullText_ThrowsArgumentNullException`

Error path: a null text is a defect in the tool, not a refusal for the model.

#### AgentKitCore-ToolResult-Binary: Content Without a Caption Carries Its Media Type

**Test**: `ToolResult_Binary_NoCaption_ReturnsDataContentWithMediaType`

Normal operation: a single content part carrying the media type the provider needs to present it.

#### AgentKitCore-ToolResult-Binary: A Captioned Result Places the Caption First

**Test**: `ToolResult_Binary_WithCaption_ReturnsCaptionThenContent`

Asserts the concrete two-element list shape, with the caption at index 0 and the content at
index 1. The order is the contract: the model reads the parts in sequence.

#### AgentKitCore-ToolResult-Binary: Content Without a Media Type Is Refused

**Test**: `ToolResult_Binary_EmptyMediaType_ThrowsArgumentException`

Error path: a provider cannot interpret content with no media type.

#### AgentKitCore-ToolResult-Image: A Captioned Image Places the Caption First

**Test**: `ToolResult_Image_WithCaption_ReturnsCaptionThenImageContent`

Asserts the caption-plus-image shape and that the image part carries its media type.

#### AgentKitCore-ToolResult-Image: A Non-Image Media Type Is Refused

**Test**: `ToolResult_Image_NonImageMediaType_ThrowsArgumentException`

Error path: a caption attached to bytes that are not an image would tell the model it is looking
at a picture when it is not, and the model has no way to discover otherwise.

#### AgentKitCore-ToolResult-Denied: Every Defined Reason Produces a Result

**Test**: `ToolResult_Denied_AnyReason_ProducesResultNotException`

A theory with one case per `DenialReason` member. Asserts each produces a non-empty returned
string and raises no exception, so an agent's turn continues.

#### AgentKitCore-ToolResult-Denied: A Refusal Names Its Reason

**Test**: `ToolResult_Denied_Result_NamesTheReason`

Asserts both the reason name and the caller's explanation reach the model, which is what lets the
model choose a permitted alternative rather than retrying blindly.

#### AgentKitCore-ToolResult-DenialRedirect: A Redirect Names the Better Tool

**Test**: `ToolResult_Denied_WithRedirect_NamesTheRedirectTool`

Asserts the redirect tool name appears in the text the model receives.

#### AgentKitCore-ToolResult-DenialRedirect: An Invalid Redirect Name Is Refused

**Test**: `ToolResult_Denied_InvalidRedirectToolName_ThrowsArgumentException`

Error path: an unprefixed redirect name would direct the model at a tool that cannot legally
exist.

#### AgentKitCore-ToolResult-DenialReasonRequired: An Undefined Reason Is Refused

**Test**: `ToolResult_Denied_UndefinedReason_ThrowsArgumentOutOfRangeException`

Boundary condition: the enumeration has no zero member, so `default` is not a reason. Asserts it
is refused rather than shown to a model.

#### AgentKitCore-ToolResult-DenialReasonRequired: A Refusal Without an Explanation Is Refused

**Test**: `ToolResult_Denied_EmptyMessage_ThrowsArgumentException`

Error path: a reason with no explanation leaves the model nothing to act on.

#### AgentKitCore-ToolResult-DenialComposedOnlyOfSuppliedText: A Refusal Adds No Host Detail

**Test**: `ToolResult_Denied_Message_ContainsOnlySuppliedTextAndReason`

Asserts the composed text is exactly the fixed prefix, the reason name and the caller's message,
and contains no directory separator at all. This is the `ToolResult`-level counterpart of the
path policy's redaction scenario; see _PathPolicy Unit Verification Design_.

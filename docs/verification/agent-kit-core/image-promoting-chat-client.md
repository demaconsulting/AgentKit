## ImagePromotingChatClient Unit Verification Design

This document describes the unit-level verification strategy for the `ImagePromotingChatClient`
class.

### Verification Approach

`ImagePromotingChatClient` is verified through unit tests that hand a conversation to the
decorator and assert on the conversation the wrapped client received. That is the whole of the
unit's behavior — it forwards a request whose message sequence has been rewritten — so the
conversation the inner client observed is the complete observable outcome.

**A scripted fake client is used rather than a real provider, and that is not a compromise.** The
asymmetry this unit exists for happens at a provider's wire format, outside this process, and
cannot be observed from inside it at all: the framework preserves the content in both the working
and the failing case, and the difference appears only in what the provider's interface accepts. A
test against a real provider would therefore require network access, credentials and a model's
non-deterministic description of an image, and would still not observe the rewrite this unit
performs. The evidence that the promotion solves the real problem is the exploratory work recorded
in *ImagePromotingChatClient Unit Design*, not a unit test.

The fake is written by hand rather than substituted, because the streaming member returns an
asynchronous sequence a substitute would have to be taught to produce anyway. It records the
messages it was handed, materialized on receipt, and returns a canned response.

Promoted content is asserted **by reference** wherever possible, so that a future change
re-encoding, resizing or copying the image fails here rather than silently changing what the model
sees.

Unit tests reside in `ImagePromotingChatClientTests.cs`, with the fake client in
`ScriptedChatClient.cs`, both within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK, using asynchronous test methods
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No provider is contacted, no credential is required and **no
  network access is used**
- **File system**: None; the image is a short byte array held in the test class
- **Mocking**: A hand-written scripted `IChatClient`; no mocking framework
- **Isolation**: Each test constructs its own decorator and its own scripted client; no state is
  shared

### Acceptance Criteria

A unit test run passes when all fourteen scenarios below pass without error or exception beyond those
explicitly asserted. Any image that fails to reach a user message, any content instance that is
copied rather than carried, any message dropped or reordered, any message added for a text-only
tool result or for content that is not an image, any promoted message that does not land
immediately after the tool result it came from, any image content left in a forwarded tool result,
any change to the caller's messages, and any difference between the streaming and non-streaming
paths constitutes a failure.

### Test Scenarios

#### AgentKitCore-ImagePromotingChatClient-ToolResultCarriesNoImageBytes: The Tool Result Carries a Placeholder

**Test**:
`ImagePromotingChatClient_GetResponseAsync_ToolResultWithImage_ToolMessageCarriesPlaceholderNotImage`

Asserts the tool message the wrapped client received holds a single function result with the
original call identifier whose result is exactly the placeholder text naming `image/png` and the
byte count, and that no `DataContent` remains in it. This is the wire-level view: what the inner
client receives is what a provider adapter serializes.

#### AgentKitCore-ImagePromotingChatClient-ToolResultCarriesNoImageBytes: A Large Image Leaves a Small Tool Message

**Test**: `ImagePromotingChatClient_GetResponseAsync_LargeImageToolResult_ForwardedToolMessageIsSmall`

A 300,000-byte image. Asserts the forwarded tool result text is under 200 characters and names
`300,000 bytes`, while the promoted user message carries the same image instance with all its
bytes. This pins the defect the placeholder fixes: a tool message whose size grew with the image.

#### AgentKitCore-ImagePromotingChatClient-ToolResultCarriesNoImageBytes: A Caption Is Kept and the Image Replaced

**Test**:
`ImagePromotingChatClient_GetResponseAsync_ToolResultWithCaptionAndImage_CaptionKeptImageReplaced`

The caption-then-image shape. Asserts the forwarded result lists the same caption instance first
and a placeholder naming `image/jpeg` second, with no `DataContent` left.

#### AgentKitCore-ImagePromotingChatClient-ToolResultCarriesNoImageBytes: The Streaming Path Leaves the Same Placeholder

**Test**:
`ImagePromotingChatClient_GetStreamingResponseAsync_ToolResultWithImage_ToolMessageCarriesPlaceholder`

Asserts the streamed request's tool result is the placeholder, as on the non-streaming path.

#### AgentKitCore-ImagePromotingChatClient-CallerMessagesUnchanged: The Caller's Messages Are Not Modified

**Test**: `ImagePromotingChatClient_GetResponseAsync_CallerMessages_AreNotModified`

Asserts that after forwarding, the caller's list still holds one message whose content is the same
function result instance still carrying the same image, and that the wrapped client received a
different message instance.

#### AgentKitCore-ImagePromotingChatClient-PromoteImages: An Image Result Adds a User Message Carrying the Same Content

**Test**:
`ImagePromotingChatClient_GetResponseAsync_ToolResultWithImage_AddsUserMessageCarryingTheSameDataContent`

The central scenario. A conversation whose last message is a tool result holding an image is sent
through the decorator. Asserts the wrapped client received exactly one extra message, that its
role is `User`, and that the content it carries is the very same instance the tool result held.

#### AgentKitCore-ImagePromotingChatClient-PromoteImages: Only the Image Parts of a Content List Are Promoted

**Test**:
`ImagePromotingChatClient_GetResponseAsync_ToolResultWithContentList_PromotesOnlyTheImageParts`

The caption-then-image shape a guarded tool produces. Asserts the image is promoted and the
caption is not duplicated: the caption already reached the provider as text in the tool result,
and repeating it would tell the model the same thing twice.

#### AgentKitCore-ImagePromotingChatClient-AnnouncesAttachment: The Promoted Message Announces the Attachment

**Test**:
`ImagePromotingChatClient_GetResponseAsync_ToolResultWithImage_PromotedMessageAnnouncesTheAttachment`

Asserts the promoted message leads with text naming the tool as the source, followed by the
attachment. A bare attachment reads to the model as an image the user has just sent, unrelated to
the tool call above it.

#### AgentKitCore-ImagePromotingChatClient-TextResultUnchanged: A Text-Only Result Adds Nothing

**Test**: `ImagePromotingChatClient_GetResponseAsync_TextOnlyToolResult_AddsNoMessage`

Normal operation for the common case. Asserts the conversation is forwarded exactly as it
arrived, so a host can leave the decorator installed without taxing every ordinary exchange.

#### AgentKitCore-ImagePromotingChatClient-TextResultUnchanged: A Non-Image Result Adds Nothing

**Test**: `ImagePromotingChatClient_GetResponseAsync_NonImageDataContentResult_AddsNoMessage`

A tool result carrying binary content of a non-image media type. Asserts nothing is added, so a
document a tool returned is left where the tool put it rather than presented to the model as
though the user had just attached it.

#### AgentKitCore-ImagePromotingChatClient-MessageOrderPreserved: Non-Tool Messages Pass Through in Order

**Test**: `ImagePromotingChatClient_GetResponseAsync_NonToolMessages_ArePassedThroughInOrder`

Asserts an ordinary system-user-assistant exchange is forwarded unchanged, by sequence equality,
so a change that dropped or reordered a message would fail here rather than by altering the
meaning of a conversation in production.

#### AgentKitCore-ImagePromotingChatClient-MessageOrderPreserved: A Promoted Message Lands Immediately After Its Tool Result

**Test**:
`ImagePromotingChatClient_GetResponseAsync_MessageAfterToolResult_PromotedMessageIsInsertedImmediatelyAfterIt`

A conversation that continues past the tool result holding the image. Asserts the promoted message
sits between the tool result and the message that followed it, so an implementation appending
promoted messages to the end of the conversation — where the image would be read as belonging to
whichever turn happened to be last — fails here.

#### AgentKitCore-ImagePromotingChatClient-StreamingAndNonStreaming: The Streaming Path Promotes the Image

**Test**:
`ImagePromotingChatClient_GetStreamingResponseAsync_ToolResultWithImage_PromotesTheImage`

Consumes the asynchronous sequence — which is what causes the request to be forwarded — and then
asserts the same promotion the non-streaming path performs. A host that streams would otherwise
silently lose the control.

#### AgentKitCore-ImagePromotingChatClient-RequiresInnerClient: A Missing Inner Client Is Refused

**Test**: `ImagePromotingChatClient_Constructor_NullInnerClient_ThrowsArgumentNullException`

Error path: a decorator with nothing to decorate can neither forward a request nor report a
response, so the defect is surfaced where the host wrote it rather than at the first conversation.

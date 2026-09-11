## ChatClientAgentFactory Unit Verification Design

This document describes the unit-level verification strategy for the `ChatClientAgentFactory` class.

### Verification Approach

`ChatClientAgentFactory` is verified through unit tests that exercise its internal decorator seam and
its construction-time validation. The decorator-install guarantee is asserted directly: the seam the
factory uses to wrap every client returns an `ImagePromotingChatClient`, so an agent built by the
factory cannot talk to its provider through anything else. The validation scenarios pin each rejected
condition a well-formed agent could not be built from.

A scripted `IChatClient` that contacts nothing stands in for a provider, and tools are built through
the framework's own function factory. No network access, credential, or live model is used.

Unit tests reside in `ChatClientAgentFactoryTests.cs`, with the scripted client in
`ScriptedChatClient.cs`, both within the `DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no network access is used**
- **Mocking**: A hand-written scripted `IChatClient`; no mocking framework
- **Isolation**: Each test constructs its own client and tools; no state is shared

### Acceptance Criteria

A unit test run passes when all six scenarios below pass without error or exception beyond those
explicitly asserted. Any wrapped client that is not an `ImagePromotingChatClient`, or any invalid
argument that is accepted rather than refused, constitutes a failure.

### Test Scenarios

#### AgentKitAgentsChatClient-ChatClientAgentFactory-DecoratorAlwaysInstalled: The Wrap Returns an Image-Promoting Client

**Test**: `ChatClientAgentFactory_WrapWithImagePromotion_ReturnsImagePromotingClient`

The central scenario. Wraps a scripted client through the seam the factory uses on every agent it
builds and asserts the returned client is an `ImagePromotingChatClient`. This is the guarantee the
package exists for, asserted rather than assumed.

#### AgentKitAgentsChatClient-ChatClientAgentFactory-RejectsNullClient: A Missing Client Is Refused

**Test**: `ChatClientAgentFactory_Create_NullClient_Throws`

Error path: an agent with no client to talk to cannot be built, so a null client is refused with
`ArgumentNullException`.

#### AgentKitAgentsChatClient-ChatClientAgentFactory-RejectsInvalidToolList: A Malformed Tool List Is Refused

**Tests**: `ChatClientAgentFactory_Create_NullTools_Throws`,
`ChatClientAgentFactory_Create_EmptyTools_Throws`,
`ChatClientAgentFactory_Create_NullToolEntry_Throws`,
`ChatClientAgentFactory_Create_DuplicateToolNames_Throws`

Four error paths: a null tool list and a null entry are refused with `ArgumentNullException`; an
empty list and two tools sharing a name are refused with `ArgumentException`, the latter naming the
colliding tool. Together they confirm a malformed tool list is refused where the host wrote it rather
than discovered from a model's undefined choice later.

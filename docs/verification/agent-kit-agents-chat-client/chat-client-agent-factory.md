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

A unit test run passes when all fifteen tests below pass without error or exception beyond those
explicitly asserted. Any wrapped client that is not an `ImagePromotingChatClient`, any invalid
argument that is accepted rather than refused, any host-built pipeline in which a tool-returned image
does not reach the inner client on a user message, any misplaced decorator that is built rather
than refused, any request that reaches the client without the selected model, effort or output
limit, or any selection an `IChatClient` cannot apply — including a context window the client does
not apply — that is accepted rather than refused, constitutes a failure.

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

#### AgentKitAgentsChatClient-ChatClientAgentFactory-UseImagePromotion: The Builder Extension Promotes Beneath Function Invocation

**Test**: `ChatClientAgentFactory_UseImagePromotion_InstallsDecoratorBeneathFunctionInvocation`

A `RecordingChatClient` scripted to call an image tool and then answer, built into
`UseFunctionInvocation().UseImagePromotion()`. Asserts the second request the inner client received
carries the placeholder in the tool result and the same image instance on a user message — the
decorator sits beneath the loop and acts on the tool result.

**Test**: `ChatClientAgentFactory_UseImagePromotion_NullBuilder_Throws`

A null builder is refused with `ArgumentNullException`.

#### AgentKitAgentsChatClient-ChatClientAgentFactory-UseImagePromotionRefusesAboveFunctionInvocation: A Misplaced Decorator Is Refused

**Test**: `ChatClientAgentFactory_UseImagePromotion_AboveFunctionInvocation_Throws`

`UseImagePromotion().UseFunctionInvocation()` — the decorator above the loop — throws
`InvalidOperationException` when built, and the message names `UseFunctionInvocation()`.

#### AgentKitAgentsChatClient-ChatClientAgentFactory-AppliesASelection: The Agent's Requests Carry the Selection

**Test**: `ChatClientAgentFactory_Create_WithSelection_RequestsCarryTheSelection`

A selection with a low effort and a 500-token output limit, bound to a hand-built model, is passed
to `Create` over a recording client. The request the agent sends carries the model identifier,
`ReasoningEffort.Low` and the output limit.

**Test**: `ChatClientAgentFactory_Create_SelectionWithATier_Throws`

Error path: a selection carrying a context tier, which an `IChatClient` cannot apply, is refused with
`ArgumentException` when the agent is built rather than dropped.

**Test**: `ChatClientAgentFactory_Create_SelectionWithAnUnappliedWindow_Throws`

Error path and its counterpart: a selection carrying an 8,192-token window, over a scripted client
that applies no window, is refused with `ArgumentException` whose message names
`ContextWindowTokens`; the same selection over a delegating client reporting, as an
`IContextWindowChatClient`, that it applies 8,192 tokens builds an agent.

#### AgentKitAgentsChatClient-ChatClientAgentFactory-UseModelSettings: The Builder Extension Applies the Selection

**Test**: `ChatClientAgentFactory_UseModelSettings_HostPipeline_RequestsCarryTheSelection`

A host-built pipeline over a recording client with `UseModelSettings(selection)` sends a request
carrying the selected model identifier and `ReasoningEffort.Low`.

**Test**: `ChatClientAgentFactory_UseModelSettings_SelectionWithAWindow_NeedsItAppliedBeneath`

A selection carrying an 8,192-token window is refused with `ArgumentException` naming
`ContextWindowTokens` when a pipeline over a plain scripted client is built, and the same selection
builds over a client that reports, as an `IContextWindowChatClient`, that it applies 8,192 tokens.

**Test**: `ChatClientAgentFactory_UseModelSettings_NullArguments_Throw`

A null builder and a null selection are each refused with `ArgumentNullException`.

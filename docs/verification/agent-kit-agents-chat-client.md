# System Verification Design

This document describes the system-level verification strategy for the AgentKitAgentsChatClient
system.

## Verification Approach

The AgentKitAgentsChatClient system is verified through system-level integration tests that exercise
the factory as a consumer would: they build an agent from a scripted chat client and a supplied tool
list, and assert on the observable outcome. The behavioral guarantee the package exists for — that
the image-promoting decorator is installed on every agent it builds — is **asserted, not assumed**.

The decorator is proven in two parts. One scenario builds an agent through `Create` and asks that
agent for the decorator, so a construction path that stopped wrapping would fail; a second wraps a
scripted `IChatClient` exactly as the factory wraps one and shows a tool-returned image reaching that
client on a following user message, so the installed decorator is shown to do the job it is installed
for. The two together establish that an agent built by the factory talks to its provider through a
functioning image-promoting decorator.

**What is out of automated scope, stated honestly.** A live end-to-end run against a real
`IChatClient` provider is not automated: the asymmetry the decorator exists for happens at a
provider's wire format, outside this process, and a real provider would add network access,
credentials, and a model's non-deterministic description of an image without observing the rewrite
this system performs. The evidence that the promotion solves the real problem is the exploratory work
recorded in the Core _ImagePromotingChatClient Unit Design_, not a live-model test.

System tests reside in `AgentKitAgentsChatClientTests.cs` within the
`DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No provider is contacted and **no network access is used**. The
  image-promotion scenario uses a scripted `IChatClient` that contacts nothing
- **File system**: None; the image is a short byte array held in the test class
- **Isolation**: Each test constructs its own scripted client, tools, and agent; no state is shared

## System-Level Test Scenarios

### Agent Construction: An Agent Carries Its Supplied Name

**Test**: `AgentKitAgentsChatClient_Create_BuildsAgentCarryingSuppliedName`

Verifies that the factory turns a chat client and a tool list into a usable agent. Builds an agent
from a scripted client, one tool, instructions, and a name, and asserts a non-null agent carrying the
supplied name is returned.

### Image Promotion: The Decorator Is Installed on an Agent the Factory Builds

**Test**: `AgentKitAgentsChatClient_Create_InstallsImagePromotingDecoratorOnTheAgent`

Verifies that an agent built through `Create` actually talks through the decorator, rather than that
the wrap seam would produce one if called. Builds an agent from a scripted client and one tool, then
asks the constructed agent's service chain for an `ImagePromotingChatClient` and asserts one is
found. This is the scenario that pins the construction path: a `Create` that stopped wrapping would
fail here while the promotion scenario below, which exercises the seam directly, would still pass.

### Image Promotion: A Tool-Returned Image Reaches the Provider on a User Message

**Test**: `AgentKitAgentsChatClient_ImagePromotion_ToolReturnedImage_ReachesProviderOnUserMessage`

Verifies that the installed decorator does the job it is installed for. Wraps a scripted client
exactly as the factory does, sends a conversation whose last message is a tool result holding an
image as the function-invocation loop would after the tool ran, and asserts the client received an
extra user message carrying the very same image instance. Paired with the scenario above, it
confirms an agent built by the factory delivers a tool-returned image to a provider that would
otherwise drop it.

## Acceptance Criteria

A system-level test run passes when all three scenarios above pass without error or exception beyond
those explicitly asserted. Any agent that fails to build, any wrong name, any agent built without the
image-promoting decorator in its client chain, any tool-returned image that fails to reach the
provider on a user message, or any content instance copied rather than carried constitutes a failure.

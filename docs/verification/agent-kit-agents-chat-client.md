# System Verification Design

This document describes the system-level verification strategy for the AgentKitAgentsChatClient
system.

## Verification Approach

The AgentKitAgentsChatClient system is verified through system-level integration tests that exercise
the package as a consumer would: they build an agent from a scripted chat client and a supplied tool
list, and they run an AgentKit Core session over a recording chat client, asserting on the observable
outcome. The behavioral guarantee the package exists for — that the image-promoting decorator is
installed on every agent it builds — is **asserted, not assumed**.

The decorator is proven in two parts. One scenario builds an agent through `Create` and asks that
agent for the decorator, so a construction path that stopped wrapping would fail; a second wraps a
scripted `IChatClient` exactly as the factory wraps one and shows a tool-returned image reaching that
client on a following user message, so the installed decorator is shown to do the job it is installed
for. The two together establish that an agent built by the factory talks to its provider through a
functioning image-promoting decorator.

The session adapter is proven end to end rather than inferred from its units. One scenario runs a
Core session over the adapter and asserts the occupancy it reports is the provider's own figure for
the prompt it was last sent, against the window the application stated. A second holds a short
conversation against a provider that reports a filling window on every turn, and asserts the whole
arrangement: the rotation happens on nothing but what the provider reported, the consolidation goes
out through a **separate** chat client, the record that client returns is found in the conversation
the provider is later sent, and the replacement session reports occupying nothing because it has not
been sent anything yet. That last assertion also pins the pipeline the session factory builds: a
replacement sharing its predecessor's prompt-size recorder would report the figure that provoked the
rotation, and the scenario would fail.

**What is out of automated scope, stated honestly.** A live end-to-end run against a real
`IChatClient` provider is not automated: the asymmetry the decorator exists for happens at a
provider's wire format, outside this process, and a real provider would add network access,
credentials, and a model's non-deterministic description of an image without observing the rewrite
this system performs. The same holds for the session adapter, where a live model would contribute
only its own tokenizer's arithmetic to figures this system deliberately does not compute. The
evidence that the promotion solves the real problem is the exploratory work recorded in the Core
_ImagePromotingChatClient Unit Design_, not a live-model test.

System tests reside in `AgentKitAgentsChatClientTests.cs` within the
`DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No provider is contacted and **no network access is used**. The
  image-promotion scenario uses a scripted `IChatClient` that contacts nothing, and the session
  scenarios use a recording `IChatClient` answering from a script
- **File system**: None; the image is a short byte array held in the test class
- **Isolation**: Each test constructs its own scripted client, tools, agent or session; no state is
  shared

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

### Session Adapter: A Core Session Runs Over a Chat Client and Reports the Provider's Occupancy

**Test**: `AgentKitAgentsChatClient_Session_AnswersAndReportsTheProvidersOccupancy`

Verifies the plainest thing the session adapter must do. Creates a `CompactingAgentSession` over a
`ChatClientProviderSessionFactory` holding a recording client and a stated window, takes one turn,
and asserts the answer came back and the session's account of the window is the provider's own
reported figure against that window. The figure is one no estimate would arrive at, so an
implementation that computed rather than reported would fail here.

### Session Adapter: A Rotation Runs on the Provider's Usage and Seeds the Replacement

**Test**: `AgentKitAgentsChatClient_Session_RotatesOnTheProvidersUsage_AndSeedsTheReplacement`

Verifies the arrangement the session adapter exists for, end to end. A provider reporting a
conversation well past the rotation threshold of its window on every turn drives a short conversation;
a separate recording client performs the consolidations and returns a marked record. The scenario
asserts a rotation occurred, the consolidation went out through the summarizing client rather than
the conversational one, the marked record reached the provider as seeded history on a later turn, and
the replacement session reports occupying nothing of the window because it has not yet been sent
anything. Nothing but the provider's reported usage drives any of it.

## Acceptance Criteria

A system-level test run passes when all five scenarios above pass without error or exception beyond
those explicitly asserted. Any agent that fails to build, any wrong name, any agent built without the
image-promoting decorator in its client chain, any tool-returned image that fails to reach the
provider on a user message, any content instance copied rather than carried, any session occupancy
that is not the provider's own figure against the stated window, any conversation that fails to
rotate when the provider reports a filling window, any consolidation sent through the conversational
client, or any replacement session that fails to carry the consolidated record constitutes a failure.

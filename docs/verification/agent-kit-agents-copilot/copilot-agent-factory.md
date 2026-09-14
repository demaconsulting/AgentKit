## CopilotAgentFactory Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotAgentFactory` class.

### Verification Approach

`CopilotAgentFactory` is verified through unit tests that exercise its session-building seam and its
construction-time validation, all offline. The host-handler override, the withheld runtime-injected
capability, the instruction handling, and the model selection are asserted against a constructed
session configuration; the validation
scenarios pin each rejected condition; and the null-client refusal is asserted through the public
entry point, which validates before it would reach the Copilot CLI.

Tools are built through the framework's own function factory, and permission requests and decisions
are constructed directly from the SDK's plain types. No Copilot CLI, credential, or network access is
used.

Unit tests reside in `CopilotAgentFactoryTests.cs` within the
`DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no Copilot CLI and no network access** is used
- **Isolation**: Each test constructs its own tools and configuration; no state is shared

### Acceptance Criteria

A unit test run passes when all ten scenarios below pass without error or exception beyond those
explicitly asserted. Any invalid argument that is accepted, any host handler that is not installed
verbatim, any instruction that is not carried onto the session's system message, any model
selection that is not carried onto the session — or that is invented when the host named none — or
any channel of runtime-injected capability left open constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotAgentFactory-RejectsInvalidToolList: A Malformed Tool List Is Refused

**Tests**: `CopilotAgentFactory_BuildSessionConfig_NullTools_Throws`,
`CopilotAgentFactory_BuildSessionConfig_EmptyTools_Throws`,
`CopilotAgentFactory_BuildSessionConfig_NullToolEntry_Throws`,
`CopilotAgentFactory_BuildSessionConfig_DuplicateToolNames_Throws`

Four error paths: a null tool list and a null entry are refused with `ArgumentNullException`; an
empty list and two tools sharing a name are refused with `ArgumentException`, the latter naming the
colliding tool. An empty list would produce an empty allow-list, and a duplicate name would make both
the published tool set and the derived allow-list ambiguous.

#### AgentKitAgentsCopilot-CopilotAgentFactory-RejectsNullClient: A Missing Client Is Refused

**Test**: `CopilotAgentFactory_Create_NullClient_Throws`

Error path: an agent with no client to run on cannot be built, so a null client is refused with
`ArgumentNullException` before any session is constructed.

#### AgentKitAgentsCopilot-CopilotAgentFactory-HostHandlerOverride: A Host Handler Is Installed Verbatim

**Test**: `CopilotAgentFactory_BuildSessionConfig_HostHandler_IsInstalled`

Verifies that a host-supplied permission handler is the exact delegate installed on the session, so a
host with its own policy governs the session itself rather than layering over the default.

#### AgentKitAgentsCopilot-CopilotAgentFactory-CarriesInstructions: Instructions Are Carried Onto the System Message

**Test**: `CopilotAgentFactory_BuildSessionConfig_Instructions_CarriedOnSystemMessage`

Verifies that supplied instructions are carried onto the session's system message, and that a session
built without instructions carries no system message, so a confined agent is governed as the host
intended and the factory adds nothing of its own when the host supplies nothing.

#### AgentKitAgentsCopilot-CopilotAgentFactory-SelectsModel: A Named Model Backs the Session

**Tests**: `CopilotAgentFactory_BuildSessionConfig_Model_CarriedOnSession`,
`CopilotAgentFactory_BuildSessionConfig_NoModel_LeavesSessionModelAtDefault`

Two paths. With a model named, the session carries exactly that name, so an application can choose
which Copilot model backs its agent without building a session itself and losing the suppression and
the default-safe handler. With no model named, the session's model equals that of a freshly
constructed session configuration — the factory set nothing — so the runtime applies its own default
and an existing application sees no change.

The unit tests prove the value is carried; they cannot prove the runtime accepts a given name, since
only the runtime knows which models the signed-in user may use. That acceptance is confirmed by
running the `document-assistant` sample against Copilot with and without `--model` and observing
both runs complete a turn.

#### AgentKitAgentsCopilot-CopilotAgentFactory-WithholdsInjectedCapability: Runtime-Injected Capability Is Withheld

**Test**: `CopilotAgentFactory_BuildSessionConfig_InjectedCapability_IsWithheld`

Verifies that the session the factory builds has the runtime's skills disabled and its custom
instructions skipped, so a confined agent receives only the capability and direction its host
attached. Both values are read from the constructed session configuration rather than inferred, so
either one reverting to the permissive value — which would widen the agent without changing anything
the host wrote — fails this scenario.

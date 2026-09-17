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

#### AgentKitAgentsCopilot-CopilotAgentFactory-OneConfinementPath: One Derivation, Two Entry Points

**Tests**: `CopilotAgentFactory_BothPaths_DeriveTheSameConfinement`,
`CopilotAgentFactory_BuildEngineSessionConfig_EmptyTools_ProducesAnEmptyAllowList`,
`CopilotAgentFactory_BuildSessionConfig_EmptyTools_Throws`,
`CopilotAgentFactory_BuildEngineSessionConfig_DuplicateToolNames_Throws`

The first builds the same tool set through both entry points and asserts the allow-list, the
published tool names, the skills setting and the custom-instruction setting are identical. It is the
scenario that makes having two entry points safe: they differ in what tool lists they accept and in
the runtime compaction, and in nothing that decides what a session may call. An implementation that
forked the derivation would pass every other scenario in this file and fail only this one.

The second asserts the engine path accepts an empty tool list and produces an empty allow-list with
the injection channels still shut — the strongest confinement the factory can express, which is what
a consolidation session needs. The third asserts the agent path still refuses the same list, because
an agent publishing no tools is a defect in its host; emptiness is the only rule the two paths
disagree about. The fourth asserts the engine path still refuses a duplicated tool name, so relaxing
emptiness did not relax everything else.

#### AgentKitAgentsCopilot-CopilotAgentFactory-LeavesTheAgentPathsCompactionAlone: The Deliberate Asymmetry

**Tests**: `CopilotAgentFactory_BuildSessionConfig_LeavesTheRuntimesCompactionUntouched`,
`CopilotAgentFactory_BuildEngineSessionConfig_DisablesTheRuntimesCompaction`

Both sides of the asymmetry are asserted, because it is the one a maintainer is most likely to
"tidy" into consistency. The agent path leaves the runtime's infinite-session setting at whatever a
freshly constructed session configuration carries — a plain agent has no AgentKit compactor behind
it, so disabling the runtime's would remove the only protection that session has when its window
fills. The engine path sets it explicitly disabled — a session the engine drives has an AgentKit
compactor behind it, and two compactors reading one occupancy signal would fight.

Whether a live runtime honors the disabling is unverified here and is recorded as such in
_AgentKitAgentsCopilot System Verification Design_; what these scenarios establish is that the
request is made on exactly the sessions it should be and on no others.

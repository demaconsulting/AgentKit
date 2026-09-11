# System Verification Design

This document describes the system-level verification strategy for the AgentKitAgentsCopilot system.

## Verification Approach

The AgentKitAgentsCopilot system is verified through system-level integration tests that exercise the
factory's session-configuration path and its permission handling **without a live Copilot CLI**. The
safety-critical property of the whole increment — that the session allow-list is derived from the
same collection as the published tools, so the two cannot drift apart — is asserted directly against
a constructed session configuration, which is a plain constructable object requiring no client. The
default permission handler is exercised by invoking it with constructed permission requests and
asserting on the decisions it returns.

**What is out of automated scope, stated honestly.** A live end-to-end Copilot run is not automated:
the SDK's agent-construction call reaches an authenticated Copilot CLI, which is not available in a
unit test and which this repository deliberately does not require. The construction call itself, and
the runtime's actual suppression of a shell or file-editing tool at execution time, are therefore
outside automated scope. The offline evidence is the assertion that the allow-list-carrying session
configuration is built correctly and that the permission handler adjudicates as required — the two
places where the suppression is decided. The runtime's own delivery of a tool-returned image (the
reason the image-promoting decorator is deliberately absent) was verified in the spike and is not
re-proven here.

System tests reside in `AgentKitAgentsCopilotTests.cs` within the
`DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot CLI, no credential, and **no network access** is used. The
  session configuration and the permission requests are plain constructable objects
- **Isolation**: Each test constructs its own tools, configuration, and requests; no state is shared

## System-Level Test Scenarios

### Tool Suppression: The Allow-List Is Derived From the Supplied Tools

**Test**: `AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools`

The safety-critical scenario. Builds the session configuration from a set of supplied tools and
asserts the available-tools allow-list is exactly the names of the published tools, in the same
order, and that the published tool set is the same size — the two derived from one collection. A
drift here would silently re-admit a built-in tool an application meant to withhold.

### Permission Handling: A Supplied Tool Is Approved

**Test**: `AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved`

Verifies the default handler approves a custom-tool request naming one of the supplied tools, by
asserting the returned decision is an approval.

### Permission Handling: An Unlisted Custom Tool Is Rejected

**Test**: `AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected`

Verifies the default handler rejects a custom-tool request whose name was not supplied, by asserting
the returned decision is a rejection.

### Permission Handling: A Built-In Tool Is Rejected

**Test**: `AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected`

Verifies the default handler rejects a built-in request — one that is not a supplied custom tool — by
asserting the returned decision is a rejection. This is the runtime-tool suppression the package
exists for, enforced at the permission boundary as well as in the allow-list.

## Acceptance Criteria

A system-level test run passes when all four scenarios above pass without error or exception beyond
those explicitly asserted. Any allow-list that diverges from the published tools, any supplied tool
that is rejected, or any unlisted or built-in request that is approved constitutes a failure.

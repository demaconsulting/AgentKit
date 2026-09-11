## Microsoft.Agents.AI.GitHub.Copilot

### Purpose

`Microsoft.Agents.AI.GitHub.Copilot` is the GitHub Copilot SDK for the Microsoft Agent Framework. It
is used because the Copilot adapter does not implement an agent of its own — it adapts a
`CopilotClient` into the framework's `AIAgent` abstraction while suppressing the tools the Copilot
runtime injects. It was chosen rather than a bespoke Copilot integration so that AgentKit contributes
only the suppression and the safe-default permission handling, while the SDK supplies the client,
session model, and permission RPC. It is a runtime NuGet dependency of the AgentKitAgentsCopilot
package only, deliberately kept out of Core.

### Features Used

- `CopilotClient` — the client the host constructs, starts, and disposes; the adapter builds an
  agent over it without taking ownership of it
- `SessionConfig` — the session configuration whose `Tools` collection publishes the agent's tools
  and whose `AvailableTools` list is the allow-list that suppresses the runtime's built-in tools
- The session-config agent-construction path — builds an `AIAgent` from a `CopilotClient` and a
  `SessionConfig` with an explicit client-ownership flag; it is the only construction path that
  carries the allow-list
- The permission RPC — the permission-request hierarchy (including the custom-tool request carrying
  a tool name) and the permission-decision type with its approve and reject results, in which the
  default permission handler is expressed
- `SystemMessageConfig` — carries the supplied instructions onto the session

### Integration Pattern

`Microsoft.Agents.AI.GitHub.Copilot` is consumed by `CopilotAgentFactory` in AgentKitAgentsCopilot,
which references it as a **direct** `PackageReference`. The factory constructs a `SessionConfig`
whose `AvailableTools` is derived from the same collection as `Tools`, installs a permission handler,
and builds the agent through the session-config construction path with `ownsClient` left false. The
host owns the client's lifetime.

The SDK's permission-decision RPC surface is marked with the `GHCP001` "evaluation purposes only"
diagnostic; the adapter necessarily references it to wire a permission handler, so that diagnostic is
suppressed in the package and in its tests, and a consumer that references the permission-handler
parameter will see the same diagnostic from its own build.

### Deployment Consequence: RID-Specific Native Runtime

`Microsoft.Agents.AI.GitHub.Copilot` carries a **RID-specific native runtime** through its SDK
dependency: the package brings a native component selected per runtime identifier rather than a
single portable managed assembly. A self-contained or RID-targeted publish resolves and ships the
native component for the target runtime identifier, and a portable publish defers that resolution to
the target machine. This is a property of the off-the-shelf SDK, and it is the reason the dependency
is isolated in its own AgentKit package: an application that never builds a Copilot agent never takes
the native runtime.

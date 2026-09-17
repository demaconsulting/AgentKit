## Microsoft.Agents.AI.GitHub.Copilot

### Purpose

`Microsoft.Agents.AI.GitHub.Copilot` is the GitHub Copilot SDK for the Microsoft Agent Framework. It
is used because the Copilot adapter implements neither an agent nor a conversation runtime of its
own — it adapts a `CopilotClient` into the framework's `AIAgent` abstraction while suppressing the
tools the Copilot runtime injects, and adapts the SDK's own session lifecycle into AgentKit's
provider-session contract so the compaction engine can run on Copilot. It was chosen rather than a
bespoke Copilot integration so that AgentKit contributes only the suppression, the safe-default
permission handling and the session accounting, while the SDK supplies the client, the session
lifecycle, the event stream and the permission RPC. It is a runtime NuGet dependency of the
AgentKitAgentsCopilot package only, deliberately kept out of Core.

### Features Used

- `CopilotClient` — the client the host constructs, starts, and disposes; the adapter builds an
  agent over it without taking ownership of it, and creates one session per rotation on it
- `SessionConfig` — the session configuration whose `Tools` collection publishes the agent's tools
  and whose `AvailableTools` list is the allow-list that suppresses the runtime's built-in tools
- The session-config agent-construction path — builds an `AIAgent` from a `CopilotClient` and a
  `SessionConfig` with an explicit client-ownership flag; it is the only construction path that
  carries the allow-list
- The permission RPC — the permission-request hierarchy (including the custom-tool request carrying
  a tool name) and the permission-decision type with its approve and reject results, in which the
  default permission handler is expressed
- `SystemMessageConfig` — carries the supplied instructions onto the session, and nothing else: the
  seeded conversation record a rotation produces is deliberately kept out of it, because the SDK
  offers no history or messages field of any kind and a record charged as overhead rather than as
  conversation makes the engine's accounting drift. It travels on the session's first message instead
- **The session lifecycle** — creating a session from a configuration, sending one message and
  waiting for the session to become idle under a caller-supplied deadline, and releasing and
  deleting the session. The deadline matters as much as the operations: the SDK applies a
  sixty-second default when told none, so the adapter states an infinite one and lets the caller's
  cancellation token be the turn's only deadline
- **The session event stream** — the usage event carrying the occupancy, the limit and the
  conversation's share; the tool-execution start and completion events; the session error event; and
  the runtime's own compaction and truncation events. A handler registered on the session
  configuration is installed before the create request is issued, which is what makes the first
  turn's events observable
- **The infinite-session configuration** — its background-compaction threshold raised clear of
  AgentKit's rotation point on every session AgentKit's own engine drives, so the runtime's compactor
  and AgentKit's do not act on one conversation. Its enablement flag is set false alongside, as a
  statement of intent; the runtime does not honor it and nothing depends on it

### Integration Pattern

`Microsoft.Agents.AI.GitHub.Copilot` is consumed by AgentKitAgentsCopilot, which references it as a
**direct** `PackageReference`. `CopilotAgentFactory` constructs a `SessionConfig` whose
`AvailableTools` is derived from the same collection as `Tools`, installs a permission handler, and
builds the agent through the session-config construction path with `ownsClient` left false. The same
factory builds the configuration for every session the compaction engine drives, adding only the
raised infinite-session compaction threshold; `CopilotProviderSessionFactory` then registers the event
handler and creates the session, and `CopilotSummarizer` creates a tool-free one per consolidation.
The host owns the client's lifetime; each session AgentKit creates is AgentKit's and is released and
deleted.

The SDK's session and client types are **sealed with non-public constructors and no virtual
members**, so neither can be faked in a test. AgentKit therefore reaches them through one internal
seam — `ICopilotTurnChannel` — so everything above it can be exercised without a live runtime; see
_CopilotTurnChannel Unit Design_. The SDK's event types, by contrast, are all constructable, so the
tests drive the genuine event shapes through the adapter's own matching code.

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

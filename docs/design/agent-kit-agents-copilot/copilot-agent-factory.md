## CopilotAgentFactory

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotAgentFactory` class builds a Microsoft Agent Framework `AIAgent` from a GitHub Copilot
`CopilotClient` and a supplied tool list, suppressing the runtime's built-in tools by deriving the
session allow-list from the supplied tools.

### Purpose

`CopilotAgentFactory` is the whole of the AgentKitAgentsCopilot system. Its essential job is
suppression: the Copilot runtime injects its own tools, and an application confining an agent to a
supplied tool set must withhold them. The available-tools list on the session configuration is an
allow-list, so the factory derives it from the same collection it assigns as the session's tools.
Deriving both from one collection, in one place, makes them impossible to drift apart.

The class is static and holds no state.

### Data Model

The factory constructs a `SessionConfig` carrying:

| Member                   | Source                                   | Purpose                                       |
|--------------------------|------------------------------------------|-----------------------------------------------|
| `Tools`                  | the supplied tools                       | the tools published to the session            |
| `AvailableTools`         | the names of the **same** supplied tools | the allow-list suppressing the built-in tools |
| `OnPermissionRequest`    | the host handler, or the safe default    | adjudicates each permission request           |
| `EnableSkills`           | `false`                                  | withholds the runtime's injected skills       |
| `SkipCustomInstructions` | `true`                                   | withholds runtime-discovered instructions     |
| `SystemMessage`          | the supplied instructions, when any      | governs a confined agent as the host intended |

### Key Methods

#### Create(CopilotClient client, IList&lt;AIFunction&gt; tools, instructions, onPermissionRequest, name)

The single public entry point. Validates its arguments, builds the session configuration, and
constructs the agent through the SDK's session-config agent-construction path with client ownership
left to the host.

**Algorithm:**

1. Reject a null `client`.
2. Validate `tools` (see `ValidateTools`).
3. Build the session configuration (see `BuildSessionConfig`).
4. Construct and return the agent from the client and the session configuration, passing the name
   and declaring that the factory does **not** own the client.

**Throws:** `ArgumentNullException` when `client` or `tools` is null, or a tool is null;
`ArgumentException` when `tools` is empty or two tools share a name.

#### BuildSessionConfig(IList&lt;AIFunction&gt; tools, string? instructions, handler? onPermissionRequest)

Internal seam producing the `SessionConfig`. It exists as a named, internal method so a test can
assert the safety-critical property — that `AvailableTools` is derived from the same collection as
`Tools` — without a live `CopilotClient`, since a `SessionConfig` is a plain constructable object.
It sets `Tools` and `AvailableTools` from `tools`, installs the supplied handler or the safe default,
disables skills and runtime custom-instruction discovery, and, when instructions are supplied,
carries them onto the session's system message with append semantics.

**Throws:** `ArgumentNullException` when `tools` is null or contains a null entry;
`ArgumentException` when `tools` is empty or two tools share a name.

#### CreateDefaultPermissionHandler(IList&lt;AIFunction&gt; tools)

Internal helper producing the safe-default permission handler. It captures the supplied tool names
once and returns a delegate that approves a request only when it is a custom-tool request naming one
of those tools, and rejects everything else. A built-in request — shell, read, write, url, and the
rest — is never a custom-tool request, so it can never match a supplied name and is always rejected.

#### ValidateTools(IList&lt;AIFunction&gt; tools)

Internal helper rejecting a null, empty, null-containing, or duplicate-named tool list, with ordinal
name comparison. An empty list would produce an empty allow-list; a duplicate name would make both
the published tool set and the derived allow-list ambiguous.

### Ownership and Disposal Contract

**The host owns the client; the factory owns nothing disposable.** The host constructs and starts
the `CopilotClient` and therefore disposes it. The factory builds the agent declaring that it does
**not** own the client, and creates nothing disposable of its own, so the rule is unambiguous:
*whoever created the client disposes it.* The factory does not return a disposable handle, because it
has nothing to dispose — a handle would only be warranted if the factory had itself constructed the
client. This is the reason the public entry point returns a bare `AIAgent` rather than an agent
paired with an ownership handle.

### Design Constraints

**The allow-list is derived, never independently supplied.** `AvailableTools` and `Tools` are both
built from the one `tools` collection in `BuildSessionConfig`, so a caller cannot publish one tool
set while allowing a different one. This is the safety property the package exists for.

**Only the session-config construction path suppresses.** The SDK offers two mutually exclusive
agent-construction paths — one taking a session configuration, one taking a bare tool list. Only the
session-configuration path carries an allow-list, so it is the one used; the tool-list path would
publish tools without suppressing the built-ins.

**No image-promoting decorator.** The Copilot runtime already delivers a tool-returned image to the
model, so the factory does not install Core's `ImagePromotingChatClient`; doing so would duplicate
content the model already received. This absence is intentional and must not be "corrected".

**Default-safe permission handling with host override.** When no handler is supplied the factory
installs the allow-list-enforcing default; when one is supplied it is installed unchanged, so a host
with its own policy governs the session itself.

**Deployment consequence.** `Microsoft.Agents.AI.GitHub.Copilot` carries a RID-specific native
runtime through its SDK dependency; a self-contained or RID-targeted publish resolves and ships the
native component for the target runtime identifier. See the *AgentKitAgentsCopilot System Design*
and the *Microsoft.Agents.AI.GitHub.Copilot Design*.

### Error Handling

| Condition                              | Handling                                   |
|----------------------------------------|--------------------------------------------|
| Null `client`                          | `ArgumentNullException` propagates         |
| Null `tools`, or a null tool entry     | `ArgumentNullException` propagates         |
| Empty `tools`                          | `ArgumentException` propagates             |
| Two tools sharing a name               | `ArgumentException` propagates             |

### Dependencies

- **Microsoft.Agents.AI.GitHub.Copilot** — supplies `CopilotClient`, `SessionConfig`, the
  session-config agent-construction path, and the permission RPC types.
- **Microsoft.Extensions.AI.Abstractions** — supplies the `AIFunction` currency the supplied tools
  are; reached transitively.

### Callers

`CopilotAgentFactory.Create` is a public API entry point. An application calls it once, after
constructing and starting its own `CopilotClient`, to obtain a Copilot agent confined to a supplied
tool set. Within this system nothing else calls it.

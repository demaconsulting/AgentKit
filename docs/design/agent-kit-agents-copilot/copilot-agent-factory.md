## CopilotAgentFactory

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotAgentFactory` class builds a Microsoft Agent Framework `AIAgent` from a GitHub Copilot
`CopilotClient` and a supplied tool list, suppressing the runtime's built-in tools by deriving the
session allow-list from the supplied tools.

### Purpose

`CopilotAgentFactory` builds the agent this package began as, and is now also the **single place a
Copilot session configuration is built** anywhere in the system. Its essential job is suppression:
the Copilot runtime injects its own tools, and an application confining an agent to a supplied tool
set must withhold them. The available-tools list on the session configuration is an allow-list, so
the factory derives it from the same collection it assigns as the session's tools. Deriving both
from one collection, in one place, makes them impossible to drift apart.

That "one place" now has three callers, not one: the agent path, `CopilotProviderSessionFactory` for
a compacting session, and `CopilotSummarizer` for a consolidation. All three end in the same
confinement block. A second builder with its own derivation is precisely the drift this design
exists to prevent, so there is not one.

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
| `Model`                  | the supplied model name, when any        | backs the session with the chosen model       |
| `InfiniteSessions`       | disabled, **engine paths only**          | stops two compactors fighting                 |

### Key Methods

#### Create(CopilotClient client, IList&lt;AIFunction&gt; tools, instructions, onPermissionRequest, name, model)

The single public entry point. Validates its arguments, builds the session configuration, and
constructs the agent through the SDK's session-config agent-construction path with client ownership
left to the host.

**Algorithm:**

1. Reject a null `client`.
2. Validate `tools` (see `ValidateTools`).
3. Build the session configuration (see `BuildSessionConfig`), passing the model through.
4. Construct and return the agent from the client and the session configuration, passing the name
   and declaring that the factory does **not** own the client.

**Throws:** `ArgumentNullException` when `client` or `tools` is null, or a tool is null;
`ArgumentException` when `tools` is empty or two tools share a name.

**Parameter ordering.** `model` is the trailing optional parameter rather than sitting beside
`instructions`, which is where it reads most naturally. The two goals conflict: inserting it earlier
would shift `onPermissionRequest` and `name`, breaking source compatibility for every positional
caller of a released public API. Compatibility wins, and the awkward position is recorded here so a
later maintainer does not "tidy" it.

#### BuildSessionConfig(IList&lt;AIFunction&gt; tools, string? instructions, handler? onPermissionRequest, string? model)

Internal seam producing the `SessionConfig`. It exists as a named, internal method so a test can
assert the safety-critical property — that `AvailableTools` is derived from the same collection as
`Tools` — without a live `CopilotClient`, since a `SessionConfig` is a plain constructable object.
It sets `Tools` and `AvailableTools` from `tools`, installs the supplied handler or the safe default,
disables skills and runtime custom-instruction discovery, and, when instructions are supplied,
carries them onto the session's system message with append semantics. When a model name is supplied
it is assigned to the session's model; when it is absent or blank the property is left untouched so
the runtime applies its own default, which is what makes the model parameter purely additive. The
model name is not validated: only the runtime knows which models the signed-in user may use.

**Throws:** `ArgumentNullException` when `tools` is null or contains a null entry;
`ArgumentException` when `tools` is empty or two tools share a name.

It leaves `InfiniteSessions` untouched, so a plain agent keeps the runtime's own compaction. See
_Session Configuration Is Built on One Path_ below.

#### BuildEngineSessionConfig(IList&lt;AIFunction&gt; tools, string? instructions, string? model)

Internal seam producing the `SessionConfig` for a session whose context AgentKit's own session engine
manages — a compacting conversation, or a consolidation. It applies exactly the same confinement as
the agent path, through the same private helper, and then sets one further property: the Copilot
runtime's infinite-session compaction is **disabled**.

It differs from the agent path in one further respect: it accepts an **empty** tool list. A
consolidation runs on a session that must offer no tools at all, which is a correct engine-driven
session and an incorrect agent. The confinement is identical either way — an empty tool list derives
an empty allow-list and a permission handler that approves nothing, which is the strongest
confinement this factory can express rather than the weakest.

**Throws:** `ArgumentNullException` when `tools` is null or contains a null entry;
`ArgumentException` when two tools share a name.

#### CreateSessionConfig(tools, instructions, onPermissionRequest, model)

Private helper: the single place a `SessionConfig` is constructed in this package. Both seams above
reach it, which is what keeps the confinement, the model choice and the system message on one path;
they differ only in which tool lists they accept and in whether they disable the runtime's own
compaction afterwards.

#### ApplyConfinement(SessionConfig config, IList&lt;AIFunction&gt; tools, handler? onPermissionRequest)

Private helper: the one safety-critical block in this package. `Tools` and `AvailableTools` are
assigned from the same collection in the same two statements, so no reachable state has a published
tool that is not allowed or an allowed name that is not published. Skills are disabled, custom
instructions are skipped, and the supplied handler or the safe default is installed.

#### CreateDefaultPermissionHandler(IList&lt;AIFunction&gt; tools)

Internal helper producing the safe-default permission handler. It captures the supplied tool names
once and returns a delegate that approves a request only when it is a custom-tool request naming one
of those tools, and rejects everything else. A built-in request — shell, read, write, url, and the
rest — is never a custom-tool request, so it can never match a supplied name and is always rejected.

#### ValidateTools(IList&lt;AIFunction&gt; tools)

Internal helper rejecting a null, empty, null-containing, or duplicate-named tool list, with ordinal
name comparison. An empty list would produce an empty allow-list for an _agent_, which is a defect in
its host; a duplicate name would make both the published tool set and the derived allow-list
ambiguous.

#### ValidateToolNames(IList&lt;AIFunction&gt; tools)

Internal helper rejecting a null, null-containing or duplicate-named tool list, and accepting an
empty one. Split out from `ValidateTools` because emptiness is the one rule the two configuration
paths disagree about; everything else it checks is a defect on either path.

### Session Configuration Is Built on One Path

Every Copilot session this package creates — for an agent, for a compacting conversation, and for a
consolidation — is configured by this class, through `CreateSessionConfig` and `ApplyConfinement`.
The two public-facing seams differ in exactly two respects, both deliberate:

| | `BuildSessionConfig` (agent) | `BuildEngineSessionConfig` (session engine) |
| --- | --- | --- |
| Empty tool list | refused | accepted |
| Runtime's own compaction | left untouched | disabled |
| Allow-list, skills, custom instructions, handler, model, system message | identical | identical |

**The compaction asymmetry is the one a maintainer is most likely to "tidy", and must not.** A
session the engine drives has an AgentKit compactor behind it, and two compactors reading the same
occupancy signal would fight: the runtime would rewrite history underneath a session whose transcript
the engine believes it owns. A plain agent has no AgentKit compactor behind it, so disabling the
runtime's there would remove the only protection that session has when its window fills. A test pins
each side.

### Ownership and Disposal Contract

**The host owns the client; the factory owns nothing disposable.** The host constructs and starts
the `CopilotClient` and therefore disposes it. The factory builds the agent declaring that it does
**not** own the client, and creates nothing disposable of its own, so the rule is unambiguous:
_whoever created the client disposes it._ The factory does not return a disposable handle, because it
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

**Model selection is reachable through the factory, not around it.** Which Copilot model backs a
session materially changes how reliably a confined agent uses its tools and how accurately it reads
an image, so the host must be able to choose one. It must be able to choose it _here_: a host that
built a session configuration itself to reach the setting would forfeit the derived allow-list, the
withheld skills and custom instructions, and the default-safe permission handler — producing an
agent that looks configured but denies every tool call. Exposing the setting on the factory removes
the incentive to bypass it.

**Deployment consequence.** `Microsoft.Agents.AI.GitHub.Copilot` carries a RID-specific native
runtime through its SDK dependency; a self-contained or RID-targeted publish resolves and ships the
native component for the target runtime identifier. See the _AgentKitAgentsCopilot System Design_
and the _Microsoft.Agents.AI.GitHub.Copilot Design_.

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
tool set. Within this system, `CopilotProviderSessionFactory` and `CopilotSummarizer` call
`BuildEngineSessionConfig` — which is what keeps every session in the package on one confinement
path; see _CopilotProviderSessionFactory Unit Design_ and _CopilotSummarizer Unit Design_.

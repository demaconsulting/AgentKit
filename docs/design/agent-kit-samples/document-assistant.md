## DocumentAssistant

![AgentKit Samples Structure](AgentKitSamplesView.svg)

The `document-assistant` sample is a console chat application that demonstrates the consumption path:
how an application attaches AgentKit's shipped tool packs to an agent under a policy that grants a
workspace to read and a separate session location to write.

### Purpose

DocumentAssistant exists to make the AgentKit safety model **observable**. It grants an agent exactly
two locations — a workspace folder and a separate session folder — hands it the shipped text-file,
file, Markdown, and image tool packs, and prints every tool call and result. Containment, asymmetric
read/write grants, capability gating, the relative-versus-absolute path dialect, and the adapters'
suppression of provider built-ins are all visible as they happen. It runs unchanged against the
GitHub Copilot runtime and any Ollama model.

The sample's most instructive surface is the per-run instructions it builds: rather than holding a
fixed constant, it constructs the instructions from the run's granted locations so it can name them
and their access truthfully. The unit's demonstrations pin that behavior — both locations named, the
access following the grant, the capability statement truthful, and the discovery, refusal-handling,
and path-dialect guidance present — so a change from a constant to a builder cannot quietly drop what
the sample relies on.

This unit is a folder of several source files rather than a single class, the same deviation from the
one-file-per-unit convention that all three samples share. The unit's `sourceRef` in the SysML2 model
points at the `samples/document-assistant/` folder and its `testRef` at the sample's test project
folder.

### Data Model

- **`CommandLineOptions`** — the validated run configuration, including the workspace and session
  locations and whether the workspace is granted read-only.
- **`AgentSetup`** — a record pairing the constructed agent with a uniform cleanup handle, so the
  host disposes one thing without learning which provider it built.
- The instructions are a string built per run; they are the unit's principal observable artifact and
  the subject of its tests.

### Key Methods

- **`AgentComposition.BuildInstructions(workspacePath, sessionPath, readOnlyWorkspace)`** — builds the
  system instructions for a run. It names both granted locations and the access each carries, states
  truthfully that the agent has no shell, terminal, code-execution, or web tool, requests discovery
  through a no-argument listing, tells the agent to read a refusal rather than retry it, and explains
  the relative-versus-absolute path dialect (relative names for the workspace, an absolute path for
  the session location, and why a relative name cannot reach the session location). Because the text
  follows the locations, different grants produce different instructions.
- **`AgentComposition.BuildTools(...)`** — composes one policy over the two granted locations and the
  shipped packs into the tool list handed to a provider factory.
- **`AgentComposition.CreateAgentAsync(...)`** — composes the tool set once, then branches only to
  select a provider factory and return a uniform cleanup handle.

### Error Handling

A malformed command line raises `CommandLineException`, reported as a usage error. A request outside a
granted location is refused by the policy as a returned denial the agent reads, not a thrown
exception. The read-only workspace grant is enforced by the policy; the instructions merely state it,
and the sample's tests confirm the statement follows the grant.

### Dependencies

- **AgentKitCore** — `PathPolicy`, `PathRule`, and the pack builder; see _AgentKitCore System
  Design_.
- **AgentKitTools** — the shipped text-file, file, Markdown, and image packs; see _AgentKitTools
  System Design_.
- **AgentKitAgentsChatClient** and **AgentKitAgentsCopilot** — the provider factories; see their
  system design documents.
- **Ollama** (`OllamaSharp`) and the GitHub Copilot SDK — the provider runtimes.

### Callers

The sample is an application entry point run by a reader; nothing in this repository calls it. Its
`AgentComposition.BuildInstructions` method is exercised directly by the
`DemaConsulting.AgentKit.Samples.DocumentAssistant.Tests` project, which asserts the instructions name
the locations, follow the grants, and keep the guidance the sample's demonstrations depend on.

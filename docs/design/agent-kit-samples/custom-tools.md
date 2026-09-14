## CustomTools

![AgentKit Samples Structure](AgentKitSamplesView.svg)

The `custom-tools` sample is a console chat application that demonstrates the extension path: how an
application author writes their own guarded tools with `GuardedToolFactory` and publishes them as
packs that compose alongside a shipped pack.

### Purpose

CustomTools exists to prove one thing to an author: **an author-written pack composes exactly like a
shipped one.** It ships two author-written packs — a `docstats` pack whose `docstats_wordcount` tool
takes a path and so goes through the policy for containment, and a `clock` pack whose `clock_now`
tool takes no path and so consults no policy — and adds both to the same `ToolPackBuilder`, on the
same `PathPolicy`, as the shipped text-file pack. The sample makes observable that a path-taking
author tool behaves indistinguishably from a shipped one (relative addressing, containment, a
returned refusal rather than a thrown exception, a structured result), and that the guarded
construction path is equally the way to build a tool that needs no location at all.

This unit is a folder of several source files rather than a single class, which is the one deviation
from the repository's usual one-file-per-unit convention. The unit's `sourceRef` in the SysML2 model
therefore points at the `samples/custom-tools/` folder rather than a single file, and its `testRef`
at the sample's test project folder. The deviation is intrinsic to what a sample is: a complete
runnable application read as one worked example, not a class under test.

### Data Model

- **`AgentSetup`** — a record pairing the constructed agent with a uniform `IAsyncDisposable` cleanup
  handle, so every line after construction is provider-neutral and the host disposes one thing
  without learning which provider it built.
- **`CommandLineOptions`** — the validated run configuration (provider, model, host), constructed
  from the command line.
- The author-written tools return structured results: `docstats_wordcount` returns a record of a
  file's path, character, word, and line counts, or a discovery structure for a no-argument request;
  `clock_now` returns a record of the current local time, UTC time, and local time zone.

### Key Methods

- **`AgentComposition.BuildTools(workspaceRoot)`** — composes one `PathPolicy` over the workspace
  (granted read-write, and set as the working directory relative paths anchor to), then adds the
  shipped `TextFilePack` and the author-written `DocStatsToolPack` and `ClockToolPack` to one
  builder. `Build` verifies every tool name carries its pack's declared family prefix, so a malformed
  custom pack fails at composition rather than at a model's call.
- **`AgentComposition.BuildInstructions(workspaceRoot)`** — builds the system instructions, naming
  the workspace and the author-written tools truthfully so the model does not invent capabilities it
  will only be refused.
- **`AgentComposition.CreateAgentAsync(options, workspaceRoot, cancellationToken)`** — composes the
  tool set once, then branches solely to select a provider factory and return a uniform cleanup
  handle. This is the only place the sample is provider-aware.
- **`DocStatsWordCountTool`** — the path-taking author tool: it resolves a relative name against the
  granted workspace through the public `PathPolicy` helpers, refuses a path outside the workspace
  with a returned denial naming the containment reason, and reports a file's location in the same
  path dialect the shipped tools use.
- **`ClockNowTool`** — the no-path author tool: it consults no policy and returns the current time as
  a structured result, demonstrating that the guarded construction path is for every tool.

### Error Handling

A malformed command line raises `CommandLineException`, which the program reports as a usage error. A
malformed tool pack (a tool name not carrying its declared family prefix) is refused by
`ToolPackBuilder.Build` at composition, before any model call. A request the policy denies — a path
outside the workspace — is answered by the path-taking tool as a returned refusal that the model
reads, not as a thrown exception. An unsupported provider selection raises `CommandLineException`.

### Dependencies

- **AgentKitCore** — `GuardedToolFactory`, `PathPolicy`, `PathRule`, `ToolName`, `ToolPack`,
  `ToolPackBuilder`; see _AgentKitCore System Design_.
- **AgentKitTools** — the shipped `TextFilePack`; see _AgentKitTools System Design_.
- **AgentKitAgentsChatClient** and **AgentKitAgentsCopilot** — the provider factories; see their
  system design documents.
- **Ollama** (`OllamaSharp`) and the GitHub Copilot SDK — the provider runtimes.

### Callers

The sample is an application entry point run by a reader; nothing in this repository calls it. Its
composition methods are exercised directly by the `DemaConsulting.AgentKit.Samples.CustomTools.Tests`
project, which invokes the tools the way a runtime would and asserts their structured results and
refusals.

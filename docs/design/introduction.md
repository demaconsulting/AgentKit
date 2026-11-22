# Introduction

This document provides the detailed design for AgentKit, a family of .NET libraries providing
guarded, provider-neutral agent tools.

## Purpose

The purpose of this document is to serve as the design entry point and provide detailed design
specifications for the AgentKit system. This documentation enables formal code
review by providing implementation specifications, supports compliance auditing by maintaining
clear traceability from requirements through design to code, aids maintenance by documenting
system structure and interactions, and ensures quality assurance through detailed technical
specifications.

This document is intended for:

- Software developers implementing and maintaining the system
- Code reviewers validating implementation against design
- Compliance auditors tracing requirements through design to implementation
- Quality assurance teams validating system behavior

## Scope

This document covers the detailed design of the AgentKit system and its constituent
software items, specifically:

- **AgentKitCore (System)** — The contract package every other AgentKit package depends upon
- **RealPathResolver (Unit)** — Reports the absolute, normalized location a path denotes, with
  relative segments collapsed
- **PathRule (Unit)** — One access grant, unrestricted or confined to a location, carrying an
  access level and its own denied patterns
- **PathPolicy (Unit)** — Holds the one working directory relative paths are anchored to and the
  zero-or-more access grants that permit locations, keeping addressing and permission orthogonal,
  and makes the single containment decision used by both direct access and directory enumeration
- **ToolLimits (Unit)** — The ceilings every governed tool observes when reading, returning and
  attaching content
- **ToolResult (Unit)** — The results a guarded tool returns to the model, including refusals and
  the declaration that an output is too large to return
- **ToolOutputOptions (Unit)** — The preview size and retention ceilings observed when large tool
  outputs are retained
- **ToolOutputStore (Unit)** — The in-memory, per-composition store of retained tool outputs, named
  by opaque references and bounded by count and size
- **ToolOutputSpill (Unit)** — Opt-in composition of retention into a tool list: an oversized output
  is retained and replaced by a bounded preview and a reference
- **ToolOutputRetention (Unit)** — One conversation's retention handle: wraps tools added at any
  time into one store, publishes the reader tools, and exports and restores the retained outputs
- **ToolOutputReadTool (Unit)** — Publishes the `tool_output_read` tool, which pages through a
  retained output by line number
- **ToolOutputSearchTool (Unit)** — Publishes the `tool_output_search` tool, which finds a pattern
  in a retained output
- **ToolName (Unit)** — The family-prefix naming convention and its validation
- **GuardedToolFactory (Unit)** — The only supported way to construct a tool, applying the
  result-delivery guard and the naming rules to every tool it creates
- **ToolPack (Unit)** — The contract a package implements to publish its tools as one
  capability-gated family, and the host capabilities a pack may require
- **ToolPackBuilder (Unit)** — Capability-gated composition of tool packs into the tool list an
  application offers a model
- **ImagePromotingChatClient (Unit)** — Makes an image a tool returned visible to a provider whose
  tool-result channel cannot carry one, by promoting it onto a following user message
- **AgentSession (Unit)** — The session contract an application programs against, and what one turn
  reports back about the answer, the usage, the rotation, the compaction level, and any dropped
  material
- **AgentSessionOptions (Unit)** — What an application configures about one session, including the
  maximum number of most-recent turns kept verbatim
- **ContextUsage (Unit)** — The one usage shape every provider session answers with: how full the
  context is, out of how much
- **SessionTranscript (Unit)** — The append-only verbatim history kept out of session, grouped into
  whole turns so a tool call is never separated from its result
- **ContextLayout (Unit)** — The whole context as the session engine accounts for it: the verbatim
  tail, the rings of consolidated slots, and the coarsest-first seed
- **RotationEngine (Unit)** — The deterministic aging function: consolidate older turns into a
  tier-one slot, cascade a full tier into the next, and report the consolidations and any material a
  failed consolidation left unrecorded
- **Summarizer (Unit)** — The injected out-of-session consolidation contract, the request that
  carries the consolidation instruction, and the documented default prompt
- **ProviderSession (Unit)** — The whole interface between the compaction engine and a provider
  adapter: the seed, the turn, the session that answers for its own window, and the factory
- **InMemoryProviderSession (Unit)** — A provider session that contacts nothing, so the engine can
  be exercised end to end without a live model
- **CompactingAgentSession (Unit)** — The implementation that sequences turns, usage reads,
  rotations and provider-session disposal
- **AgentKitChatReducer (Unit)** — A chat reducer for a host that owns its chat history,
  consolidating older messages through the summarizer while keeping the system messages and a
  verbatim tail
- **ModelInfo (Unit)** — The provider-neutral description of a model: its capabilities, the settings
  it accepts, its efforts and tiers, its ceiling window, whether it is usable, and its notices; and the
  conversion of its capabilities into host capabilities
- **ModelCatalog (Unit)** — The catalog contract, the query, and the caching base: a five-minute
  lifetime, one shared read at a time, no cached failures, a refresh on request, and the `SelectAsync`
  gate a session starts through
- **ModelSettings (Unit)** — The persistable settings a user chooses, their validation against a
  model, and the validated `ModelSelection` a provider factory accepts
- **ModelSettingsChatClient (Unit)** — The decorator that applies a selection to every `IChatClient`
  request without overriding what the caller set
- **AgentKitTools (System)** — A general-purpose capability package of guarded tool families
  built on the AgentKitCore contract, organized as one subsystem per tool family
- **TextFile (Subsystem)** — The text file tool family: policy-governed searching, reading,
  creating and editing of text files, published as one capability-gated pack
- **TextFileSearchTool (Unit)** — Publishes the `text_file_search` tool
- **TextFileReadTool (Unit)** — Publishes the `text_file_read` tool
- **TextFileCreateTool (Unit)** — Publishes the `text_file_create` tool
- **TextFileWriteTool (Unit)** — Publishes the `text_file_write` tool
- **TextFileReplaceTool (Unit)** — Publishes the `text_file_replace` tool
- **TextFileCutLinesTool (Unit)** — Publishes the `text_file_cut_lines` tool
- **TextFileCopyLinesTool (Unit)** — Publishes the `text_file_copy_lines` tool
- **TextFilePasteLinesTool (Unit)** — Publishes the `text_file_paste_lines` tool
- **TextFileLineBuffers (Unit)** — Holds the recoverable named text buffers that `text_file_cut_lines`
  and `text_file_copy_lines` capture into, that `text_file_write` captures displaced content into,
  and that `text_file_paste_lines` restores from
- **TextFilePack (Unit)** — Publishes the text file family as one pack
- **File (Subsystem)** — The file tool family: policy-governed listing, copying, moving and deleting
  of files of any type, and creating, moving and recursively deleting the directories that hold
  them, published as one capability-gated pack
- **FileListTool (Unit)** — Publishes the `file_list` tool
- **FileCopyTool (Unit)** — Publishes the `file_copy` tool
- **FileMoveTool (Unit)** — Publishes the `file_move` tool
- **FileDeleteTool (Unit)** — Publishes the `file_delete` tool
- **FileCreateDirectoryTool (Unit)** — Publishes the `file_create_directory` tool
- **FileMoveDirectoryTool (Unit)** — Publishes the `file_move_directory` tool
- **FileDeleteDirectoryTool (Unit)** — Publishes the `file_delete_directory` tool
- **FilePack (Unit)** — Publishes the file family as one pack
- **Markdown (Subsystem)** — The Markdown tool family: policy-governed outlining of a Markdown file's
  heading structure, published as one capability-gated pack
- **MarkdownOutlineTool (Unit)** — Publishes the `markdown_outline` tool
- **MarkdownPack (Unit)** — Publishes the Markdown family as one pack
- **Image (Subsystem)** — The image tool family: policy-governed reading of images and PDF
  documents for a vision-capable agent, and extraction of a region of an image — either named in
  pixels or found by trimming the image to its own content — published as one capability-gated pack
- **ImageMediaTypes (Unit)** — Maps a file's extension to the media type the image family reads,
  and composes the refusal for a file whose type it cannot read
- **ImageReadTool (Unit)** — Publishes the `image_read` tool
- **ImageCropTool (Unit)** — Publishes the `image_crop` tool
- **ImageAutoCropTool (Unit)** — Publishes the `image_auto_crop` tool
- **ImagePack (Unit)** — Publishes the image family as one pack
- **Todo (Subsystem)** — The todo tool family: one flat, in-memory task list per agent that the
  agent writes down, advances and closes out, published as one pack
- **TodoStore (Unit)** — Holds one agent's flat task list, allocated per composition so that a
  delegated agent cannot reach its parent's list
- **TodoListTool (Unit)** — Publishes the `todo_list` tool
- **TodoSetTool (Unit)** — Publishes the `todo_set` tool
- **TodoRemoveTool (Unit)** — Publishes the `todo_remove` tool
- **TodoPack (Unit)** — Publishes the todo family as one pack, and the instruction an application
  must give an agent for the family to be used at all
- **Memory (Subsystem)** — The memory tool family: a searchable record of what an agent has learned,
  each memory a short embedded descriptor with a richer never-embedded payload and its provenance,
  published as one pack
- **MemoryOptions (Unit)** — The author's near-duplicate threshold and recall count
- **MemoryStore (Unit)** — The memory and match records, the substitutable persistence contract for
  memories and its default in-process implementation
- **MemoryFileTool (Unit)** — Publishes the `memory_file` tool
- **MemoryRecallTool (Unit)** — Publishes the `memory_recall` tool
- **MemoryUpdateTool (Unit)** — Publishes the `memory_update` tool
- **MemoryReviseTool (Unit)** — Publishes the `memory_revise` tool
- **MemoryForgetTool (Unit)** — Publishes the `memory_forget` tool
- **MemoryPack (Unit)** — Publishes the memory family as one pack, taking the embedding generator,
  the author's controls and optional substitute persistence from the composing application
- **Agent (Subsystem)** — The agent tool family: delegation of a task to another agent the
  application registered by name, published as one capability-gated pack
- **AgentProfile (Unit)** — One named child agent the application is willing to have started: its
  instructions, the tool names it admits, and any narrowing of its path grants
- **AgentRunTool (Unit)** — Publishes the `agent_run` tool, and defines the request bundle the
  host's runner is handed for one delegated agent, carrying tools composed from the child's own
  state
- **AgentPack (Unit)** — Publishes the agent family as one pack, and composes a child's tools from
  the registered packs rather than from the parent's tool list
- **AgentKitAgentsChatClient (System)** — Builds a Microsoft Agent Framework agent from any
  `IChatClient`, and carries a Core session over that same `IChatClient`, installing the
  image-promoting decorator unconditionally on both
- **ChatClientAgentFactory (Unit)** — The static factory that wraps the supplied client in the
  image-promoting decorator and builds a `ChatClientAgent`
- **ChatClientProviderSession (Unit)** — One Core session over an `IChatClient`: the seeded message
  list, the conversation resent on every turn, and the occupancy reported against a supplied window
- **ChatClientProviderSessionFactory (Unit)** — Holds the client and the window, builds the pipeline
  each session runs on, and creates a session from a seed at the start of a conversation and again
  at every rotation
- **PromptSizeRecordingChatClient (Unit)** — Records the prompt size of each individual request
  beneath the tool-calling loop, so occupancy is the last request's prompt rather than usage summed
  across a tool-calling turn
- **ChatClientSummarizer (Unit)** — Consolidates history through an `IChatClient` of the
  application's choosing, out of the session being compacted
- **ToolPackContextProvider (Unit)** — Contributes AgentKit tools and instructions to every run of a
  host-built `ChatClientAgent`, composed once per conversation, with each conversation's retention
  persisted in its session
- **AgentKitAgentsCopilot (System)** — Gives a GitHub Copilot `CopilotClient` an agent of AgentKit's
  own — a Microsoft Agent Framework `AIAgent` over confined Copilot sessions, with the runtime's
  built-in tools suppressed by deriving the session allow-list from the supplied tools — and carries
  an AgentKit session over the same runtime
- **CopilotAgentFactory (Unit)** — The static factory that refuses built-in tool names, derives the
  source-qualified allow-list, withholds the runtime's injection channels, pins the permission mode,
  installs a default-deny permission handler, and builds the agent without taking ownership of the
  runtime; the single place a Copilot session's options are built, created or resumed
- **CopilotAgent (Unit)** — AgentKit's own `AIAgent` over confined Copilot sessions, with the public
  `CopilotAgentSession` naming one conversation: creates a runtime session on the first run and
  resumes it on every later one, streams the runtime's events as agent updates, sends inline data as
  in-memory attachments, and releases a conversation's session without deleting it after every run
  (a run made without a conversation deletes its session)
- **CopilotRuntime (Unit)** — The Copilot runtime AgentKit starts, confines and owns: Empty mode,
  the installed Copilot CLI by default (version-checked) or the SDK's bundled runtime on request,
  spawned as a child process with the host's `COPILOT_`/`GITHUB_COPILOT_`
  variables withheld apart from an allow-listed credential, network and telemetry-off set, a data directory AgentKit
  owns; exposes only neutral options, reports the started runtime's version, refuses one that is not
  signed in, explains a runtime that cannot start, and lets the host delete a kept conversation
- **CopilotCli (Unit)** — The installed Copilot CLI: found on `PATH` and in the platform's well-known
  installation locations with editor launchers passed over, its version read as the SDK runs it, the
  minimum version enforced, and refusals explained with install and upgrade guidance
- **CopilotProviderSession (Unit)** — One Core session over one Copilot session: sends a turn,
  carries the seeded record ahead of its first message, records the runtime's tool traffic, reports
  the runtime's own occupancy, and ends the session on a turn it cannot account for
- **CopilotProviderSessionFactory (Unit)** — Creates one seeded Copilot session per rotation,
  configuring the system message with the application's instructions alone, composing the seeded
  history into a fenced record for the first message to carry, and holding the runtime's own
  compaction clear of the engine's rotation point
- **CopilotSessionObserver (Unit)** — Watches the runtime's event stream for the usage reading, the
  turn's tool traffic, and any sign the runtime rewrote history itself
- **CopilotSummarizer (Unit)** — Consolidates history on a short-lived, tool-free Copilot session,
  out of the session being compacted
- **CopilotModelCatalog (Unit)** — Describes the models the Copilot service offers the signed-in user,
  read through the runtime AgentKit started
- **CopilotTurnChannel (Unit)** — The internal seam over the SDK's sealed session types, which is
  what makes everything above it testable without a live Copilot runtime, and the production channel
  that creates or resumes a session and on release deletes it or keeps it
- **AgentKitAgentsOllama (System)** — Makes the context window a compacting session accounts against
  the window the Ollama instance is actually using, by asking the server for a size and by reading
  back what a running instance reports, and describes the models the server holds as a model catalog
- **OllamaContextWindow (Unit)** — The discovered window and its source, the conservative fallback,
  the reading of the server, and the pure precedence function that chooses among what it reported
- **OllamaContextSizingChatClient (Unit)** — The decorator that names the application's chosen
  context length on every request, so the instance Ollama runs is the one the session accounts
  against
- **OllamaModelCatalog (Unit)** — Describes the models an Ollama server holds: their capabilities,
  whether an agent can run on them, their efforts, and their trained length as a ceiling

The following OTS items are also covered:

- **BuildMark** — build-notes documentation tool
- **ApiMark** — public API surface tracking tool
- **CanvasNet** — the runtime raster imaging library providing image header inspection including
  decode feasibility, PNG and JPEG decoding with PNG decompression bounded by the declared size,
  rectangular region extraction and PNG encoding
- **FileAssert** — document assertion tool
- **Microsoft.Agents.AI** — the runtime library providing `ChatClientAgent`, which builds an
  `AIAgent` from an `IChatClient`
- **Microsoft.Agents.AI.Abstractions** — the Agent Framework's abstractions library providing the
  `AIAgent` and `AgentSession` base types and the `AgentResponseUpdate` a streamed run produces
- **Microsoft.Copilot.SDK** — the GitHub Copilot SDK providing `CopilotClient`, the create and
  resume session options, the permission RPC, the session lifecycle, the session event stream, and
  the build-time provisioning of the native Copilot runtime, the model listing, and the per-session
  inference options (reasoning effort and context tier)
- **Microsoft.Extensions.AI.Abstractions** — the runtime library providing the
  `AIFunction`/`AIContent` tool currency, and the request options (reasoning, output limit and model
  identifier) a model selection travels on
- **OllamaSharp** — the Ollama client library providing the loaded-model report carrying a running
  instance's context length, the carriage of a context-length option onto the wire, the model listing
  and per-model description, and the mapping of a reasoning effort to Ollama's `think` option
- **Pandoc** — Markdown-to-HTML conversion tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model lint and diagram rendering tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework

The document also opens with a product-capability chapter. AgentKit's three provider capabilities —
GitHub Copilot support, `IChatClient` support, and support for a model served by Ollama — are
requirements of the product rather than of any one package, so they correspond to no software item
and appear nowhere in the software-item tree; the chapter records which system delivers each and what
it rests on. The Ollama capability is delivered through the same `IChatClient` adapter as the second,
because that is how Ollama reaches an application — there is no Ollama session, agent or summarizer —
and what `AgentKitAgentsOllama` adds beneath it is making the context window that adapter must be
told the one the Ollama instance is actually running.

A fourth product requirement, `AgentKit-ModelSelection`, promises that an application can list a
provider's models, filter them by capability, and start a session only on a validated selection, on
all three providers. Core and the three adapters deliver it jointly, and the chapter records that too.

Version applicability: This design applies to all versions of the AgentKit.

The following topics are explicitly excluded from this design documentation:

- External library internals and third-party OTS components
- Build pipeline configuration and CI/CD processes
- Deployment, packaging, and distribution mechanisms
- Infrastructure and hosting environment details
- Test projects and test infrastructure
- The demonstration samples under `samples/`, and their test projects under
  `test/DemaConsulting.AgentKit.Samples.*.Tests/`

The samples exclusion is a deliberate classification decision, not an omission, and it is
recorded here so that it is not reversed by inspection. A Software System is a "complete
deliverable product including all components and external interfaces, **contained within a
software package**", and a Software Package is "one distributable artifact". The samples are
demonstration applications: none of them is packed, published, given an SBOM, or contained in
any software package, so no sample is a software system, and nothing within a sample is a
subsystem or unit of one. They therefore appear nowhere in the software-item tree — no entry
in the SysML2 model, no requirements under `docs/reqstream/`, no design or verification
chapter, and no ReviewMark review-set — and `.reviewmark.yaml` excludes `samples/**` and the
sample test projects from `needs-review` for that reason rather than leaving them uncovered.

The samples are not unverified: each has its own test project, and those tests build and run
with every other test through `build.ps1`, which is the gate that keeps the samples working.
What the samples do not have is traceability — no requirement is written against a sample and
no sample test is counted as requirement evidence. That is the accepted position: their value
is pedagogical, they are read rather than deployed, and writing "the sample shall demonstrate
X" requirements adds compliance artifacts without adding a deliverable. An agent tempted to
add the samples to the tree should read this paragraph as the answer, not as a gap.

## Software Structure

The software structure is modeled in SysML2 under `docs/sysml2/` and rendered to the
diagram below by SysML2Tools as part of the build pipeline. AI agents should query the
SysML2 model directly (see the `sysml2tools-query` skill) rather than parsing this
diagram or the prose below.

![Software Structure](SoftwareStructureView.svg)

`AgentKitCore` is flat: its thirty-one units sit directly under the system with no intervening
subsystems. That is now a decision rather than a consequence of smallness, and the system design
chapter records what those thirty-one units actually look like: three path-safety units, four
tool-contract units, two pack-contract units, six tool-output units, ten session units, two
provider-compatibility units — `ImagePromotingChatClient` and `AgentKitChatReducer` — which stand apart
from the rest, and four model-selection units — `ModelInfo`, `ModelCatalog`, `ModelSettings` and
`ModelSettingsChatClient`.

No single boundary divides that into coherent halves. A subsystem layer would therefore not draw one
line but four or five, and each would cost a requirements file, a design document, a verification
document and a review set without removing a single unit anyone has to review. The groups are
already legible from the unit names and from the collaborations the system design chapter sets out,
which costs nothing.

This is the point at which that judgment should be revisited. If one of those groups grows enough
that a reader cannot hold it in view, the boundary around it stops being free and earns its
artifacts.

The repository contains five systems. `AgentKitCore` is the heart of the product and the one library
guaranteed to be imported. It supplies the contract every other package builds on — the policy
primitives that bound where a tool may act, the single guarded construction path, the result
constructors and the pack contract — and, alongside them, the provider-agnostic session engine that
keeps a long-running agent alive. The engine owns the conversation lifecycle and compacts a full
context by rotating into a fresh provider session seeded with tiered, consolidated history. It lives
in Core rather than in a package of its own because handing an agent capabilities that are safe by
construction is worth nothing if the agent cannot run long enough to use them: critical functionality
in a package a developer has to discover is a packaging mistake rather than a design. The engine is
provider-agnostic by design — the same rotation behavior on a provider that re-sends history each
turn and on one that holds it server-side — and carries no provider dependency; its provider seam is
the `IProviderSession` interface, which `AgentKitAgentsChatClient` implements for every provider
reached as a chat client and `AgentKitAgentsCopilot` implements natively for the GitHub Copilot
runtime — which exposes no chat client at all, and reports its own occupancy and window rather than
being told them.

`AgentKitTools` is a general-purpose capability package of
guarded tool families built on the AgentKitCore contract. It ships seven families today, each its
own subsystem: `TextFile`, which searches, reads, creates, sets, replaces and moves line ranges
within
text files under the policy; `File`, which lists, copies, moves and deletes files of any type, and
creates, moves or renames, and recursively deletes the directories holding them;
`Markdown`, which outlines a document's headings with their line ranges;
`Image`, which reads images and PDF documents for a vision-capable agent, returns a region of one
and trims one to the region its own content occupies; `Todo`, which gives an
agent one flat task list of its own; `Memory`, which gives an agent a searchable record of what it
has learned; and `Agent`, which delegates a task to another agent the
application registered. All compose through
the same guarded construction path and pack contract Core publishes. `AgentKitTools` is a peer of
the other capability packages an application may attach, depending on `AgentKitCore` but never
depended upon by another capability package.

`AgentKitAgentsChatClient` and `AgentKitAgentsCopilot` are the two provider-adapter systems. Each
turns a provider into a Microsoft Agent Framework agent carrying a supplied tool set, and each is
justified by runtime dependencies that must be kept out of Core: `AgentKitAgentsChatClient` carries
`Microsoft.Agents.AI`, and `AgentKitAgentsCopilot` carries `Microsoft.Copilot.SDK` and
`Microsoft.Agents.AI.Abstractions`. `AgentKitAgentsChatClient` builds its agent through the Agent
Framework's own `ChatClientAgent`; the Copilot SDK ships no Agent Framework agent, so
`AgentKitAgentsCopilot` carries an agent of its own over confined Copilot sessions.
`AgentKitAgentsCopilot` carries ten units: that agent, its factory, the runtime it starts and owns,
the installed CLI that runtime runs by default, a provider session, its factory, a summarizer, a model
catalog, the observer that
reads occupancy from the runtime's event stream, and the internal turn channel that makes all of it
testable without a live runtime. `AgentKitAgentsChatClient` holds six:
that factory, a provider session, its factory, a summarizer, the recorder that reads occupancy
from the last request of a turn, and the context provider that contributes AgentKit tools to a
host-built agent per conversation. Both are flat, and the two share no code and never reference each
other.

`AgentKitAgentsOllama` is the fifth system and the odd one out: it builds no agent and carries no
session. It exists for the single thing Ollama needs that the chat-client adapter cannot supply —
a context window the instance is actually running, which an `IChatClient` publishes nowhere and
which is only reachable through Ollama's own APIs. It is a package of its own because that work
needs `OllamaSharp`, and the chat-client adapter serves every provider in its family and must stay
free of any one of them. Three units — one that reads the window, one that asks for it, and one that
describes the models the server holds. It depends on Core only for the provider-neutral model
description its catalog returns, and nothing of its own appears in the session an application goes
on to build.

The `SoftwareStructureView.svg` above renders all five systems.

The demonstration samples under `samples/` are not among them. They are runnable examples rather
than deliverables, belong to no software package, and are excluded from the software-item tree for
the reasons given under Scope above; the structure view renders only the five shipped systems.

## Folder Layout

The source code folder structure mirrors the software structure organization, with file paths
and descriptions as follows:

```text
src/DemaConsulting.AgentKit.Core/
├── AgentKitChatReducer.cs      — consolidation for a host-owned chat history
├── AgentSession.cs             — the session contract and the per-turn response
├── AgentSessionOptions.cs      — what an application configures about one session
├── CompactingAgentSession.cs   — the implementation that sequences turns and rotations
├── CompactionLevel.cs          — the compaction aggressiveness reported on each turn
├── ContextLayout.cs            — the verbatim tail, the rings of slots and the coarsest-first seed
├── ContextUsage.cs             — the usage shape every provider session answers with
├── GuardedToolFactory.cs       — the only supported way to construct a tool
├── ImagePromotingChatClient.cs — promotes a tool-returned image onto a user message
├── InMemoryProviderSession.cs  — a provider session that contacts nothing, and its factory
├── ModelCatalog.cs             — the catalog contract, the query, the caching base and SelectAsync
├── ModelInfo.cs                — the provider-neutral model description and its capability flags
├── ModelSettings.cs            — persistable settings, their validation, and the validated selection
├── ModelSettingsChatClient.cs  — applies a selection to every IChatClient request
├── PathPolicy.cs               — the working-directory anchor, access grants, the single
│                                 containment decision, and the limits it carries
├── PathRule.cs                 — one access grant: unrestricted or rooted, read-only or
│                                 read-write
├── ProviderSession.cs          — the seed, the turn, the session and the factory contracts
├── RealPathResolver.cs         — the normalized absolute location a path denotes
├── RotationEngine.cs           — the deterministic aging function: consolidate, cascade, report
├── SessionTranscript.cs        — the append-only history grouped into whole turns
├── Summarizer.cs               — the consolidation contract and the documented default prompt
├── ToolLimits.cs               — the ceilings every governed tool observes
├── ToolName.cs                 — the family-prefix naming convention
├── ToolOutputOptions.cs        — preview size and retention ceilings for large outputs
├── ToolOutputReadTool.cs       — the tool_output_read tool
├── ToolOutputRetention.cs      — the per-conversation retention handle and its snapshot
├── ToolOutputSearchTool.cs     — the tool_output_search tool
├── ToolOutputSpill.cs          — opt-in retention of large outputs, and the preview
├── ToolOutputStore.cs          — the per-composition store of retained outputs
├── ToolPack.cs                 — the pack contract and host capabilities
├── ToolPackBuilder.cs          — capability-gated composition
└── ToolResult.cs               — text, content, structured data and denial results
```

The folder is flat because the system is flat: each unit is one file, except where an enumeration
sits beside the type it describes, directly under the project root, mirroring the software structure
above. A future system organized into subsystems will
mirror those subsystems as folders containing their respective units.

`AgentKitTools` has its own source tree under `src/DemaConsulting.AgentKit.Tools/`, organized into
one folder per tool family, each folder holding that family's units:

```text
src/DemaConsulting.AgentKit.Tools/
├── Agent/
│   ├── AgentPack.cs             — publishes the agent family as one pack, and composes a child
│   ├── AgentProfile.cs          — one named child agent the application registered
│   └── AgentRunTool.cs          — the agent_run tool, the child-composition seam, and what the
│                                  host's runner is handed for one delegated agent
├── File/
│   ├── FileCopyTool.cs          — the file_copy tool
│   ├── FileCreateDirectoryTool.cs — the file_create_directory tool
│   ├── FileDeleteDirectoryTool.cs — the file_delete_directory tool
│   ├── FileDeleteTool.cs        — the file_delete tool
│   ├── FileListTool.cs          — the file_list tool
│   ├── FileMoveDirectoryTool.cs — the file_move_directory tool
│   ├── FileMoveTool.cs          — the file_move tool
│   ├── FilePack.cs              — publishes the file family as one pack
│   ├── LinkGuard.cs             — shared helper; what is a link, and was this path reached through one
│   └── SubtreeGuard.cs          — shared helper; the walk over what an operation touches
├── Image/
│   ├── ImageAdmission.cs        — shared helper; read within the ceilings, triage, decode
│   ├── ImageAutoCropTool.cs     — the image_auto_crop tool
│   ├── ImageCropTool.cs         — the image_crop tool
│   ├── ImageDestination.cs      — shared helper; the destination taxonomy and the write
│   ├── ImageMediaTypes.cs       — extension-to-media-type mapping and the unreadable-type refusal
│   ├── ImagePack.cs             — publishes the image family as one pack
│   ├── ImageProbe.cs            — shared header reader; declared size, decode feasibility
│   └── ImageReadTool.cs         — the image_read tool
├── Markdown/
│   ├── MarkdownOutlineTool.cs   — the markdown_outline tool
│   └── MarkdownPack.cs          — publishes the Markdown family as one pack
├── Memory/
│   ├── MemoryDenials.cs         — shared refusals the identifier-taking tools compose
│   ├── MemoryEmbedding.cs       — shared descriptor-to-vector helper; adds no task prefix
│   ├── MemoryFileTool.cs        — the memory_file tool and near-duplicate detection
│   ├── MemoryForgetTool.cs      — the memory_forget tool
│   ├── MemoryOptions.cs         — the author's near-duplicate threshold and recall count
│   ├── MemoryPack.cs            — publishes the memory family as one pack
│   ├── MemoryRecallTool.cs      — the memory_recall tool
│   ├── MemoryReviseTool.cs      — the memory_revise tool, with settable provenance
│   ├── MemoryStore.cs           — the memory and match records, the persistence contract and its
│   │                              in-process default
│   └── MemoryUpdateTool.cs      — the memory_update tool
├── Todo/
│   ├── TodoListTool.cs          — the todo_list tool
│   ├── TodoPack.cs              — publishes the todo family as one pack
│   ├── TodoRemoveTool.cs        — the todo_remove tool
│   ├── TodoSetTool.cs           — the todo_set tool
│   └── TodoStore.cs             — one agent's flat task list, allocated per composition
└── TextFile/
    ├── TextFileBinaryGuard.cs   — shared leading-byte binary detection helper
    ├── TextFileCreateTool.cs    — the text_file_create tool
    ├── TextFileCutLinesTool.cs  — the text_file_cut_lines tool
    ├── TextFileCopyLinesTool.cs — the text_file_copy_lines tool
    ├── TextFileLineBuffers.cs   — the recoverable named text buffers write, cut, copy and paste share
    ├── TextFilePack.cs          — publishes the text file family as one pack
    ├── TextFilePasteLinesTool.cs — the text_file_paste_lines tool
    ├── TextFileReadTool.cs      — the text_file_read tool
    ├── TextFileReplaceTool.cs   — the text_file_replace tool
    ├── TextFileSearchTool.cs    — the text_file_search tool
    ├── TextFileWriteTool.cs     — the text_file_write tool
    └── TextLines.cs             — shared line-oriented reading and rewriting helper
```

Each family folder mirrors the subsystem it represents in the software structure above.

Each provider-adapter system is its own source tree. Both carry a session adapter over their
provider, and both differ only in what their provider makes available:

```text
src/DemaConsulting.AgentKit.Agents.ChatClient/
├── ChatClientAgentFactory.cs              — builds an agent from an IChatClient, decorator always installed
├── ChatClientProviderSession.cs           — one Core session over an IChatClient
├── ChatClientProviderSessionFactory.cs    — creates those sessions, holding the client, the window and the pipeline
├── PromptSizeRecordingChatClient.cs       — records each request's prompt size, beneath the tool-calling loop
├── ChatClientSummarizer.cs                — consolidates history through an IChatClient
└── ToolPackContextProvider.cs             — contributes AgentKit tools per conversation to a ChatClientAgent

src/DemaConsulting.AgentKit.Agents.Copilot/
├── CopilotAgent.cs                   — AgentKit's AIAgent over confined Copilot sessions
├── CopilotAgentFactory.cs            — builds a Copilot agent, and every session's options
├── CopilotAgentSession.cs            — one agent conversation: its runtime session and state bag
├── CopilotCli.cs                     — finds the installed Copilot CLI and checks its version
├── CopilotCliProcessTree.cs          — stops what a timed-out or canceled version probe started
├── CopilotCliSearchResult.cs         — what discovery found, searched and passed over
├── CopilotModelCatalog.cs            — describes the service's models for the signed-in user
├── CopilotProviderSession.cs         — one Core session over one Copilot session
├── CopilotProviderSessionFactory.cs  — creates one seeded Copilot session per rotation
├── CopilotRuntime.cs                 — the runtime AgentKit starts, confines and owns
├── CopilotRuntimeOptions.cs          — the neutral options a host may give the runtime
├── CopilotRuntimeSource.cs           — the installed CLI or the bundled runtime
├── CopilotSessionObserver.cs         — watches the runtime's event stream
├── CopilotSummarizer.cs              — consolidates on a separate tool-free session
└── CopilotTurnChannel.cs             — the seam over the sealed SDK session types
```

`CopilotAgent.cs` and `CopilotAgentSession.cs` together form the `CopilotAgent` unit;
`CopilotRuntime.cs`, `CopilotRuntimeOptions.cs` and `CopilotRuntimeSource.cs` the `CopilotRuntime`
unit; `CopilotCli.cs`, `CopilotCliSearchResult.cs` and `CopilotCliProcessTree.cs` the `CopilotCli`
unit; every other file in the Copilot tree is one unit.

`AgentKitAgentsOllama` is a three-file tree, one file per unit:

```text
src/DemaConsulting.AgentKit.Agents.Ollama/
├── OllamaContextSizingChatClient.cs  — names the chosen context length on every request
├── OllamaContextWindow.cs            — the discovered window, its source, the reading and the precedence
└── OllamaModelCatalog.cs             — describes the models the server holds
```

The demonstration samples live under `samples/`, one folder per sample. They are not software
items and appear nowhere in the structure above; the layout is recorded only so a reader knows
where the worked examples are:

```text
samples/
├── custom-tools/          — the extension path: author-written guarded tools published as packs
│                            alongside a shipped pack (docstats and clock)
├── document-assistant/    — the consumption path: shipped tool packs attached to an agent under a
│                            policy of granted locations, with every tool call printed
└── research-assistant/    — the agent-infrastructure path: todo, memory and agent families across
                             turns, over a read-only corpus and a separate writable notes location,
                             with an application-supplied offline embedding backend
```

Each sample folder is a self-contained console application; the samples share a common shape by
convention but no code, so each can be read in isolation.

The opt-in live suite for the Copilot system is a test project, and like every test project it is
outside the scope of this document. Its location is recorded only because it differs from the other
test projects: it is not part of the solution, so the ordinary build never builds it and the merge gate
only compiles it, never running it; it runs only when invoked explicitly with a model named in the
environment:

```text
test/DemaConsulting.AgentKit.Agents.Copilot.LiveTests/
├── CopilotLiveFixture.cs   — one started client in Empty mode, and a sandbox of granted and denied files
├── CopilotLiveTests.cs     — the scenarios, measured against the live Copilot runtime
├── CopilotModelCatalogLiveTests.cs — listing, tiers, effort and refusal against the live service
└── TestPng.cs              — generates the image the image-delivery scenarios read
```

Its scenarios are supplementary evidence for behaviors this design records as measured against the
live runtime; see *AgentKitAgentsCopilot System Verification Design*.

The opt-in live suite for the ChatClient system follows the same pattern against a real Ollama
server, named with its model in the environment:

```text
test/DemaConsulting.AgentKit.Agents.ChatClient.LiveTests/
├── OllamaLiveFixture.cs     — the run's settings, log and sandbox of granted and denied files
├── OllamaLiveTests.cs       — the scenarios, asserted on what OllamaSharp sends on the wire
├── OllamaModelCatalogLiveTests.cs — listing, think and num_ctx on the wire, trained-length refusal
├── TestPng.cs               — generates the image the image-promotion scenarios read
└── WireRecordingHandler.cs  — records every request and applies the run's window and thinking setting
```

Its scenarios are supplementary evidence; see *AgentKitAgentsChatClient System Verification Design*.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Section headings within each unit chapter follow a consistent structure: overview, data model,
  methods/algorithms, and interactions with other units.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.

## Companion Artifact Structure

Each software item has corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#)
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)
- SysML2 model: `docs/sysml2/model/{system}/.../{item}.sysml` (kebab-case)
- Review-sets: defined in `.reviewmark.yaml`

## References

- AgentKit User Guide — the compiled User Guide document for this repository.
- AgentKit Repository — the AgentKit source repository hosted on
  GitHub.

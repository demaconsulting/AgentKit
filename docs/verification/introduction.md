# Introduction

This document provides the verification design for AgentKit, a family of .NET libraries providing
hardened, provider-neutral agent tools.

## Purpose

The purpose of this document is to serve as the verification design entry point and document how
requirements will be tested across all software items in the AgentKit system. This
documentation enables formal review by mapping every requirement to named test scenarios, supports
compliance auditing by providing clear traceability from requirements through verification design
to tests, and ensures test completeness can be assessed without reading implementation code.

This document is intended for:

- Software developers implementing and maintaining tests
- Code reviewers validating test completeness against requirements
- Compliance auditors tracing requirements through verification design to tests
- Quality assurance teams validating test coverage and scenario adequacy

## Scope

This document covers the verification design for the AgentKit system and its
constituent software items, specifically:

- **AgentKitCore (System)** — The contract package every other AgentKit package depends upon
- **RealPathResolver (Unit)** — Reports the absolute, normalized location a path denotes, with
  relative segments collapsed
- **PathRule (Unit)** — One access rule, unrestricted or confined to a location, carrying its own
  denied patterns
- **PathPolicy (Unit)** — Anchors relative paths to one required working directory and permits
  locations through zero-or-more read-only or read-write access grants, keeping addressing and
  permission orthogonal, and makes the single containment decision used by both direct access and
  directory enumeration
- **ToolLimits (Unit)** — The ceilings every governed tool observes when reading, returning and
  attaching content
- **ToolResult (Unit)** — The results a guarded tool returns to the model, including refusals
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
- **AgentKitTools (System)** — A general-purpose capability package of guarded tool families
  built on the AgentKitCore contract, organized as one subsystem per tool family
- **TextFile (Subsystem)** — The text file tool family: policy-governed searching, reading,
  creating and editing of text files, published as one capability-gated pack
- **TextFileSearchTool (Unit)** — Publishes the `text_file_search` tool
- **TextFileReadTool (Unit)** — Publishes the `text_file_read` tool
- **TextFileCreateTool (Unit)** — Publishes the `text_file_create` tool
- **TextFileReplaceTool (Unit)** — Publishes the `text_file_replace` tool
- **TextFileCutLinesTool (Unit)** — Publishes the `text_file_cut_lines` tool
- **TextFileCopyLinesTool (Unit)** — Publishes the `text_file_copy_lines` tool
- **TextFilePasteLinesTool (Unit)** — Publishes the `text_file_paste_lines` tool
- **TextFileLineBuffers (Unit)** — Holds the recoverable named line buffers that `text_file_cut_lines`
  and `text_file_copy_lines` capture into and `text_file_paste_lines` restores from
- **TextFilePack (Unit)** — Publishes the text file family as one pack
- **File (Subsystem)** — The file tool family: policy-governed listing, copying, moving and deleting
  of files of any type, published as one capability-gated pack
- **FileListTool (Unit)** — Publishes the `file_list` tool
- **FileCopyTool (Unit)** — Publishes the `file_copy` tool
- **FileMoveTool (Unit)** — Publishes the `file_move` tool
- **FileDeleteTool (Unit)** — Publishes the `file_delete` tool
- **FilePack (Unit)** — Publishes the file family as one pack
- **Markdown (Subsystem)** — The Markdown tool family: policy-governed outlining of a Markdown file's
  heading structure, published as one capability-gated pack
- **MarkdownOutlineTool (Unit)** — Publishes the `markdown_outline` tool
- **MarkdownPack (Unit)** — Publishes the Markdown family as one pack
- **Image (Subsystem)** — The image tool family: policy-governed reading of images and PDF
  documents for a vision-capable agent, published as one capability-gated pack
- **ImageMediaTypes (Unit)** — Maps a file's extension to the media type the image family reads,
  and composes the refusal for a file whose type it cannot read
- **ImageReadTool (Unit)** — Publishes the `image_read` tool
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
- **AgentKitAgentsCopilot (System)** — Builds a Microsoft Agent Framework agent from a GitHub
  Copilot `CopilotClient`, suppressing the runtime's built-in tools by deriving the session
  allow-list from the supplied tools, and carries an AgentKit session over the same runtime
- **CopilotAgentFactory (Unit)** — The static factory that derives the allow-list, installs a
  default-safe permission handler, and builds the agent without taking ownership of the client
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
- **CopilotTurnChannel (Unit)** — The internal seam over the SDK's sealed session types, which is
  what makes everything above it testable without a live Copilot runtime
- **AgentKitAgentsOllama (System)** — Makes the context window a compacting session accounts against
  the window the Ollama instance is actually using, by asking the server for a size and by reading
  back what a running instance reports
- **OllamaContextWindow (Unit)** — The discovered window and its source, the conservative fallback,
  the reading of the server, and the pure precedence function that chooses among what it reported
- **OllamaContextSizingChatClient (Unit)** — The decorator that names the application's chosen
  context length on every request, so the instance Ollama runs is the one the session accounts
  against

The following OTS items are also covered:

- **BuildMark** — build-notes documentation tool
- **ApiMark** — public API surface tracking tool
- **FileAssert** — document assertion tool
- **Microsoft.Agents.AI** — the runtime library providing the `AIAgent`/`ChatClientAgent`
  abstraction
- **Microsoft.Agents.AI.GitHub.Copilot** — the GitHub Copilot SDK providing `CopilotClient`,
  `SessionConfig`, and the permission RPC
- **Microsoft.Extensions.AI.Abstractions** — the runtime library providing the
  `AIFunction`/`AIContent` tool currency
- **OllamaSharp** — the Ollama client library providing the loaded-model report carrying a running
  instance's context length, and the carriage of a context-length option onto the wire
- **Pandoc** — Markdown-to-HTML conversion tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model lint and diagram rendering tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework

This verification documentation covers the same software items as the design documentation. It also
opens with a product-capability chapter, covering the three provider capabilities that are
requirements of the product rather than of any one package.

Version applicability: This verification design applies to all versions of the AgentKit.

The following topics are explicitly excluded from this verification documentation:

- Build pipeline and CI/CD process testing
- Infrastructure and hosting environment testing
- The demonstration samples under `samples/`, and their test projects under
  `test/DemaConsulting.AgentKit.Samples.*.Tests/`

The samples are excluded because they are not software items: a sample is a demonstration
application contained in no software package, so it has no requirements and therefore no
verification design. This does not mean the samples are untested — each has its own test
project, and those tests build and run with every other test under `build.ps1`, which is the
gate that keeps the samples working. It means only that no sample test is traced to a
requirement and no sample appears in the traceability results reported here. See the Scope
section of the design introduction for the full reasoning; it is a deliberate classification
decision, not a coverage gap to be closed by adding sample requirements.

## Companion Artifact Structure

Each software item covered by this document has corresponding artifacts in parallel directory
trees. In-house items have artifacts in these parallel locations:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#)
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)

OTS items have parallel artifacts in:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml` (kebab-case)
- Verification: `docs/verification/ots/{ots-name}.md` (kebab-case)

Review-sets: defined in `.reviewmark.yaml`

## References

- AgentKit User Guide — the compiled User Guide document for this repository.
- AgentKit Repository — the AgentKit source repository hosted on
  GitHub.

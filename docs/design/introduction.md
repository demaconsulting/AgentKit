# Introduction

This document provides the detailed design for AgentKit, a family of .NET libraries providing
hardened, provider-neutral agent tools.

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
  allow-list from the supplied tools
- **CopilotAgentFactory (Unit)** — The static factory that derives the allow-list, installs a
  default-safe permission handler, and builds the agent without taking ownership of the client

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
- **Pandoc** — Markdown-to-HTML conversion tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model lint and diagram rendering tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework

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

`AgentKitCore` is flat: its twenty units sit directly under the system with no intervening
subsystems. That is now a decision rather than a consequence of smallness, and the system design
chapter records what those twenty units actually look like: three path-safety units, four
tool-contract units, two pack-contract units, ten session units, and `ImagePromotingChatClient`,
which stands apart from all of them.

No single boundary divides that into coherent halves. A subsystem layer would therefore not draw one
line but four or five, and each would cost a requirements file, a design document, a verification
document and a review set without removing a single unit anyone has to review. The groups are
already legible from the unit names and from the collaborations the system design chapter sets out,
which costs nothing.

This is the point at which that judgment should be revisited. If one of those groups grows enough
that a reader cannot hold it in view, the boundary around it stops being free and earns its
artifacts.

The repository contains four systems. `AgentKitCore` is the heart of the product and the one library
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
reached as a chat client. `AgentKitAgentsCopilot` does not implement it yet, so a Copilot-backed
application composes an agent rather than a compacting session.

`AgentKitTools` is a general-purpose capability package of
guarded tool families built on the AgentKitCore contract. It ships seven families today, each its
own subsystem: `TextFile`, which searches, reads, creates, replaces and moves line ranges within
text files under the policy; `File`, which lists, copies, moves and deletes files of any type;
`Markdown`, which outlines a document's headings with their line ranges;
`Image`, which reads images and PDF documents for a vision-capable agent; `Todo`, which gives an
agent one flat task list of its own; `Memory`, which gives an agent a searchable record of what it
has learned; and `Agent`, which delegates a task to another agent the
application registered. All compose through
the same guarded construction path and pack contract Core publishes. `AgentKitTools` is a peer of
the other capability packages an application may attach, depending on `AgentKitCore` but never
depended upon by another capability package.

`AgentKitAgentsChatClient` and `AgentKitAgentsCopilot` are the two provider-adapter systems. Each
turns a provider into a Microsoft Agent Framework agent carrying a supplied tool set, and each is
justified by a runtime dependency that must be kept out of Core: `AgentKitAgentsChatClient` carries
`Microsoft.Agents.AI`, and `AgentKitAgentsCopilot` carries `Microsoft.Agents.AI.GitHub.Copilot`.
`AgentKitAgentsCopilot` is one factory class. `AgentKitAgentsChatClient` holds five units: that
factory, a provider session, its factory, a summarizer, and the recorder that reads occupancy from
the last request of a turn. Both are flat, and the two share no code and never reference each other.

The `SoftwareStructureView.svg` above renders all four systems.

The demonstration samples under `samples/` are not among them. They are runnable examples rather
than deliverables, belong to no software package, and are excluded from the software-item tree for
the reasons given under Scope above; the structure view renders only the four shipped systems.

## Folder Layout

The source code folder structure mirrors the software structure organization, with file paths
and descriptions as follows:

```text
src/DemaConsulting.AgentKit.Core/
├── AgentSession.cs             — the session contract and the per-turn response
├── AgentSessionOptions.cs      — what an application configures about one session
├── CompactingAgentSession.cs   — the implementation that sequences turns and rotations
├── CompactionLevel.cs          — the compaction aggressiveness reported on each turn
├── ContextLayout.cs            — the verbatim tail, the rings of slots and the coarsest-first seed
├── ContextUsage.cs             — the usage shape every provider session answers with
├── GuardedToolFactory.cs       — the only supported way to construct a tool
├── ImagePromotingChatClient.cs — promotes a tool-returned image onto a user message
├── InMemoryProviderSession.cs  — a provider session that contacts nothing, and its factory
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
│   ├── FileDeleteTool.cs        — the file_delete tool
│   ├── FileListTool.cs          — the file_list tool
│   ├── FileMoveTool.cs          — the file_move tool
│   └── FilePack.cs              — publishes the file family as one pack
├── Image/
│   ├── ImageMediaTypes.cs       — extension-to-media-type mapping and the unreadable-type refusal
│   ├── ImagePack.cs             — publishes the image family as one pack
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
    ├── TextFileLineBuffers.cs   — the recoverable named line buffers cut, copy and paste share
    ├── TextFilePack.cs          — publishes the text file family as one pack
    ├── TextFilePasteLinesTool.cs — the text_file_paste_lines tool
    ├── TextFileReadTool.cs      — the text_file_read tool
    ├── TextFileReplaceTool.cs   — the text_file_replace tool
    ├── TextFileSearchTool.cs    — the text_file_search tool
    └── TextLines.cs             — shared line-oriented reading and rewriting helper
```

Each family folder mirrors the subsystem it represents in the software structure above.

Each provider-adapter system is its own source tree. The Copilot adapter is one factory class; the
chat-client adapter adds the session adapter that carries a Core session over an `IChatClient`:

```text
src/DemaConsulting.AgentKit.Agents.ChatClient/
├── ChatClientAgentFactory.cs              — builds an agent from an IChatClient, decorator always installed
├── ChatClientProviderSession.cs           — one Core session over an IChatClient
├── ChatClientProviderSessionFactory.cs    — creates those sessions, holding the client, the window and the pipeline
├── PromptSizeRecordingChatClient.cs       — records each request's prompt size, beneath the tool-calling loop
└── ChatClientSummarizer.cs                — consolidates history through an IChatClient

src/DemaConsulting.AgentKit.Agents.Copilot/
└── CopilotAgentFactory.cs      — builds a Copilot agent with the built-in tools suppressed
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

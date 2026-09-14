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
- **RealPathResolver (Unit)** — Reports the real file system location a path reaches, resolving
  symbolic links and directory junctions at every path component
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
- **TextLines (Unit)** — Shared helper for line-oriented reading and rewriting used across the family
- **TextFileBinaryGuard (Unit)** — Shared helper that detects binary content from a file's
  leading bytes, so a binary file is refused before it is decoded as text
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
- **MemoryRecord (Unit)** — One memory, and the memory-plus-similarity pair a search returns
- **MemoryStore (Unit)** — The substitutable persistence contract for memories and its default
  in-process implementation
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
- **ChildAgentRequest (Unit)** — The bundle the library hands the host's runner for one delegated
  agent, carrying tools composed from the child's own state
- **AgentRunTool (Unit)** — Publishes the `agent_run` tool
- **AgentPack (Unit)** — Publishes the agent family as one pack, and composes a child's tools from
  the registered packs rather than from the parent's tool list
- **AgentKitAgentsChatClient (System)** — Builds a Microsoft Agent Framework agent from any
  `IChatClient`, installing the image-promoting decorator on every agent unconditionally
- **ChatClientAgentFactory (Unit)** — The static factory that wraps the supplied client in the
  image-promoting decorator and builds a `ChatClientAgent`
- **AgentKitAgentsCopilot (System)** — Builds a Microsoft Agent Framework agent from a GitHub
  Copilot `CopilotClient`, suppressing the runtime's built-in tools by deriving the session
  allow-list from the supplied tools
- **CopilotAgentFactory (Unit)** — The static factory that derives the allow-list, installs a
  default-safe permission handler, and builds the agent without taking ownership of the client
- **AgentKitSamples (System)** — The repository's runnable demonstration applications, delivered as
  source rather than as a published package; each is a self-contained console application whose
  requirements are demonstration requirements
- **CustomTools (Unit)** — The extension-path sample: an application author writes their own guarded
  tools and publishes them as packs that compose alongside a shipped pack
- **DocumentAssistant (Unit)** — The consumption-path sample: an application attaches the shipped tool
  packs to an agent under a policy that grants a workspace to read and a separate location to write
- **ResearchAssistant (Unit)** — The agent-infrastructure-path sample: an agent plans, remembers, and
  delegates safely across turns, with an application-supplied offline embedding backend

The following OTS items are also covered:

- **BuildMark** — build-notes documentation tool
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

## Software Structure

The software structure is modeled in SysML2 under `docs/sysml2/` and rendered to the
diagram below by SysML2Tools as part of the build pipeline. AI agents should query the
SysML2 model directly (see the `sysml2tools-query` skill) rather than parsing this
diagram or the prose below.

![Software Structure](SoftwareStructureView.svg)

`AgentKitCore` is deliberately flat: its ten units sit directly under the system with no
intervening subsystems. Core is a small contract package, and a subsystem layer would add
artifacts — a requirements file, a design document, a verification document and a review set per
subsystem — without reducing the number of units anyone has to review. Subsystems will be
introduced when a system in this repository has enough units that architectural boundaries
between them carry real information.

The repository contains five systems. `AgentKitTools` is a general-purpose capability package of
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
Each is flat — one factory class — and the two share no code and never reference each other. The
`SoftwareStructureView.svg` above renders all five systems.

`AgentKitSamples` is the fifth system: the repository's runnable demonstration applications. It is
not a shipped library — none of its samples is packed, published, or given an SBOM — but each sample
is locally-developed software with its own test project, so the collection is modeled as a system
whose requirements are *demonstration* requirements. A sample requirement states what the sample must
prove to a reader ("the sample shall demonstrate X"), not a library capability, because the samples'
purpose is pedagogical: `DocumentAssistant` shows how to consume AgentKit, `ResearchAssistant` shows
how an agent works across turns, and `CustomTools` shows how to extend AgentKit with author-written
tools. The system is deliberately flat, its three units sitting directly beneath it: the samples share
no code and depend on each other in no way, so a "samples" subsystem layer would add a requirements
file, a design document, a verification document and a review set without carrying any architectural
information. The samples target `net10.0` only and are not a shipped multi-platform package, so —
unlike the four library systems — `AgentKitSamples` deliberately has no `platform-requirements.yaml`;
its absence is a design decision, not an oversight.

## Folder Layout

The source code folder structure mirrors the software structure organization, with file paths
and descriptions as follows:

```text
src/DemaConsulting.AgentKit.Core/
├── GuardedToolFactory.cs       — the only supported way to construct a tool
├── ImagePromotingChatClient.cs — promotes a tool-returned image onto a user message
├── PathPolicy.cs               — the working-directory anchor, access grants, the single
│                                 containment decision, and the limits it carries
├── PathRule.cs                 — one access grant: unrestricted or rooted, read-only or
│                                 read-write
├── RealPathResolver.cs         — the real location a path reaches
├── ToolLimits.cs               — the ceilings every governed tool observes
├── ToolName.cs                 — the family-prefix naming convention
├── ToolPack.cs                 — the pack contract and host capabilities
├── ToolPackBuilder.cs          — capability-gated composition
└── ToolResult.cs               — text, content, structured data and denial results
```

The folder is flat because the system is flat: each unit is one file directly under the project
root, mirroring the software structure above. A future system organized into subsystems will
mirror those subsystems as folders containing their respective units.

`AgentKitTools` has its own source tree under `src/DemaConsulting.AgentKit.Tools/`, organized into
one folder per tool family, each folder holding that family's units:

```text
src/DemaConsulting.AgentKit.Tools/
├── Agent/
│   ├── AgentPack.cs             — publishes the agent family as one pack, and composes a child
│   ├── AgentProfile.cs          — one named child agent the application registered
│   ├── AgentRunTool.cs          — the agent_run tool and the child-composition seam
│   └── ChildAgentRequest.cs     — what the host's runner is handed for one delegated agent
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
│   ├── MemoryRecord.cs          — one memory, and one memory as a search found it
│   ├── MemoryReviseTool.cs      — the memory_revise tool, with settable provenance
│   ├── MemoryStore.cs           — the persistence contract and its in-process default
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

Each provider-adapter system is one factory class in its own source tree:

```text
src/DemaConsulting.AgentKit.Agents.ChatClient/
└── ChatClientAgentFactory.cs   — builds an agent from an IChatClient, decorator always installed

src/DemaConsulting.AgentKit.Agents.Copilot/
└── CopilotAgentFactory.cs      — builds a Copilot agent with the built-in tools suppressed
```

The demonstration samples live under `samples/`, one folder per sample. Unlike a library unit — one
class in one file — each sample unit is a whole console application, so its folder holds several
source files that together form the worked example:

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

Each sample folder is a self-contained application; the samples share a common shape by convention
but no code, so each can be read in isolation.

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

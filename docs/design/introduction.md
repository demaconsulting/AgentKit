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
- **PathRule (Unit)** — One access rule, unrestricted or confined to a location, carrying its own
  denied patterns
- **PathPolicy (Unit)** — Pairs an independent read rule and write rule, carries the workspace
  location relative paths are interpreted against, and makes the single containment decision used
  by both direct access and directory enumeration
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
- **TextFile (Subsystem)** — The text file tool family: policy-governed reading, writing and
  listing of text files, published as one capability-gated pack
- **TextFileReadTool (Unit)** — Publishes the `text_file_read` tool
- **TextFileWriteTool (Unit)** — Publishes the `text_file_write` tool
- **TextFileListTool (Unit)** — Publishes the `text_file_list` tool
- **TextFilePack (Unit)** — Publishes the text file family as one pack
- **Image (Subsystem)** — The image tool family: policy-governed reading of images and PDF
  documents for a vision-capable agent, published as one capability-gated pack
- **ImageMediaTypes (Unit)** — Maps a file's extension to the media type the image family reads,
  and composes the refusal for a file whose type it cannot read
- **ImageReadTool (Unit)** — Publishes the `image_read` tool
- **ImagePack (Unit)** — Publishes the image family as one pack
- **AgentKitAgentsChatClient (System)** — Builds a Microsoft Agent Framework agent from any
  `IChatClient`, installing the image-promoting decorator on every agent unconditionally
- **ChatClientAgentFactory (Unit)** — The static factory that wraps the supplied client in the
  image-promoting decorator and builds a `ChatClientAgent`
- **AgentKitAgentsCopilot (System)** — Builds a Microsoft Agent Framework agent from a GitHub
  Copilot `CopilotClient`, suppressing the runtime's built-in tools by deriving the session
  allow-list from the supplied tools
- **CopilotAgentFactory (Unit)** — The static factory that derives the allow-list, installs a
  default-safe permission handler, and builds the agent without taking ownership of the client

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

The repository contains four systems. `AgentKitTools` is a general-purpose capability package of
guarded tool families built on the AgentKitCore contract. It ships two families today, each its
own subsystem: `TextFile`, which reads, writes and lists text files within the policy, and
`Image`, which reads images and PDF documents for a vision-capable agent. Both compose through
the same guarded construction path and pack contract Core publishes. `AgentKitTools` is a peer of
the other capability packages an application may attach, depending on `AgentKitCore` but never
depended upon by another capability package.

`AgentKitAgentsChatClient` and `AgentKitAgentsCopilot` are the two provider-adapter systems. Each
turns a provider into a Microsoft Agent Framework agent carrying a supplied tool set, and each is
justified by a runtime dependency that must be kept out of Core: `AgentKitAgentsChatClient` carries
`Microsoft.Agents.AI`, and `AgentKitAgentsCopilot` carries `Microsoft.Agents.AI.GitHub.Copilot`.
Each is flat — one factory class — and the two share no code and never reference each other. The
`SoftwareStructureView.svg` above renders all four systems.

## Folder Layout

The source code folder structure mirrors the software structure organization, with file paths
and descriptions as follows:

```text
src/DemaConsulting.AgentKit.Core/
├── GuardedToolFactory.cs       — the only supported way to construct a tool
├── ImagePromotingChatClient.cs — promotes a tool-returned image onto a user message
├── PathPolicy.cs               — the workspace base, the single containment decision, and the
│                                 limits it carries
├── PathRule.cs                 — one access rule: unrestricted or rooted
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
├── Image/
│   ├── ImageMediaTypes.cs       — extension-to-media-type mapping and the unreadable-type refusal
│   ├── ImagePack.cs             — publishes the image family as one pack
│   └── ImageReadTool.cs         — the image_read tool
└── TextFile/
    ├── TextFileListTool.cs      — the text_file_list tool
    ├── TextFilePack.cs          — publishes the text file family as one pack
    ├── TextFileReadTool.cs      — the text_file_read tool
    └── TextFileWriteTool.cs     — the text_file_write tool
```

Each family folder mirrors the subsystem it represents in the software structure above.

Each provider-adapter system is one factory class in its own source tree:

```text
src/DemaConsulting.AgentKit.Agents.ChatClient/
└── ChatClientAgentFactory.cs   — builds an agent from an IChatClient, decorator always installed

src/DemaConsulting.AgentKit.Agents.Copilot/
└── CopilotAgentFactory.cs      — builds a Copilot agent with the built-in tools suppressed
```

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

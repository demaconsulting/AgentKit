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

This verification documentation covers the same software items as the design documentation.

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

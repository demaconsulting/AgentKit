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
- **PathPolicy (Unit)** — Pairs an independent read rule and write rule, and makes the single
  containment decision used by both direct access and directory enumeration
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
  built on the AgentKitCore contract; its tool families are introduced in subsequent increments

The following OTS items are also covered:

- **BuildMark** — build-notes documentation tool
- **FileAssert** — document assertion tool
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

`AgentKitCore` is deliberately flat: its nine units sit directly under the system with no
intervening subsystems. Core is a small contract package, and a subsystem layer would add
artifacts — a requirements file, a design document, a verification document and a review set per
subsystem — without reducing the number of units anyone has to review. Subsystems will be
introduced when a system in this repository has enough units that architectural boundaries
between them carry real information.

The repository now contains two systems. `AgentKitTools` is a general-purpose capability package
of guarded tool families built on the AgentKitCore contract, scaffolded ahead of its first family:
it builds, tests, traces and reviews as an empty shell today, and its tool families — each its own
subsystem — are introduced in subsequent increments. `AgentKitTools` is a peer of the other
capability packages an application may attach, depending on `AgentKitCore` but never depended upon
by another capability package. The `SoftwareStructureView.svg` above renders both systems.

## Folder Layout

The source code folder structure mirrors the software structure organization, with file paths
and descriptions as follows:

```text
src/DemaConsulting.AgentKit.Core/
├── GuardedToolFactory.cs       — the only supported way to construct a tool
├── PathPolicy.cs               — the single containment decision, and the limits it carries
├── PathRule.cs                 — one access rule: unrestricted or rooted
├── RealPathResolver.cs         — the real location a path reaches
├── ToolLimits.cs               — the ceilings every governed tool observes
├── ToolName.cs                 — the family-prefix naming convention
├── ToolPack.cs                 — the pack contract and host capabilities
├── ToolPackBuilder.cs          — capability-gated composition
└── ToolResult.cs               — text, content and denial results
```

The folder is flat because the system is flat: each unit is one file directly under the project
root, mirroring the software structure above. A future system organized into subsystems will
mirror those subsystems as folders containing their respective units.

`AgentKitTools` has its own source tree under `src/DemaConsulting.AgentKit.Tools/`, which contains
no `.cs` files yet — the package is an empty shell scaffolded ahead of its first tool family, so no
folder tree is shown here. Its source tree appears with its first family, when that family's units
are added.

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

# OTS Integration Design

This document describes the overall Off-The-Shelf (OTS) integration strategy for the AgentKit repository.

## Overview

AgentKit Core carries exactly one runtime NuGet dependency,
`Microsoft.Extensions.AI.Abstractions`. The two provider-adapter packages each carry one runtime
dependency of their own, deliberately kept out of Core: `AgentKitAgentsChatClient` carries
`Microsoft.Agents.AI` (the Microsoft Agent Framework runtime), and `AgentKitAgentsCopilot` carries
`Microsoft.Agents.AI.GitHub.Copilot` (the GitHub Copilot SDK, which additionally brings a
RID-specific native runtime through its own SDK dependency). Each is a third-party published library
providing functionality not developed within the program, so each is an OTS item and appears in the
table below; its integration is detailed in its dedicated design document, and the relevant system
design records the dependency. Every other OTS item listed below is a build-time or
quality-pipeline tool rather than a runtime library dependency: each provides one stage of the
documentation, requirements-traceability, testing, and quality-reporting pipeline invoked by
`build.ps1`, `lint.ps1`, and the `.github/workflows/build.yaml` CI workflow, and none of those
tools are linked into, or shipped with, the compiled NuGet package. ApiMark is the one of those
tools that runs as part of the package build itself rather than as a separate pipeline step; it is
still build-time only — referenced with `PrivateAssets="All"` so it never becomes a transitive
dependency, and never linked into or shipped as runtime code. Only its Markdown *output* is placed
inside the package.

## OTS Items

| OTS Item                             | Purpose                                                              |
|--------------------------------------|----------------------------------------------------------------------|
| ApiMark                              | Generates Markdown API reference documentation from XML doc comments |
| BuildMark                            | Generates build-notes documentation from GitHub Actions metadata     |
| FileAssert                           | Validates generated documents (HTML/PDF) against acceptance criteria |
| Microsoft.Agents.AI                  | Runtime library defining `AIAgent` and `ChatClientAgent`             |
| Microsoft.Agents.AI.GitHub.Copilot   | GitHub Copilot SDK: `CopilotClient`, `SessionConfig`, permission RPC |
| Microsoft.Extensions.AI.Abstractions | Runtime library defining the `AIFunction`/`AIContent` tool currency  |
| Pandoc                               | Converts Markdown documentation to HTML                              |
| ReqStream                            | Enforces requirements-to-test traceability                           |
| ReviewMark                           | Enforces file review coverage and currency                           |
| SarifMark                            | Converts CodeQL SARIF results into a markdown report                 |
| SonarMark                            | Generates a SonarCloud quality report                                |
| SysML2Tools                          | Validates the SysML2 architecture model and renders its views to SVG |
| VersionMark                          | Captures and publishes tool-version information                      |
| WeasyPrint                           | Converts HTML documentation to PDF                                   |
| xUnit                                | Discovers and executes unit and integration tests                    |

Each item's individual design document (`docs/design/ots/{ots-name}.md`) records its Purpose,
Features Used, and Integration Pattern. Each item's requirements and verification evidence are
recorded in `docs/reqstream/ots/{ots-name}.yaml` and `docs/verification/ots/{ots-name}.md`
respectively.

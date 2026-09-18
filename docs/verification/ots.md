# OTS Verification Evidence

This document describes the overall Off-The-Shelf (OTS) verification strategy for the AgentKit repository.

## Overview

The OTS items this repository depends on fall into two groups, each verified in the way that suits
it. `Microsoft.Extensions.AI.Abstractions`, `Microsoft.Agents.AI`,
`Microsoft.Agents.AI.GitHub.Copilot` and `OllamaSharp` are runtime libraries with no self-validation
CLI; per
`software-items.md` each is verified through AgentKit's own integration tests, which build and invoke
real agents, session configurations, permission handlers and provider reports and observe that the
required
functionality behaves as required. Every other OTS item is a build-and-verify pipeline tool,
verified through a combination of self-validation CLI flags (where the tool provides a `--validate`
or equivalent self-test mode) and pipeline-evidence-based verification (where a passing CI pipeline
run, having produced and validated the expected output at each stage, constitutes proof that the
tool executed correctly). Each item's individual verification document
(`docs/verification/ots/{ots-name}.md`) records the detailed approach and named test scenarios.

## OTS Items

| OTS Item                             | Verification Approach                                                       |
|--------------------------------------|-----------------------------------------------------------------------------|
| ApiMark                              | Pipeline evidence: FileAssert assertions on the generated API reference     |
| BuildMark                            | Self-validation CLI suite plus pipeline evidence via build-notes document   |
| FileAssert                           | Self-validation CLI suite plus transitive evidence from document assertions |
| Microsoft.Agents.AI                  | AgentKit integration tests building an agent from an IChatClient            |
| Microsoft.Agents.AI.GitHub.Copilot   | AgentKit integration tests building the session config and handler          |
| Microsoft.Extensions.AI.Abstractions | AgentKit integration tests building and invoking guarded tools              |
| OllamaSharp                          | AgentKit tests: real client on loopback; reports read, num_ctx on the wire  |
| Pandoc                               | Pipeline evidence: FileAssert assertions on each generated HTML document    |
| ReqStream                            | Self-validation CLI suite plus pipeline evidence via --enforce traceability |
| ReviewMark                           | Self-validation CLI suite plus pipeline evidence via review plan/report     |
| SarifMark                            | Self-validation CLI suite plus pipeline evidence via SARIF markdown report  |
| SonarMark                            | Self-validation CLI suite plus pipeline evidence via SonarCloud report      |
| SysML2Tools                          | Self-validation CLI suite plus pipeline evidence via lint and rendered SVGs |
| VersionMark                          | Self-validation CLI suite plus pipeline evidence via version data           |
| WeasyPrint                           | Pipeline evidence: FileAssert assertions on each generated PDF document     |
| xUnit                                | Self-validation via discovery, execution, and TRX reporting of tests        |

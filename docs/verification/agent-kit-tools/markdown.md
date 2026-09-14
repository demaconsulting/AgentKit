## Markdown Subsystem Verification Design

This document describes the subsystem-level verification strategy for the Markdown tool family.

### Verification Approach

The subsystem is verified through integration tests that exercise the family the way an application
does: composed through the AgentKitCore `ToolPackBuilder` under one access policy, then invoked
through the published tool list by name and argument dictionary, exactly as an agent runtime invokes
it. Nothing is mocked. The access policy and file system are real, because the properties under
verification belong to the real host boundary.

The scenarios here assert what belongs to the family as a whole: the outline tool and
`MarkdownPack`. They verify that Markdown section structure is read only from permitted files. The
algorithm of any single tool is verified in that unit's own document.

The boundary the subsystem is exercised at is deliberately the composed tool list rather than the
units' internal factories, because that list is what an application actually attaches. A tool that
could not be reached that way would not be reachable by an agent either.

Subsystem tests reside in `Markdown/MarkdownTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: Each scenario creates and deletes its own temporary directory tree when files are
  needed
- **Isolation**: Each test constructs its own policy, composition and temporary state; no state is
  shared

### Acceptance Criteria

A subsystem test run passes when all 4 requirement scenarios below, covering 4 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing tool, a wrong
family prefix, an ignored policy decision, a containment escape, a thrown refusal, incorrect relative-path
behavior, unsafe mutation, or a ceiling violation returned as truncated content constitutes a
failure.

### Test Scenarios

#### AgentKitTools-Markdown-FamilyComposition: Family Composition

**Test**: `Markdown_Family_ComposedThroughBuilder_PublishesTheOutlineTool`

The listed tests prove a composition attaching the family publishes the single outline tool.

#### AgentKitTools-Markdown-GuardedConstruction: Guarded Construction

**Test**: `Markdown_Family_EveryTool_CarriesAValidatedNameAndDescription`

The listed tests prove every tool in the family carries a valid name and a description.

#### AgentKitTools-Markdown-PolicyGoverned: Policy Governed

**Test**: `Markdown_Family_PathOutsideGrants_IsRefused`

Security control at the composed boundary: the outline of a Markdown file placed in a sibling
directory no grant permits is requested through the published tool list and refused as
`PathNotPermitted`, with no heading from that file appearing in the result. The bait lies outside the
grants and is named by absolute path, so the request would succeed were the read decision not
consulted — the scenario fails if containment is removed.

#### AgentKitTools-Markdown-Outline: Outline

**Test**: `Markdown_Family_OutlineOfPermittedFile_ReturnsStructuredResult`

The listed tests prove the family's structured result reaches the caller as a JSON element the guard
serialized, carrying the section count the document's headings imply.

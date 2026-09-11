# System Verification Design

This document describes the system-level verification strategy for the AgentKit Tools.

## Verification Approach

The AgentKit Tools system is verified through system-level integration tests that exercise the
package as a whole from the perspective of a consumer. Tests compose the package through its
public integration surface — the AgentKitCore pack contract — and assert on observable outputs,
without relying on knowledge of internal implementation details. No mocking or stubbing is
required at the system level.

The system under verification at this stage is the package's integration surface: the promise that
its tool families are composed through the AgentKitCore `ToolPackBuilder` under one access policy,
and that a composition to which no family has been attached is well defined and contributes no
tools. The tool families themselves are introduced in subsequent increments, each with its own
verification; this document covers the composition baseline the families will build on.

System tests reside in `AgentKitToolsTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: No file system access is required; the composition baseline is exercised
  entirely in memory
- **Isolation**: Each test method constructs its own policy and builder; no state is shared
  between tests

## External Interface Simulation

No external interface is exercised at this stage. The composition baseline runs entirely against
the in-memory AgentKitCore pack contract, so there is nothing to simulate. The file-system
interfaces the future tool families will exercise are verified with those families when they are
introduced.

## System-Level Test Scenarios

### Composition: An Empty Builder Contributes No Tools

**Test**: `AgentKitTools_SystemComposition_EmptyBuilder_ContributesNoTools`

Verifies that the package composes through the AgentKitCore pack contract and that a composition to
which no family has been attached contributes no tools. Constructs a real access policy from two
unrestricted rules, builds a `ToolPackBuilder` governed by that policy without adding any family,
and asserts the resulting tool list is empty. Confirms the integration surface is well defined
before any family is added and that the package is a peer composed like any other AgentKit pack.

## Acceptance Criteria

A system-level test run passes when the scenario above passes without error or exception beyond
those explicitly asserted. Any unexpected exception, a non-empty tool list from an empty
composition, or a failure to compose through the AgentKitCore pack contract constitutes a failure.

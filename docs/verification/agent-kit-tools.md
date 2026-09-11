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
that a composition to which no family has been attached is well defined and contributes no tools,
and that attaching a family contributes exactly that family's tools — or, where the family declares
a host capability, contributes them only when the host provides it. The first such family, the
TextFile family, has its own subsystem and unit verification; see _TextFile Subsystem Verification
Design_. The Image family, gated on the Vision capability, likewise has its own; see _Image
Subsystem Verification Design_. The families introduced in subsequent increments are verified the
same way.

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

No external interface is simulated at the system level. The composition scenarios run against the
in-memory AgentKitCore pack contract, so there is nothing to substitute. The file-system interface
the TextFile family exercises is not simulated either: its subsystem and unit tests use a real
temporary directory tree containing a real reparse point, because path containment is a security
control and a simulated link would prove only that the simulation was written consistently.

## System-Level Test Scenarios

### Composition: An Empty Builder Contributes No Tools

**Test**: `AgentKitTools_SystemComposition_EmptyBuilder_ContributesNoTools`

Verifies that the package composes through the AgentKitCore pack contract and that a composition to
which no family has been attached contributes no tools. Constructs a real access policy from two
unrestricted rules, builds a `ToolPackBuilder` governed by that policy without adding any family,
and asserts the resulting tool list is empty. Confirms the integration surface is well defined
before any family is added and that the package is a peer composed like any other AgentKit pack.

### Composition: The TextFile Family Is Contributed to a Composition

**Test**: `AgentKitTools_SystemComposition_TextFilePack_ContributesTheTextFileFamily`

Verifies that attaching the TextFile pack contributes that family's tools to a composition, under
the one family prefix the pack claims. Constructs a real access policy, adds `TextFilePack` to a
`ToolPackBuilder` governed by it, and asserts the composed list is exactly `text_file_read`,
`text_file_write` and `text_file_list`. Confirms at the system level that a family is attached as
one pack rather than tool by tool, and that the package now contributes a capability rather than
only a composition baseline.

### Composition: The Image Family Is Contributed to a Vision Host

**Test**: `AgentKitTools_SystemComposition_ImagePack_ContributesTheImageFamily`

Verifies that attaching the Image pack to a host that declares the Vision capability contributes
that family's tools, under the one family prefix the pack claims. Constructs a real access policy,
declares Vision on a `ToolPackBuilder`, adds `ImagePack`, and asserts the composed list is exactly
`image_read`. Confirms at the system level that a capability-gated family, on a host that meets its
requirement, is attached as one pack.

### Composition: The Image Family Is Withheld From a Non-Vision Host

**Test**: `AgentKitTools_SystemComposition_ImagePackWithoutVision_ContributesNoTools`

Verifies that attaching the Image pack to a host that declares no capability contributes no tools.
Constructs a real access policy, adds `ImagePack` to a `ToolPackBuilder` that declares nothing, and
asserts the composed list is empty. Confirms at the system level that a family whose required
capability the host has not declared is withheld, so a model that cannot see is never offered a tool
that returns content it could only fabricate around.

## Acceptance Criteria

A system-level test run passes when the scenarios above pass without error or exception beyond
those explicitly asserted. Any unexpected exception, a non-empty tool list from an empty
composition, a family that does not contribute its tools when attached, or a failure to compose
through the AgentKitCore pack contract constitutes a failure.

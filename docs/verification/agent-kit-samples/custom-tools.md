## CustomTools Unit Verification Design

This document describes the unit-level verification strategy for the `custom-tools` sample.

### Verification Approach

The custom-tools sample is verified by exercising its real composition and author-written tool bodies
directly — no live provider is involved. Each `AIFunction` is invoked the way a runtime would,
proving the author-written tools behave exactly as a shipped tool does: a structured success, and a
returned refusal rather than a thrown exception for a request the policy denies. The composition is
exercised through `AgentComposition.BuildTools` so the assertions are about what the sample would
actually do, not about an isolated tool constructed by the test.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK, target `net10.0`
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no network access is used
- **File system**: A temporary workspace holding a known text fixture (five words across two lines,
  twenty-four characters including newlines) is created per fixture and deleted afterwards
- **Isolation**: Each test composes its own tool set over the fixture workspace; no state is shared

### Acceptance Criteria

A unit test run passes when all scenarios below pass without error or exception beyond those
explicitly asserted (IEC 62304 §5.5.2). Any custom pack that fails to compose, any structured result
with the wrong counts, any denied path that throws instead of returning a refusal, or any instruction
that fails to name the workspace or the custom tools constitutes a failure.

### Test Scenarios

#### AgentKitSamples-CustomTools-ComposesCustomPacksAlongsideShippedPack: Custom Packs Compose With a Shipped Pack

**Test**: `AgentComposition_BuildTools_ComposesCustomPacksWithShippedPack`

Composes the whole tool set and asserts both author-written tools and the shipped text-file tools are
present and every published name is a valid, family-prefixed tool name.

#### AgentKitSamples-CustomTools-PathTakingToolReturnsStructuredCounts: A Path Tool Returns Structured Counts

**Test**: `DocStatsWordCount_KnownFile_ReturnsStructuredCounts`

Counts the known fixture addressed by its bare relative name and asserts the reported path and the
deterministic character, word, and line counts.

#### AgentKitSamples-CustomTools-PathTakingToolRefusesEscape: A Path Outside the Workspace Is Refused

**Test**: `DocStatsWordCount_PathOutsideWorkspace_ReturnsPathNotPermittedDenial`

Requests a file above the workspace and asserts a returned refusal naming the containment reason
rather than a thrown exception or a read.

#### AgentKitSamples-CustomTools-PathTakingToolAnswersDiscovery: A No-Argument Request Is Discovery

**Test**: `DocStatsWordCount_NoArgument_ReturnsDiscoveryStructure`

Invokes the tool with no arguments and asserts a structured discovery result naming the inspectable
workspace and the relative-addressing dialect.

#### AgentKitSamples-CustomTools-NoPathToolReturnsStructuredResult: A No-Path Tool Returns a Structured Result

**Test**: `ClockNow_ReturnsStructuredLocalAndUtcTime`

Invokes the clock tool with no arguments and asserts a structured result naming both the local and
UTC times and the local time zone, without consulting any policy.

#### AgentKitSamples-CustomTools-InstructionsNameWorkspaceAndCustomTools: Instructions Name the Workspace and Custom Tools

**Test**: `AgentComposition_BuildInstructions_NamesWorkspaceAndCustomTools`

Builds the instructions for a run over the fixture workspace and asserts they name the workspace and
both author-written tools.

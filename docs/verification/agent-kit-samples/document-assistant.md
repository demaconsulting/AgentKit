## DocumentAssistant Unit Verification Design

This document describes the unit-level verification strategy for the `document-assistant` sample.

### Verification Approach

The document-assistant sample is verified by exercising the system instructions it builds for a run.
The instructions are built per run rather than held as a constant precisely so they can name the two
locations the run granted, and the tests pin that: both absolute paths appear, the workspace access
level follows the grant, and the statements the sample's demonstrations depend on survive the change
from a constant to a builder. No live provider is involved; the assertions are string properties of
the built instructions.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK, target `net10.0`
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no network access is used
- **File system**: None; the tests use fixture path strings unlike any real path, so a match cannot
  be accidental
- **Isolation**: Each test builds its own instructions; no state is shared

### Acceptance Criteria

A unit test run passes when all scenarios below pass without error or exception beyond those
explicitly asserted (IEC 62304 §5.5.2). Any instruction that fails to name a granted location, states
an access level that does not follow the grant, or drops the shell/web, discovery, refusal, or
path-dialect guidance constitutes a failure.

### Test Scenarios

#### AgentKitSamples-DocumentAssistant-InstructionsNameBothGrantedLocations: Both Locations Named With Access

**Test**: `AgentComposition_BuildInstructions_WritableWorkspace_NamesBothLocationsWithAccess`

Builds the instructions for a read-write run and asserts both absolute paths appear with the
workspace stated as read-write.

#### AgentKitSamples-DocumentAssistant-InstructionsReflectReadOnlyGrant: A Read-Only Grant Is Stated

**Test**: `AgentComposition_BuildInstructions_ReadOnlyWorkspace_StatesTheWorkspaceIsReadOnly`

Builds the instructions for a read-only run and asserts the workspace is described as read-only while
the session location stays read-write.

#### AgentKitSamples-DocumentAssistant-InstructionsAreBuiltPerRun: Instructions Follow the Locations

**Test**: `AgentComposition_BuildInstructions_DifferentLocations_ProduceDifferentInstructions`

Builds the instructions for two different pairs of locations and asserts the results differ and name
their own locations.

#### AgentKitSamples-DocumentAssistant-InstructionsStateNoShellOrWeb: No Shell or Web Capability

**Test**: `AgentComposition_BuildInstructions_AnyRun_DeniesShellAndWebCapabilities`

Builds the instructions and asserts the sentence denying shell, terminal, code-execution, and web
tools is present.

#### AgentKitSamples-DocumentAssistant-InstructionsKeepDiscoveryAndRefusalGuidance: Discovery and Refusal Guidance

**Test**: `AgentComposition_BuildInstructions_AnyRun_KeepsDiscoveryAndRefusalGuidance`

Builds the instructions and asserts the no-argument discovery request and the read-a-refusal guidance
are both present.

#### AgentKitSamples-DocumentAssistant-InstructionsKeepPathDialectGuidance: Path-Dialect Guidance

**Test**: `AgentComposition_BuildInstructions_AnyRun_KeepsTheRelativeAndAbsoluteDialectGuidance`

Builds the instructions and asserts the relative-name, absolute-path, and workspace-anchoring
guidance are all present.

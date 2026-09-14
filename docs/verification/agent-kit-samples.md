# AgentKitSamples System Verification Design

This document describes the system-level verification strategy for the AgentKitSamples system.

## Verification Approach

The AgentKitSamples system is verified through the demonstration tests each sample carries. Because
the samples exist to prove things to a reader, their tests assert those demonstrations directly:
they exercise a sample's real composition, tool bodies, command-line surface, and offline embedding
backend the way a runtime or a reader would, and assert the observable outcome. No test contacts a
live model provider — the demonstrations are of composition and behavior, not of a provider's wire
format — so every scenario runs deterministically and offline.

Each sample is one unit, verified through its own test project. There is no separate system-level
integration test project: the samples share no code, so there is nothing above the three units to
integrate. A system requirement's evidence is therefore a representative demonstration test drawn
from the sample it decomposes into, and the full set of demonstrations lives in the three unit
verification designs.

Test projects reside in `test/DemaConsulting.AgentKit.Samples.CustomTools.Tests`,
`test/DemaConsulting.AgentKit.Samples.DocumentAssistant.Tests`, and
`test/DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests`.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Target framework**: `net10.0` — the samples target `net10.0` only and are not multi-targeted
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No model provider is contacted and no network access is used. The
  research-assistant sample's embedding backend is the offline lexical generator; its memory store
  is in-process
- **File system**: The custom-tools sample's tests create a temporary workspace with a known text
  fixture and delete it afterwards; the other samples' tests use fixture path strings that touch no
  real file
- **Isolation**: Each test constructs its own composition, tools, and stores; no state is shared

## Acceptance Criteria

A system-level test run passes when every sample's demonstration tests pass without error or
exception beyond those explicitly asserted (IEC 62304 §5.7.2). Any composition that omits a required
tool or family, any refusal that throws instead of returning, any instruction that fails to name a
location or guidance the sample relies on, any command-line input accepted that should be refused, or
any embedding property that fails to hold constitutes a failure.

## System-Level Test Scenarios

The system scenarios are the union of the three samples' demonstrations, detailed in _CustomTools
Unit Verification Design_, _DocumentAssistant Unit Verification Design_, and _ResearchAssistant Unit
Verification Design_. Each system requirement is evidenced by a representative demonstration from the
sample it decomposes into:

### Consumption: Shipped Tools Attach Under a Policy of Granted Locations

**Representative test**: `AgentComposition_BuildInstructions_WritableWorkspace_NamesBothLocationsWithAccess`

The document-assistant sample builds a run's instructions naming both granted locations and their
access, evidencing that an application can attach the shipped tools under a policy a reader can see.

### Cross-Turn Work: Planning, Memory, and Delegation Compose Over the Reading Tools

**Representative test**: `AgentComposition_BuildTools_DelegationEnabled_ComposesPlanningMemoryAndDelegation`

The research-assistant sample composes the todo, memory, and delegation families over the reading
tools, evidencing that an agent can be equipped to work across turns.

### Extension: An Author-Written Pack Composes Alongside a Shipped Pack

**Representative test**: `AgentComposition_BuildTools_ComposesCustomPacksWithShippedPack`

The custom-tools sample composes two author-written packs onto the same builder as a shipped pack,
evidencing that authoring is the same contract as consuming.

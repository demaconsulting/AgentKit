### MemoryUpdateTool Unit Verification Design

This document describes the unit-level verification strategy for the `MemoryUpdateTool` class.

#### Verification Approach

Nothing is substituted for the unit's own collaborator: the store is the real
`InMemoryMemoryStore`. The unit takes no embedding generator at all, so there is none to substitute;
`StubEmbeddingGenerator` appears only in the arrange step, to produce the vector of the memory a
scenario starts from, and the assertion that an update embeds nothing is made at the subsystem level
where a composed family's generator can be observed.

The memory a scenario starts from is filed directly into the store rather than through
`memory_file`, so that an update scenario does not depend on a sibling unit's rules.

The tool is exercised through its constructed `AIFunction`.

Unit tests reside in `Memory/MemoryUpdateToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, no network access, no embedding backend
- **State**: Each scenario creates the fresh store it needs
- **Isolation**: No state is shared between scenarios

#### Acceptance Criteria

A unit test run passes when all 6 requirement scenarios below, covering 6 listed test method entries,
pass without error or exception beyond those explicitly asserted. A descriptor, provenance or vector
altered by an update, a result that does not state what the memory is still filed under, an unknown
identifier reported as success or refused with a prescription, or a malformed request thrown rather
than refused constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Memory-UpdateTool-ToolName: Tool Name

**Test**: `MemoryUpdateTool_Create_RequiresAStoreAndCarriesItsPublishedName`

The listed tests prove the tool is published as `memory_update`, that the constant and the
constructed tool's name agree, and that the description is not empty.

##### AgentKitTools-Memory-UpdateTool-GuardedConstruction: Guarded Construction

**Test**: `MemoryUpdateTool_Create_RequiresAStoreAndCarriesItsPublishedName`

The listed tests prove the store is required at construction and that the constructed tool carries
the validated name and description the guarded construction path is responsible for. That the unit's
factory takes no embedding generator is visible in the same scenario's single-argument call.

##### AgentKitTools-Memory-UpdateTool-DetailsOnly: Details Only

**Test**: `MemoryUpdateTool_Update_KnownMemory_ReplacesDetailsAlone`

The listed tests prove the details are replaced and that the descriptor, both provenance fields and
the vector are byte-for-byte what they were before the call.

##### AgentKitTools-Memory-UpdateTool-EchoesUnchangedParts: Echoes Unchanged Parts

**Test**: `MemoryUpdateTool_Update_ResultEchoesDescriptorAndProvenance`

The listed tests prove the result states the descriptor the memory is still filed under and the
source it still cites, which are the facts most likely to be wrong in the model's own head after an
update.

##### AgentKitTools-Memory-UpdateTool-UnknownIdentifier: Unknown Identifier

**Test**: `MemoryUpdateTool_Update_UnknownIdentifier_IsARefusalStatingFactsOnly`

The listed tests prove an identifier the store does not hold is refused with `TargetNotFound`, that
the refusal names the identifier and the number of memories held, and that it names no other tool in
the family.

##### AgentKitTools-Memory-UpdateTool-MissingArgumentsRefused: Missing Arguments

**Test**: `MemoryUpdateTool_Update_MissingArguments_AreReturnedRefusals`

The listed tests prove an omitted identifier and omitted details each come back as refusal text
rather than as a thrown exception, and that the memory is left holding exactly what it held.

### MemoryReviseTool Unit Verification Design

This document describes the unit-level verification strategy for the `MemoryReviseTool` class.

#### Verification Approach

One dependency is substituted: the embedding generator, for which scenarios use
`StubEmbeddingGenerator`. Its call count is what proves a revision naming an unknown identifier
spends no embedding call. The store is the real `InMemoryMemoryStore`, and the memory a scenario
starts from is filed directly into it rather than through `memory_file`, because several scenarios
deliberately revise a memory to wording close to its own — something the family's near-duplicate
rules would prevent at filing time and which this tool must nonetheless permit.

Three of the scenarios below are regression tests for a defect observed in the spike this family
comes from, in which a revision silently preserved the original source and left memories citing
documents they no longer reflected. They are written against the observed symptom — details from one
document beside a structured source field naming another — rather than against a paraphrase of it.

The tool is exercised through its constructed `AIFunction`, and its published schema is asserted as
well as its results, because the existence of the provenance parameters is itself a requirement.

Unit tests reside in `Memory/MemoryReviseToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, no network access, no embedding backend
- **State**: Each scenario creates the fresh store and generator it needs
- **Isolation**: No state is shared between scenarios

#### Acceptance Criteria

A unit test run passes when all 8 requirement scenarios below, covering 8 listed test method entries,
pass without error or exception beyond those explicitly asserted. A revision that keeps the old
vector, a memory that keeps a source the caller did not state, a result that does not report the
resulting provenance, a revision refused as a duplicate of the memory it is revising, an embedding
call spent on an unknown identifier, or a malformed request thrown rather than refused constitutes a
failure.

#### Test Scenarios

##### AgentKitTools-Memory-ReviseTool-ToolName: Tool Name

**Test**: `MemoryReviseTool_Create_RequiresCollaboratorsAndCarriesItsPublishedName`

The listed tests prove the tool is published as `memory_revise`, that the constant and the
constructed tool's name agree, and that the description is not empty.

##### AgentKitTools-Memory-ReviseTool-GuardedConstruction: Guarded Construction

**Test**: `MemoryReviseTool_Create_RequiresCollaboratorsAndCarriesItsPublishedName`

The listed tests prove both collaborators are required at construction and that the constructed tool
carries the validated name and description the guarded construction path is responsible for.

##### AgentKitTools-Memory-ReviseTool-ReEmbeds: Re-Embeds

**Test**: `MemoryReviseTool_Revise_KnownMemory_ReplacesDescriptorDetailsAndVector`

The listed tests prove the descriptor and the details are replaced, that the vector is no longer the
one the memory held, and that the identifier is unchanged so an identifier the model has already
read back stays valid.

##### AgentKitTools-Memory-ReviseTool-SettableProvenance: Settable Provenance

**Test**: `MemoryReviseTool_Revise_StatedProvenance_ReplacesTheSupersededSource`

**Test**: `MemoryReviseTool_Create_PublishesProvenanceParameters`

The listed tests prove the published schema offers both provenance parameters to the model, that a
memory revised from a second document cites that document in the store, and that the result reports
the citation the memory then carries. This is the direct regression test for the stale-provenance
defect.

##### AgentKitTools-Memory-ReviseTool-NoSilentInheritance: No Silent Inheritance

**Test**: `MemoryReviseTool_Revise_OmittedProvenance_ClearsTheSourceAndReportsIt`

The listed tests prove a revision that states no source leaves the memory citing nothing rather than
inheriting the source it held, and that the result reports the absence explicitly through the
`sourceStated` flag rather than by omitting a field a reader might not notice.

##### AgentKitTools-Memory-ReviseTool-NotDuplicateChecked: Not Duplicate Checked

**Test**: `MemoryReviseTool_Revise_DescriptorCloseToItsOwn_IsNotRefusedAsADuplicate`

The listed tests prove a revision to the memory's own descriptor with corrected details is applied
rather than refused, and that the store still holds exactly one memory — so the memory was not
refused against itself and no second copy was created.

##### AgentKitTools-Memory-ReviseTool-UnknownIdentifier: Unknown Identifier

**Test**: `MemoryReviseTool_Revise_UnknownIdentifier_IsRefusedWithoutEmbedding`

The listed tests prove an identifier the store does not hold is refused with `TargetNotFound` and
that the embedding generator was never called, which establishes that the lookup precedes the
embedding call.

##### AgentKitTools-Memory-ReviseTool-MissingArgumentsRefused: Missing Arguments

**Test**: `MemoryReviseTool_Revise_MissingArguments_AreReturnedRefusals`

The listed tests prove an omitted identifier, descriptor and details each come back as refusal text
rather than as a thrown exception, and that the memory is left holding exactly what it held,
including its original source.

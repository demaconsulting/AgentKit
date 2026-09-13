### MemoryFileTool Unit Verification Design

This document describes the unit-level verification strategy for the `MemoryFileTool` class.

#### Verification Approach

One dependency is substituted: the embedding generator. Scenarios use `StubEmbeddingGenerator`, the
deterministic offline bag-of-words generator described in the subsystem verification design, so that
the suite needs no service, no network and no model file, and so that two descriptors sharing most
of their words score high cosine in a way a reader can check by eye. The stub also counts the texts
it was asked to embed, which is how the descriptor-only-embedded scenario is proved.

The store is the real `InMemoryMemoryStore`. The near-duplicate decision is the interaction between
this tool and a store, and substituting the store would hide the property under test.

The tool is exercised through its constructed `AIFunction` — invoked with the argument dictionary a
runtime would supply — rather than through its private implementation, so the schema the model sees
and the result the model reads are both part of what is verified.

Unit tests reside in `Memory/MemoryFileToolTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, no network access, no embedding backend
- **State**: Each scenario creates the fresh store and generator it needs
- **Isolation**: No state is shared between scenarios

#### Acceptance Criteria

A unit test run passes when all 10 requirement scenarios below, covering 11 listed test method
entries, pass without error or exception beyond those explicitly asserted. A near-duplicate stored
anyway, a non-storage reported as prose or without naming the conflicting memory, a result naming
another tool, details reaching the embedding generator, a blank source stored as an empty string, a
malformed request thrown rather than refused, or a threshold other than the author's deciding the
outcome constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Memory-FileTool-ToolName: Tool Name

**Test**: `MemoryFileTool_Create_CarriesItsPublishedNameAndDescription`

The listed tests prove the tool is published as `memory_file`, that the constant and the constructed
tool's name agree, and that the description a model chooses it by is not empty.

##### AgentKitTools-Memory-FileTool-GuardedConstruction: Guarded Construction

**Test**: `MemoryFileTool_Create_MissingCollaborator_ThrowsArgumentNullException`

**Test**: `MemoryFileTool_Create_CarriesItsPublishedNameAndDescription`

The listed tests prove each of the three collaborators is required at construction, so a composition
that omitted one is told at the point it forgot, and that the constructed tool carries the validated
name and description the guarded construction path is responsible for.

##### AgentKitTools-Memory-FileTool-StoresNewMemory: Stores A New Memory

**Test**: `MemoryFileTool_File_NewDescriptor_StoresTheMemory`

The listed tests prove a memory whose descriptor is not a near-duplicate is stored, that the result
reports `stored: true`, an identifier beginning with the family's prefix and the size of the store,
and that the store holds it.

##### AgentKitTools-Memory-FileTool-DescriptorOnlyEmbedded: Descriptor Only

**Test**: `MemoryFileTool_File_OnlyTheDescriptorIsEmbedded`

The listed tests prove exactly one text is embedded per filed memory, and that the vector the store
holds is the descriptor's — a search for the descriptor's own vector scores 1.0 against the stored
memory, which it could not if the details had contributed.

##### AgentKitTools-Memory-FileTool-NearDuplicateNotStored: Near-Duplicate Not Stored

**Test**: `MemoryFileTool_File_NearDuplicateDescriptor_IsNotStoredAndNamesTheConflict`

The listed tests prove a contradicting restatement of a fact already held leaves the store holding
exactly one memory, so the comparison happened before anything was written.

##### AgentKitTools-Memory-FileTool-StructuredNonStorage: Structured Non-Storage

**Test**: `MemoryFileTool_File_NearDuplicateDescriptor_IsNotStoredAndNamesTheConflict`

**Test**: `MemoryFileTool_File_NearDuplicateResult_NamesNoOtherTool`

The listed tests prove the non-storage result carries `stored: false`, `reason: near_duplicate`, the
conflicting memory's identifier, descriptor and details, a similarity at or above the threshold and
the threshold itself, and that it names none of the family's other tools. This is the regression
test for the defect in which a prose refusal was followed by the model asserting it had stored the
fact.

##### AgentKitTools-Memory-FileTool-ThresholdGoverns: Threshold Governs

**Test**: `MemoryFileTool_File_ThresholdGovernsTheDecision`

The listed tests prove the same pair of descriptors is stored under one author's configuration and
declined under another's, which establishes that the author's number and not a constant in the tool
decides.

##### AgentKitTools-Memory-FileTool-Provenance: Provenance

**Test**: `MemoryFileTool_File_Provenance_IsHeldAsStated`

The listed tests prove a stated source document is held, and that a whitespace-only one is held as
an absence rather than as a source that exists and says nothing.

##### AgentKitTools-Memory-FileTool-MissingArgumentsRefused: Missing Arguments

**Test**: `MemoryFileTool_File_MissingArguments_AreReturnedRefusals`

The listed tests prove an omitted descriptor and omitted details each come back as refusal text
rather than as a thrown exception, and that nothing is stored in either case.

##### AgentKitTools-Memory-FileTool-GeneratorFaultPropagates: Generator Fault

**Test**: `MemoryFileTool_File_GeneratorProducesNothing_ThrowsInvalidOperationException`

The listed tests prove an embedding backend that answers without producing a vector surfaces as a
fault to the host rather than as a refusal the model would keep retrying against.

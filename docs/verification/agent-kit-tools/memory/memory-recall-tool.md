### MemoryRecallTool Unit Verification Design

This document describes the unit-level verification strategy for the `MemoryRecallTool` class.

#### Verification Approach

One dependency is substituted: the embedding generator, for which scenarios use
`StubEmbeddingGenerator`. Its call count is what proves that an empty store is answered without
spending an embedding call.

The store is the real `InMemoryMemoryStore`, and memories are filed into it directly rather than
through `memory_file`, so that a recall scenario does not depend on the near-duplicate rules of a
sibling unit — several scenarios file deliberately similar descriptors that filing through the
family would have declined.

The tool is exercised through its constructed `AIFunction`. The schema it publishes is asserted as
well as its results, because the absence of a count parameter is itself a requirement.

Unit tests reside in `Memory/MemoryRecallToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, no network access, no embedding backend
- **State**: Each scenario creates the fresh store and generator it needs
- **Isolation**: No state is shared between scenarios

#### Acceptance Criteria

A unit test run passes when all 7 requirement scenarios below, covering 7 listed test method
entries, pass without error or exception beyond those explicitly asserted. A match returned without
its details or provenance, a search that ranks a memory by its details, more matches than the author
configured, a count parameter offered to the model, an empty store reported as a refusal, or an
embedding call spent on an empty store constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Memory-RecallTool-ToolName: Tool Name

**Test**: `MemoryRecallTool_Create_CarriesItsPublishedNameAndDescription`

The listed tests prove the tool is published as `memory_recall`, that the constant and the
constructed tool's name agree, and that the description is not empty.

##### AgentKitTools-Memory-RecallTool-GuardedConstruction: Guarded Construction

**Test**: `MemoryRecallTool_Create_MissingCollaborator_ThrowsArgumentNullException`

The listed tests prove each of the three collaborators is required at construction.

##### AgentKitTools-Memory-RecallTool-WholeMatches: Whole Matches

**Test**: `MemoryRecallTool_Recall_MatchingQuery_ReturnsDescriptorDetailsAndProvenance`

The listed tests prove each match carries its identifier, descriptor, details, source document,
source locator and similarity, and that the nearest match is the one whose descriptor the query
matched.

##### AgentKitTools-Memory-RecallTool-DescriptorSearch: Descriptor Search

**Test**: `MemoryRecallTool_Recall_SearchesDescriptorsAndNotDetails`

The listed tests prove a query matching one memory's details word for word and another memory's
descriptor ranks the second one first, which establishes that details do not contribute to the
search.

##### AgentKitTools-Memory-RecallTool-RecallCountGoverns: Recall Count Governs

**Test**: `MemoryRecallTool_Recall_RecallCountGovernsHowManyComeBack`

The listed tests prove a store holding four memories returns two under an author who configured two,
and that the published schema offers the model no parameter by which to change it.

##### AgentKitTools-Memory-RecallTool-EmptyStore: Empty Store

**Test**: `MemoryRecallTool_Recall_EmptyStore_ReturnsNoMatchesWithoutEmbedding`

The listed tests prove an empty store answers with a zero match count and an empty match list rather
than a refusal, and that the embedding generator was not called to reach that answer.

##### AgentKitTools-Memory-RecallTool-MissingQueryRefused: Missing Query

**Test**: `MemoryRecallTool_Recall_MissingQuery_IsAReturnedRefusal`

The listed tests prove an omitted query comes back as refusal text rather than as a thrown
exception.

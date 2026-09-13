### MemoryForgetTool Unit Verification Design

This document describes the unit-level verification strategy for the `MemoryForgetTool` class.

#### Verification Approach

Nothing is substituted for the unit's own collaborator: the store is the real
`InMemoryMemoryStore`. `StubEmbeddingGenerator` appears only in the arrange step, to produce the
vectors of the memories a scenario starts from; the unit itself takes no embedding generator.

The memories a scenario starts from are filed directly into the store rather than through
`memory_file`, so that a removal scenario does not depend on a sibling unit's rules.

The tool is exercised through its constructed `AIFunction`, and its published schema is asserted as
well as its results, because the absence of any parameter that could name a set of memories is
itself a requirement.

Unit tests reside in `Memory/MemoryForgetToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, no network access, no embedding backend
- **State**: Each scenario creates the fresh store it needs
- **Isolation**: No state is shared between scenarios

#### Acceptance Criteria

A unit test run passes when all 6 requirement scenarios below, covering 6 listed test method entries,
pass without error or exception beyond those explicitly asserted. More than one memory removed by
one call, a miss reported as success, a parameter by which a set could be named, or a malformed
request thrown rather than refused constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Memory-ForgetTool-ToolName: Tool Name

**Test**: `MemoryForgetTool_Create_RequiresAStoreAndCarriesItsPublishedName`

The listed tests prove the tool is published as `memory_forget`, that the constant and the
constructed tool's name agree, and that the description is not empty.

##### AgentKitTools-Memory-ForgetTool-GuardedConstruction: Guarded Construction

**Test**: `MemoryForgetTool_Create_RequiresAStoreAndCarriesItsPublishedName`

The listed tests prove the store is required at construction and that the constructed tool carries
the validated name and description the guarded construction path is responsible for.

##### AgentKitTools-Memory-ForgetTool-KnownIdentifier: Known Identifier

**Test**: `MemoryForgetTool_Forget_KnownMemory_RemovesItAndReportsTheStoreSize`

The listed tests prove the named memory is gone, that the other memory is untouched, and that the
result reports `forgotten: true`, the identifier and the size of the store afterwards.

##### AgentKitTools-Memory-ForgetTool-SingleMemory: Single Memory

**Test**: `MemoryForgetTool_Create_PublishesOnlyASingleIdentifierParameter`

The listed tests prove the published schema offers one identifier and nothing — no query, no
descriptor — by which a set of memories could be named, so a bulk erasure is not expressible.

##### AgentKitTools-Memory-ForgetTool-UnknownIdentifier: Unknown Identifier

**Test**: `MemoryForgetTool_Forget_UnknownIdentifier_IsARefusalAndRemovesNothing`

The listed tests prove an identifier the store does not hold is refused with `TargetNotFound`, that
the refusal states how many memories are held, and that the store still holds both memories — so the
model is never told a fact is gone from a store that still returns it.

##### AgentKitTools-Memory-ForgetTool-MissingIdentifier: Missing Identifier

**Test**: `MemoryForgetTool_Forget_MissingIdentifier_IsAReturnedRefusal`

The listed tests prove a call naming no memory comes back as refusal text rather than as a thrown
exception, and that nothing at all is removed.

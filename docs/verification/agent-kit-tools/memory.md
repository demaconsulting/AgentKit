## Memory Subsystem Verification Design

This document describes the subsystem-level verification strategy for the memory tool family.

### Verification Approach

The family is exercised the way an application uses it: composed through the AgentKitCore
`ToolPackBuilder` under one access policy and one embedding generator, then invoked through the
published tool list rather than through the internal tool factories. Scenarios assert the
family-wide properties — one prefix, one store shared by the five tools, a fact that can be filed,
found, corrected, re-sourced and dropped, and refusals that arrive as returned values.

One dependency is substituted, deliberately and only one: the embedding generator. Tests use
`StubEmbeddingGenerator`, a deterministic offline bag-of-words generator, because the family's own
behavior does not depend on which backend an application chose, so verifying it against a real one
would prove something about the backend while making the suite depend on a running service or a
model file. The stub's vectors carry real if crude semantics — two sentences sharing most of their
words score high cosine, two sharing none score zero — so the near-duplicate scenarios read as the
sentences a model would actually file rather than as hand-picked float arrays. The stub also counts
how many texts it has been asked to embed, which is how the suite proves that an update costs no
embedding call and a revision costs exactly one.

The store is not substituted. What a recall returns and whether a near-duplicate is stored are
properties of the interaction between a tool and a store, and a substitute would hide exactly that.

Subsystem tests reside in `Memory/MemoryTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project. `Memory/StubEmbeddingGenerator.cs` is the shared fixture and is reviewed with this
subsystem.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, no network access, no model file, no embedding backend
- **State**: Each scenario composes the family afresh, so each starts from an empty store
- **Isolation**: No state is shared between scenarios

### Acceptance Criteria

A subsystem test run passes when all 8 requirement scenarios below, covering 9 listed test method
entries, pass without error or exception beyond those explicitly asserted. A family whose tools do
not all carry the family prefix, a contradicting restatement that is stored anyway, a revision that
leaves a memory citing a superseded document, an update that reaches the embedding backend, a
refusal delivered as a thrown exception, or any refusal naming another tool in the family
constitutes a failure.

### Test Scenarios

#### AgentKitTools-Memory-FamilyComposition: Family Composition

**Test**: `Memory_Family_ComposedThroughBuilder_PublishesTheFamily`

The listed tests prove a composition attaching the pack publishes exactly five tools, in the order
file, recall, update, revise, forget.

#### AgentKitTools-Memory-GuardedConstruction: Guarded Construction

**Test**: `Memory_Family_EveryTool_CarriesAValidatedNameAndDescription`

The listed tests prove every published tool carries a name that satisfies the naming convention,
begins with the family prefix, and has a non-empty description — the three properties the guarded
construction path is responsible for.

#### AgentKitTools-Memory-DescriptorPayloadModel: Descriptor and Payload

**Test**: `Memory_Family_AFact_IsFiledRecalledCorrectedAndForgotten`

The listed tests prove a fact filed with a descriptor, details and provenance is found again by its
descriptor and returned whole, with every tool in the family observing the same store.

#### AgentKitTools-Memory-NearDuplicateDetection: Near-Duplicate Detection

**Test**: `Memory_Family_ContradictingRestatement_IsNotStoredAndNamesTheConflict`

The listed tests prove a contradicting restatement of a fact already held is not stored, that the
result reports `stored: false` in a field rather than in prose, that it hands over the conflicting
memory's details, and that the store still holds exactly one memory afterwards.

#### AgentKitTools-Memory-CorrectionWithoutStaleProvenance: Correction and Provenance

**Test**: `Memory_Family_UpdateCostsNoEmbeddingCallAndRevisionDoes`

**Test**: `Memory_Family_RevisedMemory_IsFoundByItsNewWording`

The listed tests prove an update reaches the embedding backend zero times while a revision reaches
it exactly once, and that a revised memory is found at full similarity by its new wording. The
provenance half of this requirement — that a revision cites the document it now reflects — is
asserted within `Memory_Family_AFact_IsFiledRecalledCorrectedAndForgotten`, which recalls the
memory after revising it from a second document and checks the source the recall reports.

#### AgentKitTools-Memory-Forgetting: Forgetting

**Test**: `Memory_Family_AFact_IsFiledRecalledCorrectedAndForgotten`

The listed tests prove a named memory is removed, that the result states the size of the store
afterwards, and that a subsequent recall reports an empty answer rather than a refusal.

#### AgentKitTools-Memory-DenialsAreResults: Denials Are Results

**Test**: `Memory_Family_RefusedRequests_AreReturnedValues`

**Test**: `Memory_Family_DeniedResults_PrescribeNoRemedy`

The listed tests prove a missing descriptor, a missing query, an update naming an unknown memory and
a removal naming an unknown memory all arrive as refusal text rather than as thrown exceptions, and
that no refusal or non-storage result the family can produce contains the name of any tool in the
family.

#### AgentKitTools-Memory-InstructionRequired: Instruction Required

**Test**: `MemoryPack_SuggestedInstruction_NamesTheFilingAndRecallingTools`

The listed tests prove the published instruction names both tools it depends on and states the
granularity to file at. The test is cited from the unit level because the constant belongs to
`MemoryPack`; the subsystem requirement is what makes it a family-wide property rather than a
detail of one unit.

The two remaining subsystem requirements —
`AgentKitTools-Memory-AuthorGovernedConfiguration` and `AgentKitTools-Memory-PerCompositionMemories`
— are exercised at the pack boundary, in `MemoryPackTests`, because both are properties of how a
composition is configured rather than of how a composed family behaves. They are cited from their
own requirements and from the unit verification design for `MemoryPack`.

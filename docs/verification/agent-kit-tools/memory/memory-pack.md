### MemoryPack Unit Verification Design

This document describes the unit-level verification strategy for the `MemoryPack` class.

#### Verification Approach

One dependency is substituted: the embedding generator, for which scenarios use
`StubEmbeddingGenerator`. The store is the real `InMemoryMemoryStore` — either allocated by the pack
itself, which is the property under test in the isolation scenario, or supplied by the test acting
as the application, which is the property under test in the shared-store scenario.

The pack is exercised through its public surface: `FamilyPrefix` as a constant and through the
contract, `RequiredCapabilities`, `Options`, `SuggestedInstruction`, and `CreateTools` with a
permissive policy. Where a scenario needs to observe what a composition can see, it invokes the
composed tools rather than reaching into the store, because reaching into the store would prove the
store rather than the composition.

Unit tests reside in `Memory/MemoryPackTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, no network access, no embedding backend
- **State**: Each scenario constructs the packs and compositions it needs
- **Isolation**: No state is shared between scenarios

#### Acceptance Criteria

A unit test run passes when all 9 requirement scenarios below, covering 9 listed test method entries,
pass without error or exception beyond those explicitly asserted. Tools published in the wrong order,
a host capability required, a pack constructible without an embedding generator, a policy accepted as
null, two compositions sharing a default store, a supplied store not used, or a published instruction
that does not name the tools it depends on constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Memory-Pack-FamilyPrefix: Family Prefix

**Test**: `MemoryPack_FamilyPrefix_IsPublishedAsConstantAndContract`

The listed tests prove the prefix is `memory` and that the constant and the contract's property
agree.

##### AgentKitTools-Memory-Pack-NoCapabilityRequired: No Capability Required

**Test**: `MemoryPack_RequiredCapabilities_IsNone`

The listed tests prove the pack requires no host capability, so every composition that can construct
it receives the family.

##### AgentKitTools-Memory-Pack-RegistersFiveTools: Registers Five Tools

**Test**: `MemoryPack_CreateTools_RegistersTheFamilyInOrder`

The listed tests prove exactly five tools are created, in the order `memory_file`, `memory_recall`,
`memory_update`, `memory_revise`, `memory_forget`.

##### AgentKitTools-Memory-Pack-RequiresPolicy: Requires A Policy

**Test**: `MemoryPack_CreateTools_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a composition that omitted a policy is told at the point it omitted one, even
though this family never consults a policy.

##### AgentKitTools-Memory-Pack-InjectedGenerator: Injected Generator

**Test**: `MemoryPack_Constructor_NullGenerator_ThrowsArgumentNullException`

The listed tests prove the pack cannot be constructed without an embedding generator, which is what
makes a host-capability gate unnecessary. That the generator arrives through the standard
`IEmbeddingGenerator` abstraction is established by the test fixture implementing exactly that
interface and nothing else.

##### AgentKitTools-Memory-Pack-ConfigurationVisible: Configuration Visible

**Test**: `MemoryPack_Options_DefaultOrSupplied_AreVisibleToTheApplication`

The listed tests prove a pack given no options reports the published defaults and a configured one
reports the author's values, so an application need not keep a second copy that could disagree.

##### AgentKitTools-Memory-Pack-IndependentStores: Independent Stores

**Test**: `MemoryPack_CreateTools_NoStoreSupplied_ProducesIndependentMemories`

The listed tests prove that with no store supplied, a memory filed through one composition is not
visible to a recall through another composition of the same pack instance. This is the property that
keeps a delegated agent from reading or overwriting its parent's memories.

##### AgentKitTools-Memory-Pack-SharedStore: Shared Store

**Test**: `MemoryPack_CreateTools_SuppliedStore_IsSharedAcrossCompositions`

The listed tests prove that with a store supplied, a memory filed through one composition is
recalled whole through another, which is how an author expresses memories that outlive or span
agents.

##### AgentKitTools-Memory-Pack-SuggestedInstruction: Suggested Instruction

**Test**: `MemoryPack_SuggestedInstruction_NamesTheFilingAndRecallingTools`

The listed tests prove the published instruction names both the filing and the recall tool and
states the granularity to file at.

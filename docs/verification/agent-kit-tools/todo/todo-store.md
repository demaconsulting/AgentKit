### TodoStore Unit Verification Design

This document describes the unit-level verification strategy for the `TodoStore` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses a fresh in-memory store; append, in-place update,
remove, snapshot, validation and the status vocabulary are exercised directly. This keeps
verification at the same boundary the tool units use, rather than proving a substitute behaves
consistently with itself.

Because the store is internal and reachable only from the pack that allocates it, its per-agent
isolation is exercised at the pack level in `TodoPack_CreateTools_CalledTwice_ProducesIndependentLists`
and cited from the isolation requirement below.

Unit tests reside in `Todo/TodoStoreTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the fresh in-memory store it needs
- **Isolation**: Each test constructs its own store and items; no state is shared

#### Acceptance Criteria

A unit test run passes when all 7 requirement scenarios below, covering 8 listed test method
entries, pass without error or exception beyond those explicitly asserted. A silently accepted
unrecognized status, an existing identifier that reorders the plan under it, a snapshot that a
later write can mutate, an accepted null or empty identifier or title, or a store one composition
can reach from another constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Todo-Store-Append: Append

**Test**: `TodoStore_Set_NewIdentifier_AppendsTheTask`

The listed tests prove a new identifier appends a task at the end and the reported count is the
new size of the list.

##### AgentKitTools-Todo-Store-UpdateInPlace: Update In Place

**Test**: `TodoStore_Set_ExistingIdentifier_UpdatesInPlace`

The listed tests prove an identifier the list already holds updates that task's title, status and
note without moving it, so a status change never reorders the plan under it.

##### AgentKitTools-Todo-Store-Remove: Remove

**Test**: `TodoStore_Remove_KnownAndUnknownIdentifiers_ReportTheOutcome`

The listed tests prove a hit removes the task and reports the new size, and a miss leaves the list
unchanged and reports the miss so the caller can turn it into a refusal.

##### AgentKitTools-Todo-Store-Statuses: Statuses

**Test**: `TodoStatuses_IsValid_AcceptsExactlyThePublishedVocabulary`

The listed tests prove the vocabulary is exactly `pending`, `in_progress`, `done` and `blocked`,
compared ordinally, and that a variant like `In_Progress` is refused rather than silently
corrected.

##### AgentKitTools-Todo-Store-Snapshot: Snapshot

**Test**: `TodoStore_Items_ReturnsASnapshot`

The listed tests prove the returned items are a stable snapshot, so a concurrent write cannot
mutate a list a caller is still reading.

##### AgentKitTools-Todo-Store-Validation: Validation

**Test**: `TodoStore_Set_UnknownStatus_ThrowsArgumentException`

**Test**: `TodoStore_Set_EmptyIdentifierOrTitle_ThrowsArgumentException`

The listed tests prove a status the vocabulary does not hold is a programming error rather than
something a model did, and that a null or empty identifier or title is rejected before the list is
touched.

##### AgentKitTools-Todo-Store-PerAgentIsolation: Per-Agent Isolation

**Test**: `TodoPack_CreateTools_CalledTwice_ProducesIndependentLists`

The listed tests prove one composition's store is not reached from another composition's tools,
which is what keeps a delegated agent's list out of its parent's. The store is internal and no
public type accepts one, so this property is a structural one exercised at the pack boundary.

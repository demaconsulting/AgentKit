## ProviderSession Unit Verification Design

This document describes the unit-level verification strategy for `ProviderSessionSeed`,
`ProviderTurn`, `IProviderSession` and `IProviderSessionFactory`.

### Verification Approach

`ProviderSessionSeed` and `ProviderTurn` are validated immutable value types verified by direct
construction. Nothing is mocked; the entries and tool lists are built honestly.

`IProviderSession` and `IProviderSessionFactory` are interfaces and have no behavior of their own.
Their obligations are verified where an implementation exists, in _InMemoryProviderSession Unit
Verification Design_, and their use by the engine is verified in _CompactingAgentSession Unit
Verification Design_.

One scenario is worth singling out: `ProviderTurn_Construct_WithEntries_PreservesThemExactly`
asserts the supplied entry list is carried through by **reference**, not merely by equality. An
adapter that recorded a tool-using turn must have exactly those entries reach the engine's
transcript, because the transcript is what a tier boundary is snapped against; a defensive copy
would be harmless but a transformation would not, and reference identity is the strongest available
statement that nothing happened to them.

Unit tests reside in `ProviderSessionTests.cs` within the
`DemaConsulting.AgentKit.Sessions.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no network access is used**
- **Mocking**: None required
- **Isolation**: Each test constructs its own seed or turn; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any seed or turn that accepts a null collection or a null entry, any turn that
loses the entries an adapter recorded, or any turn with no entries at all constitutes a failure.

### Test Scenarios

#### AgentKitSessions-ProviderSession-AdapterInterface: A Seed Carries Its Three Parts Separately

**Tests**: `ProviderSessionSeed_Construct_CarriesInstructionsToolsAndHistorySeparately`,
`ProviderSessionSeed_Construct_CopiesTheSuppliedLists`

Constructs a seed for a rotation that preserved one consolidated record and one verbatim turn, and
asserts the instructions, the tools and the history are each available in their own right. Providers
accept instructions and tools as configuration rather than as messages, which is exactly why they
are accounted for as fixed overhead and not as conversation. The copy scenario clears the caller's
history list after construction and refuses a write through an `IList` cast on the seed's own
lists: a seed is an immutable snapshot an adapter may hold across a rotation, and either route would
let it start a session from something other than what was validated.

#### AgentKitSessions-ProviderSession-RecordsWhatATurnProduced: A Turn Records What Actually Happened

**Tests**: `ProviderTurn_Construct_NoEntries_RecordsOneAssistantMessage`,
`ProviderTurn_Construct_WithEntries_PreservesThemExactly`

A plain answer with no explicit entries is recorded as a single assistant message carrying that
answer — the correct history for a provider that called no tools, and one less thing a simple
adapter has to restate. A tool-using turn's entries are carried through unchanged, so the engine's
transcript matches what the provider holds and a tier boundary can be snapped correctly.

#### AgentKitSessions-ProviderSession-RejectsMalformedSeedOrTurn: A Malformed Seed or Turn Is Refused

**Tests**: `ProviderSessionSeed_Construct_NullTools_Throws`,
`ProviderSessionSeed_Construct_NullHistory_Throws`,
`ProviderSessionSeed_Construct_NullHistoryEntry_Throws`,
`ProviderTurn_Construct_NullResponseText_Throws`,
`ProviderTurn_Construct_NullEntry_Throws`

Five error paths. A null tool list or history is refused because an adapter discovering it
mid-construction would report the failure against the provider rather than against the composition.
A fresh session must be told what it is resuming from even when the answer is nothing, so an absent
history is distinguished from an empty one. A null entry inside either collection, and a null
response text, are refused for the same reason: each would otherwise fail at the provider or at a
later rotation rather than where it was introduced.

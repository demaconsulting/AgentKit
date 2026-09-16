## ProviderSession Unit Verification Design

This document describes the unit-level verification strategy for the provider-session seam:
`IProviderSession`, `IProviderSessionFactory`, `ProviderSessionSeed`, `ProviderTurn` and
`TranscriptEntry`.

### Verification Approach

Provider seam tests construct seeds, turns and entries directly. They verify a replacement provider
session receives instructions, tool declarations and seed history as separate copied collections;
that each provider turn records the response exactly once; and that malformed seed or turn material
is rejected before it reaches the compaction session.

The window question every provider session answers is verified through implementations of it, since
the interface member itself has no behavior of its own: the in-memory session answers for the window
it was given, and the compacting session is shown consuming that answer.

Ownership and disposal behavior is verified at the compacting-session level because that unit adopts
and releases provider sessions.

Unit tests reside in `ProviderSessionTests.cs`, with usage and ownership evidence in
`InMemoryProviderSessionTests.cs` and `CompactingAgentSessionTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider is contacted
- **Mocking**: None required for value tests
- **Isolation**: Each test constructs its own seed, turn or entry

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any seed that aliases caller-owned collections, any turn that omits or
duplicates its answer, any malformed seed accepted, or any invalid tool pairing accepted constitutes
a failure.

### Test Scenarios

#### AgentKitSessions-ProviderSession-AdapterInterface: A Seed Carries Its Three Parts Separately

**Test**: `ProviderSessionSeed_Construct_CopiesToolsAndHistory`

Asserts a seed carries instructions, tools and history, and copies tools and history so later caller
mutation cannot change the replacement session's starting context.

#### AgentKitSessions-ProviderSession-RecordsWhatATurnProduced: A Turn Records What Actually Happened

**Tests**:

- `ProviderTurn_Construct_NoEntries_RecordsAnswerAlone`
- `ProviderTurn_Construct_RecordsAnswerExactlyOnce`

Verifies a turn with no entries records the answer as the single assistant entry. A turn with
intermediate tool or assistant entries appends the final answer, while a turn already ending with
that answer does not duplicate it.

#### AgentKitSessions-ProviderSession-AnswersForItsOwnWindow: Every Session Says How Full It Is

**Tests**:

- `InMemoryProviderSession_Usage_AnswersForItsOwnWindow`
- `CompactingAgentSession_Usage_PrefersProviderReport`

Asserts a provider session answers with the window it holds and what it occupies, and that the
compacting session's own usage is exactly that answer. Together they show the one token figure the
engine consumes has a single source, so there is no capability to test for and no provenance to check
before the figure can be used.

#### AgentKitSessions-ProviderSession-RejectsMalformedSeedOrTurn: A Malformed Seed or Turn Is Refused

**Tests**:

- `ProviderSessionSeed_Construct_NullArguments_Throw`
- `ProviderTurn_Construct_NullArguments_Throw`
- `TranscriptEntry_Construct_ValidatesPairingIdentifier`

Rejects null seed collections, null entries, null response text, invalid tool pairing identifiers and
undefined entry kinds. These checks keep provider adapters from handing malformed transcript data to
rotation.

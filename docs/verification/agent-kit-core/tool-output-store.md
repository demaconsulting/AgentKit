## ToolOutputStore Unit Verification Design

This document describes the unit-level verification strategy for the internal `ToolOutputStore`
class.

### Verification Approach

`ToolOutputStore` is verified through unit tests that construct it directly — the test project has
access to the library's internals — add outputs, and look them up. `ToolOutputOptions` and
`ToolResult` are used as real dependencies; both are documented dependencies in *ToolOutputStore
Unit Design* and both are simple, deterministic value producers. Eviction is observed through the
public lookup outcome rather than through private state.

Unit tests reside in `ToolOutputStoreTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: None beyond the base class library; no file system or network access required
- **Mocking**: None
- **Isolation**: Each test constructs its own store

### Acceptance Criteria

A unit test run passes when all fourteen scenarios below pass. Any identifier issued out of order
or twice, any retained line altered or lost, any output over the per-output ceiling retained, any
output that fits refused by the early raw-length check, any eviction other than oldest-first, any
lookup outcome misreported, or any inconsistency under concurrent addition constitutes a failure.

### Test Scenarios

#### AgentKitCore-ToolOutputStore-OpaqueReferences: Identifiers Are Issued in Order and Resolve

**Test**: `ToolOutputStore_Add_Outputs_IssuesMonotonicIdsAndRetainsTheLines`

Normal operation: two outputs receive `output-1` and `output-2`, and the first resolves to its
lines.

#### AgentKitCore-ToolOutputStore-NormalizedForPaging: Line Endings Are Normalized

**Test**: `ToolOutputStore_Add_MixedLineEndings_AreNormalizedToLines`

Normal operation and boundaries: CRLF, CR and LF each end a line; a trailing break opens no empty
line; an empty output is one empty line; the size counts one character per break.

#### AgentKitCore-ToolOutputStore-NormalizedForPaging: A Very Long Line Is Wrapped

**Test**: `ToolOutputStore_Add_VeryLongLine_IsWrappedOntoContinuationLines`

Boundary value: a line two and a half times the wrap width becomes three lines, none longer than
the width, whose concatenation is the original, and the entry records the wrapping.

#### AgentKitCore-ToolOutputStore-NormalizedForPaging: Wrapping Keeps a Surrogate Pair Together

**Test**: `ToolOutputStore_Add_WrapAtSurrogatePair_KeepsThePairTogether`

Boundary value: a wrap boundary falling inside a surrogate pair moves before the pair.

#### AgentKitCore-ToolOutputStore-NormalizedForPaging: Compact JSON Is Indented

**Test**: `ToolOutputStore_Indent_CompactJson_SpansSeveralLines`

Normal operation: a one-line JSON document spans several lines and keeps non-ASCII text and angle
brackets readable.

#### AgentKitCore-ToolOutputStore-PerOutputCeiling: An Output Over the Ceiling Is Not Retained

**Test**: `ToolOutputStore_Add_OutputOverPerOutputCeiling_IsNotRetained`

Error path: the oversized output is refused and the output already held survives.

#### AgentKitCore-ToolOutputStore-PerOutputCeiling: An Output at the Raw-Length Bound Still Fits

**Test**: `ToolOutputStore_Add_OutputAtTheRawLengthBound_IsRetainedWhenItFits`

Boundary value: with ceilings of one and three, outputs of nothing but CRLF pairs, twice the
ceiling plus two characters long, are retained and each normalizes to exactly its ceiling.

#### AgentKitCore-ToolOutputStore-PerOutputCeiling: An Output Beyond the Raw-Length Bound Is Refused

**Test**: `ToolOutputStore_Add_OutputBeyondTheRawLengthBound_IsRefusedAndEvictsNothing`

Error path: with a ceiling of one, an output one character beyond the bound is refused and the
output already held survives.

#### AgentKitCore-ToolOutputStore-PerOutputCeiling: The Early Refusal Never Refuses an Output That Fits

**Test**: `ToolOutputStore_MustExceed_NeverRefusesAnOutputThatFits`

Exhaustive boundary analysis: for every output of up to eight characters drawn from a letter, CR
and LF, and every ceiling from one to five, an output the raw-length check refuses normalizes to
more than the ceiling; and for each ceiling the CRLF output of exactly the bound is not refused and
normalizes to exactly the ceiling, proving the bound tight.
#### AgentKitCore-ToolOutputStore-OldestEvictedFirst: The Count Ceiling Evicts the Oldest

**Test**: `ToolOutputStore_Add_BeyondTheCountCeiling_EvictsTheOldestFirst`

Boundary value: with a ceiling of two, a third output evicts the first and keeps the later two.

#### AgentKitCore-ToolOutputStore-OldestEvictedFirst: The Character Ceiling Evicts the Oldest

**Test**: `ToolOutputStore_Add_BeyondTheCharacterCeiling_EvictsTheOldestFirst`

Boundary value: a second output that would exceed the total evicts the first.

#### AgentKitCore-ToolOutputStore-LookupOutcomes: Malformed and Unissued Identifiers Are Distinguished

**Test**: `ToolOutputStore_Find_MalformedOrUnissuedIds_AreDistinguished`

Error path: null, a file name, a leading zero and a bare prefix are malformed; a well-formed
identifier above the last issued is unknown; surrounding whitespace is ignored.

#### AgentKitCore-ToolOutputStore-LookupOutcomes: Each Failure States Its Fact

**Test**: `ToolOutputStore_Refusal_EachFailure_StatesItsFact`

Error path: malformed is `InvalidRequest`; unknown is `TargetNotFound` stating it was never
retained; evicted is `TargetNotFound` stating it is no longer retained.

#### AgentKitCore-ToolOutputStore-ThreadSafe: Concurrent Additions Are Consistent

**Test**: `ToolOutputStore_Add_Concurrently_IssuesDistinctIdsAndRetainsEveryOutput`

Concurrency: two hundred parallel additions receive distinct identifiers and every one resolves.

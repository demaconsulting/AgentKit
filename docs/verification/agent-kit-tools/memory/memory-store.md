### MemoryStore Unit Verification Design

This document describes the unit-level verification strategy for the `IMemoryStore` contract and its
default `InMemoryMemoryStore` implementation.

#### Verification Approach

Nothing is mocked or stubbed. The store holds no collaborators, so each scenario uses a fresh
instance and exercises add, find, replace, remove, search, count, the dimension guard and the
validation rules directly. Vectors are stated as literal coordinate pairs rather than produced by an
embedding generator, because the properties under test are arithmetic — cosine similarity, ordering,
capping, the zero-vector case — and a generated vector would make the expected numbers something a
reader has to take on trust.

The substitutability requirement is exercised at the pack boundary, in
`MemoryPack_CreateTools_SuppliedStore_IsSharedAcrossCompositions`, because it is a property of how a
composition uses a store rather than of any one implementation.

Unit tests reside in `Memory/MemoryStoreTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the fresh in-memory store it needs
- **Isolation**: Each test constructs its own store and memories; no state is shared

#### Acceptance Criteria

A unit test run passes when all 10 requirement scenarios below, covering 12 listed test method
entries, pass without error or exception beyond those explicitly asserted. A silently overwritten
identifier, a miss reported as success, matches returned out of order or beyond the requested count,
a vector of mismatched length scored rather than refused, a division by zero on a degenerate memory,
or an accepted null or empty argument constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Memory-Store-Add: Add

**Test**: `InMemoryMemoryStore_AddAsync_NewMemory_IsHeldAndCounted`

The listed tests prove a filed memory is held and reflected in the count.

##### AgentKitTools-Memory-Store-DuplicateIdentifier: Duplicate Identifier

**Test**: `InMemoryMemoryStore_AddAsync_DuplicateIdentifier_ThrowsArgumentException`

The listed tests prove an identifier the store already holds raises rather than overwriting the
memory that carries it, because identifiers are assigned by the calling tool and a collision is a
defect in this library.

##### AgentKitTools-Memory-Store-Find: Find

**Test**: `InMemoryMemoryStore_FindAsync_UnknownIdentifier_ReportsNull`

The listed tests prove an unknown identifier is an ordinary miss rather than an error, so a tool can
turn it into a refusal the model can read.

##### AgentKitTools-Memory-Store-ReplaceInPlace: Replace In Place

**Test**: `InMemoryMemoryStore_ReplaceAsync_KnownAndUnknownIdentifiers_ReportTheOutcome`

The listed tests prove a replacement lands in the position the memory already occupied, that the
count is unchanged, that the new descriptor and source are what the store then holds, and that a
replacement of an identifier the store does not hold reports a miss and changes nothing.

##### AgentKitTools-Memory-Store-Remove: Remove

**Test**: `InMemoryMemoryStore_RemoveAsync_KnownAndUnknownIdentifiers_ReportTheOutcome`

The listed tests prove a hit removes the memory and shrinks the store, and a miss leaves the store
unchanged and reports the miss.

##### AgentKitTools-Memory-Store-CosineSearch: Cosine Search

**Test**: `InMemoryMemoryStore_SearchAsync_ReportsCosineSimilarityNearestFirst`

The listed tests prove the reported score is cosine similarity — an identical direction scores 1.0
and a 45-degree offset scores the reciprocal of the square root of two — that matches come back
nearest first, and that no more than the requested number are returned.

##### AgentKitTools-Memory-Store-EmptyResults: Empty Results

**Test**: `InMemoryMemoryStore_SearchAsync_EmptyStoreOrZeroCount_ReturnsNoMatches`

**Test**: `InMemoryMemoryStore_SearchAsync_ZeroVector_ScoresZero`

The listed tests prove an empty store and a zero count both answer with no matches rather than
failing, and that a memory whose vector has no direction scores zero rather than taking the search
down with a division by zero.

##### AgentKitTools-Memory-Store-DimensionGuard: Dimension Guard

**Test**: `InMemoryMemoryStore_MismatchedDimension_ThrowsArgumentException`

**Test**: `InMemoryMemoryStore_SearchAsync_EmptyVector_ThrowsArgumentException`

The listed tests prove neither a file nor a search with a vector of a different length is silently
scored, and that a vector holding no values at all is refused. This is what keeps two embedding
models from being mixed in one store and producing similarity numbers that look ordinary and mean
nothing.

##### AgentKitTools-Memory-Store-Validation: Validation

**Test**: `InMemoryMemoryStore_Validation_NullOrEmptyArguments_Throw`

The listed tests prove a null memory on add and on replace, an empty identifier on find and on
remove, and a negative match count on search are all reported as programming errors, because the
tools refuse a model's malformed request before the store is reached.

##### AgentKitTools-Memory-Store-Substitutable: Substitutable Persistence

**Test**: `MemoryPack_CreateTools_SuppliedStore_IsSharedAcrossCompositions`

The listed tests prove a store the application supplied is the store the family's tools use. The
scenario lives at the pack boundary because that is the only place the substitution is expressed.

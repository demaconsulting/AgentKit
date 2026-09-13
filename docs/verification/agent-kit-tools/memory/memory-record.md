### MemoryRecord Unit Verification Design

This document describes the unit-level verification strategy for the `MemoryRecord` and
`MemoryMatch` records.

#### Verification Approach

Nothing is mocked or stubbed. The records hold no collaborators and enforce no invariants of their
own, so each scenario constructs them directly and reads back what they hold. The scenarios exist to
pin the data model rather than to exercise logic: the parts a memory carries, that an absent source
is permitted, that one part can be replaced with the rest carried across, and that a score is a
property of a comparison rather than of a memory.

Unit tests reside in `Memory/MemoryRecordTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario constructs the records it needs
- **Isolation**: No state is shared between scenarios

#### Acceptance Criteria

A unit test run passes when all 4 requirement scenarios below, covering 4 listed test method
entries, pass without error or exception beyond those explicitly asserted. A part reported
differently from how it was stated, a rejected absent source, a re-statement that loses a part it
should have carried across, or a score folded into the memory constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Memory-Record-DescriptorPayloadSplit: Descriptor and Payload

**Test**: `MemoryRecord_Constructor_StatedValues_AreHeldUnchanged`

The listed tests prove a memory carries an identifier, a descriptor, a detail payload, both
provenance fields and a vector, and reports each of them exactly as stated.

##### AgentKitTools-Memory-Record-Provenance: Provenance

**Test**: `MemoryRecord_Constructor_AbsentProvenance_IsPermitted`

The listed tests prove a memory with no stated source is a legitimate memory whose absence is held
as an absence, so that a model never has to invent a citation to file what it read.

##### AgentKitTools-Memory-Record-Immutability: Immutability

**Test**: `MemoryRecord_With_ReplacesOnePartAndKeepsTheRest`

The listed tests prove a memory can be re-stated with its details replaced while its identifier,
descriptor, provenance and vector are carried across unchanged — the operation an update relies on
to be impossible to half-apply.

##### AgentKitTools-Memory-Record-Match: Match

**Test**: `MemoryMatch_Constructor_CarriesTheMemoryAndItsSimilarity`

The listed tests prove one memory can be paired with two different scores from two different
comparisons, which is what establishes that the score belongs to the comparison and not to the
memory.

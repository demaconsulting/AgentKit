## TokenEstimator Unit Verification Design

This document describes the unit-level verification strategy for `TokenEstimator`.

### Verification Approach

`TokenEstimator` is the deterministic arithmetic behind every figure no provider reported: the fixed
overhead measured from application configuration, the engine's own account of the context, and the
grouping of oversized material into summarizer calls. Tests call the estimator directly for absent
content, rounding, transcript-entry overhead and tool declarations. No model tokenizer is contacted;
the point is to verify this library's own consistent currency.

Unit tests reside in `TokenEstimatorTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no tokenizer service is contacted
- **Mocking**: None required; a simple `AIFunction` is created for tool declaration measurement
- **Isolation**: Each test builds only the strings, entries or tools it measures

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any absent content charged tokens, any non-empty content rounded to zero, any
entry framing or tool declaration skipped, or any null tool declaration dereferenced constitutes a
failure.

### Test Scenarios

#### AgentKitSessions-TokenEstimator-DeterministicEstimate: The Ratio Is Applied Exactly, and Rounds Up

**Tests**:

- `TokenEstimator_EstimateTokens_AbsentContent_IsZero`
- `TokenEstimator_EstimateTokens_RoundsUp`

Asserts null and empty content estimate to zero, while short non-empty content rounds up. This keeps
estimates deterministic and prevents present content from becoming free.

#### AgentKitSessions-TokenEstimator-ChargesEntryFraming: An Entry Is Charged for Its Framing

**Test**: `TokenEstimator_EstimateEntryTokens_IncludesFraming`

Asserts a transcript entry costs its text estimate plus per-entry framing. The library's account of
a context therefore includes the structure around each conversation entry rather than its text alone.

#### AgentKitSessions-TokenEstimator-EstimatesToolDeclarations: Declarations Are Measured, Never Skipped

**Tests**:

- `TokenEstimator_EstimateToolDeclarations_None_IsZero`
- `TokenEstimator_EstimateToolDeclarations_ChargesEachTool`
- `TokenEstimator_EstimateToolDeclarations_NullEntry_Throws`

Verifies a missing or empty tool list has no fixed overhead, each declaration is charged when
present, and null declarations are refused at the estimator boundary.

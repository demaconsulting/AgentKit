## TokenEstimator Unit Verification Design

This document describes the unit-level verification strategy for the `TokenEstimator` class.

### Verification Approach

`TokenEstimator` is static, stateless and deterministic, so it is verified by direct call with
inputs whose expected output can be computed by hand. Every scenario states its input in terms of
the published constants rather than in literal characters, so a test remains correct and remains
readable if a constant is ever revisited.

Nothing is mocked. The tool-declaration scenarios build a real `AIFunction` through the framework's
own function factory rather than a stub, because what is being measured is the name, description and
schema a provider would actually be sent; a stub with an empty schema would measure something the
estimator never sees in production.

Unit tests reside in `TokenEstimatorTests.cs` within the `DemaConsulting.AgentKit.Sessions.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no network access is used**
- **Mocking**: None; real `AIFunction` instances are built through `AIFunctionFactory`
- **Isolation**: Each test builds its own input; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any estimate that treats present content as free, omits the framing allowance,
or silently skips a tool constitutes a failure.

### Test Scenarios

#### AgentKitSessions-TokenEstimator-DeterministicEstimate: The Ratio Is Applied Exactly, and Rounds Up

**Tests**: `TokenEstimator_EstimateTokens_AbsentText_ReturnsZero`,
`TokenEstimator_EstimateTokens_WholeMultiple_AppliesCharacterRatio`,
`TokenEstimator_EstimateTokens_PartialToken_RoundsUp`

Three boundary scenarios covering absent content, an exact whole multiple of the character ratio,
and a single character far short of a whole token. Absent content must cost nothing, because a
missing system prompt or an empty tier genuinely occupies no window. A whole multiple must estimate
exactly, because that is what lets a test state a transcript's size rather than approximate it. A
single character must still cost a token, because a budget that treated short content as free would
let an unbounded number of short entries accumulate inside it.

**The rounding addition's overflow is not a scenario, because it is not reachable.** A review raised
`(text.Length + 3) / 4` as wrapping for a string whose length approaches the largest representable
count. It cannot: a string is one object, the runtime caps one object at two gigabytes, and the
very-large-object setting raises that for arrays and not for strings. Measured directly on this
repository's targets, `new string('a', 1_073_741_791)` allocates and `new string('a', 1_073_741_792)`
throws `OutOfMemoryException`, so the addition reaches at most 1,073,741,794 — short of half the
largest representable count — and a single estimate reaches at most 268,435,448 tokens. The finding
is refuted by that measurement rather than implemented, and the cap is recorded in the estimator's
own documentation so the next review reaches the same conclusion without re-deriving it.

#### AgentKitSessions-TokenEstimator-ChargesEntryFraming: An Entry Is Charged for Its Framing

**Tests**: `TokenEstimator_EstimateEntryTokens_AddsFramingAllowance`,
`TokenEstimator_EstimateEntryTokens_NullEntry_Throws`

Asserts an entry whose text is exactly two tokens estimates as two plus the published per-entry
allowance, and that a null entry is refused. Framing is charged even when the text is short, so an
estimator counting only characters would systematically under-count a long run of small entries —
exactly the shape a tool-using agent produces. A null estimated as zero would silently understate a
transcript.

#### AgentKitSessions-TokenEstimator-EstimatesToolDeclarations: Declarations Are Measured, Never Skipped

**Tests**: `TokenEstimator_EstimateToolDeclarationTokens_NoTools_ReturnsZero`,
`TokenEstimator_EstimateToolDeclarationTokens_GrowsWithToolCount`,
`TokenEstimator_EstimateToolDeclarationTokens_NullTool_Throws`

Asserts an agent with no tools carries no declaration overhead, that a real declaration costs
something and that two cost exactly twice one, and that a null tool is refused. The linearity
assertion is what pins the behavior that matters downstream: this figure is fixed overhead
subtracted before any rotation percentage, so an estimate that did not grow with the tool set would
let the rotation point drift as tools were attached. Skipping a null tool would understate the
overhead and delay rotation past the point it was meant to fire.

The rejection of a declaration block too large for a token count is **not exercised by a test**, and
that is recorded rather than implied. Reaching it needs several gigabytes of declaration text in one
process, which no build here will allocate. It is kept because the total is the one figure here that
can escape a token count while every term composing it cannot — a wrapped sum would be consumed
downstream as a real measurement of the fixed overhead — and it is placed at the point that figure
first becomes computable so no later site has to re-check it.

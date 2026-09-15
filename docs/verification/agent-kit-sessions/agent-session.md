## AgentSession Unit Verification Design

This document describes the unit-level verification strategy for the `IAgentSession` contract and
the `AgentSessionResponse` type.

### Verification Approach

`AgentSessionResponse` is a value type with validation, so it is verified directly: construct it and
assert what it reports and what it refuses. Nothing is mocked, because it depends on nothing beyond
`ContextUsage` and `SaturationSignal`, both of which are cheap to construct honestly.

The `IAgentSession` contract itself cannot be verified in isolation — an interface has no behavior —
so its obligations are verified through its implementation, in _CompactingAgentSession Unit
Verification Design_, and through the system scenarios. What is verified here is the reporting
surface every implementation must populate correctly.

Unit tests reside in `AgentSessionTests.cs` within the `DemaConsulting.AgentKit.Sessions.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no network access is used**
- **Mocking**: None required
- **Isolation**: Each test constructs its own response; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any turn report that accepts a missing answer, a missing usage figure or a null
saturation signal, or that fails to distinguish a clean turn from a saturating one, constitutes a
failure.

### Test Scenarios

#### AgentKitSessions-AgentSession-SessionContract: A Clean Turn Reports No Compaction

**Test**: `AgentSessionResponse_Construct_OrdinaryTurn_ReportsNoCompaction`

Constructs a response for a turn that needed no compaction and asserts the answer survives, no
rotation is reported, and the saturation list is empty rather than null. An application that never
inspects compaction must never be misled, and one that does must see a clean turn as clean.

#### AgentKitSessions-AgentSession-ReportsCompaction: A Saturating Rotation Is Visible

**Test**: `AgentSessionResponse_Construct_SaturatedRotation_SurfacesTheSignals`

Constructs a response for a rotation that could not reduce and asserts both the rotation and the
saturation signal are visible, with the signal instance carried through unchanged. A saturated agent
must be distinguishable from a healthy one, or the condition is invisible to the application.

#### AgentKitSessions-AgentSession-RejectsMalformedTurnReport: A Malformed Report Is Refused

**Tests**: `AgentSessionResponse_Construct_NullText_Throws`,
`AgentSessionResponse_Construct_NullUsage_Throws`,
`AgentSessionResponse_Construct_NullSaturationSignal_Throws`

Three error paths. A null answer and a null usage figure are refused with `ArgumentNullException`; a
null entry in the saturation list is refused with `ArgumentException`. Each could only come from a
defect in an adapter or in the engine, and each would otherwise surface in the application that
displayed or inspected the response rather than where it was produced.

#### Supporting Corner Cases

**Tests**: `SaturationSignal_Construct_InvalidFigures_Throws`,
`RotationOutcome_Construct_NullLayout_Throws`

Defensive scenarios for the two engine result types this unit reports. A saturation signal refuses a
tier index below one and a negative token count, so the one report an application acts on cannot
itself be nonsense; a rotation outcome refuses a missing layout, because the layout is what a fresh
provider session is seeded from and there is no recovery from its absence.

## ContextUsage Unit Verification Design

This document describes the unit-level verification strategy for `ContextUsage` and
`IContextUsageReporter`.

### Verification Approach

`ContextUsage` is a validated immutable value type verified by construction and by reading its
derived figures. `IContextUsageReporter` is verified through a minimal hand-written reporter that
reports nothing, which is the case the interface exists to make expressible; the reporting case is
covered where a real implementation lives, in _InMemoryProviderSession Unit Verification Design_.

The over-full window scenario deserves a note. It asserts that the derived free space floors at zero
while the raw counts and the fraction are left unclamped, because clamping the counts would hide
exactly the condition an application most needs to see — a provider reporting usage beyond its own
limit.

Unit tests reside in `ContextUsageTests.cs` within the `DemaConsulting.AgentKit.Sessions.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no network access is used**
- **Mocking**: A hand-written reporter that reports nothing; no mocking framework
- **Isolation**: Each test constructs its own usage figure; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any figure whose origin is lost, any derived value that disagrees with the
counts it comes from, any clamping that hides an over-full window, or any meaningless figure
accepted constitutes a failure.

### Test Scenarios

#### AgentKitSessions-ContextUsage-UsageShape: The Origin Survives, and the Derived Figures Follow

**Tests**: `ContextUsage_FromProvider_MarksTheOriginAsProvider`,
`ContextUsage_FromEstimate_MarksTheOriginAsEstimated`,
`ContextUsage_Derived_ReportsFreeTokensAndFraction`

Asserts a provider-reported figure and an estimated figure are distinguishable, and that the free
tokens and occupied fraction follow from the counts rather than being stored alongside them. The
origin is what lets an application tell a measurement from a derivation instead of treating them as
interchangeable.

#### AgentKitSessions-ContextUsage-ReportsOverFullHonestly: An Over-Full Window Is Not Hidden

**Test**: `ContextUsage_OverFullWindow_ReportsNoFreeTokensButKeepsTheCounts`

Constructs a figure whose usage exceeds its window and asserts the free space reports zero while the
occupied count is unchanged and the fraction exceeds one. A full window has no negative amount of
room, but the over-full condition itself must remain visible.

#### AgentKitSessions-ContextUsage-RejectsMeaninglessFigures: Impossible Figures Are Refused

**Tests**: `ContextUsage_Construct_NegativeUsage_Throws`, `ContextUsage_Construct_ZeroWindow_Throws`,
`ContextUsage_Construct_UndefinedOrigin_Throws`

Three error paths. A negative occupied count could only come from a defect and would make every
threshold comparison meaningless. A window of zero would leave nothing for the occupied fraction to
be a fraction of.

The third constructs a figure with a cast integer origin and asserts the parameter name on the
refusal. It is the one whose absence was not merely untidy: every consumer of the origin asks only
whether it is `Provider`, so an undefined value would have been accepted and then read as an
estimate — taking the configured-window rotation threshold and skipping the reported-window bound
check entirely. A value that names nothing would have selected a materially different code path.

#### AgentKitSessions-ContextUsage-OptionalReportingContract: A Reporter May Say It Does Not Know

**Tests**: `ContextUsage_Reporter_MayReportNothing`,
`InMemoryProviderSession_CurrentUsage_WhenNotReporting_IsNull`

Asserts that an implementation can report nothing rather than fabricating a figure, both through a
minimal hand-written reporter and through the shipped in-memory session configured not to report.
This is the case the separate optional interface exists for: an adapter for a provider that reveals
nothing must be able to say so, because an invented number is indistinguishable from a real one at
the point it is consumed.

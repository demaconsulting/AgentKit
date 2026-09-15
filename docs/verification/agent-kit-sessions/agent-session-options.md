## AgentSessionOptions Unit Verification Design

This document describes the unit-level verification strategy for the `AgentSessionOptions` class.

### Verification Approach

`AgentSessionOptions` is verified by construction: every scenario builds an options instance and
asserts either the figures it derived or the configuration it refused. Nothing is mocked except the
summarizer, which is a `FakeSummarizer` because the options only hold it and never call it.

The scenarios that matter most are the two arithmetic ones. The fixed-overhead scenario uses a
system prompt of an exact known token size and a real `AIFunction` built through the framework's own
function factory, so the overhead being subtracted is the overhead a provider would actually be sent
rather than a stand-in. The bound scenarios probe the boundary from both sides — a window one step
too small is refused, a window exactly large enough is accepted — because a bound asserted only from
the failing side could be off by any amount of hidden slack.

Unit tests reside in `AgentSessionOptionsTests.cs`, with the fake summarizer in `FakeSummarizer.cs`,
both within the `DemaConsulting.AgentKit.Sessions.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no network access is used**
- **Mocking**: A deterministic fake summarizer; no mocking framework
- **Isolation**: Each test constructs its own options; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any configuration accepted that could not converge, any overhead not subtracted
before the threshold, or any invalid argument accepted rather than refused constitutes a failure.

### Test Scenarios

#### AgentKitSessions-AgentSessionOptions-CarriesConfiguration: Defaults Are What an Application Receives

**Tests**: `AgentSessionOptions_Construct_Defaults_CarryThePublishedWindowAndPolicy`,
`AgentSessionOptions_Construct_NullSummarizer_Throws`

Asserts that configuring nothing but the summarizer yields the published window, the shared default
policy by reference, no fixed overhead and no tools — and that omitting the summarizer is refused.
The summarizer is the one required argument because defaulting it would hand an application a
session that silently never compacts, which is the exact failure this package exists to prevent.

#### AgentKitSessions-AgentSessionOptions-MeasuresFixedOverhead: Overhead Is Subtracted Before the Threshold

**Test**: `AgentSessionOptions_Construct_SubtractsFixedOverheadBeforeTheThreshold`

Configures a prompt of exactly forty tokens and one real tool in a 20,000-token window, then asserts
the prompt was measured exactly, the declarations were charged something, the fixed overhead is
their sum, the effective window is the provider window less that sum, and the rotation threshold is
the policy's fraction **of the effective window** rather than of the whole one. Applying the
percentage to the raw window would make the rotation point drift silently with how many tools an
application attached.

#### AgentKitSessions-AgentSessionOptions-AssertsTheBound: The Construction Bound Is Enforced From Both Sides

**Tests**: `AgentSessionOptions_Construct_WindowSmallerThanTierBudgets_Throws`,
`AgentSessionOptions_Construct_OverheadConsumesTheWindow_Throws`,
`AgentSessionOptions_Construct_WindowExactlyFitsTierBudgets_IsAccepted`

Boundary scenarios. A 4,000-token window cannot hold the default policy's 4,800 tokens of tier
budgets and is refused, with the message asserted so the diagnosis reaches the author; a prompt
larger than its window is refused; and a window exactly equal to the total tier budget is accepted,
which is what proves the bound is a genuine boundary rather than an approximation. A session in the
refused configuration would rotate into a context already over budget and could never converge.

#### AgentKitSessions-AgentSessionOptions-RejectsMalformedConfiguration: Invalid Arguments Are Refused

**Tests**: `AgentSessionOptions_Construct_NonPositiveWindow_Throws`,
`AgentSessionOptions_Construct_NullTool_Throws`

Two error paths. A non-positive window leaves nothing to rotate within. A null tool cannot have its
declaration estimated, and skipping it would understate the fixed overhead and delay rotation past
the point it was meant to fire — a quiet miscalculation rather than a visible failure, which is why
it is refused rather than tolerated.

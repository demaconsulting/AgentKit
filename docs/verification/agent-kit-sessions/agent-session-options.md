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
`AgentSessionOptions_Construct_NullSummarizer_Throws`,
`AgentSessionOptions_Construct_CopiesTheSuppliedTools`

Asserts that configuring nothing but the summarizer yields the published window, the shared default
policy by reference, no fixed overhead and no tools — and that omitting the summarizer is refused.
The summarizer is the one required argument because defaulting it would hand an application a
session that silently never compacts, which is the exact failure this package exists to prevent.
The copy scenario clears the caller's list after construction and asserts both that the options
still carry the tool and that their own list refuses a write through it. The declaration tokens are
measured once, so a list that could change afterwards would carry tools into every future rotation
that the effective window and threshold did not account for.

#### AgentKitSessions-AgentSessionOptions-MeasuresFixedOverhead: Overhead Is Subtracted Before the Threshold

**Tests**: `AgentSessionOptions_Construct_SubtractsFixedOverheadBeforeTheThreshold`,
`AgentSessionOptions_Construct_SubTokenThreshold_IsRefused`

Configures a prompt of exactly forty tokens and one real tool in a 20,000-token window, then asserts
the prompt was measured exactly, the declarations were charged something, the fixed overhead is
their sum, the effective window is the provider window less that sum, and the rotation threshold is
the policy's fraction **of the effective window** rather than of the whole one. Applying the
percentage to the raw window would make the rotation point drift silently with how many tools an
application attached. The second scenario configures a rotation threshold whose product with the
effective window falls below one token and asserts the configuration is refused rather than
adjusted. Truncation would otherwise produce zero, and a threshold of zero is satisfied by a
conversation of no tokens at all — the session would be willing to rotate a context holding nothing.
Refusing states the defect where the application wrote it, instead of substituting a figure the host
did not choose.

#### AgentKitSessions-AgentSessionOptions-AssertsTheBound: The Convergence Point Is Enforced From Both Sides

**Tests**: `AgentSessionOptions_Construct_WindowSmallerThanTierBudgets_Throws`,
`AgentSessionOptions_Construct_OverheadConsumesTheWindow_Throws`,
`AgentSessionOptions_Construct_WindowBelowTheConvergencePoint_IsRefused`,
`AgentSessionOptions_Construct_PolicyBoundAtTheLargestTokenCount_ReportsTheWindowItWouldNeed`

Boundary scenarios. A 4,000-token window cannot hold the default policy's 4,800 tokens of tier
budgets and is refused, with the message asserted so the diagnosis reaches the author; a prompt
larger than its window is refused; and a window exactly equal to the bound — the tier budgets plus
the framing their seeded records carry — is **refused**, while the smallest window that clears the
convergence point is accepted. Merely holding the bound is what makes a rotated context fit; landing
below the rotation threshold is what makes the session converge. A window between the two satisfies
the first and fails the second, so the session would rotate on nearly every turn without ever
getting under its own threshold, raising no saturation signal while doing it. The figures compared
are this library's estimates, so the refusal excludes the arrangements that cannot work rather than
proving the rest will.

The fourth scenario is the extreme a policy is allowed to carry. `CompactionPolicy` refuses budgets
and framing summing *past* a token count and accepts a sum of exactly the largest representable one,
so the refusal here has to be able to quote the window such a policy would need. It could not: the
minimum-window helper floored its search at that bound plus one, which is not a representable token
count, and the clamp it handed that floor to threw an argument error of its own — so a valid policy
reported a defect inside the helper instead of the non-convergent window the author had actually
configured. The scenario builds two budgets summing with their framing to exactly the maximum,
asserts the minimum window saturates at that maximum rather than throwing, and asserts the
constructor refuses the configuration naming the `compaction` parameter and the convergence reason.

#### AgentKitSessions-AgentSessionOptions-RejectsMalformedConfiguration: Invalid Arguments Are Refused

**Tests**: `AgentSessionOptions_Construct_NonPositiveWindow_Throws`,
`AgentSessionOptions_Construct_NullTool_Throws`

Two error paths. A non-positive window leaves nothing to rotate within. A null tool cannot have its
declaration estimated, and skipping it would understate the fixed overhead and delay rotation past
the point it was meant to fire — a quiet miscalculation rather than a visible failure, which is why
it is refused rather than tolerated.

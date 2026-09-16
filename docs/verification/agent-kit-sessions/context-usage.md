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
`ContextUsage_Derived_ReportsFreeTokensAndFraction`,
`ContextUsage_FromProvider_WithConversationSplit_CarriesItAndDerivesTheOverhead`

Asserts a provider-reported figure and an estimated figure are distinguishable, and that the free
tokens and occupied fraction follow from the counts rather than being stored alongside them. The
origin is what lets an application tell a measurement from a derivation instead of treating them as
interchangeable.

The fourth asserts the seam a reporting adapter uses. It constructs the figure a Copilot session
reports — a current total of 184,561 against a limit of 200,000, of which 181,834 is conversation —
and asserts the conversation is carried unchanged and the overhead is the 2,727 tokens the two
counts leave between them. Deriving the overhead from the pair, rather than accepting one from
elsewhere, is what keeps the whole figure in the currency it arrived in.

#### AgentKitSessions-ContextUsage-DefaultsToAllConversation: An Unreported Split Is Not Invented

**Test**: `ContextUsage_WithoutConversationSplit_TreatsTheWholeUsageAsConversation`

Asserts that a figure created without a conversation count — from either origin — attributes all of
its usage to the conversation and none to overhead. Estimating the split instead would be the defect
this shape exists to remove. Claiming no overhead is not a guess: it makes the rotation threshold a
fraction of the whole window and the conversation the whole usage, which rotates strictly earlier
than a correct split would. Early is the safe direction, because the guarantee at stake is that the
provider's own compactor never fires.

That is asserted of the rotation trigger and of nothing else. The convergence check
`CompactingAgentSession` makes against a reported window is the comparison for which the same default
errs the _other_ way, and it is verified separately against a provider carrying real unreported
overhead; see
_AgentKitSessions-CompactingAgentSession-RefusesUnusableReportedWindow_ in
_CompactingAgentSession Unit Verification Design_.

#### AgentKitSessions-ContextUsage-ReportsOverFullHonestly: An Over-Full Window Is Not Hidden

**Test**: `ContextUsage_OverFullWindow_ReportsNoFreeTokensButKeepsTheCounts`

Constructs a figure whose usage exceeds its window and asserts the free space reports zero while the
occupied count is unchanged and the fraction exceeds one. A full window has no negative amount of
room, but the over-full condition itself must remain visible.

#### AgentKitSessions-ContextUsage-RejectsMeaninglessFigures: Impossible Figures Are Refused

**Tests**: `ContextUsage_Construct_NegativeUsage_Throws`, `ContextUsage_Construct_ZeroWindow_Throws`,
`ContextUsage_Construct_ConversationExceedingUsage_Throws`,
`ContextUsage_Construct_NegativeConversation_Throws`,
`ContextUsage_Construct_UndefinedOrigin_Throws`

Five error paths. A negative occupied count could only come from a defect and would make every
threshold comparison meaningless. A window of zero would leave nothing for the occupied fraction to
be a fraction of.

A conversation count outside the range zero to the occupied count is the pair that repays the most
explanation, because it is the deliberate opposite of the over-full window above. Usage beyond the
window is a condition a provider genuinely reports and must stay visible; a conversation larger than
everything occupied is one no accounting produces. Accepting it would make the derived overhead
negative, which _enlarges_ the window a rotation threshold is taken from — the session would then
rotate later than the provider's own window allows, which is the one failure this package exists to
prevent. The test asserts the parameter name, so an adapter's arithmetic defect is reported where
the adapter wrote it.

The last constructs a figure with a cast integer origin and asserts the parameter name on the
refusal. It is the one whose absence was not merely untidy: every consumer of the origin asks only
whether it is `Provider`, so an undefined value would have been accepted and then read as an
estimate — taking the configured-window rotation threshold and skipping the reported-window bound
check entirely. A value that names nothing would have selected a materially different code path.

#### AgentKitSessions-ContextUsage-OptionalReportingContract: A Reporter May Say It Does Not Know

**Tests**: `ContextUsage_Reporter_MayReportNothing`,
`InMemoryProviderSession_CurrentUsage_WhenNotReporting_IsNull`,
`CompactingAgentSession_SendAsync_ProviderBeginsReportingAfterItsFirstTurn_MeasuresTheFoldThen`,
`CompactingAgentSession_SendAsync_LateReportingProviderInAConvergentWindow_RotatesAndSettles`

Asserts that an implementation can report nothing rather than fabricating a figure, both through a
minimal hand-written reporter and through the shipped in-memory session configured not to report.
This is the case the separate optional interface exists for: an adapter for a provider that reveals
nothing must be able to say so, because an invented number is indistinguishable from a real one at
the point it is consumed.

**What reporting late costs is now verified rather than conceded.** The third scenario is the
freedom this contract grants, exercised end to end: a provider silent until it has answered a turn
and then reporting totals alone, over 100 tokens of overhead it never breaks out, in a 500-token
window. Creation cannot refuse it — the provider says nothing there, so the session falls back to an
estimate against the configured window — and the first turn is where the provider first speaks and
so where the fold is measured. The test asserts the refusal names a fold other than zero and quotes
a requirement above the 446 tokens the same policy needs with no fold at all, that the provider
session was released, and that the session refuses further turns. Against the previous behavior the
turn returned an ordinary answer and the session rotated on every turn thereafter, raising no
saturation signal, and this scenario is what now fails in that state.

The fourth is the counterweight, and it is what stops the third being satisfied by a guard that
simply refused every late reporter. The same shape in a 1,200-token window is accepted, rotates, and
settles — rotating at least once and fewer than four times across four turns — and a further turn
reads the provider's own figures. Abandoning a conformant adapter after a real message has been
spent was the alternative resolution, and it was rejected for exactly this reason.

### AgentRunTool Unit Verification Design

This document describes the unit-level verification strategy for the `AgentRunTool` class.

#### Verification Approach

Nothing is mocked or stubbed. The unit's whole purpose is to behave correctly against a real
policy, a real profile list, a real runner and a real child-composition seam, and a substituted
seam would verify only the substitute. Each scenario constructs a real `PathPolicy`, a set of
real `AgentProfile` instances, a test-owned runner lambda and a test-owned
`ChildToolComposer` lambda, builds the tool through its own internal factory, and invokes it the
way a runtime does — through `InvokeAsync` with a named argument dictionary — so that the
delegation path is exercised rather than bypassed. Invoking through the constructed tool is
essential: a hand-called delegate would prove nothing about how the tool as registered marshals
its result.

The composer lambdas that unit tests supply let the scenarios assert what the tool passes into
composition without pulling in a real sibling family; the family's real composer is verified in
`AgentPack`'s own unit tests, where it belongs.

Unit tests reside in `Agent/AgentRunToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project, and use the shared `TemporaryDirectory` helper
where a policy needs a working directory.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **Runner**: A test-owned lambda that returns a stated string, records the request, or throws
- **Composer**: A test-owned lambda that returns a stated tool list and records the profile it
  was called with
- **Isolation**: Each test constructs its own policy, profiles, runner and composer; no state
  is shared

#### Acceptance Criteria

A unit test run passes when all eleven scenarios below pass without error or exception beyond
those explicitly asserted. A tool description that omits a registered profile, a missing
required constructor argument accepted, an unknown or malformed profile that reaches the
runner, a call at the depth ceiling that composes or starts anything, a child answer that is
truncated rather than refused, a null answer reported as a failure, or a composed profile
different from the one the model named each constitute a failure.

#### Test Scenarios

##### AgentKitTools-Agent-RunTool-DescriptionNamesProfiles: The Description Names the Available Profiles

**Test**: `AgentRunTool_Create_DescriptionNamesTheAvailableProfiles`

Normal operation for the request a model actually makes: a tool built from a list of profiles
carries a description that names each profile, and includes a profile's description in
parentheses where one was supplied, so the model chooses without a wasted turn.

##### AgentKitTools-Agent-RunTool-DescriptionNamesProfiles: An Empty Registration States It Has None

**Test**: `AgentRunTool_Create_NoProfiles_DescriptionStatesThereAreNone`

Boundary condition on the description: a tool built with no profiles states that none are
registered rather than presenting an empty list a model might fill by guessing.

##### AgentKitTools-Agent-RunTool-GuardedConstruction: Missing Construction Parts Throw

**Test**: `AgentRunTool_Create_MissingParts_Throw`

Error path at construction: a null policy, profiles collection, runner or composer, and a
negative depth, each throw at construction. Every one of these is supplied by the family that
owns this tool, so a missing one is a defect in the library rather than something a composing
application did.

##### AgentKitTools-Agent-RunTool-UnknownProfileRefused: An Unknown Profile Names the Available Ones

**Test**: `AgentRunTool_Run_UnknownProfile_IsRefusedAndNamesTheAvailableProfiles`

Error path and disclosure: a request naming a profile that is not registered is refused as
`TargetNotFound`, and the refusal lists the profiles that do exist as a statement of fact
about the tool's own state.

##### AgentKitTools-Agent-RunTool-UnknownProfileRefused: Profile Name Case Is Significant

**Test**: `AgentRunTool_Run_ProfileNameCase_IsSignificant`

Boundary condition: the comparison is ordinal, so a case-mismatched name is refused as an
unknown profile rather than accepted through a lenient comparison. A profile name means
exactly itself, and every provider compares the strings a model emits the same way.

##### AgentKitTools-Agent-RunTool-MalformedRequestRefused: A Missing Profile or Task Is Refused

**Test**: `AgentRunTool_Run_MissingProfileOrTask_IsRefused`

Error path: an omitted, empty or whitespace `profile` or `task` is refused as `InvalidRequest`
rather than raising an exception. The parameters carry defaults so an omitted argument is
refused by the tool rather than by the function factory.

##### AgentKitTools-Agent-RunTool-DepthCeiling: A Call at the Depth Ceiling Is Refused Without Composing or Starting

**Test**: `AgentRunTool_Run_AtTheDepthCeiling_IsRefusedWithoutComposingOrStarting`

Boundary condition and budget: builds the tool at a depth that would exceed the policy's
`MaxAgentDepth` and asserts the call is refused as `InvalidRequest`, that the composer was not
called, and that the runner was not called. Nothing is built for a run that is going to be
refused.

##### AgentKitTools-Agent-RunTool-DepthCeiling: A Call Below the Depth Ceiling Is Permitted

**Test**: `AgentRunTool_Run_BelowTheDepthCeiling_IsPermitted`

Normal operation on the other side of the same ceiling, pinning it so no caller has to guess:
a call whose child sits at or below the ceiling is composed and started, and the runner's
answer is returned.

##### AgentKitTools-Agent-RunTool-ChildResultReporting: A Child That Said Nothing Is Reported as Such

**Test**: `AgentRunTool_Run_ChildSaidNothing_IsReportedAsSuch`

Normal operation and boundary condition together: a runner returning `null` or empty text is
reported as an agent that finished without reporting anything, not as a failure. An agent can
legitimately finish having found there was nothing to report, and telling the parent it failed
would invite it to retry work that was already done.

##### AgentKitTools-Agent-RunTool-ChildResultReporting: An Oversized Answer Is Refused Rather Than Truncated

**Test**: `AgentRunTool_Run_OversizedAnswer_IsRefusedRatherThanTruncated`

Boundary condition on the result ceiling: an answer longer than `MaxResultCharacters` is
refused as `ResourceTooLarge` naming the ceiling, and no partial answer is returned. A
truncated report is worse than none, because the parent cannot tell it is reading half an
answer.

##### AgentKitTools-Agent-RunTool-ComposesNamedProfileOnly: The Named Profile Is the Only One Composed

**Test**: `AgentRunTool_Run_ComposesTheNamedProfileOnly`

The load-bearing isolation scenario for this unit: asserts the composer was invoked with the
profile the model named and with no other, and that the request handed to the runner carries
the tools the composer returned. No parent-bound tool list is filtered at this call site; the
composer is the only route by which tools reach the child.

### AgentPack Unit Verification Design

This document describes the unit-level verification strategy for the `AgentPack` class.

#### Verification Approach

Nothing is mocked or stubbed except the child pack. The pack's job is to compose *other*
families' packs for a child, so verifying it at the unit level with a real sibling family would
make the pack's unit tests depend on an unrelated subsystem — a failure in the sibling would be
indistinguishable from a failure here. The scenarios therefore use the local `StubToolPack`
helper, which publishes named do-nothing tools under a stated prefix and records the policies
it was asked to build against. Every tool it publishes carries a fresh identity per
`CreateTools` call, which is what lets a scenario prove that a child's tools are newly composed
rather than the parent's own tools handed down.

The policy and the `ToolPackBuilder` machinery the pack composes through are real, because the
properties under verification — that the narrowing rules match real `PathPolicy` behavior, that
the child composition runs through a real `ToolPackBuilder`, and that a null policy is rejected
before any composition — are properties of the real thing.

Unit tests reside in `Agent/AgentPackTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project, and use the shared `StubToolPack` and `TemporaryDirectory` helpers alongside.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: Scenarios needing a policy working directory create a temporary tree
  through `TemporaryDirectory` and delete it afterwards
- **Isolation**: Each test constructs its own pack, policy and stubs; no state is shared

#### Acceptance Criteria

A unit test run passes when all twenty-one scenarios below pass without error or exception
beyond those explicitly asserted. A prefix that differs between the constant and the contract,
a capability requirement other than delegation, a tool count other than one, a malformed
registration accepted, a null policy accepted, a child's tools drawn from the parent's list
rather than composed against the child's policy, a widening profile accepted at the
application's own composition, a narrowing profile refused, a delegation refused or thrown over
a sibling profile the child cannot reach, or a profile stating no grants that fails to inherit
the parent's policy each constitute a failure.

#### Test Scenarios

##### AgentKitTools-Agent-Pack-FamilyPrefix: The Prefix Is Published as Constant and Contract

**Test**: `AgentPack_FamilyPrefix_IsPublishedAsConstantAndContract`

Asserts the published constant is `agent` and that the contract member reports the same
value, so the two cannot drift apart.

##### AgentKitTools-Agent-Pack-RequiresDelegation: The Pack Requires Delegation

**Test**: `AgentPack_RequiredCapabilities_IsDelegation`

Normal operation: requiring the delegation capability is what lets the composition withhold
the family from a host that cannot start a second agent.

##### AgentKitTools-Agent-Pack-RegistersRunTool: The Run Tool Is Created

**Test**: `AgentPack_CreateTools_RegistersTheRunTool`

Asserts a single tool named `agent_run`, confirming the pack produces the family's tool for
an application to receive.

##### AgentKitTools-Agent-Pack-RequiresPolicy: A Null Policy Throws

**Test**: `AgentPack_CreateTools_NullPolicy_ThrowsArgumentNullException`

Error path: a family created without a policy would hand a delegating agent unrestricted
delegation while appearing correctly composed, so the mistake is reported at the line that
made it.

##### AgentKitTools-Agent-Pack-RejectsMalformedRegistration: Missing Constructor Parts Throw

**Test**: `AgentPack_Constructor_MissingParts_ThrowArgumentNullException`

Error path at construction: a null profiles collection, runner or child packs collection
throws. Each is a programming error in the composing application, reported at the line that
made it.

##### AgentKitTools-Agent-Pack-RejectsMalformedRegistration: A Duplicate Profile Name Is Refused

**Test**: `AgentPack_Constructor_DuplicateProfileName_ThrowsArgumentException`

Error path at construction: two profiles carrying one name would make the model's selection
ambiguous, and the tool would silently always choose the first. Detecting it at construction
reports the mistake at the line that made it.

##### AgentKitTools-Agent-Pack-RejectsMalformedRegistration: A Null Entry in a Collection Is Refused

**Test**: `AgentPack_Constructor_NullEntries_ThrowArgumentException`

Error path at construction: a null profile or a null child pack is a programming error in
the composing application and is refused explicitly rather than surfacing later as a null
reference.

##### AgentKitTools-Agent-Pack-RejectsMalformedRegistration: An AgentPack Listed as a Child Pack Is Refused

**Test**: `AgentPack_Constructor_AgentPackAsChildPack_ThrowsArgumentException`

Error path at construction: the family adds itself to every child composition one level
deeper, so listing it as a child pack as well would publish the `agent` prefix twice.

##### AgentKitTools-Agent-Pack-RejectsMalformedRegistration: Two Child Packs Sharing a Prefix Are Refused

**Test**: `AgentPack_Constructor_DuplicateChildPackPrefix_ThrowsArgumentException`

Error path at construction: two packs claiming one family prefix would publish tools the
child cannot tell apart, on the same basis `ToolPackBuilder.Add` enforces, and is refused at
construction so the mistake is reported when the application registers the packs rather than
the first time a model happens to delegate.

##### AgentKitTools-Agent-Pack-ChildToolsSubset: A Profile Naming an Unattached Tool Does Not Conjure It

**Test**: `AgentPack_CreateTools_ProfileNamingAnUnattachedTool_DoesNotConjureIt`

Security control: a profile whose declared tool names include a name no registered pack
publishes contributes nothing for that name. The profile is a filter over what was
published, never a source of tools.

##### AgentKitTools-Agent-Pack-ChildToolsSubset: The Child's Tools Are a Subset of What Was Attached

**Test**: `AgentPack_CreateTools_ChildTools_AreASubsetOfWhatWasAttached`

Security control and normal operation together: the tools the composer returns to a child
appear both in the profile's declared names and in what the registered packs published,
proving the subset property directly on the composer's output.

##### AgentKitTools-Agent-Pack-ChildToolsComposedAfresh: The Child's Tools Are Composed Afresh per Run

**Test**: `AgentPack_CreateTools_ChildTools_AreComposedAfreshPerRun`

The load-bearing isolation scenario for this unit: the stub records each policy it was
asked to build against, and every tool it publishes carries a fresh identity per
`CreateTools` call. The scenario delegates twice and asserts the stub was consulted twice
with a child policy — never with the parent's tool list, and never producing tools of the
parent's identity. A filter over the parent's list would fail this scenario because the
tools would carry the parent's identity rather than a fresh one.

##### AgentKitTools-Agent-Pack-PolicyNarrowingRejected: A Grant Reaching an Unreachable Location Is Refused

**Test**: `AgentPack_CreateTools_ProfileGrantingAnUnreachableLocation_Throws`

Error path: a profile whose grant names a location the parent's policy does not reach throws
at composition, naming the profile, so a delegating agent cannot acquire reach its operator
did not give it.

##### AgentKitTools-Agent-Pack-PolicyNarrowingRejected: Raising the Access Level Is Refused

**Test**: `AgentPack_CreateTools_ProfileRaisingTheAccessLevel_Throws`

Error path: a profile whose grant states a write where the parent holds only a read throws
at composition, naming the profile. A write over a read-only location is a widening whatever
the location.

##### AgentKitTools-Agent-Pack-PolicyNarrowingRejected: An Unrestricted Grant Under a Rooted Parent Is Refused

**Test**: `AgentPack_CreateTools_ProfileGrantingUnrestrictedReach_Throws`

Error path: a profile whose grant is unrestricted while the parent's grant is rooted throws
at composition, because a rooted parent grant can never contain "everywhere".

##### AgentKitTools-Agent-Pack-PolicyNarrowingRejected: Dropping a Parent Exclusion Is Refused

**Test**: `AgentPack_CreateTools_ProfileDroppingAParentExclusion_Throws`

Error path and the subtle one: a profile whose grant sits at the same location as the
parent's but drops a deny pattern the parent imposes is a strictly wider grant wearing a
narrower shape, and is refused at composition. This is the check that is easy to overlook
and matters most.

##### AgentKitTools-Agent-Pack-PolicyNarrowingAccepted: A Narrowing Profile Reaches the Child

**Test**: `AgentPack_CreateTools_ProfileNarrowingTheGrants_IsAcceptedAndReachesTheChild`

Normal operation: a profile whose grants narrow the parent's policy is accepted, and the
stub's recorded policy shows the child's grants come from the profile rather than from the
parent.

##### AgentKitTools-Agent-Pack-PolicyNarrowingAccepted: A Profile Without Grants Inherits the Parent Policy

**Test**: `AgentPack_CreateTools_ProfileWithoutGrants_InheritsTheParentPolicy`

Normal operation: a profile that states no grants leaves the child on the parent's policy
unchanged, so the common case — most children differ from their parent in what they are
told and which tools they hold, not in where they may work — reaches the child without
restatement.

##### AgentKitTools-Agent-Pack-ChildProfilesFilteredToReach: Delegating to the Narrower of Two Profiles Succeeds

**Test**: `AgentPack_CreateTools_DelegatingToTheNarrowerOfTwoProfiles_Succeeds`

The scenario the defect made unusable: two profiles are registered under a parent that covers
both — a reviewer read-only inside the workspace and an editor read-write over all of it — and
the run delegates to the narrower one. The scenario asserts the call returns rather than
throwing, and that the stub's recorded child policy permits a read inside the reviewer's
directory while refusing one at the editor's wider location, so the fix is not a widening.

##### AgentKitTools-Agent-Pack-ChildProfilesFilteredToReach: A Grandchild Is Composed at Depth

**Test**: `AgentPack_CreateTools_GrandchildDelegation_Succeeds`

Proves the property holds at every depth rather than only for the first child: the reviewer's
profile admits `agent_run`, the scenario invokes the child's own run tool, and asserts a
grandchild request arrives at depth two on a policy that still refuses the editor's location.
Validating only the delegated-to profile would pass the first delegation and fail here.

##### AgentKitTools-Agent-Pack-ChildProfilesFilteredToReach: An Unreachable Sibling Draws the Ordinary Refusal

**Test**: `AgentPack_CreateTools_ChildNamingAnUnreachableSiblingProfile_IsRefused`

Error path: the child names the sibling profile its own policy cannot cover and receives the
unknown-profile refusal naming the profiles it does have. The absent profile is a fact about
the child's own state, stated the way every other refusal in the family is stated, and it is
what confirms the child cannot reach through a sibling to something its parent narrowed away.

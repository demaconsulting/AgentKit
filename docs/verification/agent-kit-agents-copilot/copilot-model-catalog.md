## CopilotModelCatalog Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotModelCatalog` class.

### Verification Approach

`CopilotModelCatalog` is verified at three levels, each reached through the seam that fits it.

**The mapping is verified on hand-built SDK models.** `Map` is an internal pure function, so most
scenarios call it directly on a fixture that mirrors what the service sends for `claude-haiku-5.5`:
vision and a reasoning-effort setting reported, no tool-calling flag, no default effort, no declared
tiers, efforts `low`, `medium` and `high`, a largest window of 1,000,000 tokens and an output limit
of 128,000, and tiers published through billing with prompt budgets of 100,000 and 936,000. Each
scenario changes only the fields under test, so a failure points at one rule.

**The listing and caching are verified through the read seam.** The SDK's model query is a sealed
class with no public constructor, so it cannot be substituted; the catalog's internal constructor
takes the read as a function instead, and the scenarios supply a scripted read that returns
hand-built models and counts its calls.

**The runtime path is verified on a runtime that cannot start.** The disposal and sign-in scenarios
build a real `CopilotRuntime` over a real `CopilotClient` configured with a runtime executable that
does not exist, so the production read is exercised through the real SDK without any process being
started or any credential being used. A client that was never connected is what a user who is not
signed in presents, which is why the sign-in message is reached this way.

**What remains outside automated scope.** No scenario reads the live service, so a change in what
the service reports would not be detected here; the fixture records what it sent when it was
captured.

#### Supplementary Live Evidence

The opt-in live suite `CopilotModelCatalogLiveTests`, in the
`DemaConsulting.AgentKit.Agents.Copilot.LiveTests` project, runs the catalog against the real
service when `AGENTKIT_COPILOT_LIVE_MODEL` names a vision model offering both context tiers. It is
supplementary evidence rather than a linked scenario, because it needs a signed-in account the CI
pipeline does not have. It observed:

- the uncached query returning 24 entries through AgentKit's own runtime, the same count as the
  client's cached listing, so no fallback to the cached listing was needed; the automatic choice
  skipped; the vision filter matching the service's own vision flags; and a refresh reading again;
- for `claude-haiku-5.5`, catalog tier windows of 228,000 (`default`) and 1,000,000
  (`long_context`), while a live session reported a usage token limit of 100,000 in `default` and
  872,000 in `long_context` — the finding recorded under *Context Tiers and the Reported Limit* in
  *CopilotModelCatalog Unit Design*;
- an effort the model does not offer refused by selection before any session existed.

Unit tests reside in `CopilotModelCatalogTests.cs` within the
`DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot runtime is started, **no credential is used**, and no
  network access is made
- **Files**: The runtime scenarios name a runtime executable and a base directory under the system
  temporary folder that are never created
- **Mocking**: Hand-built SDK models and a scripted read through the internal seam; no mocking
  framework
- **Isolation**: Each test builds its own models, runtime and catalog; no state is shared

### Acceptance Criteria

A unit test run passes when all fifteen scenarios below pass without error or exception beyond those
explicitly asserted. Any read of a disposed runtime that does not throw, any failure to list that
does not carry the sign-in message, any refresh that does not read again, any model the service
does not report on treated as unable to call tools, any model reported unable to call tools treated
as usable, any vision flag set without a positive report, any tier window, label or default that
differs from the billing derivation, any declared tier duplicated, any effort renamed, reordered or
given an invented default, any policy-withheld model treated as usable, any service message dropped
or given the wrong kind, any picker metadata dropped, or any listing that includes the automatic
choice or changes the service's order each constitute a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotModelCatalog-ReadsTheServiceThroughTheRuntime: Sign-In Message on Failure

**Test**: `CopilotModelCatalog_ListAsync_ClientNotConnected_FailsWithTheSignInMessage`

Error path through the real SDK. A runtime whose client was never connected fails the listing with
`InvalidOperationException` carrying exactly the sign-in message, with the SDK's own
`InvalidOperationException` kept as the inner exception.

#### AgentKitAgentsCopilot-CopilotModelCatalog-ReadsTheServiceThroughTheRuntime: Disposed Runtime Refused

**Test**: `CopilotModelCatalog_Construct_DisposedRuntime_Throws`

Error path: constructing a catalog over a runtime already disposed throws `ObjectDisposedException`.

#### AgentKitAgentsCopilot-CopilotModelCatalog-ReadsTheServiceThroughTheRuntime: Later Disposal Refused

**Test**: `CopilotModelCatalog_ListAsync_RuntimeDisposedLater_Throws`

Error path. A runtime disposed after the catalog was built is refused with `ObjectDisposedException`
at the next listing — not wrapped as a sign-in failure, and not restarted — which is what reading the
runtime at every read guarantees.

#### AgentKitAgentsCopilot-CopilotModelCatalog-ReadsTheServiceThroughTheRuntime: Refresh Reads Again

**Test**: `CopilotModelCatalog_RefreshAsync_ReadsTheServiceAgain`

Two listings are served by one read, and a refresh makes a second, so a model the user was just
granted can be shown without waiting for the cache to expire.

#### AgentKitAgentsCopilot-CopilotModelCatalog-TreatsUnreportedToolCallingAsSupported: Silence Is Support

**Test**: `CopilotModelCatalog_Map_UnreportedToolCalling_IsSupported`

The documented provider-specific rule: the fixture reports no tool-calling flag, as the service does,
and the model reports tool calling and is usable.

#### AgentKitAgentsCopilot-CopilotModelCatalog-TreatsUnreportedToolCallingAsSupported: Denial Is Unusable

**Test**: `CopilotModelCatalog_Map_ToolCallingDenied_IsUnusable`

Error path. A model the service reports as unable to call tools carries no tool-calling flag and is
unusable with "Model does not support tool calling."

#### AgentKitAgentsCopilot-CopilotModelCatalog-ReportsVisionOnlyWhenTheServiceDoes: Only When Reported

**Test**: `CopilotModelCatalog_Map_Vision_IsSetOnlyWhenReported`

Three cases from one fixture: a vision flag reported true yields vision, while a missing flag and a
flag reported false each yield none, so silence is not vision.

#### AgentKitAgentsCopilot-CopilotModelCatalog-DerivesTiersFromBilling: Derived, Labeled and Capped

**Test**: `CopilotModelCatalog_Map_BillingTiers_AreDerivedLabeledAndCapped`

Normal operation. The fixture yields `default` ("Standard", 100,000 + 128,000 = 228,000) and
`long_context` ("Long context", 936,000 + 128,000 capped at 1,000,000), `default` as the default
tier, the largest window and output limit as reported, and the effort, tier and window adjustments —
with no output-limit adjustment.

#### AgentKitAgentsCopilot-CopilotModelCatalog-DerivesTiersFromBilling: Declared Tiers Are Added Once

**Test**: `CopilotModelCatalog_Map_DeclaredTiers_AreAddedOnce`

A model declaring `default` and `extended` keeps the billing-derived `default` without a duplicate
and adds `extended` after `long_context`, with no published window.

#### AgentKitAgentsCopilot-CopilotModelCatalog-KeepsTheServicesEfforts: Service Identifiers, No Invented Default

**Test**: `CopilotModelCatalog_Map_Efforts_KeepTheServicesIdentifiersAndInventNoDefault`

The efforts `minimal`, `low`, `medium`, `high`, `xhigh` and `max` are kept in the service's order by
its identifiers, mapped to no equivalent, `Low`, `Medium`, `High`, `ExtraHigh` and no equivalent;
no default is reported, and the model reports reasoning.

#### AgentKitAgentsCopilot-CopilotModelCatalog-KeepsTheServicesEfforts: Published Default Reported

**Test**: `CopilotModelCatalog_Map_PublishedDefaultEffort_IsReported`

A default the service names, `medium`, that is among the offered efforts is reported as the default.

#### AgentKitAgentsCopilot-CopilotModelCatalog-UsabilityFollowsPolicy: Policy Decides Usability

**Test**: `CopilotModelCatalog_Map_Policy_DecidesUsability`

A disabled policy yields the organization-policy reason; an unconfigured policy yields the
enable-it reason and carries the service's terms as a notice; a model with no policy is usable.

#### AgentKitAgentsCopilot-CopilotModelCatalog-SurfacesTheServicesNotices: Messages Become Notices

**Test**: `CopilotModelCatalog_Map_Messages_BecomeNotices`

An information message, a warning message and a data-retention text become, in that order, an
`Info` notice and a `Warning` notice with the service's codes, and a `Required` notice with code
`data_retention`.

#### AgentKitAgentsCopilot-CopilotModelCatalog-SurfacesTheServicesNotices: Picker Metadata Is Carried

**Test**: `CopilotModelCatalog_Map_PickerMetadata_IsCarried`

The identifier, display name, vendor, billing multiplier, category, price category and provider name
are carried as the service reported them.

#### AgentKitAgentsCopilot-CopilotModelCatalog-OmitsTheAutomaticChoice: Auto Skipped, Order Kept

**Test**: `CopilotModelCatalog_ListAsync_AutoEntry_IsSkippedAndOrderKept`

A listing whose first entry is `auto` returns the two other models in the service's order, and a
lookup of `auto` returns null.

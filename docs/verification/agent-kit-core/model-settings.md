## ModelSettings Unit Verification Design

This document describes the unit-level verification strategy for the `ModelSettings` record, the
`ModelSelection` class and the `ModelSettingsException` class.

### Verification Approach

Validation is a pure function of the settings, the model and the requirement, so it is verified
directly: each test builds settings, validates or binds them against a hand-built description from
the shared `ModelTestData` helper, and asserts the result. Nothing is mocked.

`ModelTestData` supplies three descriptions chosen to reach every rule: a vision model that accepts
every adjustment, with efforts `low`, `high` and `max` (the last with no provider-neutral
equivalent), tiers `default` at 128,000 tokens and `long_context` at 1,000,000, and an output limit
of 64,000; a text model that accepts only a window, with a 32,768-token maximum; and an unusable
model. A scenario derives a variant with a `with` expression where it needs one, for example a model
with no window adjustment or no known maximum.

**Messages are asserted verbatim.** Each message is written to be shown to a user, so a scenario
asserts the exact text — including the invariant-culture group separators in token counts — rather
than a fragment. A change of wording is a change of user-visible behavior and should fail here.

**One rule per scenario, then all at once.** Each rule is exercised with settings that break only
that rule, asserting a single problem; a separate scenario breaks several at once and asserts that
every one is reported. Window boundaries are tested at the limit and one token past it.

**The selection's construction is asserted reflectively.** That a selection can be obtained only by
validation is a property of the type, so it is asserted by reading the type's public constructors
rather than by attempting a call the compiler would refuse.

Unit tests reside in `ModelSettingsTests.cs`, with the shared descriptions in `ModelTestData.cs`,
within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access or network access required; the JSON
  round trip uses `System.Text.Json` in memory
- **Mocking**: None; descriptions are built by hand
- **Isolation**: Each test builds its own settings and descriptions; no state is shared

### Acceptance Criteria

A unit test run passes when all twenty-one scenarios below pass without error or exception beyond those
explicitly asserted. Any settings constructed or copied without a model, any settings that do not
survive a JSON round trip, any rule that accepts what it should refuse or refuses what it should
accept, any message that differs from the documented text, any problem left unreported when several
apply, any selection created other than by validation, any selection carrying an approximated
neutral effort or the wrong tier window, or any default the provider did not publish each constitute
a failure.

### Test Scenarios

#### AgentKitCore-ModelSettings-Persistable: Settings Must Name a Model

**Test**: `ModelSettings_Construct_BlankModelId_Throws`

Error path for rule V1. A blank identifier is refused with "A model id is required." at
construction, and an empty one is refused through a `with` expression, so the invariant holds for
copies too.

#### AgentKitCore-ModelSettings-Persistable: Settings Survive a JSON Round Trip

**Test**: `ModelSettings_Serialize_RoundTrips`

Normal operation. Settings with an effort and a window serialize and deserialize to an equal value,
which is how an application persists them.

#### AgentKitCore-ModelSettings-NamesTheModelItIsCheckedAgainst: Another Model Is Refused

**Test**: `ModelSettings_Validate_DifferentModel_IsRefused`

Error path for rule V2: settings naming `other` checked against `vision-large` produce exactly the
mismatch message.

#### AgentKitCore-ModelSettings-RefusesUnusableOrIncapableModels: An Unusable Model Is Refused With Its Reason

**Test**: `ModelSettings_Validate_UnusableModel_IsRefusedWithItsReason`

Error path for rule V3: the unusable model is refused with the provider's own reason appended.

#### AgentKitCore-ModelSettings-RefusesUnusableOrIncapableModels: A Missing Capability Is Named

**Test**: `ModelSettings_Validate_MissingRequiredCapability_IsRefused`

Error path for rule V4. The text model, required to support vision and tools, is refused naming only
vision — the capability it lacks — and not tool calling, which it has.

#### AgentKitCore-ModelSettings-RefusesUnofferedEffortsAndTiers: An Unoffered Effort Lists the Supported Ones

**Test**: `ModelSettings_Validate_UnofferedEffort_IsRefusedListingTheSupportedOnes`

Error path for rule V5: `medium` on the vision model is refused, listing `low, high, max` in the
model's order.

#### AgentKitCore-ModelSettings-RefusesUnofferedEffortsAndTiers: An Effort on a Model Without Efforts

**Test**: `ModelSettings_Validate_EffortOnModelWithoutEfforts_IsRefused`

Error path for rule V5: any effort on the text model, which accepts none, is refused as unsupported.

#### AgentKitCore-ModelSettings-RefusesUnofferedEffortsAndTiers: An Unoffered Tier Is Refused

**Test**: `ModelSettings_Validate_UnofferedTier_IsRefused`

Error path for rule V6, both branches: an unknown tier on the vision model is refused listing
`default, long_context`, and any tier on the text model is refused because it offers none.

#### AgentKitCore-ModelSettings-HoldsTheWindowToTheModel: A Non-Positive Window Is Refused

**Test**: `ModelSettings_Validate_NonPositiveWindow_IsRefused`

Boundary condition for rule V7: a window of zero is refused as not a positive number of tokens.

#### AgentKitCore-ModelSettings-HoldsTheWindowToTheModel: A Window on a Model That Does Not Accept One

**Test**: `ModelSettings_Validate_WindowOnModelWithoutWindowSetting_IsRefused`

Error path for rule V7: a window on a model without the window adjustment is refused.

#### AgentKitCore-ModelSettings-HoldsTheWindowToTheModel: A Window Above the Model's Maximum

**Test**: `ModelSettings_Validate_WindowAboveModelMaximum_IsRefused`

Boundary condition for rule V9. On the text model, which offers no tiers, 32,769 tokens is refused
with the formatted maximum, and 32,768 — the maximum itself — is accepted.

#### AgentKitCore-ModelSettings-HoldsTheWindowToTheModel: A Window Above the Tier's Window

**Test**: `ModelSettings_Validate_WindowAboveTierWindow_IsRefused`

Rule V8. A 200,000-token window with no tier chosen is held to the default tier's 128,000 and
refused naming that tier; the same window with `long_context` chosen is accepted.

#### AgentKitCore-ModelSettings-HoldsTheWindowToTheModel: A Chosen Tier Without a Window

**Test**: `ModelSettings_Validate_ChosenTierWithoutWindow_IsHeldToTheDefaultTier`

Boundary condition: a chosen tier that publishes no window gives way to the default tier's, so a
200,000-token window is refused naming the default tier rather than checked against the model's
largest window.

#### AgentKitCore-ModelSettings-HoldsTheWindowToTheModel: An Unknown Maximum Is Not Enforced

**Test**: `ModelSettings_Validate_UnknownMaximum_IsNotEnforced`

Boundary condition: with neither a tier nor a model maximum known, a 10,000,000-token window is
accepted, because nothing could be said about the limit.

#### AgentKitCore-ModelSettings-HoldsTheOutputLimitToTheModel: The Output Limit Is Checked Against the Model

**Test**: `ModelSettings_Validate_MaxOutput_IsCheckedAgainstTheModel`

Rule V10, all three branches: a limit on a model that does not accept one, a negative limit, and
64,001 against a 64,000 maximum are each refused with their own message.

#### AgentKitCore-ModelSettings-ReportsEveryProblem: Several Problems Are All Reported

**Test**: `ModelSettings_Validate_SeveralProblems_AreAllReported`

Settings that break five rules at once — an unusable model, a missing capability, an effort, a tier
and a negative window — produce five problems rather than only the first.

#### AgentKitCore-ModelSettings-ReportsEveryProblem: Binding Invalid Settings Throws With Every Problem

**Test**: `ModelSettings_Bind_InvalidSettings_ThrowsWithEveryProblem`

Error path. Binding settings with two problems throws `ModelSettingsException` naming the model,
carrying both problems, with a message beginning "Model settings for 'text-small' are not valid: ".

#### AgentKitCore-ModelSettings-OnlyValidatedSelectionsExist: Valid Settings Yield a Selection

**Test**: `ModelSettings_Bind_ValidSettings_YieldsSelection`

Normal operation. Settings naming `HIGH` — matched ignoring case — and `long_context` bind to a
selection holding the same settings instance, the neutral `High` effort, the 1,000,000-token tier
window and the vision host capability.

#### AgentKitCore-ModelSettings-OnlyValidatedSelectionsExist: A Provider-Only Effort Has No Neutral Equivalent

**Test**: `ModelSettings_Bind_ProviderOnlyEffort_HasNoStandardEquivalent`

Choosing `max` binds with no neutral effort rather than an approximation, and with no tier chosen the
selection reports the default tier's 128,000-token window.

#### AgentKitCore-ModelSettings-OnlyValidatedSelectionsExist: A Selection Has No Public Constructor

**Test**: `ModelSettings_ModelSelection_HasNoPublicConstructor`

Reads the public instance constructors of `ModelSelection` reflectively and asserts there are none,
so validation is the only way to obtain one.

#### AgentKitCore-ModelSettings-DefaultsInventNothing: Defaults Carry Only What Was Published

**Test**: `ModelSettings_DefaultsFor_CarriesPublishedDefaultsOnly`

A model publishing a default effort and tier yields settings carrying both and no window; a model
publishing no default effort yields settings with none, and those settings validate cleanly.

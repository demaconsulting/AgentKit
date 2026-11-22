## ModelCatalog Unit Verification Design

This document describes the unit-level verification strategy for the `IModelCatalog` contract, the
`ModelQuery` record, the `CachingModelCatalog` base class and the
`ModelCatalogExtensions.SelectAsync` session-start gate.

### Verification Approach

`CachingModelCatalog` is abstract, so it is exercised through `ScriptedModelCatalog`, a test
subclass whose read is scripted by each test and counted. The count is what makes caching
observable from outside: a scenario asserts how many times the provider would have been read, not
merely what was returned. The scripted read is handed the one-based number of the read being made,
so a test can fail the first read and succeed the second, or report a model only after a refresh.

**Time is moved by hand.** The catalog's clock is injected as `ManualTimeProvider`, which a test
advances past the lifetime, so expiry is exercised without waiting and without a timing race.

**Concurrency is controlled rather than hoped for.** The single-flight scenario holds the read open
on a completion source until twenty callers have arrived, then releases it. The cancellation
scenario holds the first read on its own token, so the owner's cancellation and the waiter's
continuation happen in a fixed order. The timeout scenario holds the first read on a completion
source until three waiters have joined, then fails it with a `TaskCanceledException` no caller's
token raised. None depends on scheduling luck.

**The gate is exercised against the real validation.** `SelectAsync` is called on scripted catalogs
holding hand-built descriptions from `ModelTestData`, so the refusals it reports come from
`ModelSettings` exactly as an application would see them. A second test subclass compares
identifiers ignoring case, standing in for a provider that accepts more than one spelling of a
model's name, so the gate's use of the catalog's own spelling can be observed without a provider.

Unit tests reside in `ModelCatalogTests.cs`, with `ScriptedModelCatalog`, `ManualTimeProvider` and
the shared descriptions in `ModelTestData.cs`, within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access or network access required
- **Mocking**: Hand-written test subclasses of `CachingModelCatalog` and a hand-moved clock; no
  mocking framework
- **Isolation**: Each test constructs its own catalog and clock; no state is shared

### Acceptance Criteria

A unit test run passes when all sixteen scenarios below pass without error or exception beyond
those explicitly asserted. Any read made before the catalog is asked, any second read within the
lifetime, any listing served after the lifetime without a new read, any concurrent callers starting
more than one read, any failure cached, any waiter inheriting another caller's cancellation, any
provider timeout that starts a second read or fails to reach a waiter, any refresh that does not
read again, any listing that is re-ordered, includes an unusable model
unasked, or omits a model satisfying the query, any lookup that cannot find an unusable model, or
any selection made for a withdrawn model, a model missing a required capability, or under a
spelling other than the catalog's each constitute a failure.

### Test Scenarios

#### AgentKitCore-ModelCatalog-FiltersByRequirement: A Query Filters by Requirement and Usability

**Test**: `ModelCatalog_ListAsync_Query_FiltersByRequirementAndUsability`

Normal operation. Over a text model, a vision model and an unusable model, the default listing omits
the unusable one, a vision-and-tools query returns only the vision model, and a query including
unusable models returns all three in order.

#### AgentKitCore-ModelCatalog-FiltersByRequirement: The Provider's Order Is Preserved

**Test**: `ModelCatalog_ListAsync_ProviderOrder_IsPreserved`

Models listed as `zeta`, `alpha`, `mu` — an order no sort would produce — are returned in that
order, confirming the catalog keeps the provider's ranking.

#### AgentKitCore-ModelCatalog-FiltersByRequirement: A Lookup Finds an Unusable Model

**Test**: `ModelCatalog_GetAsync_UnusableModel_IsFound`

A lookup returns an unusable model, so validation can say why it cannot be used, and returns null
for a model the provider does not offer.

#### AgentKitCore-ModelCatalog-CachesForALifetime: Nothing Is Read Until Asked

**Test**: `ModelCatalog_Construct_ReadsNothingUntilAsked`

Constructing a catalog makes no read, and the provider name is reported as given.

#### AgentKitCore-ModelCatalog-CachesForALifetime: Queries Within the Lifetime Read Once

**Test**: `ModelCatalog_ListAsync_WithinLifetime_ReadsOnce`

Two listings with different queries and a lookup, spread over four minutes, are served by a single
read.

#### AgentKitCore-ModelCatalog-CachesForALifetime: A Listing Past the Lifetime Is Read Again

**Test**: `ModelCatalog_ListAsync_AfterLifetime_ReadsAgain`

Boundary condition. With the clock advanced one millisecond past `DefaultLifetime`, the next listing
reads again; the scenario also pins `DefaultLifetime` at five minutes.

#### AgentKitCore-ModelCatalog-CachesForALifetime: A Non-Positive Lifetime Is Refused

**Test**: `ModelCatalog_Construct_NonPositiveLifetime_Throws`

Error path: a zero lifetime is refused with `ArgumentOutOfRangeException` at construction rather
than producing a catalog that reads on every query.

#### AgentKitCore-ModelCatalog-SharesOneRead: Concurrent Callers Share One Read

**Test**: `ModelCatalog_ListAsync_ConcurrentCallers_ShareOneRead`

Twenty callers arrive while one read is held open; when it is released, every caller receives the
listing and the provider was read once.

#### AgentKitCore-ModelCatalog-DoesNotCacheFailures: A Failed Read Is Not Cached

**Test**: `ModelCatalog_ListAsync_ReadFails_IsNotCachedAndNextQueryRetries`

Error path. The first read fails and its exception reaches the caller unchanged; the next query
reads again and succeeds, so the provider was read twice.

#### AgentKitCore-ModelCatalog-DoesNotCacheFailures: A Waiter Reads Again When the Reader Cancels

**Test**: `ModelCatalog_ListAsync_ReadingCallerCancels_WaiterReadsForItself`

Error path on cancellation. A first caller starts a read, a second joins it, and the first cancels.
The first caller's call is canceled; the second, whose own token was not canceled, receives a
listing from a read of its own, so the provider was read twice.

#### AgentKitCore-ModelCatalog-DoesNotCacheFailures: A Provider Timeout Reaches Every Waiter From One Read

**Test**: `ModelCatalog_ListAsync_ProviderTimeout_ReachesEveryWaiterWithOneRead`

Error path on a provider's own cancellation. A first caller starts a read and three more join it;
the read then fails with a `TaskCanceledException` — as an HTTP client's timeout does — while no
caller's token is canceled. The reading caller and all three waiters receive that exception, the
provider was read once, and the next query reads again and succeeds, so the timeout was a failure
rather than a cancellation and was not cached.

#### AgentKitCore-ModelCatalog-RefreshesOnRequest: A Refresh Reads Again Within the Lifetime

**Test**: `ModelCatalog_RefreshAsync_WithinLifetime_ReadsAgain`

A refresh made well within the lifetime reads again, and the next listing shows the model the second
read added.

#### AgentKitCore-ModelCatalog-GatesSessionStart: A Withdrawn Model Is Refused

**Test**: `ModelCatalog_SelectAsync_UnknownModel_IsRefusedAsNotAvailable`

Error path. Selecting a model the provider does not offer throws `ModelSettingsException` naming the
model and carrying exactly one problem, the not-available message naming the provider.

#### AgentKitCore-ModelCatalog-GatesSessionStart: A Model That Lost a Required Capability Is Refused

**Test**: `ModelCatalog_SelectAsync_ModelLostARequiredCapability_IsRefused`

Error path. A persisted choice of a model the provider now reports without vision is refused when
the application requires vision, so the requirement is re-enforced at every session start.

#### AgentKitCore-ModelCatalog-GatesSessionStart: Valid Settings Produce a Selection

**Test**: `ModelCatalog_SelectAsync_ValidSettings_ProducesSelection`

Normal operation. Valid settings yield a selection carrying the model, the requirement and the vision
host capability.

#### AgentKitCore-ModelCatalog-GatesSessionStart: The Catalog's Own Spelling Is Selected

**Test**: `ModelCatalog_SelectAsync_AlternateSpelling_SelectsTheCatalogsIdentifier`

A catalog that matches identifiers ignoring case finds `Vision-Large` for settings naming
`vision-large`, and the selection's settings name `Vision-Large`, so the provider is asked for
exactly the model that was validated.

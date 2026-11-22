## ModelCatalog

![AgentKit Core Structure](AgentKitCoreView.svg)

The `IModelCatalog` interface defines what a model catalog is, `ModelQuery` says which models a
listing returns, `CachingModelCatalog` supplies the lazy, cached, single-flight reading every
provider catalog shares, and `ModelCatalogExtensions.SelectAsync` is the session-start gate that
looks a persisted choice up and validates it in one call.

### Purpose

An application that lets a user choose a model needs three things from a provider: the list of
models to show, a way to find the one a saved choice names, and a check, every time a session
starts, that the saved choice still holds. This unit supplies all three once, so a provider package
writes only the part that is its own — how to read the provider and how to describe one model.

**Read lazily, cached briefly.** A picker lists models far more often than a provider's offering
changes, and a listing is a network call some providers rate-limit. Nothing is read until the first
listing or lookup; the listing is then kept for `DefaultLifetime`, five minutes, unless another
lifetime is given. Five minutes is long enough that one interaction never waits on a provider
twice, and short enough that a model enabled or withdrawn appears or disappears without a restart.
`RefreshAsync` reads again at once, for the user who has just been granted a model or pulled one.

**One read at a time, shared.** A user interface commonly asks for the list from several places at
once when it opens. Callers that arrive while a read is in flight wait for it rather than starting
their own, so the cost the cache exists to save is not multiplied exactly when it matters.

**Failures are not cached.** A provider that is briefly unreachable, or a user not yet signed in,
must not leave the catalog empty for five minutes after the cause is fixed. A failed read reaches
every caller waiting on it, and the next query reads again.

**One read serves every filter.** A query is applied to the cached listing when it is read, so
filtering one list many ways costs one read. The provider's order is preserved, because it is the
provider's own ranking. Unusable models are hidden from a listing by default — a picker should
offer only what will run — but a lookup still finds them, so validation can say why a persisted
choice no longer works rather than only that it is missing.

**The gate re-enforces the requirement.** An application that lists only vision models states the
requirement on the query, and states it again to `SelectAsync` when a session starts. A model that
lost the capability between the two is then refused rather than run; see *ModelSettings Unit
Design* for the rules `SelectAsync` applies.

### Data Model

`ModelQuery` is an immutable record:

| Member | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Require` | `ModelCapabilities` | `None` | Capabilities a model must report to be listed. |
| `IncludeUnusable` | `bool` | `false` | Whether models AgentKit cannot run are listed too. |

`IModelCatalog` holds no state; it states what an implementation must provide:

| Member | Returns | Contract |
| --- | --- | --- |
| `ProviderName` | `string` | The provider, as `ModelInfo.Provider` names it. |
| `ListAsync(query, token)` | `IReadOnlyList<ModelInfo>` | Matching models, in the provider's order; never null. |
| `GetAsync(modelId, token)` | `ModelInfo?` | The model, usable or not; null when not offered. |
| `RefreshAsync(token)` | `Task` | Discards what was cached and reads the provider again. |

`CachingModelCatalog` is the abstract base implementing it:

- **`DefaultLifetime`** (`static readonly TimeSpan`, five minutes) — how long a listing is kept when
  no lifetime is given. Published so a caller can reason about expiry without reading the source.
- **`ProviderName`** (`string`) — set at construction; never null or blank.
- **`_gate`** (`object`) — the lock guarding `_snapshot` and `_inFlight`. A lock rather than a
  semaphore, so the class owns nothing disposable and a subclass need not be disposed.
- **`_lifetime`** (`TimeSpan`) — positive; the supplied lifetime or `DefaultLifetime`.
- **`_timeProvider`** (`TimeProvider`) — the clock a listing's age is measured against; the system
  clock unless one is injected, so expiry can be exercised without waiting.
- **`_snapshot`** (`Snapshot?`) — the listing most recently read and when; null before the first
  read and after a refresh discarded it. A snapshot holds a private array copy of what the provider
  returned, so a subclass may return a list it reuses.
- **`_inFlight`** (`Task<Snapshot>?`) — the read in flight, which arriving callers join; null when
  none is. Backed by a `TaskCompletionSource` the reading caller owns, created with asynchronous
  continuations so completing it never runs a waiter's code on the reader's stack.

Invariants: a snapshot is fresh while `now - ReadAt < lifetime`; at most one read is in flight; a
cached snapshot never holds a null model; `_inFlight` is cleared before the waiters on it are
released, so a waiter that reads again starts a new read rather than re-joining a finished one.

`CachingModelCatalog` is safe for concurrent use.

### Key Methods

#### ModelQuery.Matches(ModelInfo model)

**Purpose:** Test whether a model is listed under the query.

**Algorithm:** `(IncludeUnusable || model.IsUsable) && model.Supports(Require)`.

**Preconditions:** `model` is not null.

#### CachingModelCatalog(string providerName, TimeSpan? lifetime, TimeProvider? timeProvider)

**Purpose:** Record the provider name, the lifetime and the clock. Reads nothing.

**Preconditions:** `providerName` is not null or blank; `lifetime`, when supplied, is positive.

**Postconditions:** No read has been made; the first listing or lookup makes one.

#### ListAsync(ModelQuery? query, CancellationToken cancellationToken)

**Purpose:** List the models that satisfy a query.

**Algorithm:** Obtain the listing through `GetModelsAsync`, then return a new list of the models
that `query` — or a default query, meaning every usable model — matches, in the listing's order.

**Postconditions:** Never null. Performs network I/O only when nothing fresh is cached.

#### GetAsync(string modelId, CancellationToken cancellationToken)

**Purpose:** Find one model by identifier, whether or not it is usable.

**Algorithm:** Reject a null or blank identifier, obtain the listing, and return the first model for
which `IdEquals(modelId, model.Id)` holds, or null.

#### RefreshAsync(CancellationToken cancellationToken)

**Purpose:** Read the provider again now, within the lifetime.

**Algorithm:** Discard the snapshot under the lock, then obtain the listing as `ListAsync` would. A
read already in flight is joined rather than duplicated. Discarding first means a refresh that fails
leaves nothing stale behind, and the next query reads again.

#### FetchAsync(CancellationToken cancellationToken)

**Purpose:** The abstract read a provider package implements.

**Contract:** Called by this class only, never concurrently with itself on one instance, and only
when nothing fresh is cached. Returns the models in the order a picker should show them.

#### IdEquals(string requested, string catalogId)

**Purpose:** Test whether a requested identifier names a listed model.

**Algorithm:** Ordinal comparison by default. A provider that accepts more than one spelling of a
model's name overrides it; Ollama resolves a bare name to its `latest` tag, see *OllamaModelCatalog
Unit Design*.

#### GetModelsAsync(bool refresh, CancellationToken cancellationToken)

**Purpose:** Return the cached listing when it is fresh, and otherwise read it, sharing one read
among every caller that arrives while it is in flight.

**Algorithm:**

1. Throw if the caller's token is already canceled.
2. When refreshing, discard the snapshot under the lock.
3. Under the lock, decide one of three outcomes: a fresh snapshot answers at once; with no read in
   flight, this caller creates the completion source and owns the read; otherwise this caller joins
   the read in flight.
4. An owner performs the read through `ReadAsync` with its own token.
5. A joiner waits on the shared task with its own token, so canceling stops only its own wait. If
   the joined read was canceled — which happens only when its owner's own token was canceled — while
   the joiner's token was not, that cancellation was not the joiner's: it returns to step 3 and reads
   again for itself. A read that failed, a provider timeout included, is faulted and its exception
   propagates to the joiner.

#### ReadAsync(TaskCompletionSource&lt;Snapshot&gt; completion, CancellationToken cancellationToken)

**Purpose:** Perform one read on behalf of every caller waiting on it, and publish the outcome.

**Algorithm:** Call `FetchAsync`. Refuse a null listing and a listing holding a null model. Copy the listing into an
array, stamp it with the clock, and under the lock store it as the snapshot and clear `_inFlight`; then complete the
shared task. On any failure, clear `_inFlight` under the lock, complete the shared task as canceled when the owner's own
token was canceled, and faulted otherwise — a cancellation the provider raised on its own, such as an HTTP client's
timeout, is a failure of the read and faults it — and rethrow to the owner. A fault is observed at once so a failure no
other caller waited on is not reported later as an unobserved task exception.

#### SelectAsync(IModelCatalog, ModelSettings, ModelCapabilities, CancellationToken)

**Purpose:** The session-start gate: find the model the settings name and validate the settings
against it, producing the `ModelSelection` a provider factory accepts.

**Algorithm:** Reject a null catalog or settings. Look the model up with `GetAsync`. When the
provider does not offer it, throw a `ModelSettingsException` carrying the single problem
"Model '{id}' is not available from provider '{provider}'. It may have been removed or you may no
longer have access." Otherwise, when the catalog matched a spelling other than its own, copy the
settings with the catalog's identifier, so the provider is asked for exactly the model that was
validated — a bare Ollama name chosen as `qwen3` is selected as `qwen3:latest`. Then bind the
settings to the model under the requirement; see *ModelSettings Unit Design*.

**Postconditions:** A selection whose settings name the model by the catalog's identifier, or an
exception listing every problem.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `providerName`, `modelId`, `catalog`, `settings`, or model passed to `Matches` | `ArgumentNullException` |
| Blank `providerName` or `modelId` | `ArgumentException` propagates |
| Zero or negative lifetime | `ArgumentOutOfRangeException` ("A catalog lifetime must be positive.") |
| The provider's read throws | Propagates to the owner and every waiter; not cached |
| The read returns no listing | `InvalidOperationException` ("The {provider} catalog read returned no listing.") |
| The listing holds a null model | `InvalidOperationException` ("The {provider} catalog read returned a null model.") |
| The caller's token is canceled | `OperationCanceledException`; only that caller's wait stops |
| The owner of a joined read cancels its own token | The waiter reads again for itself |
| The provider cancels the read (e.g. a client timeout) | A failure for the owner and every waiter; not cached |
| The provider does not offer the selected model | `ModelSettingsException` with the not-available problem |
| The settings are not valid for the model | `ModelSettingsException` from `Bind`, listing every problem |

The dividing line: a malformed argument is a defect in the caller and is refused at once; a
provider that could not be read is a condition of the environment, reported to whoever asked and
forgotten, so the next query tries again; a persisted choice that no longer fits the provider is the
user's to correct, so it is reported with every reason, each suitable to show verbatim.

### Dependencies

- **ModelInfo** — supplies `ModelInfo` and `ModelCapabilities`; see *ModelInfo Unit Design*.
- **ModelSettings** — supplies `ModelSettings`, `ModelSelection` and `ModelSettingsException`, which
  `SelectAsync` uses; see *ModelSettings Unit Design*.
- **.NET base class library** — supplies `TimeProvider` and `TaskCompletionSource`.

### Callers

- **OllamaModelCatalog** and **CopilotModelCatalog** derive from `CachingModelCatalog`; see their
  unit designs.
- **Applications** list models for a picker through `ListAsync`, find a saved choice with
  `GetAsync`, refresh on request, and call `SelectAsync` every time a session starts from persisted
  settings. The document-assistant sample is the worked example.
- A package author implements `IModelCatalog` directly, or derives from `CachingModelCatalog`, to
  publish a catalog for another provider.

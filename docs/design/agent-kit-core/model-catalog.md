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
lifetime is given, and a listing as old as the lifetime is read again on the next query. Five minutes
is long enough that one interaction never waits on a provider twice, and short enough that a model
enabled or withdrawn appears or disappears without a restart.
`RefreshAsync` reads again at once, for the user who has just been granted a model or pulled one.

**One read at a time, shared.** A user interface commonly asks for the list from several places at
once when it opens. Callers that arrive while a read is in flight wait for it rather than starting
their own, so the cost the cache exists to save is not multiplied exactly when it matters. A caller
that arrives as a read's outcome is being reported receives that outcome too; only a caller after it
starts a new read.

**Failures are not cached.** A provider that is briefly unreachable, or a user not yet signed in,
must not leave the catalog empty for five minutes after the cause is fixed. A failed read reaches
every caller waiting on it, and the next query after it reads again.

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
- **`OnReadJoined`** (`Action?`, internal) — a test seam invoked on the joining caller's thread,
  outside the lock, each time a caller joins a read in flight. Null outside tests.
- **`OnReadPublished`** (`Action?`, internal) — a test seam invoked on the reading caller's thread,
  outside the lock, at the point where a read's outcome is settled but its waiters may not have seen
  it: after a failure is published and before the read is released, or after a listing is stored and
  the read released and before the waiters are released. Null outside tests. Both seams let a test wait for
  an exact moment instead of a delay; neither is part of the public or protected surface.

Invariants:

- A snapshot is fresh while `now - ReadAt < lifetime`, so a listing as old as the lifetime is read
  again.
- At most one read is in flight, and a cached snapshot never holds a null model.
- A failed read's outcome is published on the shared task before the read is released, so a failed
  read stays joinable until then and a caller that meets it receives its failure.
- A read whose owner canceled it is treated as absent: a caller that meets it before the owner has
  released it clears it and starts a new read, rather than joining a read that is already over.
- Release is compare-and-clear: the owner clears `_inFlight` only when it still holds the owner's
  read, so it never clears a newer read that replaced a canceled one.
- A successful read is stored and released in one lock section before it is published, so from the
  release on a listing or lookup is served the stored listing without a read. Only a refresh, or a
  listing that has expired by then, reads again, because the first read is already over.

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

**Algorithm:** Obtain the listing through `GetModelsAsync`, then return a new read-only list of the models
that `query` — or a default query, meaning every usable model — matches, in the listing's order.

**Postconditions:** Never null, and read-only: the result wraps a fresh array through `Array.AsReadOnly`,
so a caller can neither cast it back to a mutable list nor alter the cached snapshot. Performs
network I/O only when nothing fresh is cached.

#### GetAsync(string modelId, CancellationToken cancellationToken)

**Purpose:** Find one model by identifier, whether or not it is usable.

**Algorithm:** Reject a null or blank identifier, obtain the listing, and return the first model for
which `IdEquals(modelId, model.Id)` holds, or null.

#### RefreshAsync(CancellationToken cancellationToken)

**Purpose:** Read the provider again now, within the lifetime.

**Algorithm:** Discard the snapshot under the lock, then obtain the listing as `ListAsync` would. A
read already in flight is joined rather than duplicated, so a refresh that arrives as a read fails
receives that failure. A refresh that arrives once a successful read has been stored reads again,
since that read has finished. Discarding first means a refresh that fails leaves nothing stale behind,
and the next query reads again.

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
   the read in flight. A read in flight that is already canceled is first cleared and treated as
   absent, so this caller owns a new read; a read in flight that has failed but is not yet released is
   joined, so this caller receives its failure.
4. An owner performs the read through `ReadAsync` with its own token.
5. A joiner invokes `OnReadJoined`, then waits on the shared task with its own token, so canceling
   stops only its own wait. If
   the joined read was canceled — which happens only when its owner's own token was canceled — while
   the joiner's token was not, that cancellation was not the joiner's: it returns to step 3 and reads
   again for itself. A read that failed, a provider timeout included, is faulted and its exception
   propagates to the joiner.

#### ReadAsync(TaskCompletionSource&lt;Snapshot&gt; completion, CancellationToken cancellationToken)

**Purpose:** Perform one read on behalf of every caller waiting on it, and publish the outcome.

**Algorithm:** Call `FetchAsync`. Refuse a null listing and a listing holding a null model.

- **On success:** copy the listing into an array, stamp it with the clock, and in one lock section store
  it as the snapshot and release the read. Then invoke `OnReadPublished` and complete the shared task
  with the snapshot.
- **On any failure:** first complete the shared task — as canceled when the owner's own token was
  canceled, and faulted otherwise. A cancellation the provider raised on its own, such as an HTTP
  client's timeout, is a failure of the read and faults it. A fault is observed at once so a failure no
  other caller waited on is not reported later as an unobserved task exception. Then invoke
  `OnReadPublished`, release the read under the lock, and rethrow to the owner.

Release clears `_inFlight` only when it still holds this read. The seam is invoked outside the lock
and inside a `try`/`finally` that releases the read or completes the task, so a seam that throws cannot
leave a finished read in flight. The two paths differ in order on purpose. A failure is published
before release so a caller arriving in between joins the read and receives its failure instead of
starting a second read. A success is released before it is published: once it is stored, an arriving
caller is served it without a read, and a refresh arriving then reads again rather than joining a read
that has finished.

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
| The provider's read throws | The provider's own exception (e.g. `HttpRequestException`) to all; not cached |
| The read returns no listing | `InvalidOperationException` ("The {provider} catalog read returned no listing.") |
| The listing holds a null model | `InvalidOperationException` ("The {provider} catalog read returned a null model.") |
| The caller's token is canceled | `OperationCanceledException`; only that caller's wait stops |
| The owner of a joined read cancels its own token | The waiter reads again for itself |
| The provider cancels the read (e.g. a client timeout) | `OperationCanceledException` to all; not cached |
| The provider does not offer the selected model | `ModelSettingsException` with the not-available problem |
| The settings are not valid for the model | `ModelSettingsException` from `Bind`, listing every problem |

"To all" means the owner of the read and every caller that joins it before it is released. A
provider's own cancellation reaches them as `OperationCanceledException` although no caller's token
was canceled, so a caller tells a timeout from its own cancellation by its token's
`IsCancellationRequested`.

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

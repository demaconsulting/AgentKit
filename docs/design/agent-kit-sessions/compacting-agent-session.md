## CompactingAgentSession

### Purpose

`CompactingAgentSession` is the concrete `IAgentSession` implementation. It owns the live provider
session, the out-of-session layout, usage state, rotation counters and adaptive compaction level, and
it sequences the safe replacement of provider sessions.

### Data Model

Private state:

- **`_options`** (`AgentSessionOptions`) — Configuration and summarizer.
- **`_factory`** (`IProviderSessionFactory`) — Creates initial and replacement provider sessions.
- **`_live`** (`LiveProviderSession`) — Provider session paired with its release state.
- **`_disposed`** (`bool`) — Prevents further sends after disposal begins.
- **`_turnsSinceRotation`** (`int`) — Hysteresis clock.

Public and internal state:

- **`Layout`** (`ContextLayout`) — Internal context shape: fixed overhead, tiers and verbatim tail.
- **`Usage`** (`ContextUsage`) — Latest usage reading.
- **`RotationCount`** (`int`) — Completed replacements.
- **`Level`** (`CompactionLevel`) — Current adaptive compaction level, initially Low.
- **`ConsolidationCount`** (`int`) — Total summarizer calls across completed rotations.

Derived hysteresis windows:

- **`K`** — `VerbatimTurns`; a second rotation within this many turns escalates.
- **`M`** — `2 * VerbatimTurns`; a quiet stretch this long allows relaxation. `M` is greater than
  `K`, so escalation and relaxation cannot both apply to one rotation.

`LiveProviderSession` pairs an `IProviderSession` with a `Released` flag. The flag is set only after
the provider's own disposal completes, which keeps disposal retryable.

### Key Methods

#### CreateAsync(AgentSessionOptions options, IProviderSessionFactory providerSessionFactory, CancellationToken cancellationToken)

**Purpose:** Create the compacting session and its first provider session.

**Algorithm:** Validate inputs, create an empty `ProviderSessionSeed`, ask the factory for a provider
session, and construct `CompactingAgentSession`. Construction creates an empty layout, adopts the
provider and reads usage. If construction fails after the provider exists, release the unowned
provider session; if that release fails, throw `AgentSessionCreationException` carrying
`RetainedProviderSession`.

**Preconditions:** `options` and `providerSessionFactory` are not null; the factory returns a non-null
provider session.

**Postconditions:** On success, the session owns the live provider session and reports initial usage.
On failure, no provider session is silently orphaned.

#### SendAsync(string message, CancellationToken cancellationToken)

**Purpose:** Answer one message and rotate afterward when occupancy reaches the threshold.

**Algorithm:**

1. Reject sends after disposal and blank messages.
2. Send the message to the live provider session.
3. Record the user message and provider turn entries as one whole turn.
4. Increment the hysteresis clock.
5. Read provider usage if available, otherwise estimate from the layout.
6. Compare conversation occupancy with a threshold computed in the same currency.
7. If below threshold, return the answer with current level and no dropped material.
8. If at or above threshold, rotate and return the answer with updated level, rotation flag and
   dropped-material flag.

**Preconditions:** `message` is not null, empty or blank; the session is not disposed.

**Postconditions:** A provider-accepted turn is recorded exactly once. Rotation, when completed,
prepares the replacement for the next turn; the current answer is produced before rotation.

#### RotateAsync(CancellationToken cancellationToken)

**Purpose:** Age the layout and replace the live provider session safely.

**Algorithm:** Compute the starting level from hysteresis, call `RotationEngine.RotateAsync`, relax the
settled level after a long quiet stretch when appropriate, and skip replacement if the engine did no
consolidation and dropped nothing. Otherwise build a seed, create a replacement, read replacement
usage before adoption, adopt the replacement, update layout, counters, usage, level and hysteresis
clock in one no-await block, then attempt to dispose the superseded provider session.

**Preconditions:** The current usage has reached the rotation threshold.

**Postconditions:** On success, state describes the replacement session. If consolidation or
replacement creation fails before adoption, the old provider session remains live and coherent.

#### DisposeAsync()

**Purpose:** Release the currently live provider session.

**Algorithm:** Mark the session disposed and call `_live.ReleaseAsync()`.

**Preconditions:** None.

**Postconditions:** When provider disposal succeeds, the live provider session is released. If
disposal fails, the exception propagates and a later disposal call retries the same provider release.

#### ReadUsage(IProviderSession provider, AgentSessionOptions options, ContextLayout layout)

**Purpose:** Choose provider-reported usage when present and estimate otherwise.

**Algorithm:** If the provider implements `IContextUsageReporter` and returns a non-null reading, use
that reading. Otherwise return `ContextUsage.FromEstimate` from the layout's total and conversation
estimates against the configured window.

**Preconditions:** Arguments are valid session objects.

**Postconditions:** The usage origin records which path produced the reading.

#### RotationThreshold(ContextUsage usage, AgentSessionOptions options)

**Purpose:** Compute the threshold in the same currency as the usage reading.

**Algorithm:** For estimated usage, return the threshold already computed from the configured window.
For provider usage, subtract provider-reported overhead from the provider-reported window and apply
`AgentSessionOptions.RotationThresholdFor`.

**Preconditions:** `usage` is valid.

**Postconditions:** The comparison uses one currency throughout.

### Error Handling

- **Null options, factory or provider session** — `ArgumentNullException` or `InvalidOperationException`
  propagates.
- **Provider usage throws during creation** — The unowned provider session is released; if release
  succeeds, the original exception is rethrown.
- **Provider usage throws during creation and release fails** — `AgentSessionCreationException`
  propagates with `RetainedProviderSession` set.
- **Provider usage throws for a replacement** — The replacement is released and the existing session
  remains live.
- **Superseded provider disposal fails after rotation** — The failure is swallowed because rotation
  has already succeeded.
- **Explicit disposal fails** — The failure propagates and remains retryable.
- **Blank message** — `ArgumentException` propagates.
- **Send after disposal** — `ObjectDisposedException` propagates.
- **Cancellation** — Propagates from the provider, summarizer or factory and leaves pre-adoption state
  coherent.

### Dependencies

- **AgentSessionOptions** — Supplies configuration, summarizer and thresholds.
- **ContextLayout** — Holds the out-of-session context.
- **ContextUsage** — Drives threshold decisions and response reporting.
- **RotationEngine** — Ages layout and reports settled level and dropped material.
- **ProviderSession** — Supplies live provider sessions and replacements.
- **AgentSession** — Defines `IAgentSession` and `AgentSessionResponse`.

### Callers

Applications call `CreateAsync`, then use the returned `IAgentSession`. No other unit constructs
`CompactingAgentSession` directly.

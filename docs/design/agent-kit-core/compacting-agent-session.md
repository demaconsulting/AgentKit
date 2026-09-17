## CompactingAgentSession

![AgentKit Core Structure](AgentKitCoreView.svg)

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

- **`Layout`** (`ContextLayout`) — Internal context shape: the tiers and the verbatim tail.
- **`Usage`** (`ContextUsage`) — Latest usage reading.
- **`RotationCount`** (`int`) — Completed replacements.
- **`Level`** (`CompactionLevel`) — Current adaptive compaction level, initially Low.
- **`ConsolidationCount`** (`int`) — Total summarizer calls across completed rotations.

Derived hysteresis windows:

- **`K`** — `VerbatimTurns`; a window that fills again within this many turns compacts one level
  terser, or discards the oldest slot — or, when no slot remains, the oldest verbatim turn — when the
  level is already High.
- **`M`** — `2 * VerbatimTurns`; a quiet stretch this long relaxes the level one step. `M` is greater
  than `K`, so escalation and relaxation cannot both apply to one rotation.

`LiveProviderSession` pairs an `IProviderSession` with a `Released` flag. The flag is set only after
the provider's own disposal completes, which keeps disposal retryable.

### Key Methods

#### CreateAsync(AgentSessionOptions options, IProviderSessionFactory providerSessionFactory, CancellationToken cancellationToken)

**Purpose:** Create the compacting session and its first provider session.

**Algorithm:** Validate inputs, create an empty `ProviderSessionSeed`, ask the factory for a provider
session, and construct `CompactingAgentSession`. Construction creates an empty layout, adopts the
provider and reads usage. If construction fails after the provider exists, release the unowned
provider session and rethrow the original failure; a release that fails as well does not displace
that failure, because the reason creation failed is of more use than the reason the cleanup after it
failed.

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
5. Read the live provider session's account of how full it is.
6. Compare the reported conversation with the threshold derived from the same reading.
7. If below threshold, return the answer with current level and no dropped material.
8. If at or above threshold, rotate and return the answer with updated level, rotation flag and
   dropped-material flag.

**Preconditions:** `message` is not null, empty or blank; the session is not disposed.

**Postconditions:** A provider-accepted turn is recorded exactly once. Rotation, when completed,
prepares the replacement for the next turn; the current answer is produced before rotation.

#### RotateAsync(CancellationToken cancellationToken)

**Purpose:** Age the layout and replace the live provider session safely.

**Algorithm:** Decide the pressure response from the hysteresis clock alone: a window that filled
again within `K` turns escalates the level, or, when the level is already High, discards the oldest
slot of the coarsest tier holding one — or the oldest verbatim turn when no tier holds any; `M` quiet
turns relax the level instead. Call `RotationEngine.RotateAsync` once at that level, and skip
replacement if the engine consolidated nothing and nothing was dropped. Otherwise build a seed,
create a replacement, read replacement usage before adoption, adopt the replacement, update layout,
counters, usage, level and hysteresis clock in one no-await block, then attempt to dispose the
superseded provider session.

**Preconditions:** The current usage has reached the rotation threshold.

**Postconditions:** On success, state describes the replacement session, and the reported
dropped-material flag covers both a slot discarded under pressure and a consolidation the summarizer
failed to produce. If consolidation or replacement creation fails before adoption, the old provider
session remains live and coherent.

#### DropOldest(ContextLayout layout)

**Purpose:** Discard the oldest thing the context holds: the oldest slot of the coarsest tier that
holds one, or the oldest verbatim turn when no tier holds any.

**Algorithm:** Scan from the coarsest tier toward the finest, and drop the oldest slot of the first
tier holding any. When every tier is empty, fall through and drop the oldest verbatim turn instead,
unless the tail holds only one turn. Report whether anything was dropped and return the reduced
layout.

**Preconditions:** None beyond a non-null layout.

**Postconditions:** At most one slot, or at most one verbatim turn, is gone. The newest turn is never
dropped: a session must be able to answer the message it was just given, so a tail of one turn is
left alone and nothing is reported as dropped.

The tier scan is rule 4's ring brought forward: the coarsest tier normally sheds its oldest slot only
when a new one arrives, and under sustained pressure the same move is made on demand because the
context is filling faster than that schedule empties it. The coarsest slot covers the oldest and
least detailed span of the conversation, which is the part the agent will miss least. No measurement
is involved.

**The fall-through is this unit's termination guarantee, not a nicety.** Every tier is empty exactly
when no consolidation has ever succeeded, and that is the same condition under which a rotation
cannot shrink anything: a summarizer answering blank leaves the material where it is, by design, and
blankness is content-dependent and therefore sticky, so the same material answers blank again on the
next rotation. Without the fall-through the verbatim tail would gain a turn per message and shed
nothing, forever, while every rotation reported success and spent a fresh provider session. Binning
the oldest page when there are no cards left is the only move a count-based design leaves.

#### DisposeAsync()

**Purpose:** Release the currently live provider session.

**Algorithm:** Mark the session disposed and call `_live.ReleaseAsync()`.

**Preconditions:** None.

**Postconditions:** When provider disposal succeeds, the live provider session is released. If
disposal fails, the exception propagates and a later disposal call retries the same provider release.

#### ReadUsage(IProviderSession provider)

**Purpose:** Read the provider session's own account of how full it is.

**Algorithm:** Return `IProviderSession.CurrentUsage`. There is nothing to choose between and nothing
to reconcile: the adapter answers for its own provider and the engine believes it. The method is kept
named because it marks the one place a token figure enters the engine.

**Preconditions:** The provider session has been created and not yet disposed.

**Postconditions:** The reading is whatever the adapter answered, in the adapter's own currency. The
engine performs no arithmetic on it beyond comparing the conversation it carries with the threshold
derived from the window it carries.

#### RotationThreshold(ContextUsage usage)

**Purpose:** Derive the conversation size at which this turn should rotate.

**Algorithm:** Subtract the overhead the reading credits from the window it reports, and apply
`AgentSessionOptions.RotationThresholdFor`.

**Preconditions:** `usage` is the reading taken from the live provider session this turn.

**Postconditions:** The window, the overhead and the conversation compared against the threshold all
come from one reading, so the comparison is in one currency and there is no second window to
reconcile.

### Error Handling

- **Null options, factory or provider session** — `ArgumentNullException` or `InvalidOperationException`
  propagates.
- **Provider usage throws during creation** — The unowned provider session is released; if release
  succeeds, the original exception is rethrown.
- **Provider usage throws during creation and release fails** — The original creation failure still
  propagates.
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

- **AgentSessionOptions** — Supplies configuration, the summarizer and the shared threshold
  arithmetic.
- **ContextLayout** — Holds the out-of-session context.
- **ContextUsage** — Drives threshold decisions and response reporting.
- **RotationEngine** — Ages the layout at the level it is given and reports the consolidations it
  performed and whether a consolidation failed.
- **ProviderSession** — Supplies live provider sessions, replacements, and the usage reading.
- **AgentSession** — Defines `IAgentSession` and `AgentSessionResponse`.

### Callers

Applications call `CreateAsync`, then use the returned `IAgentSession`. No other unit constructs
`CompactingAgentSession` directly.

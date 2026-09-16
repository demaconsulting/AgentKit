## ContextUsage

### Purpose

`ContextUsage` is the single shape used for context-window accounting. It records how much of the
window is occupied, how much of that is conversation rather than fixed overhead, the window size, and
whether the numbers came from a provider report or this library's estimate.

### Data Model

`ContextUsageOrigin` values:

- **`Provider`** — A provider session reported the usage.
- **`Estimated`** — The library estimated usage from its own layout.

`ContextUsage` properties:

- **`UsedTokens`** (`int`) — Occupied tokens, never negative.
- **`WindowTokens`** (`int`) — Window size, always positive.
- **`ConversationTokens`** (`int`) — Conversation portion of `UsedTokens`, never negative and never
  greater than `UsedTokens`.
- **`Origin`** (`ContextUsageOrigin`) — Source of the figures.
- **`OverheadTokens`** (`int`) — Derived fixed overhead, `UsedTokens - ConversationTokens`.
- **`FreeTokens`** (`int`) — Remaining room, floored at zero.
- **`UsedFraction`** (`double`) — Fraction of the window occupied; may exceed one.

The conversation count is stored rather than derived by consumers so every threshold comparison can
use a single currency. A provider split is counted by the provider; an estimated split is counted by
this library.

`IContextUsageReporter` is an optional provider-session capability. A provider session implements it
only when it can return the latest usage without making another provider call.

### Key Methods

#### ContextUsage(int usedTokens, int windowTokens, int conversationTokens, ContextUsageOrigin origin)

**Purpose:** Create a validated immutable usage reading.

**Algorithm:** Validate non-negative used and conversation counts, positive window, conversation not
greater than used, and defined origin; then store the values.

**Preconditions:** Counts form a meaningful usage reading.

**Postconditions:** Derived values are safe: overhead cannot be negative, free room is never negative,
and over-window usage remains visible.

#### FromProvider(int usedTokens, int windowTokens, int? conversationTokens = null)

**Purpose:** Create a usage reading from provider-reported figures.

**Algorithm:** Use the supplied conversation split when present; otherwise treat the whole used total
as conversation, which rotates earlier than a correct split would.

**Preconditions:** Provider totals are non-negative, the window is positive, and any split is within
the used total.

**Postconditions:** The reading is marked `Provider`.

#### FromEstimate(int usedTokens, int windowTokens, int? conversationTokens = null)

**Purpose:** Create a usage reading from this library's own estimate.

**Algorithm:** Use the supplied estimated conversation split when present; otherwise treat the whole
used total as conversation.

**Preconditions:** Estimated totals are non-negative, the window is positive, and any split is within
the used total.

**Postconditions:** The reading is marked `Estimated`.

### Error Handling

- **Negative used tokens or conversation tokens** — `ArgumentOutOfRangeException` propagates.
- **Non-positive window** — `ArgumentOutOfRangeException` propagates.
- **Conversation greater than used** — `ArgumentOutOfRangeException` propagates.
- **Undefined origin** — `ArgumentOutOfRangeException` propagates.

### Dependencies

N/A - `ContextUsage` is a value shape. `CompactingAgentSession` reads it after each turn, and
provider adapters may produce it through `IContextUsageReporter`.

### Callers

`CompactingAgentSession` consumes provider-reported usage when available and otherwise constructs an
estimated reading. Applications read `ContextUsage` from `IAgentSession` and `AgentSessionResponse`.

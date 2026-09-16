## ContextUsage

### Purpose

`ContextUsage` is the single shape used for context-window accounting. It records how much of the
window is occupied, how much of that is conversation rather than fixed overhead, the window size, and
whether the adapter measured those figures or estimated them.

### Data Model

`ContextUsageOrigin` values:

- **`Provider`** — The adapter reported figures its provider counted.
- **`Estimated`** — The adapter derived the figures itself, its provider revealing none.

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
use a single currency: whoever produced the totals produced the split.

Every provider session answers with this shape through `IProviderSession.CurrentUsage`. There is no
separate optional reporting interface: requiring the answer of every adapter is what gives the engine
one path and one source, and an adapter whose provider reveals nothing answers with an estimate and
marks it as one.

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

**Purpose:** Create a usage reading an adapter derived rather than measured.

**Algorithm:** Use the supplied estimated conversation split when present; otherwise treat the whole
used total as conversation.

**Preconditions:** Estimated totals are non-negative, the window is positive, and any split is within
the used total.

**Postconditions:** The reading is marked `Estimated`, so an application can tell it from a figure a
provider counted.

### Error Handling

- **Negative used tokens or conversation tokens** — `ArgumentOutOfRangeException` propagates.
- **Non-positive window** — `ArgumentOutOfRangeException` propagates.
- **Conversation greater than used** — `ArgumentOutOfRangeException` propagates.
- **Undefined origin** — `ArgumentOutOfRangeException` propagates.

### Dependencies

N/A - `ContextUsage` is a value shape. `CompactingAgentSession` reads it after each turn, and every
provider adapter produces it through `IProviderSession.CurrentUsage`.

### Callers

`CompactingAgentSession` reads the live provider session's usage after every turn and derives the
rotation threshold from it. Applications read `ContextUsage` from `IAgentSession` and
`AgentSessionResponse`.

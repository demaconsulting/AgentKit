## ContextUsage

![AgentKit Core Structure](AgentKitCoreView.svg)

### Purpose

`ContextUsage` is the single shape used for context-window accounting. It records how much of the
window is occupied, how much of that is conversation rather than fixed overhead, and how large the
window is.

### Data Model

`ContextUsage` properties:

- **`UsedTokens`** (`int`) — Occupied tokens, never negative.
- **`WindowTokens`** (`int`) — Window size, always positive.
- **`ConversationTokens`** (`int`) — Conversation portion of `UsedTokens`, never negative and never
  greater than `UsedTokens`.
- **`OverheadTokens`** (`int`) — Derived fixed overhead, `UsedTokens - ConversationTokens`.

The conversation count is stored rather than derived by consumers so every threshold comparison can
use a single currency: whoever produced the totals produced the split.

The shape records no provenance. There is nothing to distinguish, because the engine performs no
token arithmetic of its own: every figure in every comparison arrived from one provider reading, so
a note of where it came from would have no second source to be told apart from.

Every provider session answers with this shape through `IProviderSession.CurrentUsage`. There is no
separate optional reporting interface: requiring the answer of every adapter is what gives the engine
one path and one source, and an adapter whose provider reveals nothing answers with a figure it
derived and owns that choice.

### Key Methods

#### ContextUsage(int usedTokens, int windowTokens, int conversationTokens)

**Purpose:** Create a validated immutable usage reading.

**Algorithm:** Validate non-negative used and conversation counts, a positive window, and a
conversation no greater than the used total; then store the values.

**Preconditions:** Counts form a meaningful usage reading.

**Postconditions:** Derived overhead cannot be negative, and usage beyond the window is carried
through unchanged so the over-full condition stays visible.

#### FromProvider(int usedTokens, int windowTokens, int? conversationTokens = null)

**Purpose:** Create a usage reading from the figures a provider session answers with.

**Algorithm:** Use the supplied conversation split when present; otherwise treat the whole used total
as conversation, which rotates earlier than a correct split would.

**Preconditions:** Provider totals are non-negative, the window is positive, and any split is within
the used total.

**Postconditions:** The reading carries the figures exactly as the adapter answered them, including
a used total that exceeds the window.

### Error Handling

- **Negative used tokens or conversation tokens** — `ArgumentOutOfRangeException` propagates.
- **Non-positive window** — `ArgumentOutOfRangeException` propagates.
- **Conversation greater than used** — `ArgumentOutOfRangeException` propagates.

### Dependencies

N/A - `ContextUsage` is a value shape. `CompactingAgentSession` reads it after each turn, and every
provider adapter produces it through `IProviderSession.CurrentUsage`.

### Callers

`CompactingAgentSession` reads the live provider session's usage after every turn and derives the
rotation threshold from it. Applications read `ContextUsage` from `IAgentSession` and
`AgentSessionResponse`.

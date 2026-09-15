## AgentSessionOptions

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `AgentSessionOptions` class carries everything an application configures about one session, and
derives from it the fixed overhead, the effective window and the rotation threshold.

### Purpose

Five things fully describe a session: what the agent is told, what it may call, how large the
provider's window is, when to compact, and who does the compacting. `AgentSessionOptions` holds
exactly those and computes the arithmetic that follows from them, once, at construction.

Only the summarizer is required. Compaction cannot happen without one, and defaulting it would hand
an application a session that silently never compacts — the exact failure this package exists to
prevent. Everything else has a defensible default, so a session can be configured in one line and
refined later.

The class also **asserts the construction bound**. A configuration whose effective window cannot
hold the policy's tier budgets would rotate into a context already over budget and could never
converge; a configuration whose overhead consumed the window would have no room for a conversation
at all. Both are refused. If these options construct, the arrangement fits.

### Data Model

- **`Summarizer`** (`ISummarizer`) — Never null
- **`Instructions`** (`string?`) — Null when there are none
- **`Tools`** (`IReadOnlyList<AIFunction>`) — Never null; never contains null; may be empty
- **`ProviderWindowTokens`** (`int`) — Positive
- **`Compaction`** (`CompactionPolicy`) — Never null; `CompactionPolicy.Default` when not configured
- **`SystemTokens`** (`int`) — Estimated tokens of `Instructions`; not negative
- **`ToolDeclarationTokens`** (`int`) — Estimated declaration overhead of `Tools`; not negative
- **`FixedOverheadTokens`** (`int`) — Derived: `SystemTokens + ToolDeclarationTokens`
- **`EffectiveWindowTokens`** (`int`) — `ProviderWindowTokens - FixedOverheadTokens`; always positive
- **`RotationThresholdTokens`** (`int`) — `(int)(EffectiveWindowTokens * Compaction.RotationThreshold)`

`DefaultProviderWindowTokens` is 128,000. A provider reached through an `IChatClient` reports no
window size, so one has to be assumed; this is a common contemporary window offered as a starting
point rather than a claim about any particular model. An application that knows its provider's
window should say so — under-stating it wastes context, and over-stating it rotates too late, which
is the failure that loses work.

### Key Methods

#### The AgentSessionOptions Constructor

**Algorithm:**

1. Reject a null summarizer and a non-positive provider window.
2. Select the supplied compaction policy, or `CompactionPolicy.Default`.
3. Measure `SystemTokens` from the instructions and `ToolDeclarationTokens` from the tools. This
   step also validates the tool list, because a null declaration cannot be estimated.
4. Compute the effective window as the provider window less the fixed overhead. Reject a
   non-positive result.
5. Reject an effective window smaller than the policy's total tier budget.
6. Assign, and compute the rotation threshold by truncating the effective window multiplied by the
   policy's threshold fraction.

**Why the overhead is subtracted before the percentage.** Tool declarations are sent with every
request and are never consolidated; for a realistic tool set they run to thousands of tokens — the
compaction spike measured 2,589 tokens for a set of 11 tools (n = 11 tools, one measurement,
recorded in that spike). Applying the rotation percentage to the raw window would make the rotation
point drift with how many tools an application attached: attach more tools and the agent silently
gets less conversation before rotating, with nothing in the configuration saying so.

**Why the threshold is compared against conversation tokens.** `RotationThresholdTokens` is a
fraction of the effective window, so the figure compared against it must also exclude the fixed
overhead. `CompactingAgentSession` subtracts `FixedOverheadTokens` from the usage before comparing,
which is what makes the comparison mean the same thing whether the usage came from a provider or
from this library's own estimate.

### Error Handling

- **Null summarizer** — `ArgumentNullException` propagates
- **Non-positive provider window** — `ArgumentOutOfRangeException` propagates
- **Null tool in the list** — `ArgumentException` propagates
- **Fixed overhead consumes the whole window** — `ArgumentException` propagates, naming both measured figures
- **Effective window smaller than the total tier budget** — `ArgumentException` propagates, naming both figures

Every rejected condition is a defect in the composing application's configuration code, surfaced
where the application wrote it rather than discovered from a session that never settles. Validation
happens before any assignment, so a rejected configuration never exists even briefly.

### Dependencies

- **TokenEstimator** — measures the system prompt and the tool declarations; see _TokenEstimator
  Unit Design_.
- **CompactionPolicy** — supplies the tier budgets and the rotation threshold fraction; see
  _CompactionPolicy Unit Design_.
- **Summarizer** — supplies the `ISummarizer` contract; see _Summarizer Unit Design_.
- **Microsoft.Extensions.AI.Abstractions** — supplies `AIFunction`.

### Callers

`CompactingAgentSession` reads every derived figure on this type: the instructions and tools when
seeding a provider session, the fixed overhead and threshold when deciding whether to rotate, the
policy when creating its layout, and the summarizer when rotating. An application constructs one
instance and hands it to `CompactingAgentSession.CreateAsync`.

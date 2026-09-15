## InMemoryProviderSession

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `InMemoryProviderSession` unit publishes a provider session that contacts nothing, and
`InMemoryProviderSessionFactory`, which creates them and remembers every one it made.

### Purpose

**Shipped rather than confined to this library's tests, deliberately.** The compaction engine's
whole promise is that a long-running agent keeps the detail that matters, and that promise is only
believable if it can be exercised end to end without a live model. An application author writing
their own summarizer, choosing tier budgets, or deciding what to do about a saturation signal needs
the same ability. Keeping the fake in the package makes that a supported activity instead of
something each consumer reimplements.

**It can be either provider shape.** Real providers differ: one reports current and limit figures,
the other reports nothing. Through the `reportsUsage` switch this session can be either, so both
engine paths — preferring the provider's account, and falling back to the library's own estimate —
are reachable from a test. Without that, one of the two ships unexercised.

The factory's remembering is the point. Rotation creates a replacement session and disposes the
previous one, and both halves have to be observable for that behavior to be verifiable at all:
against a real provider a leaked session holds a server-side conversation open and keeps being
billed for.

### Data Model

`InMemoryProviderSession` properties:

- **`Seed`** (`ProviderSessionSeed`) — What this session was created from; exposed so a test can assert what a
  rotation carried forward
- **`WindowTokens`** (`int`) — The context window this session pretends to have; positive
- **`FixedOverheadTokens`** (`int`) — The instructions plus the tool declarations, charged as a real provider would
- **`History`** (`IReadOnlyList<TranscriptEntry>`) — Seeded entries first, everything since after them; cleared on
  disposal
- **`TurnCount`** (`int`) — How many turns this session has answered
- **`IsDisposed`** (`bool`) — Whether the session has been disposed
- **`CurrentUsage`** (`ContextUsage?`) — The fixed overhead plus every history entry, marked `Provider`; null when
  configured not to report

Private state: the responder producing a turn for a message, and the `reportsUsage` switch.

`InMemoryProviderSessionFactory` properties: `WindowTokens`, `ReportsUsage`, and `Sessions` — every
session created so far, oldest first. For a conversation that ran to completion, `Sessions.Count`
equals the rotation count plus one.

**Instances are not safe for concurrent use**, consistent with `IProviderSession`, and the factory's
record of created sessions is an ordinary list because a factory serves one session's rotations in
sequence.

### Key Methods

#### The InMemoryProviderSession Constructor

Copies the seeded history into its own list and measures the fixed overhead the seed implies. The
overhead is charged here too, so a test exercising the rotation threshold sees the same arithmetic
the engine performs against a real provider.

#### CurrentUsage

Returns null when configured not to report. Otherwise sums the fixed overhead and every history
entry's estimate and marks the result `ContextUsageOrigin.Provider` — because from the engine's
point of view that is exactly what it is.

#### SendAsync(string message, CancellationToken cancellationToken)

Records the incoming message, invokes the responder, records everything the turn produced, increments
`TurnCount` and returns the turn. Recording the message before answering is what makes the history
match what a provider holding the conversation server-side would have.

**Throws:** `ArgumentNullException` for a null message; `ObjectDisposedException` once disposed;
`OperationCanceledException` on cancellation; `InvalidOperationException` when the responder returns
null.

#### DisposeAsync()

Marks the session disposed and drops its history, which is what makes a leaked session detectable in
a test. Disposing twice is permitted and does nothing the second time.

#### InMemoryProviderSessionFactory.CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken)

Creates a session, records it in `Sessions`, and returns it. The default responder — selected when
none is supplied — echoes the message back as an assistant answer, which is enough to exercise the
lifecycle when what the model says does not matter, and names the message so a test can tell turns
apart.

### Error Handling

- **Null seed or responder** — `ArgumentNullException` propagates
- **Non-positive window** — `ArgumentOutOfRangeException` propagates
- **Null message** — `ArgumentNullException` propagates
- **Send after disposal** — `ObjectDisposedException` propagates
- **Responder returns null** — `InvalidOperationException` propagates
- **Second disposal** — Permitted; does nothing

### Dependencies

- **ProviderSession** — implements `IProviderSession` and `IProviderSessionFactory`, and consumes
  `ProviderSessionSeed` and `ProviderTurn`; see _ProviderSession Unit Design_.
- **ContextUsage** — implements `IContextUsageReporter` and produces `ContextUsage`; see
  _ContextUsage Unit Design_.
- **TokenEstimator** — measures the simulated fixed overhead; see _TokenEstimator Unit Design_.
- **AgentSessionOptions** — supplies `DefaultProviderWindowTokens` as the factory's default window;
  see _AgentSessionOptions Unit Design_.
- **SessionTranscript** — supplies `TranscriptEntry`; see _SessionTranscript Unit Design_.

### Callers

An application or a test hands the factory to `CompactingAgentSession.CreateAsync`. Nothing inside
the engine references these types: they satisfy the provider seam like any other adapter would.

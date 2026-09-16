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
- **`CurrentUsage`** (`ContextUsage?`) — The fixed overhead plus every history entry, with the history total also
  reported as the conversation, marked `Provider`; null when configured not to report

Private state: the responder producing a turn for a message, and the `reportsUsage` switch.

`InMemoryProviderSessionFactory` properties: `WindowTokens`, `ReportsUsage`, and `Sessions` — every
session created so far, oldest first. For a conversation that ran to completion, `Sessions.Count`
equals the rotation count plus one.

**Sessions are not safe for concurrent use**, consistent with `IProviderSession`: one session serves
one conversation. **The factory is**, as `IProviderSessionFactory` requires: it serializes creation
under its own lock and returns a snapshot from `Sessions`, so several sessions rotating against one
factory can neither lose a created session nor observe the record halfway through an addition.

### Key Methods

#### The InMemoryProviderSession Constructor

Copies the seeded history into its own list and measures the fixed overhead the seed implies. The
overhead is charged here too, so a test exercising the rotation threshold sees the same arithmetic
the engine performs against a real provider.

#### CurrentUsage

Returns null when configured not to report. Otherwise sums every history entry's estimate as the
conversation, adds the fixed overhead for the total, and marks the result
`ContextUsageOrigin.Provider` — because from the engine's point of view that is exactly what it is.
Reporting the conversation separately rather than leaving it to be inferred is the shape a real
reporting adapter uses, so the engine's reported path is exercised as it will actually be driven.

Note what this fake cannot demonstrate: its overhead is measured with the very `TokenEstimator` the
engine would otherwise have used, so its reported currency and this library's estimated currency
coincide exactly. A test that needs to tell a measurement from an estimate must script the reported
figures instead; see *CompactingAgentSession Unit Verification Design*.

#### SendAsync(string message, CancellationToken cancellationToken)

Invokes the responder, then records the incoming message and everything the turn produced — the tool
work and the answer that ends it — increments `TurnCount` and returns the turn. Recording the turn's
entries in full is what keeps this session's history and the engine's transcript describing the same
conversation.

**Why nothing is recorded until the responder has answered.** A responder that throws, or returns
null, would otherwise leave a user message behind that no turn ever answered, while
`CompactingAgentSession` correctly records nothing when a provider refuses a turn. This session is
shipped, and adapter authors read it as the reference implementation; a fake whose history diverges
from the contract under failure is worse than no fake. Both halves of the turn are appended
together, so an observer of `History` never sees a message without the turn that answered it.

**Why cancellation is checked on both sides of the responder.** `IProviderSession` documents that a
canceled turn leaves the session as it was, and a check made only before the responder honors that
for a token canceled earlier and not for one canceled while the responder was running — a responder
may cancel the token itself, and an adapter for a real provider awaits a call a cancellation can
overtake. The responder then completed normally, the message and the answer were recorded, and
`TurnCount` was incremented, for a turn whose caller had been told it was canceled. Checking again
before the result is accepted makes a canceled turn leave no history whichever moment the
cancellation arrived in.

**Throws:** `ArgumentNullException` for a null message; `ObjectDisposedException` once disposed;
`OperationCanceledException` on cancellation; `InvalidOperationException` when the responder returns
null.

#### DisposeAsync()

Marks the session disposed and drops its history, which is what makes a leaked session detectable in
a test. Disposing twice is permitted and does nothing the second time.

#### InMemoryProviderSessionFactory.CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken)

Creates a session, records it under the factory's lock, and returns it. The default responder — selected when
none is supplied — echoes the message back as an assistant answer, which is enough to exercise the
lifecycle when what the model says does not matter, and names the message so a test can tell turns
apart.

### Error Handling

- **Null seed or responder** — `ArgumentNullException` propagates
- **Non-positive window** — `ArgumentOutOfRangeException` propagates
- **Null message** — `ArgumentNullException` propagates
- **Send after disposal** — `ObjectDisposedException` propagates
- **Responder returns null** — `InvalidOperationException` propagates; nothing is recorded
- **Responder throws** — Propagates; nothing is recorded
- **Second disposal** — Permitted; does nothing

### Dependencies

- **ProviderSession** — implements `IProviderSession` and `IProviderSessionFactory`, and consumes
  `ProviderSessionSeed` and `ProviderTurn`; see *ProviderSession Unit Design*.
- **ContextUsage** — implements `IContextUsageReporter` and produces `ContextUsage`; see
  *ContextUsage Unit Design*.
- **TokenEstimator** — measures the simulated fixed overhead; see *TokenEstimator Unit Design*.
- **AgentSessionOptions** — supplies `DefaultProviderWindowTokens` as the factory's default window;
  see *AgentSessionOptions Unit Design*.
- **SessionTranscript** — supplies `TranscriptEntry`; see *SessionTranscript Unit Design*.

### Callers

An application or a test hands the factory to `CompactingAgentSession.CreateAsync`. Nothing inside
the engine references these types: they satisfy the provider seam like any other adapter would.

## CopilotProviderSession

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotProviderSession` class carries one AgentKit Core session over one GitHub Copilot
session, for a provider that holds the conversation itself and reports what it holds.

### Purpose

`CopilotProviderSession` is what lets the session engine run on GitHub Copilot at all. Until it
existed the engine had exactly one adapter, for the stateless `IChatClient` family, and the Copilot
package produced only an agent — so the compaction engine could not be exercised against the one
provider this project's continuous integration can reach.

**It is the opposite shape to the stateless adapter, and almost everything follows from that.** A
stateless provider is resent the whole conversation each turn, so its adapter keeps a message list
and can count what it sent. Copilot keeps the conversation server-side: a turn sends one prompt, and
what the session holds afterwards is the runtime's business. So this class keeps no running history.
The one exception is the seeded record a rotation produced, which it holds only until the first
message carries it — Copilot's configuration has no history channel, and the record is untrusted
material that must not travel in the system one. Composing that record is
`CopilotProviderSessionFactory`'s work; carrying it is this class's. Releasing is disposing the
runtime's session.

**There is no window parameter in the sense the stateless adapter has one.** That adapter has to be
told its window because an `IChatClient` publishes none. Copilot reports both figures — occupancy and
limit — in the same usage event, so asking an application for a number the provider already knows
would create a second source of truth for one fact, which is exactly what the engine's single-reading
design retired. Nothing here is estimated. A host may state a **ceiling**, which only ever lowers the
window the session accounts against; the runtime's figure still governs, and a ceiling above it is
ignored.

**No image-promoting decorator**, for the reason _CopilotAgentFactory Unit Design_ records: the
Copilot runtime already delivers a tool's binary results to the model — the SDK's tool-completion
result carries them explicitly — so promoting an image onto a user message here would duplicate
content the model already received. The absence is intentional and is restated in this class's own
remarks so a future maintainer does not add it.

The class implements `IProviderSession`. Instances are not safe for concurrent use, consistent with
that contract.

**Construction is internal.** A session is obtained from `CopilotProviderSessionFactory`, which is
the only thing that can build one correctly: a channel opened without the observer registered as the
session's event handler would answer turns and report no occupancy at all. See
_CopilotProviderSessionFactory Unit Design_.

### Data Model

- **`_channel`** (`ICopilotTurnChannel`) — The runtime session this instance owns. Invariant: the
  same session the observer beside it was registered on. Released by `DisposeAsync` and by nothing
  else. See _CopilotTurnChannel Unit Design_.
- **`_observer`** (`CopilotSessionObserver`) — Watches the runtime's event stream and holds the
  occupancy reading and the current turn's entries. Invariant: registered on `_channel`'s session
  before that session was created. See _CopilotSessionObserver Unit Design_.
- **`_maxWindowTokens`** (`int?`) — The ceiling a host stated on the window this session accounts
  against, or null for none. Invariant: it only ever lowers — the effective window is the smaller of
  this and the runtime's reported limit.
- **`_historyPreamble`** (`string?`) — The seeded conversation record, until the first message
  carries it, and null thereafter. Invariant: consumed at most once, so a long conversation carries
  it exactly once. Held rather than configured because the system message is the wrong channel for
  material a tool result may have written; see _CopilotProviderSessionFactory Unit Design_.
- **`_unusableReason`** (`string?`) — Why this session may no longer be used, or null while it may.
  Invariant: it latches — once set, never cleared.
- **`IsReleased`** (`bool`) — Whether this session has been released. Invariant: once true, never
  false again.
- **`ProvisionalWindowTokens`** (`const int`, one) — The window reported before the runtime has
  reported one. Deliberately not a plausible window; see `CurrentUsage` below.

### Key Methods

#### The CopilotProviderSession Constructor (ICopilotTurnChannel, CopilotSessionObserver, int?, string?)

**Purpose:** Take ownership of one runtime session and the observer watching it, and hold what the
seed produced until the first turn carries it.

**Algorithm:** Reject a null channel and a null observer, and record both, along with the window
ceiling where the host stated one and the composed history preamble where the seed carried history.
There is nothing else to do: the confinement and the instructions were settled when the session was
created, so a constructed session is ready to take its first turn.

The constructor is internal because only the factory can supply a channel whose session was created
with this observer already registered, and a pair that does not match is an arrangement no
application can have. Its argument checks are therefore a guard on an internal contract rather than
a message to an application.

**Preconditions:** `channel` and `observer` are not null.

**Postconditions:** The session is ready to take its first turn. Nothing has been sent.

#### CurrentUsage

**Purpose:** Answer the engine's one token question, from the runtime's own reporting.

**Algorithm:** Where the observer holds no reading, report nothing occupied out of
`ProvisionalWindowTokens`. Otherwise take the runtime's three figures: the occupancy, the limit, and
the conversation's share where the runtime reported one — or the whole occupancy where it did not,
which credits the session with no overhead at all and so rotates strictly earlier than a correct
split would. Where the host stated a ceiling, the window reported is the smaller of that ceiling and
the runtime's limit, so a ceiling can lower the accounted window and never raise it.

Three properties of that arithmetic are deliberate and are each stated where they happen:

- **The narrowing from the runtime's wider counters is a clamp, not a checked conversion.** Reading
  usage is documented as unable to fail, so a throw here would be a contract violation. A context
  window is some millions of tokens against a limit of some billions, so the clamp is unreachable in
  practice; it exists to say in one place what happens if that ever stops being true.
- **A conversation reported larger than the total is held to the total.** `ContextUsage` refuses
  that combination — correctly, because a negative overhead would enlarge the effective window and
  rotate later than the provider's own limit allows — and this property must not throw.
- **The occupancy is reported as it came back, including past the window.** A session that has
  overrun its own limit is the condition an application most needs to see.

**Before the first reading**, the placeholder window is reported. It cannot affect a decision: the
rotation test compares the conversation against a threshold that is never below one token, the
conversation is zero until the runtime has reported, and zero is below one. It is deliberately not a
plausible number, because a plausible one would be mistaken for a measurement by anyone reading it.
A turn that completes with no reading at all is refused by `SendAsync` rather than reported from
here, because reporting is not allowed to fail and that condition must not pass silently.

**Preconditions:** None beyond construction invariants.

**Postconditions:** A `ContextUsage` produced through `ContextUsage.FromProvider`, because that is
exactly what it is. Reading it contacts nothing and cannot fail.

#### SendAsync(string message, CancellationToken cancellationToken)

**Purpose:** Take one turn and record what it produced.

**Algorithm:** Reject a null message, a released session, a session already found unusable, and a
canceled token. Tell the observer a turn is starting, which discards anything the last one left
behind. Take the text to send — the caller's message, preceded by the seeded record where one is
still pending — and send it through the channel, waiting for the session to become idle. Everything
after that is `CompleteTurn`: in order, refuse three conditions; and only if none holds, build the
turn from the runtime's answer and the entries the observer collected. Any failure out of
`CompleteTurn` is recorded as the reason this session may no longer be used, and then rethrown.

The three refusals are ordered because the first invalidates the others:

1. **The runtime rewrote this session's history.** Every session the engine drives is created with the
   runtime's own compaction threshold raised clear of the engine's rotation point, which is a margin
   rather than a guarantee; if it compacted or truncated anyway, the engine's transcript describes a
   conversation the provider no longer holds, and every figure below is about that vanished
   conversation. See _The Runtime's Own Compaction Is Held Clear of Rotation_ in
   _AgentKitAgentsCopilot System Design_.
2. **The session went idle without producing an assistant message.** The runtime's wait returns
   nothing in that case, which means the turn failed rather than that the model had nothing to say.
   The message names the runtime's own error where one was reported, because a refusal an
   application cannot act on is little better than a silent empty answer.
3. **The runtime answered without reporting its occupancy.** Knowing when the window is filling is
   the one thing the engine needs a token count for, and Copilot reports both figures itself — so an
   absent reading means something an application can see and fix.

Every refusal happens **before** anything is recorded, and the observer's entries are drained only
on the successful path, so a refused turn leaves no half-recorded history behind.

**The send is the dividing line, and a failure past it ends the session rather than the turn.** Once
the runtime has processed the turn it holds one the engine's transcript does not — and on a first
turn it has also consumed the seeded record, which will not be sent again. Neither is recoverable by
retrying on this session, and retrying an `InvalidOperationException` is the ordinary host response,
which would re-run the application's tools, with their real side effects, against a conversation the
engine no longer describes. So the reason is latched and every later turn is refused before it is
sent. See _A Session the Engine Cannot Account For Is Finished_ in _AgentKitAgentsCopilot System
Design_.

The answer reaches this method twice — as an event the observer collected and as the value the wait
returned — and is recorded once, because `ProviderTurn` takes a trailing assistant entry whose text
is the answer to _be_ the answer.

**Preconditions:** `message` is not null; the session is not released and has not been found
unusable; cancellation has not been requested.

**Postconditions:** On success, a `ProviderTurn` carrying the answer and the entries that led to it,
and an occupancy reading the engine can rotate on; and the seeded record, if there was one, has been
sent and will not be sent again. On a refusal raised before the send, the session is exactly as it
was. On a failure raised after it, the session is finished and says why.

#### CompleteTurn(AssistantMessageEvent? answer)

**Purpose:** Turn what the runtime produced into a recorded turn, or refuse it.

**Algorithm:** Apply the three refusals above in order, then drain the observer's entries and build
the `ProviderTurn`.

Separated from the send so that everything the runtime has already seen sits in one place and the
caller can treat every failure in it the same way — as the end of the session rather than the end of
a turn. A condition checked on one side of that line and a condition checked on the other are not the
same kind of failure, and keeping them in one method would make that impossible to see.

**Preconditions:** The runtime has processed the turn.

**Postconditions:** The recorded turn, or an `InvalidOperationException` naming which of the three
conditions held.

#### TakeMessageToSend(string message)

**Purpose:** Take the text to send, carrying the seeded record ahead of it on the first message.

**Algorithm:** Where no preamble is pending, return the message unchanged. Otherwise clear the
preamble and return it followed by a blank line and the message.

The preamble is consumed rather than kept, so it rides exactly one message. Prepending it to a
message the engine was already sending is what makes a rotation cost one request rather than two, and
what keeps the material in the conversation channel instead of the system one.

**Preconditions:** `message` is not null.

**Postconditions:** The text to send; no preamble remains pending.

#### ThrowIfUnusable()

**Purpose:** Refuse the session if an earlier turn left it describing a conversation the runtime no
longer holds.

**Algorithm:** Refuse if the observer has seen the runtime rewrite history, and refuse if a turn the
runtime processed could not be recorded, naming in that case the original failure.

One rule for two causes, because the consequence is identical: the engine's transcript and the
runtime's conversation have parted, and nothing this session does afterwards can bring them back
together. The engine's answer to a session it cannot use is to seed a replacement from its own
transcript, which is the state known to be good.

**Preconditions:** None.

**Postconditions:** Returns, or throws `InvalidOperationException` naming the cause.

#### DisposeAsync()

**Purpose:** Release the runtime session without releasing the application's client.

**Algorithm:** Mark the session released and release the channel, which disposes and deletes the
runtime's session. The `CopilotClient` is deliberately not disposed: it was constructed and started
by the host, is shared by every session a rotation creates, and outlives all of them. Repeated
disposal is permitted, because a rotation and an application's own release may both reach the same
session.

**Preconditions:** None.

**Postconditions:** `IsReleased` is true, the runtime session is released, and later turns are
refused.

#### Narrow(long value, int minimum)

**Purpose:** Bring one of the runtime's token figures into the range Core's usage shape accepts.

**Algorithm:** Clamp between the stated minimum and the widest representable value. A named method
rather than an inline cast so the narrowing happens in exactly one place and a reader can see on
sight that it is a clamp.

**Preconditions:** None.

**Postconditions:** A value within the accepted range.

### Error Handling

| Condition                                    | Handling                                        |
|----------------------------------------------|-------------------------------------------------|
| Null `channel` or `observer`                 | `ArgumentNullException` propagates              |
| Null `message`                               | `ArgumentNullException` propagates              |
| Turn on a released session                   | `ObjectDisposedException` propagates            |
| Cancellation before the turn                 | `OperationCanceledException` propagates         |
| Runtime compacted or truncated the history   | `InvalidOperationException` propagates          |
| Session idle with no assistant message       | `InvalidOperationException` propagates          |
| No usage reported for the turn               | `InvalidOperationException` propagates          |
| Turn on a session finished by an earlier one | `InvalidOperationException` naming that failure |
| Failure inside the turn                      | Propagates; the turn records nothing            |

None of the last five is a programming error, and none is softened. Each names a fact the engine
cannot proceed without, and each is something an application can see and act on. The first of them
is the one that would otherwise be invisible: two compactors acting on one conversation produce no
exception anywhere — the engine simply starts seeding replacements from a history the provider has
discarded — so it is converted into a refusal that says so.

The fourth of them is what the three before it become on the next attempt. Each of them is detected
after the runtime has already processed the turn, so each leaves the runtime holding a turn the
transcript does not; the session therefore latches unusable and names the original failure rather
than reporting a fresh one. The failure surfaces twice — once where it happened, once on every later
attempt — because a host that retries must be refused as clearly as the turn that failed.

### Dependencies

- **AgentKitCore** — supplies `IProviderSession`, `ProviderTurn`, `TranscriptEntry` and
  `ContextUsage`; see _ProviderSession Unit Design_ and _ContextUsage Unit Design_.
- **CopilotSessionObserver** — holds the occupancy reading and the turn's entries; see
  _CopilotSessionObserver Unit Design_.
- **CopilotTurnChannel** — carries the turn and owns the runtime session; see _CopilotTurnChannel
  Unit Design_.
- **Microsoft.Agents.AI.GitHub.Copilot** — supplies the assistant-message event a turn returns; see
  _Microsoft.Agents.AI.GitHub.Copilot Design_.

### Callers

`CopilotProviderSessionFactory` constructs one at the start of a conversation and again at every
rotation; see _CopilotProviderSessionFactory Unit Design_. Because the constructor is internal and
takes a channel only that factory opens, nothing else constructs one — within this system or outside
it.

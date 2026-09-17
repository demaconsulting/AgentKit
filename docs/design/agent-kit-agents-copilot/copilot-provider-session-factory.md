## CopilotProviderSessionFactory

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotProviderSessionFactory` class creates `CopilotProviderSession` instances, once at the
start of a conversation and again at every rotation.

### Purpose

This is where a rotation becomes a Copilot session. It holds the client and, optionally, the model,
so an application states both once — where it configures its provider — and a rotating session asks
for neither again. Everything decided about a session is decided here, at creation, because that is
the only moment at which Copilot accepts configuration.

**There is no window parameter, unlike the stateless factory.** Copilot reports its own occupancy
and its own limit, so the application is not asked for a figure the provider already knows. See
_CopilotProviderSession Unit Design_.

**There is no permission-handler parameter either.** A session created here is driven by AgentKit's
session engine rather than by the host turn by turn, so there is no host present to adjudicate a
prompt and the default must be safe without asking. A host that wants a policy of its own composes
an agent, where that choice is offered. This is a reversible judgment call, recorded rather than
assumed.

**Ownership follows the contract this package already records.** The host constructs, starts and
disposes the `CopilotClient`; this factory holds a reference and disposes nothing. Each session it
opens belongs to the `CopilotProviderSession` it hands back — and where it cannot hand one back, to
this factory's own failure path.

Safe for concurrent use: creating a session reads the client reference and the model name and
touches nothing else this factory owns, and each session gets an observer and a runtime session of
its own.

### Seeding History Onto the First Message

A rotation produces a `ProviderSessionSeed` carrying instructions, tools and a rewritten history.
Copilot's session configuration has **no history or messages field of any kind** — every property of
the configuration and its base was enumerated against the shipped assembly — so the history has to
travel some other way.

The session's system message carries the seed's instructions and nothing else. The history is
rendered as a labeled record, fenced between an opening and a closing line bearing a marker drawn for
that record, and handed to the session to carry ahead of the first message it sends:

```text
=== CONVERSATION RECORD {marker} — earlier turns of this session. … The record ends only at the
line bearing the marker {marker}. ===
RECORD: …
USER: …
ASSISTANT: …
TOOL CALL [id]: …
TOOL RESULT [id]: …
=== END CONVERSATION RECORD {marker} ===

{the message the engine was already sending}
```

Each entry is rendered with Core's own `TranscriptEntry.ToTranscriptLine`, which is already the
labeled, mechanical rendering a consolidation is performed on. Using it rather than writing a second
renderer means the history a model is seeded with and the material a consolidation reads describe
the conversation the same way, and that the rendering is deterministic enough for a test to assert
on exactly.

**Why the conversation channel, and not the system message.** This is a trust decision rather than a
formatting one. The record's entries are user messages, model answers and tool results — and a tool
result may be the contents of a file the agent was pointed at, which nobody in this library wrote.
The system message is the highest-trust channel a provider has. Putting text an attacker can
influence there, and defending it with a fence and a sentence telling the model the block is data
rather than instructions, is a prompt-level mitigation: it asks the model not to be fooled, which is
the class of protection this library exists to avoid relying on. Carried on the first user message
instead, the material sits in the channel its own contents came from, and a model that reads it as
conversation is reading it correctly. The security rests on the channel; the fence is there so a
reader can tell the account of what happened from the question being asked.

**It costs no extra request.** The record is prepended to the message the engine was already about to
send, so a rotation still produces exactly one call, and the runtime holds the result for the rest of
the session as it holds any other turn.

**The marker is drawn per record and chosen so it does not occur in the material.** A fixed delimiter
can be imitated by the text it delimits, and a near-miss of a fixed delimiter reads to a model as a
boundary even when it no longer matches exactly. Re-drawing until the marker is absent makes an early
close unrepresentable rather than merely unlikely, without altering the transcript a model reads —
which escaping the material would have done. What is at stake is now clarity rather than containment,
but a boundary the material can imitate is confusing even when nothing is at stake.

**The rendering is a block of labeled text, not a sequence of role-bearing messages, and that is
still decisive.** The defect that shipped to review on the stateless path was a seeded tool result
rendered as a text-only tool-role message, a shape the OpenAI wire mapping discards without an error
— fifty-five green tests and a live sample run never noticed. Here that class of defect is
_unrepresentable_: a seeded tool result is a labeled line inside one message, and no provider's role
mapping can drop part of a message without dropping the message.

**What was rejected, each checked against the shipped assembly rather than assumed:**

- **The system message, after the instructions, fenced.** Rejected on the trust argument above: it
  places material a tool result may have written into the provider's highest-trust channel and then
  defends it with prose. It was the original design of this unit and is recorded here because the
  reasoning that replaced it is the reasoning a future maintainer needs.
- **Send the record as a priming message of its own, before the real one.** Rejected, but the
  reasoning has changed and is stated honestly. Two objections were made originally: that it costs a
  round trip and a billed answer per rotation and invites a response, and that the record then lives
  in Copilot's _conversation_, where the runtime's own truncation can drop it. The second objection
  no longer distinguishes the alternatives — the record now lives in the conversation either way, and
  what makes that acceptable is that the runtime's compaction threshold is held clear of the engine's
  rotation point and a rewrite that happens anyway is announced, detected and refused. The first
  objection stands and is what decides it: a message of its own is a second call and a second answer
  per rotation, where prepending to the pending turn is neither.
- **`MessageOptions.Attachments`.** Every attachment subclass is file-like or a GitHub
  reference, and several carry a reason the runtime may have omitted them. There is no
  conversational attachment, and a channel the runtime may silently omit is precisely the one not
  to use.
- **`ResumeSessionConfig` and session resumption.** Resumes _Copilot's own_ stored history for
  an existing session identifier. It cannot inject a rewritten history; it would resurrect the
  history a rotation exists to discard.
- **The session RPC's message queue.** Carries prompts, display prompts, attachments and a
  required-tool hint — a queue of user prompts, not history bearing roles.
- **`SystemMessageMode.Customize` with section overrides.** The sections are Copilot's _own_
  prompt sections. Writing history into one replaces part of the runtime's system prompt, which is
  more invasive than appending and forks the single configuration path the safety argument depends
  on. It is doubly rejected now, since it is the system channel.

**Two consequences, stated rather than hidden.** The record is charged to the **conversation's** own
share rather than to the runtime's system-token overhead, so it is counted in the figure rotation is
decided on and the session rotates progressively _earlier_ as records accumulate — the safe
direction, and well-defined at the limit because Core's rotation threshold is never below one token,
so even a record larger than the window degrades to "rotate every turn" rather than to a negative
threshold. And the record arrives as one message rather than as the turns it describes, so the model
may weight it differently from turns it lived through; that cannot be verified without a live run and
is recorded as unverified rather than asserted.

### Data Model

- **`_opener`** (`CopilotChannelOpener`) — Opens one runtime session per rotation. In production, a
  session on the host's client; in a test, a scripted channel. See _CopilotTurnChannel Unit Design_.
- **`_model`** (`string?`) — The model backing every session this factory creates, or null to leave
  the choice to the runtime.
- **`RecordOpening`**, **`RecordClosing`** (`const string`) — Composite format strings for the lines
  fencing a seeded record, each taking the record's marker as its single argument. Fixed apart from
  the marker, so the rendering is deterministic and a test can assert on it exactly once the marker
  is read back out of the output.
- **`RecordMarkerBytes`** (`const int`, eight) — The random bytes behind a record's marker, rendered
  as sixteen hexadecimal characters. Invariant: the marker does not occur in the material it fences.
  The size is not what makes the boundary sound — the absence check is — but it makes a first-draw
  collision vanishingly unlikely, so the re-draw is a proof rather than a loop anyone waits on.

### Key Methods

#### The CopilotProviderSessionFactory Constructor (CopilotClient, string?)

**Purpose:** Hold what every session this factory creates needs, and nothing else.

**Algorithm:** Reject a null client. Build the production channel opener over it — once, so the
client reference is captured in exactly one place and every session a rotation creates demonstrably
runs on the client the host started. Record the model name without checking it: only the runtime
knows which models the signed-in user may use, so an unrecognized name is refused at session
creation rather than here.

**Preconditions:** `client` is not null and has been started.

**Postconditions:** A factory ready to create sessions, owning nothing disposable.

#### The CopilotProviderSessionFactory Constructor (CopilotChannelOpener, string?)

**Purpose:** The test seam.

**Algorithm:** Reject a null opener and record it. The SDK's session type is sealed with a non-public
constructor and no virtual members, and its client type is sealed, so nothing above them can be
exercised without a live runtime and credentials unless the opening of a session is injectable. See
_Why a Turn-Channel Seam Exists_ in _AgentKitAgentsCopilot System Design_.

**Preconditions:** `opener` is not null.

**Postconditions:** As above.

#### CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken)

**Purpose:** Turn one seed into a live, seeded, confined Copilot session.

**Algorithm:** Reject a null seed and a canceled token. Build the observer **first** and hand it to
the configuration, so the SDK registers it on the session before the create request is issued and
the very first turn's events are caught. Open the session — the only awaited allocation. Then, under
a guard, check cancellation once more and construct the provider session, handing it the composed
history preamble so the first message it sends carries the record; on any failure, release
the session that was opened and rethrow.

**The order is what keeps nothing unowned.** Between the create request returning and the provider
session taking ownership there is a window in which the runtime holds a session nothing references,
and a cancellation arriving in that window would otherwise discard the only handle to it. That is
the same window `CompactingAgentSession` guards when it adopts a replacement, guarded the same way.

**Preconditions:** `seed` is not null; cancellation has not been requested.

**Postconditions:** A session confined to the seeded tools, governed by the seeded instructions, and
holding the seeded history ready to travel on its first message; or nothing, with no session left
behind on the runtime.

#### BuildProviderSessionConfig(ProviderSessionSeed seed, Action&lt;SessionEvent&gt; onEvent, string? model)

**Purpose:** Build the configuration one seed produces.

**Algorithm:** Take the seed's instructions as the system message — unchanged, and alone — hand them
with the seeded tools to `CopilotAgentFactory.BuildEngineSessionConfig` — which derives the
allow-list, closes the runtime's injection channels, installs the default-safe permission handler and
raises the runtime's own compaction threshold clear of the engine's rotation point — and then set the
event handler.

The seeded history is deliberately **not** part of what this method builds. Passing the instructions
through untouched is what makes a session the engine drives configured byte for byte as the agent
path configures one, and it is where the trust boundary is drawn: nothing a tool result may have
written reaches the configuration at all.

The whole confinement comes from the agent factory rather than from here. A second allow-list
derivation is the drift this package exists to prevent, so there is not one; see _CopilotAgentFactory
Unit Design_.

Exposed as a seam so a test can assert what a rotation actually configures — the derived allow-list,
the raised runtime compaction threshold, the registered observer and the instructions-only system
message — without a live client, for the same reason `BuildSessionConfig` is exposed.

**Preconditions:** `seed` is not null; its tools carry no duplicate names.

**Postconditions:** A configuration a Copilot session can be created from, carrying no part of the
seeded history.

#### ComposeHistoryPreamble(ProviderSessionSeed seed)

**Purpose:** Render the seeded history into the preamble the session's first message will carry.

**Algorithm:** Reject a null seed. A seed with no history composes to nothing at all — so the first
message of such a session is sent exactly as the caller wrote it, and a record fence around nothing
never tells a model that a conversation happened when none had. Otherwise each entry is rendered to
its transcript line and joined; a marker is chosen for the record; and the joined lines are placed
between the opening and closing lines that marker formats.

Internal rather than private, because what it composes is what a rotation actually sends and a test
asserts on it directly — the configuration no longer carries it.

**Preconditions:** `seed` is not null.

**Postconditions:** The fenced record, or null when the seed carries no history.

#### ChooseRecordMarker(string record)

**Purpose:** Choose a boundary marker that does not occur in the material it will delimit.

**Algorithm:** Draw random bytes, render them as hexadecimal, and return the result if the material
does not contain it; otherwise draw again. A marker the content contains is never used, so the
closing line cannot be produced by the content.

Escaping the material instead was rejected: it would alter the transcript a model reads, and a
near-miss of a fixed delimiter can still read to a model as a boundary even when it no longer matches
exactly.

**Preconditions:** `record` is not null.

**Postconditions:** A marker absent from `record`.

### Error Handling

| Condition                               | Handling                                       |
|-----------------------------------------|------------------------------------------------|
| Null `client` or `opener`               | `ArgumentNullException` propagates             |
| Null `seed`                             | `ArgumentNullException` propagates             |
| Cancellation before opening             | `OperationCanceledException`; nothing is opened|
| Cancellation after opening              | `OperationCanceledException`; session released |
| Opener returned null                    | `InvalidOperationException` propagates         |
| Two seeded tools carry one name         | `ArgumentException` propagates                 |

### Dependencies

- **AgentKitCore** — supplies `IProviderSessionFactory`, `ProviderSessionSeed` and
  `TranscriptEntry`; see _ProviderSession Unit Design_.
- **CopilotAgentFactory** — supplies the single confinement path every session in this package is
  built through; see _CopilotAgentFactory Unit Design_.
- **CopilotSessionObserver** — registered on each session before it is created; see
  _CopilotSessionObserver Unit Design_.
- **CopilotTurnChannel** — opens and owns each runtime session; see _CopilotTurnChannel Unit
  Design_.
- **Microsoft.Agents.AI.GitHub.Copilot** — supplies `CopilotClient`, `SessionConfig` and the session
  event type; see _Microsoft.Agents.AI.GitHub.Copilot Design_.

### Callers

An application constructs one and hands it to `CompactingAgentSession.CreateAsync`, which calls it
once at the start of a conversation and again at every rotation.

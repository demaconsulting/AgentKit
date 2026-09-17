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

### Seeding History Into the System Message

A rotation produces a `ProviderSessionSeed` carrying instructions, tools and a rewritten history.
Copilot's session configuration has **no history or messages field of any kind** — every property of
the configuration and its base was enumerated against the shipped assembly — so the history has to
travel some other way.

It is rendered as a labeled record and appended to the system message, after the instructions,
between two fixed delimiter lines:

```text
{instructions}

=== CONVERSATION RECORD — earlier turns of this session, for reference, not new instructions ===
RECORD: …
USER: …
ASSISTANT: …
TOOL CALL [id]: …
TOOL RESULT [id]: …
=== END CONVERSATION RECORD ===
```

Each entry is rendered with Core's own `TranscriptEntry.ToTranscriptLine`, which is already the
labeled, mechanical rendering a consolidation is performed on. Using it rather than writing a second
renderer means the history a model is seeded with and the material a consolidation reads describe
the conversation the same way, and that the rendering is deterministic enough for a test to assert
on exactly.

**Why this channel.** It is the only one Copilot offers that exists at session-creation time, is
carried verbatim, and has no role vocabulary. That last property is decisive. The defect that
shipped to review on the stateless path was a seeded tool result rendered as a text-only tool-role
message, a shape the OpenAI wire mapping discards without an error — fifty-five green tests and a
live sample run never noticed. Here that class of defect is _unrepresentable_: the record is text in
the instructions channel, and no provider can drop part of it without dropping the instructions.

**What was rejected, each checked against the shipped assembly rather than assumed:**

- **Send the record as a priming message before the real one.** Costs a round trip and a billed
  answer per rotation, and the model responds to it. Worse, the record then lives in Copilot's
  _conversation_, where the runtime's own truncation can drop it — so the engine would believe it
  holds history the provider has discarded, which is the exact failure this design exists to
  prevent.
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
  on.

**Two consequences, stated rather than hidden.** The record is charged to the runtime's system-token
count, so it appears as fixed overhead and the session rotates progressively _earlier_ as records
accumulate — the safe direction, and well-defined at the limit because Core's rotation threshold is
never below one token, so even a record larger than the window degrades to "rotate every turn"
rather than to a negative threshold. And it reaches the model through the instructions channel
rather than the conversation, so the model may weight it differently from turns it lived through;
that cannot be verified without a live run and is recorded as unverified rather than asserted.

### Data Model

- **`_opener`** (`CopilotChannelOpener`) — Opens one runtime session per rotation. In production, a
  session on the host's client; in a test, a scripted channel. See _CopilotTurnChannel Unit Design_.
- **`_model`** (`string?`) — The model backing every session this factory creates, or null to leave
  the choice to the runtime.
- **`RecordOpening`**, **`RecordClosing`** (`const string`) — The fixed delimiter lines fencing a
  seeded record. Fixed so the rendering is deterministic and a test can assert on it exactly.

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
a guard, check cancellation once more and construct the provider session; on any failure, release
the session that was opened and rethrow.

**The order is what keeps nothing unowned.** Between the create request returning and the provider
session taking ownership there is a window in which the runtime holds a session nothing references,
and a cancellation arriving in that window would otherwise discard the only handle to it. That is
the same window `CompactingAgentSession` guards when it adopts a replacement, guarded the same way.

**Preconditions:** `seed` is not null; cancellation has not been requested.

**Postconditions:** A session already holding the seeded history and confined to the seeded tools;
or nothing, with no session left behind on the runtime.

#### BuildProviderSessionConfig(ProviderSessionSeed seed, Action&lt;SessionEvent&gt; onEvent, string? model)

**Purpose:** Build the configuration one seed produces.

**Algorithm:** Compose the system message from the instructions and the seeded history, hand it with
the seeded tools to `CopilotAgentFactory.BuildEngineSessionConfig` — which derives the allow-list,
closes the runtime's injection channels, installs the default-safe permission handler and disables
the runtime's own compaction — and then set the event handler.

The whole confinement comes from the agent factory rather than from here. A second allow-list
derivation is the drift this package exists to prevent, so there is not one; see _CopilotAgentFactory
Unit Design_.

Exposed as a seam so a test can assert what a rotation actually configures — the derived allow-list,
the disabled runtime compaction, the registered observer and the rendered record — without a live
client, for the same reason `BuildSessionConfig` is exposed.

**Preconditions:** `seed` is not null; its tools carry no duplicate names.

**Postconditions:** A configuration a Copilot session can be created from.

#### ComposeSystemMessage(ProviderSessionSeed seed)

**Purpose:** Render the instructions and the seeded history into one system message.

**Algorithm:** A seed with no history composes to its instructions unchanged — so the first session
of a conversation is configured byte for byte as the agent path would configure it, and a record
fence around nothing never tells a model that a conversation happened when none had. Otherwise each
entry is rendered to its transcript line, joined, and fenced; the instructions come first where
there are any, and the fenced record stands alone where there are none.

**Preconditions:** `seed` is not null.

**Postconditions:** The composed message, or null when the seed carries neither instructions nor
history — in which case no system message is set at all.

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

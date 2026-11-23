## CopilotSummarizer

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotSummarizer` class consolidates session history on a short-lived, tool-free GitHub Copilot
session of its own.

### Purpose

**Shipped because the mechanism is this library's to guarantee.** Compaction cannot happen without a
summarizer, so a provider AgentKit carries a session for but ships no summarizer for is a provider on
which compaction quietly never happens. This is that part, using the consolidation prompt Core
publishes and the aggressiveness levels the session drives it with. An application remains free to
write its own — the contract is one method — but it should not have to.

**It runs out of the session it is compacting, which is the whole reason the contract takes a
summarizer at all.** A consolidation sent through the live conversation would spend the very context
it exists to reclaim, and would itself count toward the occupancy that triggered the rotation. So
each call creates a session of its own, uses it once, and releases it — on the failure path as well
as the successful one. That also makes a consolidation a pure function of its material: nothing
carries over from the last one.

**The session it creates carries no tools at all.** A consolidation is a pure function from material
to a record of it; there is nothing for a tool to do, and a tool that could act would be acting
outside everything the conversation's own confinement was reasoned about. An empty tool list derives
an empty allow-list and a permission handler that approves nothing, which is the strongest
confinement this package can express rather than the weakest — and it is why
`CopilotAgentFactory.BuildEngineSessionConfig` accepts an empty list where the agent path refuses
one.

**The runtime's own compaction is held clear of the rotation point on this session too.** A
consolidation is one prompt and one answer; there is nothing to compact, and a runtime that reshaped
the material mid-consolidation would produce a record of something other than what it was given. The
session options come whole from `CopilotAgentFactory.BuildEngineSessionConfig`, so a consolidation
session is configured exactly as a conversation session is.

**There is no working directory.** Unlike `CopilotProviderSessionFactory`, the summarizer takes none:
a consolidation session offers no tools, so there is nothing a working directory could locate, and the
runtime's default is left in place.

**Ownership.** The `CopilotRuntime` is the host's and is disposed by the host; every session this
summarizer creates is its own and is released and deleted by it.

Safe for concurrent use: each call creates a session of its own and shares nothing but the runtime
and the model name.

### Data Model

- **`_opener`** (`CopilotChannelOpener`) — Opens one short-lived runtime session per consolidation.
  See *CopilotTurnChannel Unit Design*.
- **`_model`** (`string?`) — The model performing each consolidation, or null to leave the choice to
  the runtime.
- **`_selection`** (`ModelSelection?`) — The validated model selection performing each consolidation,
  or null when the host named a model, or none, instead.
- **`_signIn`** (`CopilotSignInPlan?`) — The sign-in of the runtime the consolidations run on, whose
  tokens a relayed failure is redacted of, or null to redact token-shaped text only (`runtime.SignIn`
  in production).

### Key Methods

#### The CopilotSummarizer Constructor (CopilotRuntime, string?, ModelSelection?)

**Purpose:** Hold what every consolidation needs, and nothing else.

**Algorithm:** Reject a null runtime, check it is not disposed (`CopilotRuntime.ThrowIfDisposed`),
refuse an ambiguous or foreign selection, and one carrying a context window or an output limit,
through `CopilotAgentFactory.ValidateSelection` with `windowAccounted: false`, and build
the production channel opener over the runtime — one that deletes each session when it is
released, because a consolidation session is finished with once it has answered. The opener is handed
the runtime, not its client, and reads the client at each open and release: the SDK restarts a
disposed client on demand, so a summarizer that held the client would bring a runtime the host had
disposed back to life. Record the runtime's sign-in (`runtime.SignIn`), whose tokens a relayed
failure is redacted of. Record the model name without checking it, for the same reason the session factory
does: only the runtime knows which models the signed-in user may use.

The model is named separately from the conversation's because consolidation is summarization rather
than reasoning, so a smaller and cheaper model is usually the right choice — and because it is a
different *conversation*, so changing it alters nothing the agent can observe.

A `selection` may be given instead of a model name. It is a model the host's user chose from a
`CopilotModelCatalog`, validated; it names the model each consolidation runs on, and its reasoning
effort and context tier, each only when chosen, are set on each consolidation session. The same rules
as the agent factory's apply: a cheaper model is chosen and validated the same way, and a model named
both ways is refused. A consolidation session is accounted against no window, so a selection's
context window has nothing to lower and is refused; the runtime takes no output limit, so a
selection's `MaxOutputTokens` is refused too, rather than either being dropped silently.

**Preconditions:** `runtime` is not null and not disposed; a model is named at most once; a
selection carries no context window and no output limit.

**Postconditions:** A summarizer ready to consolidate, owning nothing disposable.

#### The CopilotSummarizer Constructor (CopilotChannelOpener, string?, ModelSelection?, CopilotSignInPlan?)

**Purpose:** The test seam, for the reason *CopilotTurnChannel Unit Design* gives.

**Algorithm:** Reject a null opener, refuse a selection as the public constructor does,
and record the opener, the model, the selection and the sign-in, which is null unless a test passes
one.

**Preconditions:** `opener` is not null.

**Postconditions:** As above.

#### ConsolidateAsync(ConsolidationRequest request, CancellationToken cancellationToken)

**Purpose:** Produce the record that consolidates one span of history.

**Algorithm:** Reject a null request and a canceled token. Build fresh, tool-free engine session
options carrying the model or the selection, open a session from them through
`CopilotSignIn.RelayAsync` — so a failure to open it is relayed redacted, as a failure of the send is
below — send the composed
consolidation prompt as the only message —
a `MessageOptions` carrying the prompt and nothing else — and return the answer. A failure of the
send is relayed through `CopilotSignIn.RedactRelayed(failure, signIn)`: a new exception of the same
kind with a redacted message and the failure's redacted copy as its cause, never the SDK's own
exception, whose message, chain or RPC data can quote a token; a cancellation carrying nothing to
redact passes unchanged. Release the session
in a `finally`, so a consolidation that failed,
timed out or was canceled leaves nothing behind on the runtime — which matters because a long
conversation performs one of these per rotation, and a session leaked here would accumulate for
exactly as long as the conversation this exists to prolong.

An empty answer — including a session that went idle without answering at all — is returned as an
empty record rather than raised. The engine already treats a consolidation it could not obtain as
material to keep rather than material to lose, and a model declining to answer is a thing that
happens rather than a defect to escalate. Note the deliberate asymmetry with
`CopilotProviderSession.SendAsync`, which refuses the same condition: a silent turn loses the user's
question, while a silent consolidation loses only an optimization the engine already has a fallback
for.

**Preconditions:** `request` is not null; cancellation has not been requested.

**Postconditions:** The consolidated record, or the empty string; and no session left open.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `runtime` or `opener` | `ArgumentNullException` propagates |
| Disposed `runtime` | `ObjectDisposedException` propagates |
| Both a model name and a selection, or a foreign selection | `ArgumentException` naming `selection` propagates |
| A selection carrying a context window or an output limit | `ArgumentException` naming `selection` propagates |
| `runtime` disposed after construction | `ObjectDisposedException` from the next consolidation; nothing started |
| Null `request` | `ArgumentNullException` propagates |
| Cancellation before opening | `OperationCanceledException`; nothing is opened |
| Opener returned null | `InvalidOperationException` propagates |
| Opening the session fails | Relayed as a redacted copy of the same kind (`RelayAsync`); nothing to release |
| Session answered nothing | Empty record returned |
| Failure during the turn | Relayed as a redacted copy of the same kind (`RedactRelayed`); the session is released |

### Dependencies

- **AgentKitCore** — supplies `ISummarizer`, `ConsolidationRequest` and `ConsolidationPrompt`; see
  *Summarizer Unit Design*; and `ModelSelection`; see *ModelSettings Unit Design*.
- **CopilotAgentFactory** — supplies the tool-free engine session options; see
  *CopilotAgentFactory Unit Design*.
- **CopilotSignIn** — relays a consolidation's runtime or SDK failure, opening its session or taking
  its turn, redacted with the runtime's sign-in (`RelayAsync(operation, plan)`,
  `RedactRelayed(failure, plan)`); see *CopilotSignIn Unit Design*.
- **CopilotTurnChannel** — opens and owns each short-lived runtime session; see *CopilotTurnChannel
  Unit Design*.
- **CopilotRuntime** — the runtime every consolidation runs on, whose client the opener reads at each
  open and release; see *CopilotRuntime Unit Design*.
- **Microsoft.Copilot.SDK** — supplies `CopilotClient` and `MessageOptions`; see
  *Microsoft.Copilot.SDK Design*.

### Callers

An application hands one to `AgentSessionOptions`, and the rotation engine calls it once per
consolidation.

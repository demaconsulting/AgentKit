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

**The runtime's own compaction is disabled on this session too.** A consolidation is one prompt and
one answer; there is nothing to compact, and a runtime that reshaped the material mid-consolidation
would produce a record of something other than what it was given.

**Ownership.** The `CopilotClient` is the host's and is disposed by the host; every session this
summarizer creates is its own and is released by it.

Safe for concurrent use: each call creates a session of its own and shares nothing but the client
reference and the model name.

### Data Model

- **`_opener`** (`CopilotChannelOpener`) — Opens one short-lived runtime session per consolidation.
  See _CopilotTurnChannel Unit Design_.
- **`_model`** (`string?`) — The model performing each consolidation, or null to leave the choice to
  the runtime.

### Key Methods

#### The CopilotSummarizer Constructor (CopilotClient, string?)

**Purpose:** Hold what every consolidation needs, and nothing else.

**Algorithm:** Reject a null client and build the production channel opener over it. Record the
model name without checking it, for the same reason the session factory does: only the runtime knows
which models the signed-in user may use.

The model is named separately from the conversation's because consolidation is summarization rather
than reasoning, so a smaller and cheaper model is usually the right choice — and because it is a
different _conversation_, so changing it alters nothing the agent can observe.

**Preconditions:** `client` is not null and has been started.

**Postconditions:** A summarizer ready to consolidate, owning nothing disposable.

#### The CopilotSummarizer Constructor (CopilotChannelOpener, string?)

**Purpose:** The test seam, for the reason _CopilotTurnChannel Unit Design_ gives.

**Algorithm:** Reject a null opener and record it.

**Preconditions:** `opener` is not null.

**Postconditions:** As above.

#### ConsolidateAsync(ConsolidationRequest request, CancellationToken cancellationToken)

**Purpose:** Produce the record that consolidates one span of history.

**Algorithm:** Reject a null request and a canceled token. Build a tool-free engine session
configuration, open a session from it, send the composed consolidation prompt as that session's only
message, and return the answer. Release the session in a `finally`, so a consolidation that failed,
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

| Condition                     | Handling                                             |
|-------------------------------|------------------------------------------------------|
| Null `client` or `opener`     | `ArgumentNullException` propagates                   |
| Null `request`                | `ArgumentNullException` propagates                   |
| Cancellation before opening   | `OperationCanceledException`; nothing is opened      |
| Opener returned null          | `InvalidOperationException` propagates               |
| Session answered nothing      | Empty record returned                                |
| Failure during the turn       | Propagates; the session is released first            |

### Dependencies

- **AgentKitCore** — supplies `ISummarizer`, `ConsolidationRequest` and `ConsolidationPrompt`; see
  _Summarizer Unit Design_.
- **CopilotAgentFactory** — supplies the tool-free engine session configuration; see
  _CopilotAgentFactory Unit Design_.
- **CopilotTurnChannel** — opens and owns each short-lived runtime session; see _CopilotTurnChannel
  Unit Design_.
- **Microsoft.Agents.AI.GitHub.Copilot** — supplies `CopilotClient`; see
  _Microsoft.Agents.AI.GitHub.Copilot Design_.

### Callers

An application hands one to `AgentSessionOptions`, and the rotation engine calls it once per
consolidation.

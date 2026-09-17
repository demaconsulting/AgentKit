## ChatClientSummarizer

![AgentKit ChatClient Agents Structure](AgentKitAgentsChatClientView.svg)

The `ChatClientSummarizer` class consolidates session history through a `Microsoft.Extensions.AI`
`IChatClient`.

### Purpose

`ChatClientSummarizer` ships because the mechanism is this library's to guarantee. Compaction cannot
happen without a summarizer, so requiring every application to write one left the promise with a hole
in it: the part of compaction that decides what survives was the part nobody was given. This is that
part, using the consolidation prompt AgentKit Core publishes and the aggressiveness clauses the
session drives it with. An application remains free to write its own — the contract is one method —
but it should not have to.

It runs outside the session it is compacting. The client here is the application's to choose and is
deliberately separate from the one carrying the conversation: a consolidation sent through the live
session would consume the very context it exists to reclaim, which was measured on a real backend and
is the reason the session contract takes a summarizer at all rather than reducing in place. A smaller
and cheaper model is usually the right choice, because consolidation is summarization rather than
reasoning.

The class implements `ISummarizer` and is safe for concurrent use if the underlying client is, as
that contract requires.

### Data Model

- **`_client`** (`IChatClient`) — The client each consolidation is sent through. Owned by the
  application and never disposed here. Invariant: not null.

The class holds nothing else. A summarizer that remembered anything between consolidations would make
a record depend on what the same instance had been asked before, which the contract forbids.

### Key Methods

#### The ChatClientSummarizer Constructor (IChatClient client)

**Purpose:** Record the client each consolidation is sent through.

**Algorithm:** Reject a null client, then record it.

**Preconditions:** `client` is not null, and should not be the client carrying the conversation being
compacted.

**Postconditions:** The summarizer is ready to consolidate.

#### ConsolidateAsync(ConsolidationRequest request, CancellationToken cancellationToken)

**Purpose:** Produce the record that consolidates the request's material.

**Algorithm:** Reject a null request and a canceled token. Compose the request into the full prompt
through `ConsolidationPrompt.Compose` and send it as a single user message, with no options and no
prior messages. Return the answer's text, or the empty string where the model answered with nothing.

No tools are offered and no history is carried: a consolidation is a pure function from the material
to a record of it, which is what lets a rotation be replayed and reasoned about. An empty answer is
returned as one rather than turned into an exception, because the engine treats a consolidation it
could not obtain as material to keep rather than material to lose, and a model declining to answer is
a thing that happens rather than a defect to escalate.

**Preconditions:** `request` is not null; cancellation has not been requested.

**Postconditions:** The consolidated record, never null. The client received exactly one message,
carrying exactly the composed prompt.

### Error Handling

| Condition                         | Handling                                |
|-----------------------------------|-----------------------------------------|
| Null `client`                     | `ArgumentNullException` propagates      |
| Null `request`                    | `ArgumentNullException` propagates      |
| Cancellation before the request   | `OperationCanceledException` propagates |
| Model answered with nothing       | Returned as an empty record             |
| Client failure during the request | Propagates unchanged to the engine      |

A client failure is deliberately not wrapped: the session engine already treats a consolidation it
could not obtain as material to keep, and the application is better served by the provider's own
failure than by this library's paraphrase of it.

### Dependencies

- **AgentKitCore** — supplies `ISummarizer`, `ConsolidationRequest` and `ConsolidationPrompt`; see
  _Summarizer Unit Design_.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient` and `ChatMessage`; see
  _Microsoft.Extensions.AI.Abstractions Design_.

### Callers

An application constructs one and hands it to `AgentSessionOptions`; the rotation engine calls
`ConsolidateAsync` once per consolidation a rotation performs; see _RotationEngine Unit Design_.
Within this system nothing else calls it.

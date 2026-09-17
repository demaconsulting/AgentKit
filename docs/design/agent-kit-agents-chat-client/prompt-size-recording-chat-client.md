## PromptSizeRecordingChatClient

![AgentKit ChatClient Agents Structure](AgentKitAgentsChatClientView.svg)

The `PromptSizeRecordingChatClient` class records the prompt size of each individual request a turn
makes, so occupancy is read from the last one rather than from the sum of them all.

### Purpose

`PromptSizeRecordingChatClient` exists because **a turn is not one request**. When the model calls
tools, the function-invoking layer answers them and asks again, as many times as the model keeps
calling tools, and the `ChatResponse` it finally returns carries usage *aggregated across every
request it made*. A turn with six tool calls therefore reports roughly six times the conversation.

Read as occupancy that figure is badly wrong, and wrong in the direction that does harm: a tool-using
agent — which is every agent this library exists for — would conclude its window was full on its
first turn, rotate on every turn after it, compact harder each time, and eventually discard history
to reclaim room it never occupied. That is precisely the failure the session engine exists to
prevent.

What a session actually needs is how much of the window the conversation occupies, and that is the
prompt of the **last** request the turn made: by then the tool calls and their results are part of
the conversation being sent. So this class sits underneath the function-invoking layer, where each
request is visible separately, and keeps the most recent figure.

The class derives from `DelegatingChatClient` and is internal: an application never constructs one.
`ChatClientProviderSessionFactory` builds it, one per session, and hands it to the session it
creates; see *ChatClientProviderSessionFactory Unit Design*. Instances are not safe for concurrent
use, because the figure they hold belongs to one conversation.

### Data Model

- **`LastPromptTokens`** (`long?`) — The prompt tokens the most recent request reported, or null
  when no request has reported any. Invariant: set only from a figure a request actually reported,
  never estimated, and never cleared once set.

The class holds nothing else. It observes a conversation rather than participating in one.

### Key Methods

#### GetResponseAsync(IEnumerable&lt;ChatMessage&gt; messages, ChatOptions? options, CancellationToken cancellationToken)

**Purpose:** Pass the request down and record what the answer reported about it.

**Algorithm:** Forward the request to the inner client. Where the answer carries an input-token
count, record it as the most recent prompt size. Return the answer unchanged.

An answer reporting no usage leaves the previous figure standing rather than clearing it. A
tool-calling turn makes several requests and a provider need not report usage on every one of them,
so clearing the figure there would make a session that had already been told the truth refuse the
turn that followed. A provider that reports usage on no request at all still leaves nothing recorded,
which is the condition the session refuses.

**Preconditions:** None beyond those of the inner client.

**Postconditions:** The answer is returned exactly as the inner client produced it, and the recorded
figure is the most recent one any request has reported.

#### GetStreamingResponseAsync(IEnumerable&lt;ChatMessage&gt; messages, ChatOptions? options, CancellationToken cancellationToken)

**Purpose:** Do the same for a streamed answer, which reports usage differently.

**Algorithm:** Forward the request and yield every update through unchanged. Where an update carries
usage content holding an input-token count, record it as the most recent prompt size.

A streaming provider reports usage as one more update in the sequence rather than on a response, so
the figure has to be taken from the stream. The updates are yielded on rather than collected, because
this class observes the exchange and must not change what a caller receives or when.

**Preconditions:** None beyond those of the inner client.

**Postconditions:** Every update the inner client produced reaches the caller, in order, and the
recorded figure is the most recent reported input-token count.

### Error Handling

| Condition                                  | Handling                                    |
|--------------------------------------------|---------------------------------------------|
| Inner client fails a request               | The exception propagates unchanged          |
| Answer reports no usage                    | The previously recorded figure is kept      |
| No request has reported usage              | The recorded figure remains null            |

Nothing is caught here. This class reports what a provider said and adds no failure mode of its own;
a request that failed reports nothing about a prompt, and a session reading an unchanged figure after
a failed turn is reading the last turn that succeeded.

### Dependencies

- **Microsoft.Extensions.AI.Abstractions** — supplies `DelegatingChatClient`, `IChatClient`,
  `ChatMessage`, `ChatOptions`, `ChatResponse`, `ChatResponseUpdate` and `UsageContent`; see
  *Microsoft.Extensions.AI.Abstractions Design*.

### Callers

`ChatClientProviderSessionFactory` constructs one per session and hands it to the session alongside
the pipeline built over it; see *ChatClientProviderSessionFactory Unit Design*.
`ChatClientProviderSession` reads `LastPromptTokens` to answer for the window; see
*ChatClientProviderSession Unit Design*. Within this system nothing else uses it, and being internal
it is reachable from nowhere outside.

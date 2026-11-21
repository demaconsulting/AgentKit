## ImagePromotingChatClient

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ImagePromotingChatClient` class makes an image a tool returned visible to a provider whose
tool-result channel cannot carry one, by promoting it onto a following user message.

### Purpose

`ImagePromotingChatClient` exists because delivering an image to a model is not finished when a
tool returns one. Providers differ in where they accept image content, and the difference is
silent.

**The asymmetry is observed rather than theoretical.** A tool returning image content reaches the
model unchanged on the GitHub Copilot path, whose runtime converts a binary tool result into
content the model sees. On the Ollama path the identical content is preserved by the framework,
arrives intact in the function result, and is then dropped at the wire — that interface accepts
images on messages, not in tool responses. The model answers anyway, describing an image it never
received, and nothing in the exchange reports an error.

**It is a channel problem, not a capability one.** A control exchange placing the same bytes on a
user message was described correctly by the same model on the same provider. Moving the content
onto the channel every provider honors is therefore sufficient, and no re-encoding, resizing or
captioning of the image is involved. With the decorator installed, the same model described the
tool-returned image correctly.

**The image leaves the tool result.** Promotion alone is not enough when the provider adapter
flattens what it cannot carry into text. OllamaSharp serializes image content found in a function
result into the tool message as a base64 data URI, so without further action the image travels
twice — once usable, on the user message, and once as text. A 316 KB image produced a
421,629-character tool message that truncated the context window and lost the user's question.
The decorator therefore forwards the tool result with each promoted image replaced by a short
placeholder naming its media type and size, so the tool message stays small whatever the size of
the image.

The unit is opt-in. A host whose provider already delivers images from tool results installs
nothing and pays nothing; a host targeting a provider that does not wraps its chat client once.
Core supplies no agent loop of its own into which the decorator could be installed automatically,
and inventing one to install it would be a far larger change than the problem warrants.

The class holds no mutable state of its own and is as safe for concurrent use as the client it
wraps.

### Data Model

`ImagePromotingChatClient` adds no state to the client it decorates. Its only data are fixed
constants:

| Member | Type | Description |
| --- | --- | --- |
| `PromotionNotice` | `const string` | The fixed text introducing a promoted image. |
| `PlaceholderFormat` | `const string` | The text left in a tool result in place of a promoted image. |
| `ImageMediaType` | `const string` | The top-level media type identifying content to promote. |

The inner client is held by the base `DelegatingChatClient` and is never replaced.

Invariants:

- Every message the decorator receives appears in the forwarded conversation, in the order it was
  received; a tool message carrying a promoted image appears as a copy with that image replaced.
- A promoted message is inserted immediately after the tool result it was derived from.
- The content instances on a promoted message are the same instances the tool result carried;
  nothing is copied or re-encoded.
- No forwarded tool result carries a promoted image; each is replaced by
  `[Image promoted to the following user message: {media type}, {bytes} bytes.]`, with the byte
  count formatted with invariant-culture group separators.
- The caller's messages, their content lists and their function results are never modified.

### Key Methods

#### ImagePromotingChatClient(IChatClient innerClient)

Wraps a chat client. The inner client is required: a decorator with nothing to decorate can
neither forward a request nor report a response, so an absent client is a defect in the composing
application.

**Throws:** `ArgumentNullException` when `innerClient` is null.

#### GetResponseAsync(IEnumerable&lt;ChatMessage&gt; messages, ChatOptions? options, CancellationToken cancellationToken)

Forwards a request whose message sequence has been rewritten by `Promote`. The options and the
cancellation token are forwarded unchanged.

**Postconditions:** the wrapped client receives the rewritten conversation; the response is
returned exactly as the wrapped client produced it.

#### GetStreamingResponseAsync(IEnumerable&lt;ChatMessage&gt; messages, ChatOptions? options, CancellationToken cancellationToken)

The same contract for a streamed response. The two paths rewrite identically, because the
difference between them is in how a response is delivered rather than in what the request
carries; a host that streams would otherwise silently lose the control.

#### Promote(IEnumerable&lt;ChatMessage&gt; messages)

Private helper producing the rewritten conversation.

**Algorithm:**

1. Forward any message whose role is not `Tool` as the same instance; only a tool message can
   carry a function result, and only a function result can carry content the provider is about to
   discard or flatten into text.
2. For a tool message, rebuild its content list, replacing each `FunctionResultContent` with the
   result of `ReplaceImages` and collecting the images found.
3. When none was found, forward the original message — a text-only result needs no announcement
   and no copy.
4. Otherwise forward a clone of the message whose `Contents` is the rebuilt list (a clone shares
   the original's list, so a new one is assigned rather than edited), then append a `User` message
   holding the fixed announcement followed by the same image instances.

**Postconditions:** message order is preserved, no original message is dropped, and no message,
content list or function result the caller holds is altered.

**Throws:** `ArgumentNullException` when `messages` is null.

#### ReplaceImages(FunctionResultContent result, List&lt;DataContent&gt; images)

Private helper reading the image content a function result carries, in either of the two shapes a
guarded tool produces, and returning the result to forward:

- a `DataContent` image on its own becomes a copy whose result is the placeholder string;
- a sequence of `AIContent` becomes a copy whose result is a new list with every part in its
  original order, each image part replaced by a `TextContent` placeholder;
- any other shape, or a sequence holding no image, is returned as the same instance.

Non-image content is deliberately left where it is — a document a provider accepts in a tool
result should not be duplicated onto a user message, and one it does not accept is not made
readable by moving it. The images found are appended to `images` in the order they appear.

#### Placeholder(DataContent image)

Private helper formatting `PlaceholderFormat` with the image's media type and byte count, using
the invariant culture so the text does not vary with the host's locale.

#### CopyWithResult(FunctionResultContent original, object value)

Private helper creating a `FunctionResultContent` with the original's call identifier and the
replacement value, copying the exception, raw representation, annotations and additional
properties so that a provider adapter or telemetry reading them sees what the original carried.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `innerClient` at construction | `ArgumentNullException` propagates |
| Null `messages` | `ArgumentNullException` propagates |
| Tool result carrying no image | Not an error; nothing is added |
| Tool result carrying non-image content | Not an error; the content is left where it is |
| Failure inside the wrapped client | Propagates unchanged; this unit adds no handling |

The dividing line is the one the rest of the system draws: a missing client or a missing
conversation is a programming error in the host, while anything a model or a tool produced is
ordinary traffic. Nothing here is reported to a model, because this unit is beneath the
conversation rather than inside it, so it has no refusal channel of its own and needs none.

### Design Constraints

**The decorator must sit beneath the function-invocation loop.** It observes the conversation
after tool results have been appended. Placed above the loop it would see the request before any
tool had run and would have nothing to promote — a mistake that produces no error, only an
absence.

**Message order is the contract.** The model reads an exchange in sequence, so a promoted image
must be inserted immediately after the tool result it came from. Promoted to the end of the
conversation it would be read as belonging to whichever turn happened to be last.

**The announcement is fixed text and adds no host detail.** It names the tool as the source so the
transcript reads sensibly, and interpolates nothing — not the tool name, not the file, not the
media type. The placeholder left in the tool result interpolates only the media type and the byte
count, both values the image itself carries. The transcript leaves this process, so the same
discipline the denial messages keep applies here.

**The caller's history is never edited.** The history a host or the function-invocation loop
holds is replayed on every request and may be persisted, so it must keep recording what the tool
returned. Editing it in place would also destroy the image the next request needs to promote
again.

**A text-only exchange must cost nothing.** A decorator a host can leave installed for every
conversation is one it will leave installed; a decorator that appends an announcement saying no
image was attached is one it will remove.

**No new dependency.** `DelegatingChatClient`, `DataContent.HasTopLevelMediaType` and
`FunctionResultContent` all come from `Microsoft.Extensions.AI.Abstractions`, which Core already
takes. A registration extension for the chat client builder was considered and **rejected**: that
builder lives in `Microsoft.Extensions.AI` rather than in the abstractions package, so offering
the convenience would cost Core a second runtime dependency that every consumer would inherit.

**The library writes nothing to the console.** The exploratory version of this code printed a line
each time it promoted an image, which was useful while the behavior was being established and is
not a library's business.

### Dependencies

- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`, `DelegatingChatClient`,
  `ChatMessage`, `ChatRole`, `AIContent`, `TextContent`, `DataContent` and
  `FunctionResultContent`; see *Microsoft.Extensions.AI.Abstractions Design*.

No unit within AgentKitCore is used by this one. It shares the content vocabulary with
*ToolResult Unit Design* — it reads the caption-plus-content shape those constructors produce —
but takes no compile-time dependency on it.

### Callers

`ImagePromotingChatClient` is a public API entry point: a host constructs one around its own chat
client, beneath the function-invocation loop, when it has chosen a provider that does not carry
images out of tool results. Within this system nothing else calls it.

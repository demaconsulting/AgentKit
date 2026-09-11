## ChatClientAgentFactory

![AgentKit ChatClient Agents Structure](AgentKitAgentsChatClientView.svg)

The `ChatClientAgentFactory` class builds a Microsoft Agent Framework `AIAgent` from any
`IChatClient` and a supplied tool list, installing the image-promoting decorator on every agent it
builds.

### Purpose

`ChatClientAgentFactory` is the whole of the AgentKitAgentsChatClient system. A provider reached
through an `IChatClient` carries no tools of its own, so handing it the supplied tools is the whole
job of turning it into an agent — except for the one thing that would otherwise break images
silently. The factory absorbs that concern so an application never has to know it exists: it wraps
the supplied client in Core's `ImagePromotingChatClient` before constructing the agent, on every
path, with no option to omit it.

The class is static and holds no state.

### Key Methods

#### Create(IChatClient client, IList&lt;AIFunction&gt; tools, string? instructions, string? name)

The single public entry point. Validates its arguments, wraps `client` in the image-promoting
decorator, and constructs a `ChatClientAgent` over the wrapped client carrying `instructions`,
`name`, and the supplied tools.

**Algorithm:**

1. Reject a null `client`.
2. Validate `tools` (see `ValidateTools`).
3. Wrap `client` through `WrapWithImagePromotion`.
4. Construct and return a `ChatClientAgent` over the wrapped client, passing the instructions, the
   name, and the tools cast to the framework's tool type.

**Ownership:** the caller retains ownership of the client it supplied. The factory wraps that
client but constructs nothing else the caller must dispose; the returned agent's lifetime, and the
client beneath it, remain the host's to manage.

**Throws:** `ArgumentNullException` when `client` or `tools` is null, or a tool is null;
`ArgumentException` when `tools` is empty or two tools share a name.

#### WrapWithImagePromotion(IChatClient client)

Internal seam returning `new ImagePromotingChatClient(client)`. It exists as a named, internal
method for one reason: so a test can assert the decorator is installed — the guarantee this package
exists for — without reaching inside a constructed agent, whose chat client the framework may
further wrap with its own function-invocation middleware. The wrap is unconditional; there is no
code path in the factory that produces an agent whose chat client is not image-promoting.

**Throws:** `ArgumentNullException` when `client` is null.

#### ValidateTools(IList&lt;AIFunction&gt; tools)

Internal helper rejecting a tool list a well-formed agent could not be built from: a null list, an
empty list, a list containing a null entry, or a list carrying two tools of the same name. Names are
compared with ordinal string comparison. The collision is refused here, where the application
composed its tools, rather than discovered from a model's undefined choice between two identically
named tools later.

### Design Constraints

**The decorator must be installed unconditionally and beneath the loop.** `ChatClientAgent` runs
its function-invocation loop above the `IChatClient` it is given, so wrapping the client before
handing it to the agent places the decorator beneath that loop — exactly where a tool result, and
the image it may carry, has already been appended to the conversation. There is no parameter to
disable the wrap, because the entire value of the package is that a caller cannot forget it.

**No image re-encoding or agent runtime of its own.** The factory adds a decorator and constructs
the framework's agent; it does not resize, caption, or re-encode an image, and it does not
implement a tool-calling loop. Those behaviors belong to Core's decorator and to Microsoft Agent
Framework respectively.

**Thin by intent.** The factory validates, wraps, and constructs. Any behavior beyond that would
duplicate the framework it adapts.

### Error Handling

| Condition                              | Handling                                   |
|----------------------------------------|--------------------------------------------|
| Null `client`                          | `ArgumentNullException` propagates         |
| Null `tools`, or a null tool entry     | `ArgumentNullException` propagates         |
| Empty `tools`                          | `ArgumentException` propagates             |
| Two tools sharing a name               | `ArgumentException` propagates             |

Every rejected condition is a defect in the composing application, surfaced where the host wrote it
rather than at the first conversation.

### Dependencies

- **AgentKitCore** — supplies `ImagePromotingChatClient`; see _ImagePromotingChatClient Unit
  Design_.
- **Microsoft.Agents.AI** — supplies `AIAgent` and `ChatClientAgent`; see _Microsoft.Agents.AI
  Design_.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`, `AIFunction`, and `AITool`.

### Callers

`ChatClientAgentFactory.Create` is a public API entry point. An application calls it once to obtain
an agent for a provider reached through an `IChatClient`. Within this system nothing else calls it.

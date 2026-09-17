## Microsoft.Agents.AI

### Purpose

`Microsoft.Agents.AI` is the Microsoft Agent Framework runtime. It is used because the ChatClient
adapter does not implement an agent of its own — it adapts an `IChatClient` into the framework's
`AIAgent` abstraction, built through `ChatClientAgent`. It was chosen rather than a bespoke agent
loop so that AgentKit contributes the one thing a provider reached through an `IChatClient` is
missing — faithful image delivery — while the framework supplies the tool-calling loop, conversation
state, and agent abstraction it already provides. It is a runtime NuGet dependency of the
AgentKitAgentsChatClient package only, deliberately kept out of Core.

### Features Used

- `AIAgent` — the provider-neutral agent abstraction the factory returns to a host
- `ChatClientAgent` — builds an `AIAgent` from an `IChatClient` and a tool list, and runs the
  function-invocation loop above the client it is given, which is what places the image-promoting
  decorator beneath that loop
- `FunctionInvokingChatClient` — the function-invocation loop itself, which ships in the
  `Microsoft.Extensions.AI` package this dependency brings. `ChatClientProviderSessionFactory`
  installs one above the client an application supplies, because a session declares its tools on
  every request and a bare client would emit tool calls nothing answers

### Integration Pattern

`Microsoft.Agents.AI` is consumed by `ChatClientAgentFactory` in AgentKitAgentsChatClient, which
references it as a **direct** `PackageReference`. The factory wraps the supplied `IChatClient` in
Core's `ImagePromotingChatClient` and constructs a `ChatClientAgent` over the wrapped client,
passing the supplied instructions, name, and tools. There is no initialization or disposal step of
the framework's own; the returned `AIAgent`, and the client beneath it, remain the host's to manage.

`ChatClientProviderSessionFactory` consumes the same package graph directly, constructing a
`FunctionInvokingChatClient` for each session it creates and placing AgentKit's own prompt-size
recorder beneath it. Neither wrapper is disposed by the session: the client at the bottom of the
chain is the application's and outlives every session a rotation creates.

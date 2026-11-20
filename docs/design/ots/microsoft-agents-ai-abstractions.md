## Microsoft.Agents.AI.Abstractions

### Purpose

`Microsoft.Agents.AI.Abstractions` is the Microsoft Agent Framework's abstractions package: the
provider-neutral agent and session types, without the framework's runtime. It is used because
AgentKitAgentsCopilot carries an agent of its own over Copilot sessions — the Copilot SDK ships no
Agent Framework agent — and an application should receive that agent as the same `AIAgent` it would
receive from any other Agent Framework source. It was chosen rather than the full framework runtime
because the Copilot agent needs only the base types: the Copilot runtime runs the conversation and the
tool-calling loop, so nothing of the framework's own agent implementations is used. It is a runtime
NuGet dependency of the AgentKitAgentsCopilot package only, deliberately kept out of Core.

### Features Used

- `AIAgent` — the base type `CopilotAgent` derives from, and the type `CopilotAgentFactory.Create`
  returns. The agent overrides its extension points for creating a session, serializing and
  deserializing a session, running, and running with streaming, and reports its `Name` and
  `Description` through it
- `AgentSession` and `AgentSessionStateBag` — the base type `CopilotAgentSession` derives from, and
  the state bag it carries, serializes and restores alongside the runtime session identifier
- `AgentResponseUpdate` — the streamed update a run produces, carrying the role, the content items,
  the agent identifier, the author name, the creation time, the message and response identifiers, and
  the raw runtime event
- `AgentResponse` and the gathering of a stream of updates into one response, which is how the
  agent's non-streaming run is defined so the two run methods cannot disagree

### Integration Pattern

`Microsoft.Agents.AI.Abstractions` is consumed by `CopilotAgent` in AgentKitAgentsCopilot, which
references it as a **direct** `PackageReference`. The package has no initialization, configuration or
disposal step: `CopilotAgentFactory` constructs a `CopilotAgent` and returns it as an `AIAgent`, and
the application drives it through the abstraction's own surface — creating sessions, running, and
serializing and deserializing sessions. The agent holds no disposable resource and the application
does not dispose it; the Copilot client beneath it remains the host's to manage.

The serialized form of a `CopilotAgentSession` — a JSON object with a `sessionId` and a `stateBag` — is
AgentKit's own, written and read by the session type through the abstraction's serialization extension
points; the state bag's own serialized form is the abstraction's.

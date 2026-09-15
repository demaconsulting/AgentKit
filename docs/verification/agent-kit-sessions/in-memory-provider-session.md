## InMemoryProviderSession Unit Verification Design

This document describes the unit-level verification strategy for `InMemoryProviderSession` and
`InMemoryProviderSessionFactory`.

### Verification Approach

These types are the substitute for a live provider, so they are verified directly rather than
through another substitute — mocking a fake would prove nothing. Every scenario constructs a real
session or factory, drives it, and asserts on what it recorded.

The scenarios divide by what each half of the unit exists to make observable:

- **That it contacts nothing** — verified implicitly by every scenario here and throughout this test
  project, none of which touches a network, a file or a model. The responder is a plain function, so
  what "the model said" is entirely under the scenario's control.
- **That it can be either provider shape** — verified by driving `CurrentUsage` in both
  configurations, which is what makes both engine paths reachable elsewhere.
- **That rotation evidence is observable** — verified by asserting disposal is visible and final,
  and that the factory records every session it made. Against a real provider a leaked session holds
  a server-side conversation open and keeps being billed for, so a rotation that failed to dispose
  one must be detectable rather than merely suspected.

Unit tests reside in `InMemoryProviderSessionTests.cs` within the
`DemaConsulting.AgentKit.Sessions.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. **No provider, no model and no network access is used**
- **Mocking**: None; the subject is itself the substitute for a provider
- **Isolation**: Each test constructs its own session or factory; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any session that loses its seeded history, that fails to record what a turn
produced, that records anything when its responder failed, that reports usage when configured not
to, that accepts a turn after disposal, or any factory that forgets a session it created constitutes
a failure.

### Test Scenarios

#### AgentKitSessions-InMemoryProviderSession-ContactsNothing: A Session Starts From Its Seed and Records Its Turns

**Tests**: `InMemoryProviderSession_Construct_StartsHoldingTheSeededHistory`,
`InMemoryProviderSession_SendAsync_RecordsTheMessageAndTheTurn`,
`InMemoryProviderSession_SendAsync_ResponderThrows_RecordsNoGhostEntry`,
`InMemoryProviderSession_SendAsync_ResponderReturnsNull_RecordsNoGhostEntry`,
`InMemoryProviderSessionFactory_DefaultResponder_Answers`

The first asserts a session created from a seed carrying a consolidated record and one verbatim turn
starts holding both, and exposes the seed itself so a rotation's preserved content can be inspected.
The second drives a turn whose responder calls a tool and asserts the incoming message, both tool
entries and the answer that ends the turn were recorded in order — the history a provider holding the
conversation server-side would have, and the same history the engine's own transcript holds, because
both record a turn's entries and those entries end with the answer.

The next two are the failure half of the same rule, and are the reason the responder runs before
anything is recorded. One responder throws and one returns null; each asserts the history is empty
and the turn count is still zero. Recording the incoming message first left a ghost user message
behind in both cases, while `CompactingAgentSession` correctly records nothing when a provider
refuses a turn — so the shipped fake's history diverged from the engine's transcript under exactly
the condition the engine's own rule exists for. This fake is shipped and adapter authors read it as
the reference implementation, so a divergence here is a defect in published guidance, not merely in
a test double.

The last asserts the default responder answers and names the message, so a scenario about the
session lifecycle is not obliged to also invent what a model says.

#### AgentKitSessions-InMemoryProviderSession-SimulatesBothProviderShapes: Usage Is Reported, or Withheld

**Tests**: `InMemoryProviderSession_CurrentUsage_ReportsProviderOriginAndGrows`,
`InMemoryProviderSession_CurrentUsage_WhenNotReporting_IsNull`

The first asserts usage is marked as the provider's own, grows with the conversation, and carries
the configured window. The second asserts a session configured not to report returns nothing rather
than a fabricated figure. Together these make both engine paths — preferring the provider's account,
and falling back to the library's estimate — reachable from a test; without them one of the two
ships unexercised.

#### AgentKitSessions-InMemoryProviderSession-RecordsRotationEvidence: Disposal and Creation Are Both Observable

**Tests**: `InMemoryProviderSession_DisposeAsync_MarksDisposedAndRefusesFurtherTurns`,
`InMemoryProviderSessionFactory_CreateAsync_RecordsEverySessionItMakes`,
`InMemoryProviderSessionFactory_CreateAsync_ConcurrentCreations_RecordsEveryOne`

The first disposes a session that has taken a turn, disposes it a second time to confirm that is
permitted, and asserts disposal is visible, the history was released, and a further turn is refused
with `ObjectDisposedException`. The second creates two sessions as a conversation with one rotation
would, and asserts both are recorded oldest first carrying their own seeds. These two scenarios are
what make the rotation assertions elsewhere possible at all. The third creates two thousand sessions
from many threads at once and asserts every one was recorded, and that a list already handed out is a
snapshot a later creation cannot disturb. The factory is required to be safe for concurrent use
because an application may run several sessions against one, and an unsynchronized record can lose a
session or be observed halfway through an addition — a defect a single-threaded scenario cannot
detect.

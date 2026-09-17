## ChatClientProviderSessionFactory Unit Verification Design

This document describes the unit-level verification strategy for the
`ChatClientProviderSessionFactory` class.

### Verification Approach

`ChatClientProviderSessionFactory` is verified through unit tests that ask it for sessions and then
prove, from the outside, that each session carries the two facts the factory holds: the client and
the window. Neither is asserted by reaching inside the created session. The window is read back from
the occupancy the session reports, and the client is established by taking a turn and finding the
request on the factory's own recording client — which is what a rotation actually depends on.

The same hand-written recording `IChatClient` the session tests use stands in for a provider; it
contacts nothing and answers from a script. Core's `ProviderSessionSeed` is used as itself, because
passing the seed through is the behavior under test.

**What is out of automated scope, stated honestly.** The contract's concurrency promise — that an
application may create sessions from one factory on more than one thread — is not proven by a test.
It rests on the factory holding nothing mutable: creating a session reads the client reference and
the window and touches nothing else, which is a property of the code a reviewer can see and a race
test could only fail to disprove.

Unit tests reside in `ChatClientProviderSessionFactoryTests.cs`, with the recording client in
`RecordingChatClient.cs`, both within the `DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider is contacted and **no network access is used**
- **Mocking**: A hand-written recording `IChatClient`; no mocking framework
- **Isolation**: Each test constructs its own client, factory and seeds; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any created session reporting a window other than the factory's, any turn that
reaches a client other than the factory's, any seed that fails to reach the session it was given for,
any two creations yielding the same session, or any invalid argument accepted rather than refused
constitutes a failure.

### Test Scenarios

#### AgentKitAgentsChatClient-ChatClientProviderSessionFactory-CreatesSessionsCarryingItsClientAndWindow: Both Facts

**Tests**:

- `ChatClientProviderSessionFactory_CreateAsync_CreatesSessionCarryingItsClientAndWindow`
- `ChatClientProviderSessionFactory_CreateAsync_EachCall_ReturnsADistinctSessionOverTheSameClient`

Creates a session from a factory configured with a window distinct from any default, takes a turn on
it, and asserts the occupancy is reported against that window and the turn arrived at the factory's
client. The second scenario creates two sessions, takes a turn on each, and asserts they are separate
sessions holding separate conversations carried on the one client — which is exactly what a rotation
asks for and what a factory returning a cached session would break.

#### AgentKitAgentsChatClient-ChatClientProviderSessionFactory-SeedsTheCreatedSession: The Seed Reaches the Session

**Test**: `ChatClientProviderSessionFactory_CreateAsync_PassesTheSeedToTheSession`

Creates a session from the seed a rotation produces — instructions and a consolidated record — takes a
turn, and asserts the provider received the instructions and the record before the new message. A
factory that dropped the seed would produce a replacement that started the conversation over, which no
other scenario here would notice.

#### AgentKitAgentsChatClient-ChatClientProviderSessionFactory-RejectsInvalidArguments: Bad Configuration Is Refused

**Tests**:

- `ChatClientProviderSessionFactory_Constructor_NullClient_Throws`
- `ChatClientProviderSessionFactory_Constructor_NonPositiveWindow_Throws`
- `ChatClientProviderSessionFactory_CreateAsync_NullSeed_Throws`
- `ChatClientProviderSessionFactory_CreateAsync_Canceled_Throws`

Error paths and boundary values. A missing client and a window of zero or a negative window are
refused where the application configured its provider; a missing seed is refused rather than turned
into a session starting from nothing; and a canceled creation yields no session, so a canceled
rotation leaves nothing unowned.

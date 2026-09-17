## ChatClientProviderSessionFactory Unit Verification Design

This document describes the unit-level verification strategy for the
`ChatClientProviderSessionFactory` class.

### Verification Approach

`ChatClientProviderSessionFactory` is verified through unit tests that ask it for sessions and then
prove, from the outside, what each session carries: the client and the window the factory holds, and
the pipeline the factory built around that client. None of it is asserted by reaching inside the
created session. The window is read back from the occupancy the session reports, the client is
established by taking a turn and finding the request on the factory's own recording client, and the
pipeline is established by what a turn *does* — a seeded tool actually runs, and the occupancy a
tool-using turn reports is the last request's prompt rather than the total the turn was billed for.

**The pipeline scenarios are the ones that matter, and they are arranged as the defect was.** A turn
that calls a tool is two requests reporting two different prompt sizes; the response the tool-calling
layer returns reports their sum. The occupancy scenario asserts the session reports the last prompt
and, explicitly, that it does not report the sum, so it fails against an implementation reading
`ChatResponse.Usage`. The isolation scenario takes a reading on a replacement created after an
earlier session over the same factory has spoken, so it fails against a factory that built one
pipeline and shared it.

The same hand-written recording `IChatClient` the session tests use stands in for a provider; it
contacts nothing and answers from a script. The tool-calling layer the factory installs is used as
itself rather than simulated: whether it invokes a tool and how it reports usage are exactly what the
placement around it assumes, and a stand-in would verify the stand-in. Core's `ProviderSessionSeed`
is used as itself, because passing the seed through is the behavior under test.

**What is out of automated scope, stated honestly.** The contract's concurrency promise — that an
application may create sessions from one factory on more than one thread — is not proven by a race
test. It rests on the factory holding nothing mutable: creating a session reads the client reference
and the window and builds a pipeline of the session's own, which is a property of the code a reviewer
can see and a race test could only fail to disprove. The isolation scenario proves the half of it
that is deterministic — that two sessions do not share a reading — without claiming to prove the
rest.

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
any two creations yielding the same session, any seeded tool that is declared but not invoked, any
occupancy that is a total across a turn's requests rather than its last prompt, any replacement
reporting a figure its predecessor left behind, or any invalid argument accepted rather than refused
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

#### AgentKitAgentsChatClient-ChatClientProviderSessionFactory-OwnsTheSessionPipeline: Both Placements, One Per Session

**Tests**:

- `ChatClientProviderSessionFactory_CreateAsync_SeededTool_IsInvokedAndItsResultReachesTheTranscript`
- `ChatClientProviderSessionFactory_CreateAsync_ToolUsingTurn_ReportsTheLastRequestsPromptNotTheSum`
- `ChatClientProviderSessionFactory_CreateAsync_AfterAnEarlierSessionSpoke_TheReplacementOccupiesNothing`

The first scenario seeds a tool that records being run and scripts a provider that calls it and then
answers. It asserts the tool ran once, that a further request carried the result back to the
provider, and that the call and its result are both in the transcript beside the answer. A factory
that declared the tools and installed no loop would leave the model's call unanswered, and nothing
about the seeding alone would notice.

The second scenario is the one that fails against the defect. The two requests report 100 and 150,
and the response the loop returns reports 250. It asserts two requests were made, that the occupancy
is 150, and — explicitly — that it is not 250, so the reading an implementation taking
`ChatResponse.Usage` would produce is named rather than merely absent.

The third scenario proves the pipeline belongs to the session rather than to the factory. A first
session takes a turn reporting a figure, is released, and a replacement is created from the same
factory as a rotation would; the scenario asserts the predecessor's figure and then the
replacement's zero. A shared recorder would hand the replacement the figure that provoked the
rotation, so the engine would rotate a session that had sent nothing, again and again.

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
refused where the application configured its provider — which is the only place either can be
supplied, because the session's own constructor is internal; a missing seed is refused rather than
turned into a session starting from nothing; and a canceled creation yields no session, so a canceled
rotation leaves nothing unowned.

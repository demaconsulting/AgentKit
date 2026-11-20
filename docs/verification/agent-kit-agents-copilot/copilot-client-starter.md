## CopilotClientStarter Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotClientStarter` class.

### Verification Approach

`CopilotClientStarter` is verified through unit tests that call an internal overload of its start
method, which takes the client's start as a delegate and reads the environment through an injected
reader. The public `StartAsync(CopilotClient, CancellationToken)` is a thin forwarder to that
overload, passing the client's own start and the process environment.

**Both dependencies are injected, and for the same reason.** Starting a real `CopilotClient` would
load the native Copilot runtime, which a unit test must not depend on, and the condition under test
is precisely a runtime that *cannot* load. Reading `COPILOT_CLI_PATH` from the real process
environment would make the tests depend on the shell they run in, and every Copilot CLI shell sets
that variable; changing it would race any other test in the process. So the start is scripted to
succeed, fail with a chosen exception, or be canceled, and the environment reader answers with a
chosen value or none. Nothing about the starter's own logic is stubbed: the classification of the
failure, the message it composes and the cause it keeps are the production code.

Every failure test asserts that the original exception is kept as the inner exception, because the
explanation is a guess about the most likely cause and the original failure is the fact.

**What is out of automated scope, stated honestly.** That a real in-process runtime fails to load
when `COPILOT_CLI_PATH` names a Copilot CLI install, and that the starter's explanation then reaches
the host, is a property of the runtime rather than of this class. It is shown by the opt-in live
Copilot suite's `AgentKitAgentsCopilotLive_ClientStarter_AmbientCopilotCliPath_IsExplained`, which
starts a second client with a bogus value set and asserts the explanation names the variable and its
value. That suite is supplementary evidence outside the trace matrix, described in
*AgentKitAgentsCopilot System Verification Design*.

Unit tests reside in `CopilotClientStarterTests.cs` within the
`DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot runtime is started, **no credential is used**, and no
  network access is made
- **Mocking**: A delegate standing in for the client's start and a delegate standing in for the
  environment; no mocking framework
- **File system**: None
- **Isolation**: No test reads or changes the process environment; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any successful start that throws or consults the environment, any failed start
whose explanation omits the variable, its value or the remedies when the variable is set, any failed
start that does not name the direct SDK reference when the variable is unset, any explanation that
loses the original failure, any cancellation that is wrapped or explained, or a missing client that
is accepted constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotClientStarter-StartsTheClient: A Good Start Passes Through

**Test**: `CopilotClientStarter_Start_Succeeds_DoesNotThrow`

Normal operation. The scripted start succeeds; the test asserts the start was invoked, nothing was
thrown, and the environment was **not** consulted. The last assertion pins that the starter's only
work on the success path is the start itself, so a host that never has a failure never pays for the
diagnosis.

#### AgentKitAgentsCopilot-CopilotClientStarter-ExplainsARuntimeOverride: The Ambient Override, by Name

**Test**: `CopilotClientStarter_Start_FailsWithCliPathSet_ExplainsTheOverrideAndKeepsTheCause`

Error path. The start fails while the environment reader reports `COPILOT_CLI_PATH` set to a Copilot
CLI path. The test asserts the result is an `InvalidOperationException` whose message names the
variable and its value, tells the host to unset it, and offers `RuntimeConnection.ForSpawnedStdio`
as the alternative, and whose inner exception is the original failure, by reference. The variable is
named because it is the cause a host is least likely to suspect: it is set by the shell the host was
started from, not by anything the host wrote.

#### AgentKitAgentsCopilot-CopilotClientStarter-ExplainsAMissingRuntime: The Missing Direct Reference

**Test**: `CopilotClientStarter_Start_FailsWithoutCliPath_ExplainsTheMissingRuntime`

Error path. The start fails while the variable is not set. The test asserts the message says the
variable is not set and names `Microsoft.Copilot.SDK`, the package a host must reference directly
for the runtime to be provisioned beside it, and that the original failure is kept as the cause.
This is the boundary of the previous scenario: the same failure, explained by the other common cause
when the first cannot apply.

#### AgentKitAgentsCopilot-CopilotClientStarter-PassesCancellationThrough: Cancellation Is Not a Fault

**Test**: `CopilotClientStarter_Start_Canceled_PropagatesUnchanged`

The scripted start is canceled while the environment reader would report a value. The test asserts
the very same `OperationCanceledException` instance reaches the caller. A cancellation the host asked
for is not a runtime that failed to load, and explaining it as one would send the host looking for a
fault that does not exist.

#### AgentKitAgentsCopilot-CopilotClientStarter-RejectsNullClient: A Missing Client Is Refused

**Test**: `CopilotClientStarter_Start_NullClient_Throws`

Error path through the public entry point: a null client is refused with `ArgumentNullException`
before anything is started.

### ChildAgentRequest Unit Verification Design

This document describes the unit-level verification strategy for the `ChildAgentRequest` class.

#### Verification Approach

Nothing is mocked or stubbed. The type is a small immutable record with an internal constructor,
so the meaningful verification is what its properties describe and how the runner it is handed
to observes cancellation and propagates failures. The runner scenarios exercise the type as the
family actually uses it: they compose `AgentPack` with a test-owned runner, invoke `agent_run`,
and assert what the runner saw and what happened when it faulted.

Composing the pack rather than constructing a `ChildAgentRequest` directly is essential: the
constructor is internal so a hand-built request would not exercise the path the family actually
takes, and the runner's cancellation-observation and exception-propagation behavior is only
meaningful when the request travels from `AgentRunTool` to the runner exactly as production
code sends it.

Unit tests reside in `Agent/ChildAgentRequestTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project, and use the shared `StubToolPack` helper for the
child pack the delegated agent draws on.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access, or network access required
- **Runner**: A test-owned lambda that either records the request, waits on the cancellation
  token to observe cancellation, or throws a stated exception; no model provider is contacted
- **Isolation**: Each test constructs its own pack and runner; no state is shared

#### Acceptance Criteria

A unit test run passes when all three scenarios below pass without error or exception beyond
those explicitly asserted. A property that fails to describe the child the host must build, a
cancellation token the runner did not observe, or a runner exception silently converted into a
returned refusal each constitute a failure.

#### Test Scenarios

##### AgentKitTools-Agent-Request-DescribesTheChild: The Properties Describe the Child the Host Must Build

**Test**: `ChildAgentRequest_Properties_DescribeTheChildTheHostMustBuild`

Normal operation: composes the family, delegates through `agent_run`, and asserts on the
`ChildAgentRequest` the runner received that `ProfileName`, `Instructions`, `Tools`, `Task` and
`Depth` describe the child the host would build — the profile the model selected, the
application-authored instructions, the tools composed against the child's own policy, the task
the parent stated, and the child's delegation depth.

##### AgentKitTools-Agent-Request-RunnerContract: The Runner Observes the Caller's Cancellation Token

**Test**: `ChildAgentRequest_Runner_ObservesTheCallersCancellationToken`

Normal operation for cancellation: the parent turn's cancellation token is passed through to
the runner so a canceled parent turn reaches the child rather than leaving a delegated agent
detached. The scenario cancels the token and asserts the runner observes it.

##### AgentKitTools-Agent-Request-RunnerContract: A Host Failure Propagates

**Test**: `ChildAgentRequest_Runner_HostFailure_Propagates`

Error path: the runner throws a stated exception and the scenario asserts the exception
propagates out of `agent_run` rather than being converted into a returned refusal. This
library cannot tell a transient outage from a misconfiguration from the runner's exception
alone, and reporting either as a refusal would tell the model something the library does not
know.

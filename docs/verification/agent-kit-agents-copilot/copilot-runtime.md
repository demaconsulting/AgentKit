## CopilotRuntime Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotRuntime` class and its
options type, `CopilotRuntimeOptions`.

### Verification Approach

`CopilotRuntime` is verified through unit tests of its pure, internal seams, so no test starts a
Copilot runtime:

- **`BuildClientOptions` and `BuildRuntimeEnvironment`** take the host environment as a parameter, so
  a test passes a hostile environment of its own and inspects the client options and the replacement
  environment AgentKit would hand the SDK. The process environment is never read or changed.
- **`PrepareDataDirectory` and `MarkDataDirectory`** run against real directories created under the
  system temporary folder and deleted afterwards, because the behavior under test is what the
  directory holds. `MarkDataDirectory` is driven directly to reproduce a concurrent writer that
  created the marker first. `PrepareDataDirectory` takes a `beforeMark` callback so a test populates a
  new or empty directory between the emptiness check and the marker, as a concurrent writer could. The
  symbolic-link case is skipped on a Windows session that lacks the
  privilege to create symbolic links; it runs on Linux, macOS and elevated or Developer Mode Windows,
  and a marker that is a directory covers the non-link refusal everywhere.
- **`StartClientAsync` and `StartOwnedAsync`** take the start, and the creation and disposal, as
  delegates, because the SDK's client cannot be made to fail on demand without a broken runtime.
- **Disposal and deletion** are exercised on a runtime constructed over a `CopilotClient` that was never started
  through the internal constructor. Constructing a client spawns nothing, and the SDK accepts the
  Empty-mode options because they carry a base directory.
- **Nothing built on a disposed runtime starts it again.** The SDK restarts a disposed client on
  demand, so the tests for an agent, a provider-session factory, a summarizer and the production
  openers construct a runtime over a client that can never start: its runtime path names a file that
  does not exist. An attempt to start it fails with the SDK's own start failure and spawns nothing, so
  a start that should not have been attempted is observable rather than silent; the tests assert the
  runtime's `ObjectDisposedException` instead. These tests live beside the units they drive.

Nothing about the runtime's own logic is stubbed: the classification of options, the filtering of
variables, the data-directory rules, the explanation and the disposal ordering are production code.

**What is out of automated scope, stated honestly.** That the runtime the SDK spawns honors the forced
options is a property of the runtime rather than of this class. The opt-in live Copilot suite starts
every scenario on a `CopilotRuntime` and shows the effect: L11 starts a runtime with a bogus
`COPILOT_CLI_PATH` and an approve-all switch in the process environment, while an in-process client
under the same environment fails to start, and L14 and L15 show which MCP sources and ambient channels
reach a session on it. That suite is supplementary evidence outside the trace matrix, described in
*AgentKitAgentsCopilot System Verification Design*.

Unit tests reside in `CopilotRuntimeTests.cs` within the `DemaConsulting.AgentKit.Agents.Copilot.Tests`
project; the tests that nothing built on a disposed runtime starts it reside in
`CopilotAgentFactoryTests.cs`, `CopilotProviderSessionFactoryTests.cs`, `CopilotSummarizerTests.cs` and
`CopilotTurnChannelTests.cs` in the same project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot runtime is started, **no credential is used**, and no
  network access is made
- **Mocking**: Delegates standing in for the client's start, creation and disposal, and a hashtable
  standing in for the host environment; no mocking framework. A client that cannot start — a real
  `CopilotClient` whose runtime path names no file — stands in for a runtime that must not be started
- **File system**: Directories under the system temporary folder, one per test, deleted afterwards;
  the runtime path and base directory of the client that cannot start are never created
- **Isolation**: No test reads or changes the process environment; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any client options in another mode, on another connection, with a runtime path,
arguments or a child working directory, or with any client-level capability channel set; any
`COPILOT_` or `GITHUB_COPILOT_` variable outside the pass-through set reaching the runtime environment,
or any other host variable dropped; a pass-through set other than the documented one; a data directory
accepted when it names a file, is a non-empty directory without the marker, or has a marker that is a
link, a directory, the wrong size or the wrong content, or is found populated once its marker is
written, or marked when refused; a marker written in
any form other than the exact versioned bytes, or an existing marker overwritten;
a failed start whose explanation omits the runtime's error, the direct SDK reference or an
authentication source, or loses the
original failure; a cancellation that is wrapped; a failed or canceled start that leaves the client
undisposed; a disposal that is not idempotent or leaves the client usable; an agent, provider-session
factory, summarizer or production opener built on a disposed runtime that reaches the SDK client; or
invalid options or a blank session identifier that are accepted constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotRuntime-ForcesEmptyMode: Empty Mode Is Forced

**Test**: `CopilotRuntime_BuildClientOptions_ForcesEmptyMode`

Normal operation. The client options are built from the default runtime options; the test asserts
the mode is `CopilotClientMode.Empty`.

#### AgentKitAgentsCopilot-CopilotRuntime-SpawnsTheBundledRuntime: The Provisioned Runtime, Spawned

**Test**: `CopilotRuntime_BuildClientOptions_SpawnsTheBundledRuntimeOverStdio`

The test asserts the connection is a spawned stdio connection with no runtime path, no arguments and
no child working directory, whose base directory is the prepared data directory and whose environment
is a replacement rather than inherited.

#### AgentKitAgentsCopilot-CopilotRuntime-WithholdsCopilotEnvironment: The Host's Copilot Variables Stay Out

**Tests**:

- `CopilotRuntime_BuildRuntimeEnvironment_RemovesCopilotConfigurationVariables`
- `CopilotRuntime_BuildRuntimeEnvironment_MixedCaseOnWindows_IsRemoved` (Windows only)
- `CopilotRuntime_BuildRuntimeEnvironment_KeepsHostVariablesAndThePassThroughSet`
- `CopilotRuntime_PassThroughVariables_AreTheDocumentedSet`
- `CopilotRuntime_BuildClientOptions_CarriesTheWithheldEnvironment`

A host environment carrying a runtime path override, an approve-all switch, an instructions directory
and an injected MCP configuration is filtered; the tests assert none of them reaches the runtime, that
a mixed-case name is withheld on Windows, whose names are case-insensitive, that `PATH`, `GH_TOKEN`,
`GITHUB_TOKEN`, `HTTPS_PROXY` and every pass-through variable are kept with their values, that the
pass-through set is exactly the seven documented names, and that the client options carry the
filtered environment rather than the host's.

#### AgentKitAgentsCopilot-CopilotRuntime-OwnsACleanDataDirectory: A Directory AgentKit Owns

**Tests**:

- `CopilotRuntime_PrepareDataDirectory_Missing_IsCreatedAndMarked`
- `CopilotRuntime_PrepareDataDirectory_Empty_IsMarked`
- `CopilotRuntime_PrepareDataDirectory_Marked_IsAcceptedWithRuntimeContent`
- `CopilotRuntime_PrepareDataDirectory_UnmarkedNonEmpty_IsRefused`
- `CopilotRuntime_PrepareDataDirectory_PathIsAFile_IsRefused`
- `CopilotRuntime_PrepareDataDirectory_Fresh_WritesExactlyTheVersionedMarker`
- `CopilotRuntime_PrepareDataDirectory_MarkerWithOtherContent_IsRefusedAsInvalid`
- `CopilotRuntime_PrepareDataDirectory_MarkerOversized_IsRefusedAsInvalid`
- `CopilotRuntime_PrepareDataDirectory_MarkerIsADirectory_IsRefusedAsInvalid`
- `CopilotRuntime_PrepareDataDirectory_MarkerIsASymbolicLink_IsRefusedAsInvalid` (skipped on
  unprivileged Windows)
- `CopilotRuntime_MarkDataDirectory_MarkerWrittenConcurrentlyValid_IsAcceptedNotOverwritten`
- `CopilotRuntime_MarkDataDirectory_MarkerWrittenConcurrentlyInvalid_IsRefusedNotOverwritten`
- `CopilotRuntime_PrepareDataDirectory_MissingPopulatedBeforeMarking_IsRefusedAndLeftUnmarked`
- `CopilotRuntime_PrepareDataDirectory_EmptyPopulatedBeforeMarking_IsRefusedAndLeftUnmarked`
- `CopilotRuntime_MarkDataDirectory_MarkerWrittenConcurrentlyValidWithContent_IsAccepted`

Normal operation and the error paths. A missing nested directory is created and marked, and its full
path returned; an empty one is marked; in both cases the marker's bytes equal the format-1 marker,
which the test writes out independently of the library: UTF-8 without a byte order mark, LF line
endings, the signature and format version on the first line and a final line feed. A marked
directory is accepted with the runtime's own content in it, which is left in place. A directory
holding an `mcp-config.json` it did not get from AgentKit is refused with an
`InvalidOperationException` naming it and saying it was not created by AgentKit, not that its marker
is invalid, and is left unmarked; a path naming a file is refused naming the path.

A marker that is empty, carries a byte order mark, has CRLF line endings, has an extra or a missing
final line feed, or holds the earlier unversioned text is refused as "not the marker AgentKit
writes", naming the directory, and is left byte for byte as it was. So is a marker that starts with
the expected bytes but is a megabyte (1 MiB) larger (the reason names its size), a marker entry that
is a directory (the reason says so, rather than reporting the directory as unmarked), and a symbolic
link whose target outside the data directory holds exactly the valid bytes (the reason names a link;
the target is left alone). When a concurrent writer created the marker first, a valid one is
accepted with its bytes and last-write time unchanged, and a partial one is refused as invalid and
left as it was.

A directory a writer populates — with an `mcp-config.json`, or a subdirectory — after it was found
new or empty and before it is marked is refused as not created by AgentKit, left without a marker,
its content untouched, and refused again on the next start. A valid marker a concurrent writer
created first is accepted with content beside it, as on reuse.

#### AgentKitAgentsCopilot-CopilotRuntime-OffersOnlyNeutralOptions: Nothing That Widens the Session

**Tests**:

- `CopilotRuntime_BuildClientOptions_LeavesEveryCapabilityChannelUnset`
- `CopilotRuntime_BuildClientOptions_CarriesTokenLogLevelAndLogger`
- `CopilotRuntimeOptions_Constructor_BlankToken_IsNull`
- `CopilotRuntimeOptions_Constructor_RelativeDataDirectory_IsResolvedToAFullPath`

With every option a host can set supplied, the tests assert no plugin directory, session filesystem,
test options, extension launch provider, installation handler, model request handler, model listing
handler, telemetry, network callbacks, Entra registration or telemetry forwarder is set; that the
token, log level and logger reach the client options; that a blank token is treated as none; and that
a relative data directory is resolved to a full path when the options are built.

#### AgentKitAgentsCopilot-CopilotRuntime-ExplainsAMissingRuntime: The Missing Direct Reference

**Tests**:

- `CopilotRuntime_StartClient_FailedStart_ExplainsTheMissingRuntimeAndKeepsTheCause`
- `CopilotRuntime_StartClient_Succeeds_DoesNotThrow`

Error path and normal operation. A start failing with the SDK's runtime-not-found error is rethrown as
an `InvalidOperationException` whose message carries the runtime's error and the remedy — a
`PackageReference` to `Microsoft.Copilot.SDK` — and names every authentication source,
`COPILOT_GITHUB_TOKEN` among them, and whose inner exception is the original, by
reference; a failure whose message is null is explained the same way rather than masked. A start that
succeeds passes through.

#### AgentKitAgentsCopilot-CopilotRuntime-PassesCancellationThrough: Cancellation Is Not a Fault

**Test**: `CopilotRuntime_StartClient_Canceled_PropagatesUnchanged`

The very same `OperationCanceledException` instance reaches the caller.

#### AgentKitAgentsCopilot-CopilotRuntime-LeavesNoRuntimeBehind: Disposed on Failure, Once on Request, Never Restarted

**Tests**:

- `CopilotRuntime_StartOwned_FailedStart_DisposesTheClient`
- `CopilotRuntime_StartOwned_Canceled_DisposesTheClient`
- `CopilotRuntime_StartOwned_Succeeds_ReturnsTheClientUndisposed`
- `CopilotRuntime_DisposeAsync_IsIdempotentAndClientIsThenUnavailable`
- `CopilotAgentFactory_Create_RuntimeDisposedAfterCreation_RunsFailWithoutStartingTheRuntime`
- `CopilotProviderSessionFactory_CreateAsync_RuntimeDisposedAfterConstruction_FailsWithoutStartingTheRuntime`
- `CopilotSummarizer_ConsolidateAsync_RuntimeDisposedAfterConstruction_FailsWithoutStartingTheRuntime`
- `CopilotSessionChannel_OpenersOnADisposedRuntime_FailWithoutStartingTheRuntime`

A start that fails or is canceled disposes the created client and rethrows; a start that succeeds
returns the client undisposed. A runtime disposed twice disposes once, its client is then unavailable
with `ObjectDisposedException`, and its data directory remains readable.

Error path. An agent, a provider-session factory and a summarizer are built on a runtime over the
client that cannot start, and the runtime is then disposed. The agent's run without a conversation, its
first run of a conversation and its run on a conversation with a recorded session identifier; the
factory's next session; and the summarizer's next consolidation each fail with
`ObjectDisposedException` whose object name is `CopilotRuntime`. So do the production openers made
before the disposal — `Open`, `OpenRetained` and `Resume` — and `CreateConfinedSessionAsync` and
`ResumeConfinedSessionAsync` called after it. Had any of them reached the SDK client, the SDK would
have tried to start it and failed with its own start failure instead, so the assertion proves no
start was attempted. Before the fix, the agent, factory and summarizer tests failed exactly that way.

#### AgentKitAgentsCopilot-CopilotRuntime-DeletesAKeptConversation: Deletion Without the Client

**Tests**:

- `CopilotRuntime_DeleteSessionAsync_BlankSessionId_Throws`
- `CopilotRuntime_DeleteSessionAsync_AfterDisposal_Throws`

A null or blank session identifier is refused before the runtime is reached, and deletion through a
disposed runtime fails with `ObjectDisposedException`. That the runtime deletes a real session is a
property of the SDK call the method forwards to; the opt-in live suite's agent scenarios delete their
conversations through this method.

#### AgentKitAgentsCopilot-CopilotRuntime-RejectsInvalidOptions: Missing Options Are Refused

**Tests**:

- `CopilotRuntime_StartAsync_NullOptions_Throws`
- `CopilotRuntimeOptions_Constructor_BlankDataDirectory_Throws`
- `CopilotRuntimeOptions_Constructor_InvalidDataDirectory_ThrowsNamingTheParameter`

Null options are refused with `ArgumentNullException` before anything is created; a null data
directory with `ArgumentNullException`, a whitespace one with `ArgumentException`, and a path the
platform cannot represent with `ArgumentException` naming `dataDirectory`, the platform's own failure
kept as its inner exception.

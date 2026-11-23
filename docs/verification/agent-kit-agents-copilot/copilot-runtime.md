## CopilotRuntime Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotRuntime` class, its
options type, `CopilotRuntimeOptions`, its runtime-source type, `CopilotRuntimeSource`, and its
token-provider types, `CopilotGitHubTokenRequest` and `CopilotGitHubToken`.

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
  new or empty directory between the emptiness check and the marker, as a concurrent writer could.
  `MarkDataDirectory` also has an overload taking the re-enumeration and the best-effort delete, so a
  test substitutes a failing enumeration and a failing delete to prove the marker left behind is
  invalid. The symbolic-link case is skipped on a Windows session that lacks the
  privilege to create symbolic links; it runs on Linux, macOS and elevated or Developer Mode Windows,
  and a marker that is a directory covers the non-link refusal everywhere.
- **`StartClientAsync` and `StartOwnedAsync`** take the start, and the creation and disposal, as
  delegates, because the SDK's client cannot be made to fail on demand without a broken runtime.
  `StartClientAsync` also takes the launch, so the explanation for each runtime source, and for the
  SDK's protocol-version mismatch, is asserted from a fabricated failure.
- **`ResolveLaunchAsync`** takes the search context and the process runner, so it runs against
  `FakeCopilotCliMachine` — a fake file system for any platform with a scripted version probe — and
  every launch outcome is asserted without a CLI on the machine.
- **`VerifyStartedAsync`** takes the status and authentication reads as delegates, fed with
  `GetStatusResponse` and `GetAuthStatusResponse` objects the test fabricates (the SDK's types have
  public setters), because a client cannot report a status without a runtime.
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
explicitly asserted. Any client options in another mode, on another connection, with a runtime path
other than the resolved installed CLI (none for the bundled runtime), with arguments or a child
working directory, or with any client-level capability channel set; options or environment that
differ between the two runtime sources; a launch that searches or probes for the bundled runtime,
prefers discovery to a named CLI, or follows `COPILOT_CLI_PATH`; a missing named CLI, a machine
without a CLI, or a too-old CLI that is not refused before a client exists; a protocol mismatch not
explained as too old or too new; a runtime that does not report its launch, status and sign-in, or an
unauthenticated one that is accepted or refused without each sign-in source's finding; a resolved
token or host that does not reach the client options, or an environment changed when the plan names
neither; a session provider applied on a runtime without one, or missing on a runtime with one; any
`COPILOT_` or `GITHUB_COPILOT_` variable outside the pass-through set reaching the runtime environment,
or any other host variable dropped; a pass-through set other than the documented one; a data directory
accepted when it names a file, is a non-empty directory without the marker, or has a marker that is a
link, a directory, the wrong size or the wrong content, or is found populated once its marker is
written, or marked when refused; a marker written in
any form other than the exact versioned bytes, or an existing marker overwritten;
a failed bundled start whose explanation omits the runtime's error, the direct SDK reference or an
authentication source, or keeps the original failure rather than a redacted copy of it as the cause, or leaves a
token anywhere in the cause's chain, or a failed installed-CLI start whose
explanation blames the SDK reference; a cancellation that is wrapped; a failed or canceled start that
leaves the client
undisposed; a disposal that is not idempotent or leaves the client usable; an agent, provider-session
factory, summarizer or production opener built on a disposed runtime that reaches the SDK client; or
invalid options or a blank session identifier that are accepted constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotRuntime-ForcesEmptyMode: Empty Mode Is Forced

**Test**: `CopilotRuntime_BuildClientOptions_ForcesEmptyMode`

Normal operation. The client options are built from the default runtime options; the test asserts
the mode is `CopilotClientMode.Empty`.

#### AgentKitAgentsCopilot-CopilotRuntime-SpawnsTheSelectedRuntime: The Selected Executable, Spawned Alike

**Tests**:

- `CopilotRuntime_BuildClientOptions_Bundled_SpawnsTheProvisionedRuntimeOverStdio`
- `CopilotRuntime_BuildClientOptions_InstalledCli_SpawnsTheResolvedCliOverStdio`
- `CopilotRuntimeOptions_Constructor_Defaults_InstalledCliWithNoPath`
- `CopilotRuntime_ResolveLaunch_Bundled_SkipsDiscoveryAndTheVersionProbe`
- `CopilotRuntime_ResolveLaunch_ExplicitPath_WinsOverDiscovery`
- `CopilotRuntime_ResolveLaunch_SupportedCli_CarriesPathAndVersion`
- `CopilotRuntime_ResolveLaunch_CopilotCliPathVariable_IsIgnored`

Normal operation. For the bundled runtime the connection is a spawned stdio connection with no runtime
path, no arguments and no child working directory, whose base directory is the prepared data
directory and whose environment is a replacement rather than inherited. For the installed CLI, under
a host environment carrying `COPILOT_CLI_PATH`, the connection's path is exactly the resolved CLI, and
the mode, base directory and environment equal the bundled runtime's entry for entry. Default options
choose the installed CLI with no path. Resolving the bundled runtime runs no probe even with a CLI on
`PATH`; a named CLI wins over one on `PATH` and is the one probed; a supported CLI found by discovery
is launched with its path and the version it printed. A host environment whose `COPILOT_CLI_PATH`
names an existing CLI, on a machine where discovery finds none, is refused as having no CLI and runs
no probe.

#### AgentKitAgentsCopilot-CopilotRuntime-RefusesAnUnsupportedCli: Refused Before a Runtime Starts

**Tests**:

- `CopilotRuntime_ResolveLaunch_ExplicitPathMissing_IsRefused`
- `CopilotRuntime_ResolveLaunch_NothingFound_IsRefusedWithInstallGuidance`
- `CopilotRuntime_ResolveLaunch_TooOldCli_IsRefusedBeforeAnyClientExists`
- `CopilotRuntime_ResolveLaunch_NoVersionReported_DefersToTheHandshake`
- `CopilotRuntime_StartClient_ProtocolMismatchOlderServer_IsExplainedAsTooOld`
- `CopilotRuntime_StartClient_ProtocolMismatchNewerServer_IsExplainedAsTooNew`

Error paths. A named CLI that does not exist is refused naming it, with no probe run. A machine with
nothing installed is refused naming the location searched, the WinGet install command and the
bundled alternative, with no probe run. A CLI printing the real `1.0.106` banner is refused naming the
CLI, its version, the minimum and the `--no-auto-update` version check. A CLI whose output names no
version is launched with no version, leaving the decision to the handshake. The SDK's mismatch error
for a server on protocol 3 is rethrown with the SDK's message, the protocol, the minimum and the
WinGet upgrade command, and without the package-reference advice; for protocol 5 it advises updating
AgentKit.

#### AgentKitAgentsCopilot-CopilotRuntime-ReportsTheRuntimeVersion: The Version That Runs

**Tests**:

- `CopilotRuntime_Properties_ReportTheLaunchAndTheStatus`
- `CopilotRuntime_VerifyStarted_Authenticated_ReturnsTheStatus`

A runtime built with a launch and a status reports the CLI path, the runtime version and the protocol
version; one built without reports no path, an empty version and protocol 0. A signed-in runtime's
status is returned as read.

#### AgentKitAgentsCopilot-CopilotRuntime-RefusesAnUnauthenticatedRuntime: Not Signed In Is Refused

**Test**: `CopilotRuntime_VerifyStarted_Unauthenticated_IsRefusedWithSignInGuidance`

Error path. A runtime reporting it is not authenticated is refused with an `InvalidOperationException`
carrying the runtime's status message, the four sign-in sources numbered in order with what each
found, and the remedies: `copilot login`, `gh auth login` and `gitHubHost`. The message no longer
says that the Copilot CLI's own sign-in is not used.

#### AgentKitAgentsCopilot-CopilotRuntime-UsesTheResolvedSignIn: The Chosen Token and Host Reach the Runtime

**Tests**:

- `CopilotRuntime_BuildClientOptions_SignInPlan_CarriesTheTokenAndHost`
- `CopilotRuntime_BuildClientOptions_NoTokenOrHost_LeavesTheEnvironmentUnchanged`

Normal operation. A plan carrying the Copilot CLI's token and its host puts the token in the client's
GitHub token and the bare host name in the runtime's `COPILOT_GH_HOST`, replacing the host
environment's value, still in Empty mode. A plan for an environment token hands over no token and
leaves the runtime's environment exactly the filtered host environment.

#### AgentKitAgentsCopilot-CopilotRuntime-ReportsHowItSignedIn: The Account That Signed In

**Test**: `CopilotRuntime_Properties_ReportTheSignIn`

A runtime built with a Copilot CLI plan and an authentication status of kind `token` reports
`CopilotSignInSource.CopilotCli` and the reported host and login; one built without reports
`Unknown` and no host or login.

#### AgentKitAgentsCopilot-CopilotRuntime-SuppliesTheSessionSignIn: Sessions Ask the Host's Provider

**Test**: `CopilotRuntime_ApplySessionSignIn_SetsTheProviderOnlyWhenTheRuntimeHasOne`

A runtime started with a token provider sets a session token provider on create and resume options
and no session token; one without leaves the session's GitHub options untouched.

#### AgentKitAgentsCopilot-CopilotRuntime-WithholdsCopilotEnvironment: The Host's Copilot Variables Stay Out

**Tests**:

- `CopilotRuntime_BuildRuntimeEnvironment_RemovesCopilotConfigurationVariables`
- `CopilotRuntime_BuildRuntimeEnvironment_MixedCaseOnWindows_IsRemoved` (Windows only)
- `CopilotRuntime_BuildRuntimeEnvironment_KeepsHostVariablesAndThePassThroughSet`
- `CopilotRuntime_PassThroughVariables_AreTheDocumentedSet`
- `CopilotRuntime_BuildClientOptions_CarriesTheWithheldEnvironment`
- `CopilotRuntime_ResolveLaunch_ProbesUnderTheWithheldEnvironment`

A host environment carrying a runtime path override, an approve-all switch, an instructions directory
and an injected MCP configuration is filtered; the tests assert none of them reaches the runtime, that
a mixed-case name is withheld on Windows, whose names are case-insensitive, that `PATH`, `GH_TOKEN`,
`GITHUB_TOKEN`, `HTTPS_PROXY` and every pass-through variable are kept with their values, that the
pass-through set is exactly the seven documented names, that the client options carry the
filtered environment rather than the host's, and that the installed CLI's version probe runs under
exactly that filtered environment with only `COPILOT_HOME` added.

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
- `CopilotRuntime_MarkDataDirectory_ReEnumerationAndDeleteFail_LeavesInvalidMarker`

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
created first is accepted with content beside it, as on reuse. When the re-enumeration after writing
the marker fails and the delete fails too, the enumeration's exception propagates and the marker left
behind is empty, so the next start refuses the directory as carrying an invalid marker.

#### AgentKitAgentsCopilot-CopilotRuntime-OffersOnlyNeutralOptions: Nothing That Widens the Session

**Tests**:

- `CopilotRuntime_BuildClientOptions_LeavesEveryCapabilityChannelUnset`
- `CopilotRuntime_BuildClientOptions_CarriesTokenLogLevelAndLogger`
- `CopilotRuntimeOptions_Constructor_BlankToken_IsNull`
- `CopilotRuntimeOptions_Constructor_RelativeDataDirectory_IsResolvedToAFullPath`
- `CopilotRuntimeOptions_Constructor_BlankCliPath_IsNull`
- `CopilotRuntimeOptions_Constructor_FullyQualifiedCliPath_IsKept`
- `CopilotRuntimeOptions_GitHubHost_IsNormalized`
- `CopilotRuntimeOptions_SignInOptions_DefaultAndKeepTheirValues`
- `CopilotGitHubToken_Constructor_ValidatesAndRedacts`

With every option a host can set supplied, the tests assert no plugin directory, session filesystem,
test options, extension launch provider, installation handler, model request handler, model listing
handler, telemetry, network callbacks, Entra registration or telemetry forwarder is set; that the
token, log level and logger reach the client options; that a blank token is treated as none; that
a relative data directory is resolved to a full path when the options are built; that a blank CLI path
is treated as none for either source; and that a fully qualified CLI path is kept, normalized. A
GitHub host given as `example.ghe.com`, `https://example.ghe.com` or `https://example.ghe.com/` is kept
as `https://example.ghe.com`, and a blank one as none; by default there is no host and no provider and
the Copilot CLI sign-in is allowed, and a provider and `useCopilotCliSignIn: false` are kept. A
provider's token must not be blank and must report more than one hour of life, and no string form of
it carries the token.

#### AgentKitAgentsCopilot-CopilotRuntime-ExplainsAMissingRuntime: The Cause That Fits What Was Launched

**Tests**:

- `CopilotRuntime_StartClient_FailedStart_ExplainsTheMissingRuntimeAndKeepsTheCause`
- `CopilotRuntime_StartClient_InstalledCliFailure_DoesNotBlameTheSdkReference`
- `CopilotRuntime_StartClient_Succeeds_DoesNotThrow`
- `CopilotRuntime_StartClient_FailureQuotingASignInToken_IsRedacted`

Error path and normal operation. A bundled start failing with the SDK's runtime-not-found error is rethrown as an
`InvalidOperationException` whose message carries the runtime's error and the remedy — a `PackageReference` to
`Microsoft.Copilot.SDK` — and names every sign-in source, in order, from `gitHubToken` or `gitHubTokenProvider` to the
user's Copilot CLI sign-in, and whose inner exception is a redacted copy of the original — not the original — carrying
its type name and message; a failure whose message is null, on
a start whose launch is not known, is explained for both sources rather than masked. An installed CLI's failure names
the CLI and its version, keeps a redacted copy of the cause and an authentication source, and does not mention a package
reference. A start
that succeeds passes through. On a runtime started with a token provider, whose session provider has handed a session
its token, a start failure quoting both the start token and the session token — neither shaped like a GitHub token —
and chaining an inner exception that chains an aggregate of two exceptions quoting them, is explained with `<redacted>`
in their places and neither value in the message; the inner exception is a redacted copy carrying the original's type
name and the redacted message, the aggregate is copied with its type name, and a walk of the whole chain, every inner
exception of any aggregate included, finds neither value in any message, stack trace or string form.

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
- `CopilotRuntimeOptions_Constructor_UndefinedSource_Throws`
- `CopilotRuntimeOptions_Constructor_RelativeCliPath_Throws`
- `CopilotRuntimeOptions_Constructor_BatchFileCliPath_Throws` (`.cmd`, `.CMD` and `.bat`)
- `CopilotRuntimeOptions_Constructor_CliPathWithBundled_Throws`
- `CopilotRuntimeOptions_InvalidGitHubHost_Throws`
- `CopilotRuntimeOptions_TokenAndProvider_Throws`

Null options are refused with `ArgumentNullException` before anything is created; a null data
directory with `ArgumentNullException`, a whitespace one with `ArgumentException`, and a path the
platform cannot represent with `ArgumentException` naming `dataDirectory`, the platform's own failure
kept as its inner exception. An undefined runtime source is refused with
`ArgumentOutOfRangeException` naming `runtimeSource`; a relative CLI path, and a CLI path given with
the bundled runtime, with `ArgumentException` naming `cliPath`. A CLI path naming a batch file —
`copilot.cmd`, `copilot.CMD` or `copilot.bat` — is refused with `ArgumentException` naming `cliPath`,
whose message says it is a batch file and points to `copilot.exe` or the npm package's
`npm-loader.js`. A GitHub host using `http`, carrying a path, a port or a user name is refused with
`ArgumentException` naming `gitHubHost`, and a token given with a token provider with
`ArgumentException` naming `gitHubTokenProvider`.

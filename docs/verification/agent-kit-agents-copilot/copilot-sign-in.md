## CopilotSignIn Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotSignIn` class, its plan,
account and helper types, and the public `CopilotSignInSource` enumeration.

### Verification Approach

`CopilotSignIn` is verified through unit tests of its internal entry points, so no test starts a
Copilot runtime, reads the real user's Copilot CLI home or touches a credential store:

- **`ResolveAsync`** takes the host's options, the host environment as a hashtable, the launch, and a
  `CopilotSignInContext`. The tests pass a context whose user profile is a folder under the system
  temporary folder holding a Copilot CLI `config.json` the test writes, whose `PATH` and file system
  are a `FakeCopilotCliMachine`, whose helper homes are folders under the test's root, and whose
  helper factory returns a **fake helper**: it records the options it was created with, reads the
  helper home's `config.json` and entries when started, and reports a scripted status and account
  list, or fails, hangs until canceled, or cancels the caller. The real `DeleteHelperHome` deletes
  each helper home, so the tests observe that nothing is left behind.
- **`LookUpCopilotCliAccount` and `ReadCopilotCliAccount`** read real files under the test's root, so
  missing, malformed, oversized and partial configurations are exercised as the user's files would be.
- **`BuildHelperEnvironment` and `BuildHelperOptions`** take the host environment and a fake machine
  for the GitHub CLI and `node` lookups, and return the environment and the SDK client options, which
  the tests inspect.
- **`ExplainUnauthenticated`, `Classify`, `NormalizeHost`, `CreateSessionTokenProvider`,
  `RedactFailure`, `RedactRelayed` and `Report`** are pure, or take a fabricated `GetAuthStatusResponse`, SDK
  token-provider arguments, a fabricated exception chain, and a logger that keeps every entry. The
  redacted copy's and the relayed failure's tests walk the whole exception chain, every inner
  exception of an aggregate included.

A sentinel token is used wherever a token appears, and the tests assert it never appears in a
finding, a message, a log entry or a string form. The redaction tests also use secrets that are not
shaped like a GitHub token (such as `s3cr3t-value-xyz`), so they prove the exact values the sign-in
knows are removed and not only the GitHub token prefixes.

**What is out of automated scope, stated honestly.** That the Copilot CLI reads its own token from
the system credential store in CodingAgent mode on a home holding only the account entry, that the
runtime accepts the token and host, and that it asks a session token provider for a session's token
on a client started with a token, are properties of the runtime. The opt-in live suite shows them on
Windows with the installed CLI (L-S1 to L-S5), outside the trace matrix; see *AgentKitAgentsCopilot
System Verification Design*. macOS, Linux and the bundled runtime as the helper are untested there.

Unit tests reside in `CopilotSignInTests.cs` within the `DemaConsulting.AgentKit.Agents.Copilot.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot runtime is started, no credential store is read, no
  credential is used and no network access is made
- **Mocking**: A fake helper implementing `ICopilotSignInHelper`, a `FakeCopilotCliMachine` for
  `PATH`, a hashtable for the host environment and a list logger; no mocking framework
- **File system**: A root folder under the system temporary folder per test, holding the fake user's
  profile, the data directory and every helper home, deleted afterwards
- **Isolation**: No test reads or changes the process environment or the user's own profile

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. A source that wins out of order; a helper run when a host or environment token
applies, or when the host turned the Copilot CLI sign-in off; a provider asked for another host, as a
refresh, or more than once at start; a provider failure, a missing token or a cancellation not
surfaced as specified; a Copilot CLI account used on a host other than the one requested; a host not
given to the runtime as specified; a helper home holding anything but the account entry and an empty
GitHub CLI folder; a helper environment carrying a token or host variable, the host's Copilot
configuration, or a `PATH` folder holding the GitHub CLI (other than one holding `node` for a script
CLI); a helper in another mode, on another executable or with a token; an account list read before
the status shows the account's stored sign-in; a token taken from another account's entry; a helper
failure, time-out or account-list failure that fails the start; a `COPILOT_HOME` that is not a valid
path that fails the start or is quoted; an invalid `COPILOT_GH_HOST` that is ignored, quoted, or
refused when `gitHubHost` was given; a valid one handed to the runtime other than normalized; a helper
not disposed or a helper home
not deleted on any outcome; a configuration with comments and trailing commas not read; an unusable
configuration that yields an account; a refusal missing a source, its finding or a remedy; a token
in any finding, message, log entry or string form; a known token of any shape left in quoted runtime,
SDK or exception text; a cause copy that keeps the original, loses its type name, message or stack
trace, carries its data, or leaves a token anywhere in its chain; a relayed failure that is the
original, changes the kind a host catches, loses a cancellation's token, or leaves a token anywhere in
its chain; or a lifetime of more than one hour
reported to the runtime as one hour or less constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotSignIn-OrdersTheSources: The First Source That Applies Wins

**Tests**:

- `CopilotSignIn_ResolveAsync_ExplicitToken_WinsWithoutRunningTheHelper`
- `CopilotSignIn_ResolveAsync_Provider_IsAskedForTheEffectiveHost`
- `CopilotSignIn_ResolveAsync_EnvironmentToken_SkipsTheCopilotCli` (each of the three variables)
- `CopilotSignIn_ResolveAsync_NoTokens_UsesTheCopilotCliTokenAndHost`
- `CopilotSignIn_ResolveAsync_CopilotCliTurnedOff_DoesNotReadTheConfiguration`
- `CopilotSignIn_Classify_MapsTheRuntimesKindOfSignIn`

Normal operation. With a Copilot CLI account and `GH_TOKEN` both present, a host token wins, no
helper is created, and the environment step says the host's token took precedence. A provider is
asked once and its token used. Each environment variable alone leaves the sign-in to the runtime, runs
no helper, names the variable and the account the Copilot CLI step would have used, and never carries
the variable's value. With no token, the Copilot CLI's token and its account's host are used; with the
Copilot CLI sign-in turned off, its configuration is not read and the step says so. The runtime's
`token`, `token-provider`, `env`, `user` and `gh-cli` kinds map to their sources, and an unknown kind
to `Unknown`.

#### AgentKitAgentsCopilot-CopilotSignIn-KeepsTheHostWithTheSource: Each Source on Its Own Host

**Tests**:

- `CopilotSignIn_ResolveAsync_ExplicitTokenWithHost_SetsTheHostName`
- `CopilotSignIn_ResolveAsync_Provider_IsAskedForTheEffectiveHost` (option, `COPILOT_GH_HOST`, default)
- `CopilotSignIn_ResolveAsync_RequestedHostDiffers_SkipsTheCopilotCli` (option and `COPILOT_GH_HOST`)
- `CopilotSignIn_ResolveAsync_Fallbacks_KeepTheChosenHostAndReportTheGitHubCli`
- `CopilotSignIn_ResolveAsync_EnvironmentHost_IsGivenToTheRuntimeNormalized`
- `CopilotSignIn_NormalizeHost_AcceptedForms_Normalize`
- `CopilotSignIn_NormalizeHost_RejectedForms_Throw`

Normal operation and boundaries. A token with `https://Example.ghe.com/` gives the runtime
`example.ghe.com`. A provider is asked for the option's host over `COPILOT_GH_HOST`, for
`COPILOT_GH_HOST` without the option, and for `https://github.com` without either. A Copilot CLI
account on `example.ghe.com` is not used when `github.com` or another host is requested, and the
finding says which. The fallbacks get the option's host name and none otherwise, and their finding
names the GitHub CLI found on `PATH`, or says none was found. A `COPILOT_GH_HOST` of
`https://Example.GHE.com/` is handed over as `example.ghe.com` with a host token, with an environment
token and to the runtime's own fallbacks alike, and the client options built from the fallbacks' plan
set the runtime's `COPILOT_GH_HOST` to `example.ghe.com`. Bare names, https URLs and one trailing
slash normalize to lower case; `http`, other schemes, a path, query, fragment, user name, port, an
empty authority or a non-host are refused naming the parameter.

#### AgentKitAgentsCopilot-CopilotSignIn-RefusesAnInvalidEnvironmentHost: An Invalid COPILOT_GH_HOST Stops the Start

**Tests**:

- `CopilotSignIn_ResolveAsync_InvalidEnvironmentHost_IsRefused` (`http://x.ghe.com`, `x.ghe.com/path`,
  `https://x.ghe.com:8443`, a URL with a user name and password, `not a host`)
- `CopilotSignIn_ResolveAsync_InvalidEnvironmentHostWithTheOption_UsesTheOption`

Error paths and boundaries. Each invalid `COPILOT_GH_HOST`, with a Copilot CLI account present and
with or without a token provider, refuses the resolution with exactly the refusal message, which
names `COPILOT_GH_HOST` and `gitHubHost` and quotes neither the value nor the password a URL carried;
the provider is never asked and no helper is created. With `gitHubHost` given, an invalid
`COPILOT_GH_HOST` is not consulted and the option's host is used; a blank `COPILOT_GH_HOST` counts as
unset.

#### AgentKitAgentsCopilot-CopilotSignIn-ReusesTheCopilotCliSignIn: The Account, Read Through the CLI

**Tests**:

- `CopilotSignIn_LookUpCopilotCliAccount_ReadsCopilotHomeOrTheProfileDefault`
- `CopilotSignIn_LookUpCopilotCliAccount_DataDirectoryAsHome_IsNotRead`
- `CopilotSignIn_ReadCopilotCliAccount_SingleAccountWithCommentsAndTrailingCommas_IsRead`
- `CopilotSignIn_ReadCopilotCliAccount_UnusableConfiguration_ReportsWhy`
- `CopilotSignIn_ReadCopilotCliAccount_TooLarge_IsNotRead`
- `CopilotSignIn_BuildHelperOptions_RunsTheSameExecutableWithNothingElse`
- `CopilotSignIn_BuildHelperEnvironment_WithholdsTokensAndHidesTheGitHubCli`
- `CopilotSignIn_BuildHelperEnvironment_ScriptCli_KeepsTheFolderHoldingNode`
- `CopilotSignIn_ResolveAsync_HelperNotSignedInAsTheAccount_DoesNotReadTheAccountList`
- `CopilotSignIn_ResolveAsync_AccountList_TakesOnlyTheAccountsUserEntry`

Normal operation, boundaries and error paths. The account is read from `COPILOT_HOME` when set and
from the profile's `.copilot` otherwise, and never from a `COPILOT_HOME` naming the data directory
(compared as a path; a link to the data directory is a documented known limitation, not tested). A
configuration with comments, trailing commas and only a single `loggedInUsers` entry yields that
account, its host normalized. No file, invalid JSON, a non-object, no account, several accounts and no
last one, an `http` host, a host with a path, a blank login, a login with a control character and a
non-string login each yield no account and a finding saying why; a file over 1 MiB is not read. The
helper runs the same executable in CodingAgent mode on its home with the given environment and no
token, arguments or capability channel. Its environment drops all five token variables, both host
variables and the host's Copilot configuration, keeps `HOME`, names the empty GitHub CLI folder, and
keeps only rooted `PATH` folders without the GitHub CLI — except a folder holding `node` when the CLI
is a `.js` script. A helper not signed in, signed in through the GitHub CLI, or signed in as another
login is never asked for its account list; the token is taken from the matching account's entry and
not another login's, and an account list without one is a finding.

#### AgentKitAgentsCopilot-CopilotSignIn-SharesOnlyTheAccountEntry: Only the Login Crosses Over

**Tests**:

- `CopilotSignIn_ResolveAsync_HelperHome_HoldsOnlyTheAccountEntry`
- `CopilotSignIn_ResolveAsync_NoTokens_UsesTheCopilotCliTokenAndHost`
- `CopilotSignIn_ResolveAsync_HelperFails_FallsThroughWithAFinding`
- `CopilotSignIn_ResolveAsync_HelperTimesOut_FallsThroughWithAFinding`
- `CopilotSignIn_ResolveAsync_CallerCancels_PropagatesAndCleansUp`

Normal operation and error paths. From a user configuration that also configures an MCP server,
trusted folders and other keys, the helper's home holds exactly
`{"loggedInUsers":[{"host":"https://example.ghe.com","login":"octo_user"}],"lastLoggedInUser":{"host":"https://example.ghe.com","login":"octo_user"}}`
in `config.json` and an empty `gh` folder, nothing else. The plan hands over only the token and the
host name. On success, failure, time-out and cancellation alike, the helper is disposed and every
helper home is gone.

#### AgentKitAgentsCopilot-CopilotSignIn-FallsThroughOnFailure: A Failed Step Never Fails the Start

**Tests**:

- `CopilotSignIn_ResolveAsync_HelperFails_FallsThroughWithAFinding`
- `CopilotSignIn_ResolveAsync_HelperCannotBeCreated_FallsThroughWithAFinding`
- `CopilotSignIn_ResolveAsync_HelperTimesOut_FallsThroughWithAFinding`
- `CopilotSignIn_ResolveAsync_AccountListFails_FallsThroughWithAFinding`
- `CopilotSignIn_ResolveAsync_HelperNotSignedInAsTheAccount_DoesNotReadTheAccountList`
- `CopilotSignIn_ResolveAsync_CallerCancels_PropagatesAndCleansUp`
- `CopilotSignIn_ReadCopilotCliAccount_UnusableConfiguration_ReportsWhy`
- `CopilotSignIn_LookUpCopilotCliAccount_InvalidCopilotHome_ReportsWhy`
- `CopilotSignIn_ResolveAsync_InvalidCopilotHome_FallsThroughWithAFinding`

Error paths. A helper whose start throws (with a token-shaped string in its message), one that never
answers within a 100 ms limit, and an account list that throws the SDK's own deserialization error
each leave the plan to the runtime's fallbacks with a finding — redacted, for the start failure —
rather than failing. A helper client whose construction throws (again with a token-shaped string in
its message) likewise leaves a redacted finding, with nothing to dispose and its home deleted. The
caller's cancellation while the helper runs propagates. A `COPILOT_HOME` holding a null character,
which no platform accepts as a path, yields no account, no configuration path and a finding that
names `COPILOT_HOME` without quoting its value; resolving with it, though the profile's default home
holds an account, leaves the plan to the runtime's fallbacks with that finding and runs no helper.

#### AgentKitAgentsCopilot-CopilotSignIn-AsksTheHostsTokenProvider: The Host's Provider, at Start and per Session

**Tests**:

- `CopilotSignIn_ResolveAsync_Provider_IsAskedForTheEffectiveHost`
- `CopilotSignIn_ResolveAsync_ProviderReturnsNull_Throws`
- `CopilotSignIn_ResolveAsync_ProviderThrows_ThrowsWithTheCause`
- `CopilotSignIn_ResolveAsync_ProviderCanceled_PropagatesCancellation`
- `CopilotSignIn_CreateSessionTokenProvider_AsksTheHostsProvider`
- `CopilotSignIn_CreateSessionTokenProvider_FractionalLifetime_RoundsUp`

Normal operation, boundaries and error paths. At start the provider is asked once, not as a
refresh. A provider
returning no token is refused naming the provider; one that throws is refused with the failure as the
inner exception, by reference; a canceled one propagates the cancellation. The session provider asks
the host's provider with the normalized host and `IsRefresh` false for `Initial` and true for
`Refresh`, and hands the runtime the token with eight hours when the lifetime is unknown and the
given lifetime otherwise, in whole seconds rounded up: one hour and one tick is 3601 seconds, one
hour and half a second is 3601, and two hours is 7200.

#### AgentKitAgentsCopilot-CopilotSignIn-ReportsWhatEachSourceFound: Every Source, No Secret

**Tests**:

- `CopilotSignIn_ExplainUnauthenticated_ListsEverySourceWithoutTheToken`
- `CopilotSignIn_ExplainUnauthenticated_RejectedTokens_SayWhatToChange`
- `CopilotSignIn_ExplainUnauthenticated_NonGitHubShapedTokens_AreRedacted` (host token, provider
  token, `GH_TOKEN`, Copilot CLI token)
- `CopilotSignIn_ResolveAsync_HelperFailureQuotingAKnownSecret_IsRedacted`
- `CopilotSignIn_CreateSessionTokenProvider_RecordsEachTokenForRedaction`
- `CopilotSignIn_PlanRedact_LongestSecretFirstAndTokenShapes`
- `CopilotSignIn_PlanToString_RedactsTheToken`
- `CopilotSignIn_Report_LogsTheSignInAndWarnsOnAnotherAccount`

Error paths. The refusal lists all four steps numbered with their findings, says the Copilot CLI's
token was not accepted, and names `copilot login` and `gh auth login`; a runtime status message
carrying the sentinel token is redacted, so neither the token nor its body appears. A host token
without a host adds the GitHub Enterprise Cloud host hint, and with a host does not; an environment
token says how to move on. The plan's string form shows the host and `<redacted>`. The sign-in is
logged at information level with source, host and login, and a GitHub CLI sign-in on another account
than the Copilot CLI's is warned about naming the Copilot CLI's host; no entry carries the token.

The secret `s3cr3t-value-xyz`, which no token-shape pattern recognizes, is resolved in turn as the
host's token, the provider's token, the value of `GH_TOKEN` and the Copilot CLI's token; a runtime
status message quoting it is explained with `<redacted>` in its place and the secret nowhere in the
refusal. A helper start failure quoting a `GH_ENTERPRISE_TOKEN` value (which does not make the
environment step apply) leaves a finding with `<redacted>` in its place. A token the host's provider
returns for a session is recorded, so text redacted afterwards no longer carries it. Overlapping
secrets are removed whole, longest first; `null` and blank values are ignored; and a GitHub-shaped
token is still reduced to its prefix and `<redacted>`.

#### AgentKitAgentsCopilot-CopilotSignIn-KeepsOnlyARedactedCause: A Cause Is a Redacted Copy

**Tests**:

- `CopilotSignIn_RedactFailure_ChainQuotingKnownSecrets_CarriesNoSecret`
- `CopilotSignIn_RedactFailure_KeepsTheTypeNameMessageAndStackTrace`

Error paths and boundaries. A thrown failure quoting a known secret, the plan's token and a
GitHub-shaped token, chaining an inner exception that chains an aggregate of two exceptions — the
first with a stack trace that quotes two of the secrets — is copied whole: the outer copy, the inner
copy, the aggregate's copy and the copy of the aggregate's first inner exception each carry the
original's type name, and the test walks every exception in the chain, every inner exception of any
aggregate included, and finds none of the three values in a message, a stack trace or a string form,
nor in the string form of an outer exception carrying the copy. The aggregate's message, which lists
both inner messages, is redacted. A thrown `FileNotFoundException` with data attached is copied with
its type name, message, stack trace and `HResult` unchanged, a string form laid out as an exception's
and led by the type name, and no data; a failure never thrown has no stack trace; and a chain of
twenty exceptions is copied to `MaximumDepth`, sixteen.

#### AgentKitAgentsCopilot-CopilotSignIn-RelaysFailuresRedacted: A Relayed Failure Is a New, Redacted Exception

**Tests**:

- `CopilotSignIn_RedactRelayed_SessionErrorQuotingATokenAndRpcData_IsRelayedRedacted`
- `CopilotSignIn_RedactRelayed_KeepsTheKindAHostCatches`
- `CopilotSignIn_RedactRelayed_CleanCancellationOrDisposal_PassesUnchanged`
- `CopilotSignIn_RedactRelayed_CancellationOrDisposalCarryingASecret_IsRebuiltRedacted`
- `CopilotSignIn_RedactRelayed_AlreadyRelayed_IsReturnedUnchanged`

Error paths and boundaries. The first relays a thrown failure in the SDK's shape — an
`InvalidOperationException` reading `Session error: …` that quotes a known secret and a GitHub-shaped
token, over an RPC-shaped exception of a type outside the base class library whose message, data and
own error-data property quote the secret. The result is a new `InvalidOperationException` reading
exactly `Session error: refused <redacted> gho_<redacted>`; its cause is a `CopilotRedactedException`
naming `System.InvalidOperationException`, chaining a copy naming the RPC-shaped type that carries no
data; and a walk of the whole chain, every inner exception of an aggregate included, finds neither
value in a message, a stack trace or a string form.

The second, a theory, relays an `EndOfStreamException`, a `TimeoutException`, an
`InvalidOperationException` and a `JsonException`, each quoting a GitHub-shaped token, and asserts
an `IOException`, a `TimeoutException`, an `InvalidOperationException` and an
`InvalidOperationException` respectively — each a new exception with the redacted message, a cause
naming the original type, and a clean chain.

The third asserts that an `OperationCanceledException` carrying a token, a `TaskCanceledException`
and an `ObjectDisposedException`, none with a cause or anything to redact, come back as the same
instance. The fourth relays an `OperationCanceledException` whose cause quotes a secret and an
`ObjectDisposedException` whose message and cause quote secrets: each is rebuilt as the same type —
not the same instance — the cancellation keeping its token, the disposal's message redacted, and both
chains clean. The fifth asserts that a failure already built around a redacted copy, and a failure
relayed once, are each returned unchanged, so relaying twice changes nothing.

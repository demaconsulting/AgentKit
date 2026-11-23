## CopilotTurnChannel Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotTurnChannel` unit — the
`ICopilotTurnChannel` interface, the `CopilotChannelOpener` and `CopilotChannelResumer` delegates, and
the production `CopilotSessionChannel`.

### Verification Approach

This unit is the seam the rest of the package is tested through, and **the production implementation
behind it is, apart from its release policy and its opening protocol, the one piece of this package
no automated test reaches.** That is stated first, plainly, because implying otherwise would misrepresent what the
evidence covers.

**Why it cannot be reached.** `CopilotSession` is sealed, its constructor is not public, and none of
its methods is virtual; `CopilotClient` is sealed too. A `CopilotSessionChannel` can therefore only
be constructed by opening or resuming a real session on a real client, which requires a live Copilot
runtime and an authenticated user — neither of which this repository's merge gate has, and neither of
which it deliberately requires.

**The release policy is the exception, and it is tested directly.** Whether a channel deletes the
session it releases is a decision, and the decision is factored out of the channel's disposal into
`CopilotSessionChannel.ReleaseAsync`, which takes the detach step and the optional delete step as
delegates. The tests supply recording delegates and assert the order of the steps, that a retained
channel has no delete step, and that neither step's failure escapes. The factories that choose
between a deleting and a retaining channel — `Open` for the session engine, the summarizer and an
agent's run made without a conversation; `OpenRetained` and `Resume` for an agent's conversation —
are one-line compositions over the SDK and are reviewed rather than tested.

**The opening protocol is the second exception, and it is tested the same way.** The order and
failure handling of opening a session — discover the ambient MCP servers and disable them on the
options, open the session, seal it, and abandon it if the seal fails — is factored into
`CopilotSessionChannel.OpenConfinedAsync`, and the sealing policy — clear the installed plugins,
disable every server still enabled, verify, and refuse naming any that remains — into
`CopilotSessionChannel.SealAsync`; both take the runtime's steps as delegates. The tests supply
recording delegates over the SDK's own configured-server type and assert the order of the steps,
what the options carried when the session was opened, the abandonment, and the refusal. The live
compositions over the SDK — the client's discovery, the session's options patch, listing and disable,
and which abandonment a created and a resumed session receive — are reviewed rather than tested here.
The opt-in live suite shows on the real runtime what each ambient-server scenario covers: discovery
reports the server and the channel names it on the create-time disabled list, with no server enabled
afterwards (L12a); a session created without the channel has it enabled (L12b); the seal's
session-scoped disable turns off a server left enabled at creation, and the re-listing confirms it
(L12c); and a session resumed through the channel has it named before the resume and no server
enabled afterwards (L12d). On the runtime AgentKit owns, a data-directory server that discovery
reports is never executed through the channel while the same server is executed without it (L14c,
L14d). The seal's listing is also shown to be incomplete: it does not report the built-in GitHub
server while that server is connected (L14b), so the seal is defense in depth rather than proof that
no server runs. The plugins patch is shown accepted, not shown clearing an installed plugin, because
no fixture installs one.

**What is verified through the units above, and what that is worth.** The *contract* this unit
publishes — that a channel names its session, takes a turn carrying a prompt and any attachments, and
releases exactly one runtime session; that an opener and a resumer are injectable; and that the
caller's cancellation token is the turn's only deadline — is verified through the units built on it,
which are exercised against a scripted channel that replays the SDK's own event types. Those tests
prove that the code above the seam uses it correctly: that one session is opened per rotation, per
consolidation and per conversation, that a conversation's later runs resume the session its first run
recorded, that each channel is released on every path including failure, that the client is never
disposed, and that a canceled turn sends nothing.

**What is deliberately not claimed.** Three behaviors of the production implementation are reviewed
rather than tested, and each is recorded here rather than implied to be covered:

- **That a session is created or resumed, that a turn reaches the model, that release and deletion
  take effect, and that the runtime honors the MCP and plugin calls of the opening protocol** — all of
  which require the live runtime. The opt-in live Copilot suite shows
  the first three on the real runtime, including a conversation released by one agent and resumed by
  another, and shows of the fourth exactly what L12a to L12d cover, as listed above: the create-time
  disabled list, the seal's session-scoped disable and re-listing, and the resume-time disabled list
  on a session resumed through the channel, plus the L14 scenarios' prevention of data-directory and
  workspace servers and the listing's omission of the built-in server, with
  the plugins patch accepted but not shown clearing an installed plugin. It is supplementary evidence
  outside the trace matrix, described in *AgentKitAgentsCopilot
  System Verification Design*.
- **That the SDK accepts an infinite timeout and treats it as "no deadline".** This was established
  by reading the SDK's own implementation, which carries the value into a linked cancellation
  source's delayed cancel, where `Timeout.InfiniteTimeSpan` means "never" — a documented framework
  behavior. It was checked rather than assumed, because the alternative is the SDK's sixty-second
  default, which would fail nearly every tool-using turn and would look like a model or tool fault.
  It is nonetheless a reading of someone else's code rather than an observation of a running system.
- **That the best-effort session deletion succeeds.** Its failure is deliberately swallowed, so even
  a live run would not report it; what a live run can establish is whether per-rotation session data
  accumulates on disk.

**The mitigation is that the production implementation is kept small enough to read.** It forwards a
turn, exposes the session's identity, owns one guarded release and composes one opening protocol over
the SDK's calls, and every decision worth testing
is placed *above* the seam or factored out where the tests reach it. A wider seam would move decisions
below the line and out of evidence, which is the reason the interface has one property and one method
beyond its disposal.

The scripted channel and runtime that stand in for it live in `FakeCopilotTurnChannel.cs` within the
`DemaConsulting.AgentKit.Agents.Copilot.Tests` project, and are claimed by this unit's review-set for
the same reason the ChatClient adapter's recording client is claimed by its units: they are the double
the evidence rests on, and a reviewer cannot judge that evidence without them. The release policy
and opening protocol tests reside in `CopilotTurnChannelTests.cs` in the same project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot runtime is started, **no credential is used**, and no
  network access is made — which is the whole reason this seam exists
- **Mocking**: A hand-written scripted channel, opener and resumer, and recording delegates for the
  release steps and the opening protocol's runtime steps; no mocking framework
- **File system**: None
- **Isolation**: Each test constructs its own channel or delegates; no state is shared

### Acceptance Criteria

A test run passes when every scenario below passes without error or exception beyond those explicitly
asserted. Any session opened and not released, any release that also disposes the client, any
release that throws, any engine, summarizer or conversation-less agent channel released without
deleting its session, any agent conversation channel that deletes its session, any session shared
between rotations or consolidations, any later run that does not resume the recorded session, or
any turn taken after a caller's cancellation, any session opened before every discovered MCP server
was disabled on its options, any seal that does not clear the installed plugins before listing the
servers, any server reported enabled that is not disabled, any session accepted while a server stays
enabled, or any refused session not abandoned constitutes a failure. **No acceptance criterion here
covers the production implementation's own calls into the SDK**; see the verification approach
above.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotTurnChannel-CarriesOneRuntimeSession: The Seam Carries a Turn

**Tests**:

- `CopilotProviderSession_Send_Answer_IsReturnedAndRecordedOnce`
- `CopilotProviderSessionFactory_CreateAsync_RegistersTheObserverBeforeCreation`
- `CopilotSummarizer_Consolidate_RunsOutOfTheSessionBeingCompacted`
- `CopilotAgent_Run_DataContent_IsSentAsABlobAttachment`
- `CopilotAgent_Run_FirstRun_CreatesAConfinedSessionAndRecordsItsId`

These are the scenarios that demonstrate the seam is sufficient: a turn sent through it produces an
answer and a transcript, a session opened through it carries the event handler that was on its
configuration, a fresh one is opened per consolidation, a turn carries attachments beside its prompt,
and the identity a channel names is the one the agent records for its conversation. They belong to
the units above the seam and are described in full in their own verification chapters; they are
listed here because they are the evidence this unit's contract has, and saying so is more honest than
restating them as if they were tests of the channel itself.

#### AgentKitAgentsCopilot-CopilotTurnChannel-OwnsTheSessionNotTheClient: One Session, Released, Client Untouched

**Tests**:

- `CopilotProviderSession_Dispose_DisposesTheRuntimeSession_AndNotTheClient`
- `CopilotSummarizer_Consolidate_Fails_DisposesTheSession`
- `CopilotSessionChannel_Release_DeletingChannel_DetachesThenDeletes`
- `CopilotSessionChannel_Release_FailingSteps_DoNotThrow`

The first asserts a released provider session released exactly one runtime session and that another
can still be opened on the same runtime afterwards — which a channel that had disposed the client
would have made impossible, and which is exactly what a rotation does. The second asserts the release
happens on the failing path too, which is where a leak hides.

The last two test the production release policy itself. The third asserts a deleting channel detaches
its session handle and then asks for the session's deletion, in that order: releasing alone is
documented to *preserve* a session's state on disk, which would leave one preserved session per
rotation behind. The fourth makes both steps fail and asserts nothing escapes and that the deletion
was still attempted after the detach failed. Release runs on disposal paths where no failure can be
acted on, and a runtime that declined to delete a session is housekeeping left undone, not a fault in
the conversation.

#### AgentKitAgentsCopilot-CopilotTurnChannel-RetainsAgentSessions: An Agent Conversation's Session Survives Its Release

**Tests**:

- `CopilotSessionChannel_Release_RetainedChannel_DetachesWithoutDeleting`
- `CopilotSessionChannel_Release_DeletingChannel_DetachesThenDeletes`
- `CopilotAgent_Run_AfterEveryRun_ReleasesTheSessionAndKeepsItResumable`

The first asserts a retained channel — the kind the agent opens and resumes for a conversation —
detaches its session handle and takes no deletion step at all, so the conversation stays resumable on
the runtime. The second is the other side of the same rule: the channels the session engine, the
summarizer and a conversation-less agent run open still delete what they release, because such a
session is never resumed. The third
asserts, through the agent, that every run's channel is released once and that the conversation still
names the session its first run created, so the next run resumes it rather than replacing it.

#### AgentKitAgentsCopilot-CopilotTurnChannel-ResumesARecordedSession: Reopened by Its Identifier

**Test**: `CopilotAgent_Run_LaterRun_ResumesTheRecordedSessionWithTheSameConfinement`

Runs a conversation twice through an agent built over the scripted runtime's injectable resumer and
asserts the second run reopened the recorded session by its identifier, under the resumption
configuration the agent supplied for it, rather than creating another. That the injectable resumer is
the only path by which a later run reaches the runtime is what lets the agent's resumption be tested
at all. That the real runtime keeps a released session resumable is shown by the live suite's
resumption scenario.

#### AgentKitAgentsCopilot-CopilotTurnChannel-CallerGovernsTheDeadline: No Deadline of Its Own

**Tests**:

- `CopilotProviderSession_Send_Canceled_SendsNothing`
- `CopilotSummarizer_Consolidate_Canceled_OpensNothing`

These assert the caller's token is honored above the seam: a canceled turn sends nothing and a
canceled consolidation opens nothing. The scripted channel deliberately does **not** refuse a
canceled turn of its own accord, so the first assertion falsifies the adapter's own check rather than
the fake's.

That the channel places **no timeout of its own** on a live turn is, as stated above, established by
reading the SDK rather than by a test. The consequence of getting it wrong is worth restating: the
SDK's default is sixty seconds, a tool-using research turn routinely exceeds it, and the resulting
failure would read as a model or tool fault rather than as a deadline nobody chose.

#### AgentKitAgentsCopilot-CopilotTurnChannel-ClosesAmbientMcpServersAndPlugins: Ambient MCP Servers Disabled Before Any Turn, Plugins Cleared, Session Refused If One Stays Enabled

**Tests**:

- `CopilotSessionChannel_Open_AmbientMcpServers_AreDisabledBeforeTheSessionIsCreated`
- `CopilotSessionChannel_Open_DiscoveryFails_OpensNothing`
- `CopilotSessionChannel_Open_SealFails_AbandonsTheSessionAndRethrows`
- `CopilotSessionChannel_Seal_ClearsInstalledPluginsFirst`
- `CopilotSessionChannel_Seal_ServerStillEnabledAfterCreation_IsDisabledThenVerified`
- `CopilotSessionChannel_Seal_ServerThatStaysEnabled_RefusesNamingIt`
- `CopilotSessionChannel_Seal_RefusalNamingASecret_IsRedactedWhenRelayed`
- `CopilotSessionChannel_NoInstalledPluginsPatch_ReplacesTheListWithAnEmptyOne`

The first runs the opening protocol over a confined engine configuration with a discovery that
reports a non-GitHub server, the built-in GitHub server and a plugin's server, and snapshots the
disabled servers at the moment the session is opened: discovery ran first, all three are disabled,
and the factory's entry for the GitHub server appears exactly once. The second makes discovery fail
and asserts nothing is opened. The third makes the seal fail and asserts the opened session is
abandoned through the supplied policy and the seal's own exception propagates.

The next three test the sealing policy. The fourth asserts the installed plugins are cleared before
the servers are listed, so a server a plugin contributed is gone before the listing. The fifth reports
a non-GitHub server enabled on the first listing — the case of a source discovery does not report —
and disabled on the second, and asserts only that server was disabled, the configuration was listed
again, and the session was accepted. The sixth keeps reporting the server enabled and asserts the
refusal names it and points to the signed-in account or its organization among the sources to check.
The seventh keeps a server enabled whose name quotes the secret `s3cr3t-value-xyz` and runs the seal
through `CopilotSignIn.RelayAsync` with a sign-in that knows it, as every opener does: the refusal
arrives as a new `InvalidOperationException` naming `'docs-<redacted>'`, with a redacted copy as its
cause, and `SecretAssert` finds the secret nowhere in the chain.

The last asserts the plugin patch carries an empty installed list rather than none, which the runtime
would read as no change.

Which abandonment a created and a resumed session receive — deleted, or only released — is a
composition over the SDK and is reviewed rather than tested; the system-level ambient-server scenario
in *AgentKitAgentsCopilot System Verification Design* runs the protocol end to end over the
factory's own configurations, and the opt-in live suite shows on the real runtime the created path
(L12a, L12c) and the resume-time disabled list on a session resumed through the channel (L12d); the
abandonment policy itself is reviewed.

#### AgentKitAgentsCopilot-CopilotTurnChannel-AppliesTheHostsTokenProvider: Sessions on a Provider's Runtime Ask It

**Test**: `CopilotSessionChannel_Open_RuntimeWithTokenProvider_AppliesItToTheSession`

Normal operation. On a runtime built with a sign-in plan carrying a token provider, over a client that
can never start, `CreateConfinedSessionAsync` and `ResumeConfinedSessionAsync` each fail at discovery
— so no session is created — after setting a session token provider on the options; on a runtime
without one, none is set. Invoking the session provider with the SDK's arguments for
`https://example.ghe.com` and `Refresh` asks the host's provider once, with that host and
`IsRefresh` true, and hands back its token. That the runtime then asks the session provider is shown
by the opt-in live suite (L-S4). The discovery's start failure goes through
`CopilotRuntime.StartClientAsync` with the runtime's sign-in, so its explanation is redacted and its
cause is a redacted copy of the runtime's failure; that is asserted in *CopilotRuntime Unit
Verification Design*.

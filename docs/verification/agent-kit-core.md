# System Verification Design

This document describes the system-level verification strategy for the AgentKit Core.

## Verification Approach

The AgentKit Core system is verified through system-level integration tests that
exercise the library as a whole from the perspective of a consumer. Tests instantiate the library
using its public API and assert on observable outputs, without relying on knowledge of internal
implementation details. No mocking or stubbing is required at the system level — the entire
integrated system is exercised as it would be used by a real caller.

The system under verification is the contract Core publishes: the policy primitives that bound
where a tool may act, the guarded path by which a tool is constructed, the results a tool returns
through, the pack contract by which an application composes tools, and the session engine that keeps
a long-running agent alive by compacting its context. Each scenario below therefore exercises one of
those properties end to end rather than any single unit in isolation; unit-level behavior is verified
separately in the unit verification documents.

The session engine is verified through deterministic end-to-end conversations against the shipped
`InMemoryProviderSession`, the divergent-tokenizer provider fake, and `FakeSummarizer`. No live
provider or model is contacted. The tests prove the compaction core by observing the public session
behavior, the provider sessions created during rotation, and the seed history carried into each
replacement session: a session keeps a configurable verbatim tail, then retains older context in a
fixed round-robin shape of three tiers with four slots per tier. The unit tests cover the internal
rules; the system tests cover the observable promise that a long conversation keeps answering, takes
its window from whatever provider it is on, and preserves important early detail.

The high-pressure cases are part of system verification. `DivergentTokenizerProviderSession` reports
usage at 1x, 2x and 3x the rate `InMemoryProviderSession` charges for the same history, so the same
session behavior is exercised when one provider counts the seeded context far more heavily than
another. Those tests prove the response to pressure terminates and is reported instead of silently
churning. Compaction pressure is reported as `CompactionLevel.Low`, `CompactionLevel.Medium` or
`CompactionLevel.High`, and the compacting session adapts that level with hysteresis: near-repeat
rotations escalate the level, while a long quiet stretch relaxes it.

The package surface is also verified mechanically. `PublicSurfaceTests.cs` asserts the assembly
exports exactly the deliberate list of public types, and that the compaction-core internals — the
layout, the tiers, the transcript and the rotation engine — are not among them, so the API shape is
checked against the built assembly rather than maintained only by prose. The list is asserted rather
than a count: a count is a metric, and pinning one pressures whoever comes next toward the number
instead of the design, which is how a genuinely useful type ends up hidden to keep a total down.

System tests reside in `AgentKitCoreTests.cs`, `AgentKitSessionsTests.cs`,
`CompactingAgentSessionTests.cs`, `PublicSurfaceTests.cs` and `XmlDocExampleTests.cs` within the
`DemaConsulting.AgentKit.Core.Tests` project, with helpers in `FakeSummarizer.cs`,
`SessionTestData.cs`, `DivergentTokenizerProviderSession.cs`, `ProviderTestDoubles.cs`,
`ScriptedChatClient.cs`, `StubToolPack.cs` and `TempDirectoryFixture.cs`.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required. The image
  promotion scenario uses a scripted `IChatClient` that contacts nothing; see
  _ImagePromotingChatClient Unit Verification Design_ for why a real provider would not observe
  the behavior under test
- **File system**: The path-containment scenarios require a writable temporary directory. The
  session scenarios need none; transcripts and provider sessions are held in memory
- **Models**: None; consolidation is performed by deterministic summarizer fakes
- **Isolation**: Each test method constructs its own policy, tool, temporary directory tree, or
  provider factory, summarizer, options and session; no state is shared between tests

## External Interface Simulation

The path-containment scenarios touch one external interface — the host file system — and it is
deliberately **not** simulated. The decision under verification is made about real paths and the
listing it filters comes from the real operating system, so a simulated file system would verify
the simulation rather than the control. Each scenario instead creates a disposable temporary tree
and removes it afterwards.

## System-Level Test Scenarios

### Path Containment: A Relative Escape Is Denied

**Test**: `AgentKitCore_SystemPathContainment_RelativeEscape_IsDenied`

Verifies that the system judges access by the normalized location a path denotes. Configures a
policy confined to one location and requests a file in a sibling directory by climbing out of the
permitted location with a parent segment. Asserts the request is refused, that no location is
handed back, and that a reason is supplied.

### Path Containment: Enumeration Lists Only Permitted Files

**Test**: `AgentKitCore_SystemPathContainment_Enumeration_ListsOnlyPermittedFiles`

Verifies at the system boundary that a listing of the permitted location reports the files it
contains and does not reach into a sibling directory outside it. Places one file inside the
permitted location and one in a sibling directory, and lists the permitted location through the
public API. Asserts the contained file appears and the sibling file does not. The scenario states
the containment boundary of a listing only; the narrower guarantee that a file **inside** the
permitted location which a read would refuse is also excluded is pinned by
`PathPolicy_EnumerateFiles_DeniedPatternFile_IsNotListed`, described in _PathPolicy Unit
Verification Design_, because a sibling directory is never walked and so cannot exercise the
per-candidate decision.

### Path Policy: Reading Widely While Writing Narrowly

**Test**: `AgentKitCore_SystemPathPolicy_ReadWideWriteNarrow_AllowsReadDeniesWrite`

Verifies that read access and write access are independent. Configures unrestricted reads with
writes confined to one location, then reads and attempts to write the same location outside it.
Asserts the read is permitted and the write is refused.

### Path Policy: A Denied Path Returns a Denial Without Throwing

**Test**: `AgentKitCore_SystemPathPolicy_DeniedPath_ReturnsDenialWithoutThrowing`

Verifies that a refusal reaches the caller as a return value carrying a reason, not as an
exception. Requests a location outside the permitted one and asserts the call returns a refusal
with no location and a non-empty reason.

### Path Policy: A Denial Message Discloses the Permitted Locations

**Test**: `AgentKitCore_SystemPathPolicy_DenialMessage_DisclosesPermittedLocations`

Verifies that a denial tells a confined model the truthful map of where it may work. Requests a
path outside the permitted location and asserts the message echoes the requested path, names the
permitted location, and marks its access level — the host-path-disclosure rule the earlier
redaction requirement enforced having been deliberately dropped.

### Path Policy: A Relative Path From a Model Resolves Against the Working Directory

**Test**: `AgentKitCore_SystemPathPolicy_RelativePathFromModel_ResolvesAgainstWorkingDirectory`

Verifies that the system reads a path the way a model writes one. Configures a policy for one
working directory holding a file, requests that file by name alone, and asserts the request is
permitted and the returned location reads back the file's content. This is the system-level
regression for a defect in which such a request was refused because the name was measured from the
location the host process happened to be running from.

### Path Policy: A Denial Message States How to Recover

**Test**: `AgentKitCore_SystemPathPolicy_DenialMessage_StatesHowToRecover`

Verifies that a relative request escaping the working directory is refused with a denial an agent
can act on. Asserts the message echoes the caller's input verbatim, states how the relative path
was interpreted against the working directory, and names the permitted location — the three
ordered parts of a disclosing denial.

### Image Delivery: A Tool-Returned Image Reaches the Provider on a User Message

**Test**: `AgentKitCore_SystemImagePromotion_ToolImageResult_ReachesTheProviderOnAUserMessage`

Verifies provider-independent image delivery end to end. Builds the caption-then-image tool result
a guarded tool produces, sends the conversation through the decorator as a function-invocation
loop would, and asserts the client behind it received the image on a following user message, as
the same content instance. Confirms a host can make image delivery independent of whether its
provider carries images out of tool results.

### Path Policy: Construction Without a Working Directory Is Rejected

**Test**: `AgentKitCore_SystemPathPolicy_ConstructionWithoutWorkingDirectory_IsRejected`

Verifies that the system refuses to create a path access policy without a working directory, and
refuses a null grant entry, confirming at the system boundary that an unguarded policy is
unrepresentable.

### Tool Limits: The Access Policy Carries the Published Ceilings

**Test**: `AgentKitCore_SystemToolLimits_PolicyCarriesDefaultLimits_ExposesPublishedValues`

Verifies that the ceilings a tool observes reach it through the access policy a host actually
builds. Constructs a real policy from one rooted rule without configuring any ceilings, and
asserts it exposes the four published values. Confirms that a host which states no budget still
operates within a bounded one.

### Guarded Tool: An Image Result Reaches the Runtime as Content

**Test**: `AgentKitCore_SystemGuardedTool_ImageResult_ReachesRuntimeAsContent`

Verifies the whole tool contract end to end. Composes a name through `ToolName`, builds a tool
through the only supported construction path whose delegate is declared to return an object, and
invokes it through the runtime's own entry point. Asserts the result is a two-element content
list — caption then image — rather than serialized JSON. The declared return type is deliberate:
a strongly-typed declaration would pass without the result-delivery guard and would prove
nothing. The guard is selective rather than a blanket passthrough — text and content are
preserved while structured data is serialized — and the unit-level scenarios that pin both edges
of that selection are described in _GuardedToolFactory Unit Verification Design_.

### Guarded Tool: A Denied Path Returns a Refusal Rather Than Throwing

**Test**: `AgentKitCore_SystemGuardedTool_DeniedPath_ReturnsDenialResultNotException`

Verifies the access policy, the result constructors and the guarded factory acting together.
Builds a tool governed by a policy confined to one location, invokes it with a path outside that
location, and asserts the call completes and returns refusal text naming the reason. Confirms a
refusal is a recoverable step for an agent rather than the end of its turn.

### Tool Naming: A Bare File Access Name Is Rejected

**Test**: `AgentKitCore_SystemToolNaming_BareFileAccessName_IsRejected`

Verifies at the system boundary that the only supported construction path will not issue a name
that collides with the Agent Framework's bare file access tools, so an application combining both
libraries cannot offer the model two tools with the same name.

### Guarded Tool: A Constructed Tool Carries Its Validated Name and Description

**Test**: `AgentKitCore_SystemGuardedTool_ConstructedTool_CarriesValidatedNameAndDescription`

Verifies that a tool built the only supported way is selectable by a model rather than anonymous.
Asserts the created tool carries the composed name and the supplied description.

### Tool Packs: A Host Without a Capability Is Offered No Tools From the Dependent Pack

**Test**: `AgentKitCore_SystemToolPacks_HostWithoutCapability_PackContributesNoTools`

Verifies capability-gated registration end to end. Composes a real access policy, a pack that
requires nothing and a pack that requires vision, and builds for a host that declares nothing.
Asserts the composed list contains only the undemanding pack's tool **and** that the vision pack
was never asked to create its tools — the assertion that distinguishes "not registered" from
"registered then filtered", and the reason the model can never see a tool it cannot use.

### Tool Packs: A Capable Host Receives Every Attached Tool

**Test**: `AgentKitCore_SystemToolPacks_CapableHost_ReceivesEveryAttachedTool`

Verifies that gating withholds nothing a host can support. Declares vision, attaches both packs,
and asserts the exact composed sequence — pack-add order, then each pack's own order — and that
the policy the application constructed is the one the pack was handed.

### Tool Packs: Colliding Family Prefixes Are Rejected

**Test**: `AgentKitCore_SystemToolPacks_CollidingFamilyPrefixes_AreRejected`

Verifies at the system boundary that an application cannot attach two packs claiming one family
prefix, a situation in which which of two identically prefixed tools a model invokes is undefined.
Asserts the refusal occurs where the application composed its packs rather than in a model's
behavior later.

### Long Conversation: The Session Outlives the Provider's Window

**Test**: `AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering`

Runs forty turns against a small reporting provider window and asserts every turn returns text and
that at least one rotation occurred. The scenario proves the session engine's headline behavior: a
compacting agent session continues to answer after accumulated history exceeds the provider window.

### Rotation: Replacement Sessions Preserve Operation

**Test**: `AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering`

The same long conversation also verifies rotation at the system boundary. The provider factory
creates replacement sessions as compaction occurs, the live session remains usable after each
replacement, and rotation is observable through `RotationCount`.

### Tiered Retention: Early Detail Survives Many Rotations

**Test**: `AgentKitSessions_AfterManyRotations_EarlyDetailIsStillCarriedInContext`

Places a distinctive marker in the first turn, drives many later turns and rotations, and asserts
the live replacement session still carries that marker in its seed history. The deterministic
summarizer preserves marker facts while reducing routine padding, so the test verifies the
round-robin retention structure rather than model memory.

### Pressure Response: Providers That Count Differently Keep Answering

**Tests**:

- `AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering`
- `CompactingAgentSession_DivergentTokenizer_KeepsAnsweringAndTerminates`
- `CompactingAgentSession_DivergentTokenizer_RotatesMoreOftenAndEscalatesHigher`
- `CompactingAgentSession_AfterAQuietStretch_RelaxesTheCompactionLevel`
- `CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial`

These tests cover the response to pressure. A normal long conversation keeps answering, and a
provider fake counting at 1x, 2x and 3x completes every turn. Divergence is then verified as a
difference rather than asserted away: a 2x and 3x provider rotates strictly more often and escalates
to a strictly higher level than a 1x one, each collapsing and failing if reverted to 1x. Adapting is
verified in both directions — the level comes back down after a quiet stretch, so a session that met
one busy period does not pay for it in fidelity thereafter. Separately, a window too small to hold a
full structure escalates to `CompactionLevel.High` and reports `MaterialDropped`. Together they
verify that pressure is answered in counts of turns and slots, and that the answer terminates,
without anything measuring a context that has not been sent.

### Out-of-Session Summarizer: Consolidation Is Deterministic

**Test**: `AgentKitSessions_AfterManyRotations_EarlyDetailIsStillCarriedInContext`

Uses an injected summarizer outside the live provider session. The marker-preserving fake records
consolidated material deterministically, proving the system can verify retention without asking the
provider to summarize itself.

### Tool Traffic: Whole Turns Are Indivisible

**Tests**:

- `SessionTranscript_AppendTurn_GroupsEntriesAsOneTurn`
- `RotationEngine_Rotate_HandsTheOlderTurnsOverAsRenderedMaterial`

Verifies the transcript records a user message, tool traffic and assistant answer as one turn, and
that a rotation hands the older material to the summarizer as whole rendered turns. Since all
boundaries are turn-granular, a tool call and its result are retained, consolidated or dropped
together, and a result can never reach a summarizer without its call.

### Compaction Reporting: Level and Dropped Material Are Visible

**Test**: `CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial`

Drives a window too small to hold a full structure, so compaction becomes aggressive and then
discards history, using a provider whose window is narrow rather than one that counts more heavily.
The response stream is asserted to include `CompactionLevel.High` and `MaterialDropped`, which are the
application-visible signals that compaction pressure is high and history was discarded.

### Provider Neutrality: The Window Comes From the Provider

**Test**: `AgentKitSessions_WindowComesFromTheProvider_NarrowCompactsWhereWideDoesNot`

Runs the same conversation against two providers differing only in the window they report. The narrow
one must provoke compaction and the wide one must not, with nothing configured alongside the session
to distinguish them. This verifies the window is a fact the adapter answers for, and that every
occupancy comparison stays in the currency of the reading it came from.

### In-Memory Verification: No Live Model Is Required

**Test**: `AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering`

Exercises the full session lifecycle through the shipped in-memory provider. The test proves the
same provider offered to application authors can demonstrate creation, turns, rotation and disposal
without network access.

### Public Surface: Exported Types Are Mechanical Evidence

**Tests**:

- `AgentKitCore_PublicSurface_IsTheDeliberateSet`
- `AgentKitCore_PublicSurface_ExcludesDeletedAndInternalTypes`

Reflects over the built assembly and asserts the public surface is exactly the deliberate list of
exported types, so a type becoming public is a decision someone made rather than an accident. The
companion test asserts deleted or internal compaction-core types are not exported, keeping the
merged API boundary verifiable.

### Documented Examples: Every Published Example Compiles

**Test**: `AgentKitCore_XmlDocExamplesCompile`

Compiles every shipped XML documentation example against the real API. A stale example that names a
removed member or omits the new response signals fails here before a consumer follows it.

## Acceptance Criteria

A system-level test run passes when all twenty-eight scenarios above pass without error or exception
beyond those explicitly asserted. Any unexpected exception, wrong exception type, wrong return
value, permitted path that should have been refused, relative path resolved against the process
working directory, escaped file appearing in a listing, denial message that fails to echo the
request, name the permitted locations, or otherwise offer a way forward, tool
result arriving as serialized JSON rather than as the content the tool produced, tool-returned
image failing to reach the provider, or tool offered
to a host that cannot support it constitutes a failure. For the session scenarios, any session that
stops answering as the conversation grows, any rotation that leaks a superseded provider session, any
comparison that mixes a figure one party counted with a figure another did, any pathological
compaction that fails to terminate, any missing dropped-material signal, any lost early detail, or
any public-surface drift likewise constitutes a failure.

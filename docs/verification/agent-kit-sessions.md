# AgentKitSessions System Verification Design

This document describes the system-level verification strategy for the AgentKitSessions system.

## Verification Approach

The AgentKitSessions system is verified through system-level tests that run whole conversations
end to end against `InMemoryProviderSession` and a deterministic fake summarizer, asserting the
system's promise rather than any one unit's behavior.

**Everything is deterministic, and that is the central point of the strategy.** The compaction
engine is a pure function of the layout it is handed and the summarizer it is injected with, so
replacing the one non-deterministic collaborator — the model — with a fake makes an entire
conversation reproducible: the same messages always produce the same tiers, the same rotation count
and the same saturation reports. Nothing here is a probabilistic assertion about what a model might
remember; every assertion is about what the engine actually kept.

The shipped in-memory provider is used rather than a test-only double, so the system tests exercise
exactly the code an application author is offered for the same purpose. It is run in both of its
configurations — reporting its own usage, and reporting nothing — so both provider families are
covered.

**What is out of automated scope, stated honestly.** No live provider and no real model is
contacted. Two things therefore rest on the exploratory measurement recorded in the design rather
than on a test: that a real summarizing model, prompted as this system prompts it, preserves the
categories of detail the prompt names; and that the tiered arrangement out-recalls a flat rolling
summary over 50 rotations. Both are recorded in _AgentKitSessions System Design_ with their sample
sizes and their source. What the tests establish is the mechanism those measurements depend on:
that rotation happens when it should, that tiers age as designed, that the previous record is
carried forward, that the bound holds, and that a failure to reduce is reported.

System tests reside in `AgentKitSessionsTests.cs`, with the deterministic fake summarizer in
`FakeSummarizer.cs` and the exact-size transcript builders in `SessionTestData.cs`, all within the
`DemaConsulting.AgentKit.Sessions.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No provider is contacted and **no network access is used**
- **File system**: None. Every transcript is built in memory
- **Models**: None. Consolidation is performed by `FakeSummarizer`, which computes its answer
  arithmetically from the request
- **Isolation**: Each test constructs its own provider factory, summarizer, options and session; no
  state is shared

## Acceptance Criteria

A system-level test run passes when every scenario below passes without error or exception beyond
those explicitly asserted. Any session that stops answering as a conversation grows, any rotation
that leaves the context outside its construction bound, any early detail absent from the context
after many rotations, any superseded provider session left undisposed, or any failure to reduce that
goes unreported constitutes a failure.

## Test Scenarios

### Long Conversation: The Session Outlives the Provider's Window

**Test**: `AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering`

Runs twenty turns against a deliberately small window, each turn larger than a fraction of it, and
asserts the session keeps answering, rotated at least five times, created exactly one provider
session per rotation plus the original, disposed every superseded session, and left only the live
one open. This is the system's headline promise: a conversation far larger than the window keeps
working, and nothing is leaked doing it.

### Long Conversation: The Context Stays Within Its Construction Bound

**Test**: `AgentKitSessions_LongConversation_StaysWithinItsConstructionBound`

Runs the same conversation and checks the bound **at the moment of every rotation**, not merely at
the end. The bound — fixed overhead, the sum of the tier budgets, and the framing each tier record
carries when it is seeded — is a property of the
configuration alone, so a rotation that left the context outside it would mean the arrangement is
not in fact bounded. Checking at each rotation rather than once at the end is what makes a transient
violation detectable.

### Long Conversation: Early Detail Survives Many Rotations

**Test**: `AgentKitSessions_AfterManyRotations_EarlyDetailIsStillCarriedInContext`

States a distinctive fact — a specific file path — in the first turn, buries it under thirty later
turns, and asserts that after at least five rotations the path is still present in the context the
session would send. The summarizer used here keeps everything it is given, so what survives is
decided by the tier arrangement and the ratchet rather than by a model's discretion. This is the
property the tiered scheme exists for, asserted rather than assumed: a flat rolling summary
re-summarizes its own summary and loses old material entirely.

### Saturation: A Context That Cannot Be Reduced Says So

**Test**: `AgentKitSessions_ContextWithNoRedundancyLeft_ReportsSaturation`

Uses a summarizer that returns its material unchanged — a context with no redundancy left — runs
until the first rotation, and asserts the turn reports saturation naming the tier that could not
reduce. Without detection this failure is invisible: every rotation appears to succeed while buying
no room.

### Provider Neutrality: Both Provider Shapes Compact and Stay Bounded

**Test**: `AgentKitSessions_SameConversation_CompactsAndStaysBoundedOnBothProviderShapes`

Runs the identical conversation twice, once against a provider that reports its own usage and once
against one that reports nothing, and asserts each used the usage source its provider offered, that
both compacted, that both stayed within their bound, and that both kept consolidated records ahead
of verbatim turns.

**Rotation counts are deliberately not asserted equal, and the reason is recorded here rather than
worked around.** A provider's own figures count framing this library never sees — the envelope
around each seeded record, for one — so a reporting provider legitimately crosses the threshold
sooner than the engine's own estimate does. That difference is a true account of the two providers,
and asserting it away would mean preferring an estimate over a measurement. What must hold on both
is that compaction happens, that the bound is respected, and that the context keeps its shape.

### Documented Examples: Every Published Example Compiles

**Test**: `AgentKitSessions_XmlDocExamplesCompile`

Compiles every `<example><code>` block in the package's shipped XML documentation against the real
API. The examples are the instructions a consumer follows to configure tier budgets, supply a
summarizer and run a compacting conversation; an example naming a member the code does not have
would fail only once the reader had acted on it.

## Platform Verification

The system's platform and runtime requirements are evidenced by running
`AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering` under source filters for
Windows, Linux and macOS and for .NET 8, 9 and 10. That test is chosen because it exercises the
whole system — transcript, estimation, rotation, consolidation, provider lifecycle and disposal — so
a platform on which any part of it failed would not produce the evidence.

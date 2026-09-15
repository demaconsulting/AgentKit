## Summarizer Unit Verification Design

This document describes the unit-level verification strategy for `ConsolidationRequest`,
`ISummarizer` and `ConsolidationPrompt`.

### Verification Approach

`ConsolidationRequest` and `ConsolidationPrompt` are deterministic and stateless, so they are
verified by direct construction and direct call with no mocking.

`ISummarizer` is an interface and has no behavior of its own. Its obligations — that the engine
injects it, that a previous record arrives as an input, that a null return is refused — are verified
where they are observable, in _RotationEngine Unit Verification Design_.

Two scenarios here are unusual and are the reason this document is worth reading. The first asserts
that the recommended instruction contains **no digit at all**. That is a mechanical proxy for a
design rule that would otherwise rest on review: a model cannot count its own output, so the prompt
must never name a target size, and a check that simply looks for digits catches any future edit that
reintroduces one. The second asserts, by phrase, that the instruction still asks for each category
of detail the design names — paths, decisions, constraints, errors, outstanding work — because those
categories are the whole of what makes a consolidation worth keeping, and prose drifts.

Unit tests reside in `SummarizerTests.cs` within the `DemaConsulting.AgentKit.Sessions.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. **No model and no network access is used**
- **Mocking**: None required
- **Isolation**: Each test constructs its own request; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any prompt that names a target size, any prompt that has stopped asking for a
category of detail the design names, any composition that places the new material before the
previous record, or any malformed request accepted constitutes a failure.

### Test Scenarios

#### AgentKitSessions-Summarizer-InjectedContract: A First Recording Is Distinguished From an Extension

**Test**: `ConsolidationRequest_IsDegradation_TrueWhenNoPreviousRecord`

Asserts a request with no previous record reports itself as a degradation and one with a previous
record does not. This is what tells a summarizer whether the ratchet rule applies: never drop detail
an earlier consolidation kept, except when deliberately coarsening.

#### AgentKitSessions-Summarizer-RejectsMalformedRequest: An Impossible Request Is Refused

**Tests**: `ConsolidationRequest_Construct_TierZero_Throws`,
`ConsolidationRequest_Construct_BlankMaterial_Throws`,
`ConsolidationRequest_Construct_NonPositiveBudget_Throws`

Three error paths. Tier zero is verbatim history by definition, so a request to consolidate into it
could only be a defect in the engine. Blank material would spend a model round trip to say nothing.
A tier that could hold nothing is not a tier. Each is refused where it was constructed rather than
discovered from a record that makes no sense.

#### AgentKitSessions-Summarizer-ConsolidationPrompt: The Prompt Names No Target Size

**Test**: `ConsolidationPrompt_Instruction_NamesNoTargetSize`

Asserts the recommended instruction contains no digit. Asking a model to hit a token budget does not
work — in the compaction spike, consolidations asked for between 9,870 and 19,741 tokens returned
1,665 and 4,259 tokens (n = 2 requests, recorded in that spike). This scenario is what stops that
rule eroding through a well-meaning edit.

#### AgentKitSessions-Summarizer-ConsolidationPrompt: The Prompt Still Asks for Specific Content

**Test**: `ConsolidationPrompt_Instruction_AsksForSpecificContent`

A data-driven scenario asserting the instruction still names each category of detail the design
requires to survive: paths, decisions, constraints, errors and outstanding work. Prompting for
content rather than for length is the whole approach, and prose drifts without a check on it.

#### AgentKitSessions-Summarizer-ConsolidationPrompt: Composition Reads in the Ratchet's Order

**Tests**: `ConsolidationPrompt_Compose_PlacesThePreviousRecordBeforeTheNewMaterial`,
`ConsolidationPrompt_Compose_Degradation_StatesThereIsNoPreviousRecord`,
`ConsolidationPrompt_Compose_AlwaysCarriesTheInstruction`,
`ConsolidationPrompt_Compose_NullRequest_Throws`

Asserts both the previous record and the new material appear and that the previous record appears
**first** — by index comparison, not merely by presence — because that is the order the ratchet
reads in: carry this forward, then fold this in. Asserts a degradation states the absence explicitly
rather than presenting an empty section a model might try to fill from nothing. Asserts the
published instruction always leads the composed text, and that a null request is refused rather than
composing a prompt about nothing.

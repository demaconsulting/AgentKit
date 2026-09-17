## Summarizer Unit Verification Design

This document describes the unit-level verification strategy for `ISummarizer`,
`ConsolidationRequest` and `ConsolidationPrompt`.

### Verification Approach

The summarizer seam is verified by constructing requests and composing prompts. Tests assert a
request carries the target tier, material and aggressiveness `Instruction`; it does not carry a
previous record or a requested length. Prompt tests verify material is treated as peers, repetition
may be collapsed, preservation categories remain named, and each `CompactionLevel` selects a plain
language instruction.

Unit tests reside in `SummarizerTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no model is contacted
- **Mocking**: None required; tests exercise request and prompt construction directly
- **Isolation**: Each test constructs its own request

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any request that loses tier, material or instruction, any prompt that restores a
previous-record section, any instruction that omits preservation categories, or any malformed request
accepted constitutes a failure.

### Test Scenarios

#### AgentKitCore-Summarizer-InjectedContract: A First Recording Is Distinguished From an Extension

**Test**: `ConsolidationRequest_Construct_CarriesTierMaterialAndInstruction`

Asserts a request carries `TierIndex`, `Material` and `Instruction`. The scenario verifies the new
peer-consolidation contract: the summarizer receives material and an aggressiveness instruction, not
a previous record or a size target.

#### AgentKitCore-Summarizer-RejectsMalformedRequest: An Impossible Request Is Refused

**Tests**:

- `ConsolidationRequest_Construct_TierBelowOne_Throws`
- `ConsolidationRequest_Construct_Blank_Throws`
- `ConsolidationPrompt_Compose_Null_Throws`

Rejects a tier below one, blank material, a blank instruction and a null request to compose. The
verbatim tail is not a consolidation tier, and a request without material or instruction has no
meaningful summarizer work.

#### AgentKitCore-Summarizer-ConsolidationPrompt: The Prompt Names No Target Size

**Test**: `ConsolidationPrompt_Compose_HasNoPreviousRecordSection`

Composes a prompt and asserts it includes the base instruction, selected aggressiveness clause and
material while excluding any previous-record section. The prompt treats the material as peers.

#### AgentKitCore-Summarizer-ConsolidationPrompt: The Prompt Still Asks for Specific Content

**Test**: `ConsolidationPrompt_Instruction_LicensesCollapsingRepetition`

Asserts the base instruction allows collapsing repetition while preserving facts such as decisions,
errors and outstanding work. This is how consolidation can buy room without discarding the named
classes of useful context.

#### AgentKitCore-Summarizer-ConsolidationPrompt: Composition Reads in Peer Order

**Test**: `ConsolidationPrompt_InstructionFor_SelectsPerLevelClause`

Asserts each compaction level maps to its own aggressiveness instruction. The instruction is plain
language, so the summarizer adapts to low, medium or high pressure without receiving a numeric target.

### TextFileLineBuffers Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileLineBuffers` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses a fresh in-memory buffer store; default slots,
named slots, non-consuming paste and validation are exercised directly. This keeps verification at
the same boundary the runtime or composing application uses, rather than proving a substitute
behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `TextFile/TextFileLineBuffersTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the fresh in-memory buffer store it needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 5 requirement scenarios below, covering 10 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-Buffers-DefaultSlot: Default Slot

**Test**: `TextFileLineBuffers_DefaultSlot_IsPublished`

**Test**: `TextFileLineBuffers_DistinctSlots_HoldIndependentFragments`

The listed tests prove the default slot name is published for the omitted-name case; distinct slots
hold independent fragments.

##### AgentKitTools-TextFile-Buffers-CaptureAndPaste: Capture And Paste

**Test**: `TextFileLineBuffers_Capture_ThenPaste_ReturnsTheCapturedTextWithoutConsuming`

The listed tests prove captured text is returned by a paste from the same slot, without consuming
it.

##### AgentKitTools-TextFile-Buffers-Replace: Replace

**Test**: `TextFileLineBuffers_Recapture_ReplacesTheSlot`

The listed tests prove a later capture into the same slot replaces the earlier one.

##### AgentKitTools-TextFile-Buffers-Validation: Validation

**Test**: `TextFileLineBuffers_EmptySlot_IsAMiss`

**Test**: `TextFileLineBuffers_InvalidArguments_ThrowArgumentExceptions`

The listed tests prove an empty slot is reported as a miss rather than as empty text; a null or
empty slot name is a programming error, and a null capture is rejected.

##### AgentKitTools-TextFile-Buffers-PopulatedSlots: Populated Slots

**Test**: `TextFileLineBuffers_PopulatedSlots_Empty_IsEmpty`

**Test**: `TextFileLineBuffers_PopulatedSlots_OneSlot_NamesIt`

**Test**: `TextFileLineBuffers_PopulatedSlots_SeveralSlots_AreOrderedOrdinally`

**Test**: `TextFileLineBuffers_PopulatedSlots_AfterPaste_StillContainsTheSlot`

The listed tests prove the store reports the names of the currently populated slots in ordinal order
— nothing when empty, one when a single slot holds text, several ordered ordinally regardless of
capture order — and that a paste leaves the slot populated, the property the paste refusal relies on
when it names slots that hold content.

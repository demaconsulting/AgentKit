## ToolOutputRetention Unit Verification Design

This document describes the unit-level verification strategy for the `ToolOutputRetention` class and
its snapshot types, `ToolOutputRetentionSnapshot` and `RetainedToolOutput`.

### Verification Approach

`ToolOutputRetention` is verified through unit tests that create real retentions, wrap tools built
with the plain `AIFunctionFactory`, invoke them and the reader tools exactly as a runtime would, and
assert on the text the model would receive. `ToolOutputStore`, `SpillAttachment`, the two readers and
`GuardedFunction` are used as real dependencies, all documented in *ToolOutputRetention Unit Design*:
the behavior under verification is that one handle owns one store across every tool it wraps and that
the store's content and counter survive an export and a restore, which only the real collaborators can
show. Persistence is exercised through the published source-generated JSON contract rather than a
substitute serializer.

Unit tests reside in `ToolOutputRetentionTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK, using asynchronous test methods for scenarios
  that invoke a tool
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: None beyond the base class library and `Microsoft.Extensions.AI.Abstractions`; no
  file system or network access
- **Mocking**: None; tools are small `AIFunctionFactory` functions returning 400 numbered lines
- **Isolation**: Each test constructs its own retentions

### Acceptance Criteria

A unit test run passes when all seventeen scenarios below, covering twenty tests, pass. Any reference not shared across
wraps, any reader instance that changes, any mutable reader list, any invalid ceiling, tool or list accepted, any
restored reference answering different content, any reference re-issued after a restore, any snapshot not fitted to
the restoring ceilings, any malformed snapshot partly restored, any reference above the counter restored, any snapshot
or retained output that changes when the caller's collection changes, any missing lines accepted, any JSON round trip
that alters a snapshot, or any reference shared between retentions constitutes a failure.

### Test Scenarios

#### AgentKitCore-ToolOutputRetention-SharedAcrossWraps: Tools Wrapped Separately Share One Store

**Test**: `ToolOutputRetention_Wrap_ToolsWrappedSeparately_ShareOneStore`

One tool is wrapped singly and another through the list overload. Asserts their previews name
`output-1` and `output-2` and the reader pages `output-2`.

#### AgentKitCore-ToolOutputRetention-ReaderToolsPublished: The Readers Are Stable

**Test**: `ToolOutputRetention_ReaderTools_AreTheTwoReadersAndStable`

Asserts the readers are `tool_output_read` then `tool_output_search`, the same list instance on every
read, and that `Limits` and `Options` are the values supplied (the default options when none were).

#### AgentKitCore-ToolOutputRetention-ReaderToolsPublished: The Readers Are a Read-Only List

**Test**: `ToolOutputRetention_ReaderTools_IsReadOnlyCollection`

Asserts `ReaderTools` is a `ReadOnlyCollection` over the two readers in order, and that replacing an
element through `IList` throws `NotSupportedException`.

#### AgentKitCore-ToolOutputRetention-MinimumResultCeiling: A Too-Small Ceiling Is Refused

**Test**: `ToolOutputRetention_Constructor_CeilingBelowMinimum_ThrowsArgumentException`

A ceiling one below `MinimumResultCharacters` throws `ArgumentException` naming `limits`; a null
ceiling throws `ArgumentNullException`.

#### AgentKitCore-ToolOutputRetention-RejectsInvalidTools: Invalid Tools Are Refused

**Test**: `ToolOutputRetention_Wrap_ReservedPrefix_Throws`

**Test**: `ToolOutputRetention_Wrap_OverlongName_Throws`

**Test**: `ToolOutputRetention_Wrap_DuplicateNamesInOneCall_Throws`

**Test**: `ToolOutputRetention_Wrap_Null_Throws`

A reader wrapped back into its retention, a name one past `ToolName.MaxLength` (naming `tool`), two
tools of one name in one call (naming `tools`), and a null tool, null list or null entry are each
refused.

#### AgentKitCore-ToolOutputRetention-ExportRestore: A Restored Reference Reads the Same Lines

**Test**: `ToolOutputRetention_ExportRestore_RestoredReaderReturnsSameLines`

Asserts a page of `output-1` read after `Export` and `Restore` is exactly the page read before.

#### AgentKitCore-ToolOutputRetention-ExportRestore: The Wrapped-Lines Flag Survives

**Test**: `ToolOutputRetention_ExportRestore_WrappedFlagSurvives`

A single 5,000-character line is wrapped when retained; asserts the flag is exported and is still set
after a restore and a second export.

#### AgentKitCore-ToolOutputRetention-ExportRestore: A Snapshot Survives a JSON Round Trip

**Test**: `ToolOutputRetention_Snapshot_JsonRoundTrip_IsEquivalent`

Serializes a snapshot with `ToolOutputRetentionSnapshot.JsonTypeInfo`, asserts camel-case names, and
asserts the deserialized copy has the same counter, reference, lines and flag.

#### AgentKitCore-ToolOutputRetention-NumberingContinuesAfterRestore: New Outputs Number After the Last Issued

**Test**: `ToolOutputRetention_Restore_NewOutputsNumberAfterLastIssued`

A retention keeping one output retains three; after a restore, the next output is `output-4`, never a
re-issued earlier reference.

#### AgentKitCore-ToolOutputRetention-NumberingContinuesAfterRestore: A Counter-Only Snapshot Answers No Longer Retained

**Test**: `ToolOutputRetention_Restore_CountersOnlySnapshot_EarlierReferenceAnsweredNoLongerRetained`

A snapshot with counter 2 and no outputs; asserts `output-2` is answered `TargetNotFound`, "no longer
retained", rather than "never retained".

#### AgentKitCore-ToolOutputRetention-RestoreAppliesCurrentCeilings: Smaller Ceilings Evict the Oldest

**Test**: `ToolOutputRetention_Restore_SmallerCeilings_EvictsOldestFirst`

Three outputs restored under a two-output ceiling; asserts `output-2` and `output-3` remain and the
counter is still 3.

#### AgentKitCore-ToolOutputRetention-RestoreRejectsMalformedSnapshot: A Malformed Snapshot Is Refused

**Test**: `ToolOutputRetention_Restore_MalformedSnapshot_Throws`

A negative counter, a malformed reference, a repeated reference, an output with no lines, a line
holding a break, a line longer than 2,000 characters and a null output each throw
`ArgumentException`; a null snapshot throws `ArgumentNullException`.

#### AgentKitCore-ToolOutputRetention-RestoreRejectsMalformedSnapshot: A Reference Above the Counter Is Refused

**Test**: `ToolOutputRetention_Restore_ReferenceAboveCounter_Throws`

A snapshot with counter 1 holding `output-2`, and one with counter 0 holding `output-1`, each throw
`ArgumentException` naming `snapshot`, so a reference never issued is never installed.

#### AgentKitCore-ToolOutputRetention-SnapshotOwnsItsData: A Snapshot Ignores Changes to the Caller's List

**Test**: `ToolOutputRetentionSnapshot_Constructor_CallerListChanged_SnapshotUnchanged`

A snapshot is built from a list holding `output-1`; the caller then adds `output-2` and removes
`output-1`. Asserts the snapshot still holds only `output-1`, in a `ReadOnlyCollection`.

#### AgentKitCore-ToolOutputRetention-SnapshotOwnsItsData: A Retained Output Ignores Changes to the Caller's Lines

**Test**: `RetainedToolOutput_Constructor_CallerLinesChanged_OutputUnchanged`

An output is built from the lines `a` and `b`; the caller then replaces the first and appends a third.
Asserts the output still holds `a` and `b` in a `ReadOnlyCollection`, and a retention restored from it
pages exactly those two lines.

#### AgentKitCore-ToolOutputRetention-SnapshotOwnsItsData: Missing Lines Are Refused

**Test**: `RetainedToolOutput_Constructor_NullLines_ThrowsArgumentNullException`

Null lines throw `ArgumentNullException` naming `lines`, and deserializing a snapshot whose output
omits `lines` throws `ArgumentNullException`.

#### AgentKitCore-ToolOutputRetention-Isolated: Two Retentions Share Nothing

**Test**: `ToolOutputRetention_SeparateInstances_AreIsolated`

A reference issued by one retention, read through another's reader, is answered "never retained".

### AgentProfile Unit Verification Design

This document describes the unit-level verification strategy for the `AgentProfile` class.

#### Verification Approach

Nothing is mocked or stubbed. The class has no dependency worth substituting: it validates its
own arguments, captures them, and exposes them as read-only properties. What must be verified is
what a composing application can observe — that the stated parts survive construction, that a
malformed argument is rejected as a programming error, and that the source collections are
copied rather than aliased.

The immutability scenario is asserted **behaviorally** rather than by type inspection: the
scenario constructs a profile from mutable source collections, mutates the source afterwards,
and asserts the profile's public collections are unchanged. A profile that aliased the source
would pass a type check that only asserted `IReadOnlyList<T>` and fail this one.

Unit tests reside in `Agent/AgentProfileTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access, or network access required
- **Isolation**: Each test constructs its own profile; no state is shared

#### Acceptance Criteria

A unit test run passes when all seven scenarios below pass without error or exception beyond
those explicitly asserted. A stated part missing after construction, an accepted null or empty
name or instructions, an accepted null or empty tool entry, an accepted null grant entry, and a
profile that reflected later mutations of its source collections each constitute a failure.

#### Test Scenarios

##### AgentKitTools-Agent-Profile-StatedParts: The Stated Parts Survive Construction

**Test**: `AgentProfile_Constructor_StatedParts_AreKept`

Normal operation: constructs a profile with a name, instructions and tool names, and asserts
each is exposed on the corresponding property.

##### AgentKitTools-Agent-Profile-StatedParts: Grants and Description Survive Construction

**Test**: `AgentProfile_Constructor_GrantsAndDescription_AreKept`

Normal operation for the optional parts: a profile stating grants and a description exposes
each on the corresponding property, confirming both survive the same construction path.

##### AgentKitTools-Agent-Profile-StatedParts: An Empty Tool List Is Permitted

**Test**: `AgentProfile_Constructor_NoTools_IsPermitted`

Boundary condition: a profile stating no tools is a legitimate profile — summarizing or judging
text the parent passes in the task needs no tools at all — and is not treated as a mistake. The
scenario asserts the construction succeeds and the tool collection is empty.

##### AgentKitTools-Agent-Profile-RejectsMalformed: A Missing Name or Missing Instructions Is Refused

**Test**: `AgentProfile_Constructor_MissingNameOrInstructions_Throws`

Error path: a null or empty name, and a null or empty instructions string, each throw at
construction. A nameless profile could not be selected, and a silent profile has no job, so the
mistake is reported at the line that made it.

##### AgentKitTools-Agent-Profile-RejectsMalformed: An Empty Tool Name Is Refused

**Test**: `AgentProfile_Constructor_EmptyToolName_ThrowsArgumentException`

Error path: a tool collection containing a null or empty entry throws at construction. An empty
tool name could never match an attached tool and would silently reduce the child's admitted
tools, so the mistake is reported explicitly.

##### AgentKitTools-Agent-Profile-RejectsMalformed: A Null Grant Is Refused

**Test**: `AgentProfile_Constructor_NullGrant_ThrowsArgumentException`

Error path: a grant collection containing a null entry throws at construction. A null grant
leaves the child's reach undetermined, so the mistake is reported at the line that made it.

##### AgentKitTools-Agent-Profile-Immutable: The Source Collections Are Copied

**Test**: `AgentProfile_Constructor_SourceCollections_AreCopied`

Boundary condition on the type's immutability: constructs a profile from mutable source
collections, mutates each source afterwards, and asserts the profile's collections are
unchanged. A profile that aliased its sources could be adjusted by whatever holds a reference
to those sources, and the point of the type is that the child's identity has exactly one
author.

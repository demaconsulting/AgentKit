## TextFile Subsystem Verification Design

This document describes the subsystem-level verification strategy for the TextFile tool family.

### Verification Approach

The subsystem is verified through integration tests that exercise the family the way an application
does: composed through the AgentKitCore `ToolPackBuilder` under one access policy, then invoked
through the published tool list by name and argument dictionary, exactly as an agent runtime
invokes it. Nothing is mocked. The access policy, the file system and the reparse points are all
real, because the properties under verification — that enumeration and access reach the same
conclusion, that a link cannot be used to escape, that a refusal reaches a model as a result —
are properties of the real thing and a test double would prove only that the double behaves.

The scenarios here assert what belongs to the family as a whole: one family prefix, one policy
governing every tool, refusals that are returned rather than thrown, and refusal text carrying no
host location. The algorithm of any single tool is verified in that unit's own document.

The boundary the subsystem is exercised at is deliberately the composed tool list rather than the
units' internal factories, because that list is what an application actually attaches. A tool that
could not be reached that way would not be reachable by an agent either.

Subsystem tests reside in `TextFile/TextFileTests.cs`, with the shared reparse-point test fixture in
`TextFile/ReparsePointFixture.cs`, within the `DemaConsulting.AgentKit.Tools.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: Each scenario creates its own temporary directory tree — a permitted root and a
  sibling directory outside it — and deletes it afterwards
- **Reparse points**: Created by the shared fixture using a directory junction on Windows
  (`cmd.exe /c mklink /J`, which needs no elevation) and a symbolic link elsewhere. Failure to
  create a link **fails** the test rather than skipping it, so a missing safety control can never
  appear as coverage
- **Isolation**: Each test constructs its own policy, composition and temporary tree; no state is
  shared between tests

### Acceptance Criteria

A subsystem test run passes when all ten scenarios below pass without error or exception beyond
those explicitly asserted. A tool published outside the family prefix, a refusal raised as an
exception rather than returned, a policy refusal that fails to disclose the permitted location, a relative name
that is not resolved against the workspace, an escaped file
appearing in a listing, a permitted read that fails, a refused write that succeeds, and a truncated
result where a refusal was required each constitute a failure.

### Test Scenarios

#### AgentKitTools-TextFile-FamilyComposition: The Family Publishes Read, Write and List

**Test**: `TextFile_Family_ComposedThroughBuilder_PublishesReadWriteAndList`

Normal operation: composes the pack through a `ToolPackBuilder` and asserts the three tool names,
in order, confirming the family is attached as one unit and publishes what it promises.

#### AgentKitTools-TextFile-FamilyComposition: A Host Declaring Nothing Still Receives the Family

**Test**: `TextFile_Family_HostDeclaringNoCapability_StillReceivesTheFamily`

Boundary condition: a host that declares no capability at all still receives all three tools,
confirming the family is not gated behind a declaration an operator would have to know to make.

#### AgentKitTools-TextFile-GuardedConstruction: Every Tool Carries a Validated Name and a Description

**Test**: `TextFile_Family_EveryTool_CarriesAValidatedNameAndDescription`

Runs each published name through the naming convention's own validation and asserts a non-empty
description, confirming no tool is offered to a model that the model cannot identify or choose.

#### AgentKitTools-TextFile-GuardedConstruction: A Result Reaches the Caller Unserialized

**Test**: `TextFile_Family_ToolResult_ReachesTheCallerUnserialized`

Asserts the result arrives as plain text and specifically not as a `JsonElement`. This is the
observable consequence of the guarded construction path; without it the result would be flattened
into JSON and the failure would be silent.

#### AgentKitTools-TextFile-PolicyGoverned: A Read-Wide, Write-Narrow Policy Is Honored

**Test**: `TextFile_Family_ReadWideWriteNarrow_PermitsTheReadAndRefusesTheWrite`

Composes a policy permitting reads beneath the root and writes only outside it, then reads one path
successfully and is refused the write of that same path. The successful read is what makes the
refusal meaningful: the path is reachable through a read-only grant, and only the absence of a
read-write grant there refused the write.

#### AgentKitTools-TextFile-PolicyGoverned: A Path Beneath a Link Outside the Root Is Refused by Every Tool

**Test**: `TextFile_Family_PathBeneathLinkOutsideRoot_IsRefusedByEveryTool`

Error path and security control: creates a real reparse point inside the permitted root pointing at
a sibling directory, proves the escaped file is readable through the link on disk, then asserts the
read and the write are refused and the listing never mentions the file. The on-disk read is what
stops a broken fixture from making the scenario pass vacuously.

#### AgentKitTools-TextFile-PolicyGoverned: A Relative Path From a Model Is Resolved Against the Workspace

**Test**: `TextFile_Family_RelativePathFromAModel_IsResolvedAgainstTheWorkspace`

Verifies the family's three tools agree about what a name means. Lists the workspace without
naming it — the first request an agent makes — and then reads the name that listing reported,
supplying it exactly as the listing gave it. Asserts the listing is the relative name and the read
returns the file's content. A family whose tools interpreted names differently would let an agent
list a name it then could not read.

#### AgentKitTools-TextFile-DenialsAreResults: A Refused Request Returns a Result

**Test**: `TextFile_Family_DeniedRequest_ReturnsAResultWithoutThrowing`

Error path: a request for a location outside the permitted one returns text naming a denial reason
rather than raising an exception, confirming a refused agent is told why rather than stranded.

#### AgentKitTools-TextFile-DenialsAreResults: Every Refusal Discloses the Permitted Location

**Test**: `TextFile_Family_DenialText_DisclosesPermittedLocation`

Collects the refusals of all three tools for a location outside the permitted one and asserts each
names the permitted location so a confined model learns where it may work. The transcript leaves
the process, so this disclosure is a deliberate control — the rule that once redacted these
refusals is dropped.

#### AgentKitTools-TextFile-ObservesPolicyLimits: A File Beyond the Read Ceiling Is Refused, Not Truncated

**Test**: `TextFile_Family_FileBeyondTheReadCeiling_IsRefusedNotTruncated`

Boundary condition: composes the family under a policy carrying a small read ceiling and asserts the
result is a refusal naming that ceiling, containing no part of the file's content.

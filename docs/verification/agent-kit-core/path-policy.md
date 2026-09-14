## PathPolicy Unit Verification Design

This document describes the unit-level verification strategy for the `PathPolicy` class.

### Verification Approach

`PathPolicy` is verified through unit tests that exercise the policy against a **real file
system**. `RealPathResolver` and `PathRule` are used as real dependencies
rather than substitutes — both are documented dependencies in _PathPolicy Unit Design_, and
substituting either would remove exactly the behavior the tests exist to confirm.

The suite verifies the current path model from an agent's viewpoint. A policy has one required
`WorkingDirectory`, normalized to an absolute location and used only as the anchor for relative
requests,
and zero or more `Grants`, each a `PathRule` carrying an `AccessLevel`. The working directory and
grants are deliberately orthogonal: the anchor may be granted read-write, granted read-only, or
granted nothing at all. An empty grant set is valid and permits nothing.

The tests state paths the way a model states them — a bare file name, a leading current-directory
token, a nested relative name, a placeholder, and no path at all — because those spellings are how a
request actually arrives. Relative requests are measured from the working directory, not from the
process directory, while absolute requests remain accepted and are judged by the same containment
decision. A separate group covers dialect mirroring for emitted names: relative input yields
relative output only when the working directory is granted and the result lies within it; otherwise
the emitted name is absolute.

One scenario is written as a contrast rather than as a plain assertion, so that it cannot quietly
become vacuous: the naive-prefix scenario asserts that a text-based check _would_ accept the
request before asserting the policy refuses it. Denial
scenarios verify the disclosure behavior: the request is echoed, a relative interpretation is
reported only when one occurred and in canonical (lexically normalized) form with `.` and `..`
collapsed, and the permitted locations are enumerated with access levels or reported as empty.

Unit tests reside in `PathPolicyTests.cs` within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **File system**: A temporary directory tree created per test through `TempDirectoryFixture`,
  with real files written into both granted locations and locations outside them
- **Unresolvable input**: A path containing an embedded null character provides a portable,
  deterministic way to exercise the "location cannot be determined" path
- **Mocking**: None
- **Isolation**: Each test constructs its own policy and its own fixture; no state is shared

### Acceptance Criteria

A unit test run passes when all forty-seven scenarios below pass without error or exception beyond
those explicitly asserted. Any escaping path that is permitted, any excluded file that
appears in a listing, any relative request resolved against the process working directory, any
missing working directory accepted, any null grant accepted, any empty grant set permitting access,
any read-only grant authorizing a write, any denial that fails to echo and enumerate as specified,
or any exception escaping a refusal constitutes a failure.

### Test Scenarios

#### AgentKitCore-PathPolicy-DenyEscapedPath: A Request Passing a Naive Prefix Check Is Still Refused

**Test**: `PathPolicy_TryResolveRead_NaivePrefixCheckWouldPass_StillDenied`

The naive-prefix-check scenario. Asserts first that the requested path text genuinely begins with
the granted location followed by a separator — so a string comparison would accept it — and then
that the policy refuses it anyway, because the parent segments it carries name a location outside
every grant. Pins the reason containment cannot be decided on requested text.

#### AgentKitCore-PathPolicy-DenyEscapedPath: A Relative Path Climbing Out of the Working Directory Is Refused

**Test**: `PathPolicy_TryResolveRead_RelativeParentTraversal_ReturnsDenial`

Asserts that accepting relative requests does not weaken containment: the escape is made absolute
against the working directory, normalized, and refused.

#### AgentKitCore-PathPolicy-RealPathReported: A Permitted Read Returns Its Real Location

**Test**: `PathPolicy_TryResolveRead_PermittedPath_ReturnsRealPath`

Normal operation: a contained file is permitted, no denial is produced, and the returned location
reads back the expected content.

#### AgentKitCore-PathPolicy-DenialResult: A Refused Read Returns Without Throwing

**Test**: `PathPolicy_TryResolveRead_DeniedPath_ReturnsFalseWithoutThrowing`

Error path: a location outside the permitted one is refused by return value, with a non-empty reason
and no location.

#### AgentKitCore-PathPolicy-UnresolvablePathDenied: A Malformed Path Is Refused

**Test**: `PathPolicy_TryResolveRead_MalformedPath_ReturnsDenial`

Boundary condition: a path containing an embedded null character cannot be interpreted by any
platform. It is refused with a reason rather than allowed to throw, confirming the fail-safe reading
for input a model controls.

#### AgentKitCore-PathPolicy-OrthogonalWorkingDirectory: A Read-Write Working Directory Handles Relative Work

**Test**: `PathPolicy_WorkingDirectoryGrantedReadWrite_RelativeRequestsSucceed`

Agent viewpoint for the common shape: the working directory is also a read-write grant. A bare read
succeeds, a bare write resolves beneath the anchor, and `EmitRelative` confirms the result should be
reported in the relative dialect.

#### AgentKitCore-PathPolicy-OrthogonalWorkingDirectory: A Read-Only Working Directory Refuses a Write

**Test**: `PathPolicy_WorkingDirectoryGrantedReadOnly_RelativeWriteDenied`

Agent viewpoint for a read-only anchor. A bare read succeeds because read-only grants authorize
reads, but a bare write is refused and the denial enumerates the working directory as
`(read-only)`.

#### AgentKitCore-PathPolicy-OrthogonalWorkingDirectory: An Ungranted Working Directory Permits Nothing

**Test**: `PathPolicy_WorkingDirectoryGrantedNothing_RelativeReadDenied_NamesNoLocations`

The app-folder anchor case: a bare name resolves under the working directory even when no grant
covers it, and is then correctly denied. The denial states `No locations are permitted.` rather than
implying the anchor itself carries permission.

#### AgentKitCore-PathPolicy-OrthogonalWorkingDirectory: An Ungranted Anchor Enumerates Grants Elsewhere

**Test**: `PathPolicy_UngrantedWorkingDirectory_RelativeReadDenied_EnumeratesElsewhere`

A policy anchored at one directory grants a different directory read-only. A bare request resolves
under the ungranted anchor, is refused, and the denial names the location that is actually permitted
with its read-only level.

#### AgentKitCore-PathPolicy-GrantPermissionModel: Multiple Grants Support a Cross-Location Task

**Test**: `PathPolicy_TwoGrants_CrossLocationTask_ReadOnlyReadsAndReadWriteWrites`

Constructs a policy anchored at a read-only work folder with a separate read-write session folder.
The scenario proves the agent can read the work input, write the session output, and still cannot
write back into the read-only work location.

#### AgentKitCore-PathPolicy-GrantPermissionModel: A Readable Location Is Not Thereby Writable

**Test**: `PathPolicy_TryResolveWrite_ReadableButNotWritablePath_ReturnsDenial`

Constructs wide read access plus a narrower read-write grant. The outside location is readable but
not writable, proving read permission never implies write permission.

#### AgentKitCore-PathPolicy-WorkingDirectoryRequired: Missing Working Directory Throws

**Test**: `PathPolicy_Constructor_MissingWorkingDirectory_ThrowsArgumentException`

A theory over null and empty working-directory values. A policy has no safe process-directory
fallback, so a missing anchor is a programming error rejected at construction.

#### AgentKitCore-PathPolicy-GrantsValidated: Null Grants Throw

**Test**: `PathPolicy_Constructor_NullGrants_ThrowsArgumentNullException`

Asserts a null grant collection is rejected at construction rather than producing a half-built
policy.

#### AgentKitCore-PathPolicy-GrantsValidated: A Null Grant Entry Throws

**Test**: `PathPolicy_Constructor_NullGrantEntry_ThrowsArgumentNullException`

Asserts a null entry inside the grant collection is rejected, so every grant the policy exposes is a
real `PathRule`.

#### AgentKitCore-PathPolicy-GrantsValidated: Empty Grants Are Valid and Permit Nothing

**Test**: `PathPolicy_Constructor_EmptyGrants_IsValidAndPermitsNothing`

Constructs a policy with an empty grant set. It exposes no grants and refuses even its own working
directory, proving "valid" is not the same as "permissive."

#### AgentKitCore-PathPolicy-CarriesLimits: A Policy Created Without Ceilings Carries the Defaults

**Test**: `PathPolicy_Constructor_NoLimits_UsesDefaultLimits`

Asserts the two-argument constructor yields the **same shared instance** as `ToolLimits.Default`,
rather than merely an equal one, so the delegation cannot silently allocate a fresh set of ceilings.

#### AgentKitCore-PathPolicy-CarriesLimits: A Policy Exposes the Ceilings the Host Supplied

**Test**: `PathPolicy_Constructor_CustomLimits_ExposesSuppliedLimits`

Normal operation: the host's ceilings are the ceilings the governed tools observe.

#### AgentKitCore-PathPolicy-CarriesLimits: A Policy Cannot Be Created With Missing Ceilings

**Test**: `PathPolicy_Constructor_NullLimits_ThrowsArgumentNullException`

Error path: "unbounded" is not a sensible default, so an explicitly absent set of ceilings is the
same kind of programming error as an absent grant collection.

#### AgentKitCore-PathPolicy-WorkingDirectoryRequired: A Working Directory Spelled With Relative Segments Is Normalized

**Test**: `PathPolicy_WorkingDirectory_SpelledWithRelativeSegments_IsReportedNormalized`

Boundary condition: the working directory is named through a redundant parent-directory detour. The
policy holds the normalized location, so every later comparison is made between locations spelled
the same way.

#### AgentKitCore-PathPolicy-RelativePathAgainstWorkingDirectory: Current-Directory Prefix Resolves Beneath the Anchor

**Test**: `PathPolicy_TryResolveRead_DotSlashFileName_ResolvesBeneathTheAnchor`

Asserts the leading token a model often adds reaches the same file the bare name reaches.

#### AgentKitCore-PathPolicy-RelativePathAgainstWorkingDirectory: Nested Relative Path Resolves Beneath the Anchor

**Test**: `PathPolicy_TryResolveRead_NestedRelativePath_ResolvesBeneathTheAnchor`

Uses a forward slash deliberately, because that is the separator a model writes regardless of the
host platform and the separator the list tool reports in names.

#### AgentKitCore-PathPolicy-RelativePathAgainstWorkingDirectory: Relative Paths Ignore the Process Directory

**Test**: `PathPolicy_TryResolveRead_RelativePath_IsNotResolvedAgainstTheProcessDirectory`

The regression scenario for the observed defect. The resolved location lies beneath the working
directory **and** is not the location the process working directory would have produced.

#### AgentKitCore-PathPolicy-RelativePathAgainstWorkingDirectory: Absolute Path Inside Anchor Is Permitted

**Test**: `PathPolicy_TryResolveRead_AbsolutePathInsideRoot_ReturnsRealPath`

Asserts that anchoring bare names at the working directory does not withdraw the absolute form a
host composing paths itself relies on.

#### AgentKitCore-PathPolicy-DenyEscapedPath: Absolute Path Outside the Working Directory Is Refused

**Test**: `PathPolicy_TryResolveRead_AbsolutePathOutsideRoot_ReturnsDenial`

Asserts containment is unchanged by the introduction of the working-directory anchor.

#### AgentKitCore-PathPolicy-OmittedPathMeansWorkingDirectory: Omitted Path Denotes the Working Directory

**Test**: `PathPolicy_TryResolveRead_OmittedPath_ResolvesToTheAnchor`

A theory over a missing, empty and whitespace request. Each resolves to the working directory rather
than raising, which is the promise that no caller-supplied path is an exception.

#### AgentKitCore-PathPolicy-OmittedPathMeansWorkingDirectory: Placeholder Word Denotes the Working Directory

**Test**: `PathPolicy_TryResolveRead_PlaceholderPath_ResolvesToTheAnchor`

A theory over the literal words a model runtime may print for absence. Each is treated exactly as an
omitted argument is.

#### AgentKitCore-PathPolicy-RelativePathAgainstWorkingDirectory: Bare File Name Resolves for Writing Too

**Test**: `PathPolicy_TryResolveWrite_BareFileName_ResolvesBeneathTheAnchor`

Asserts reads and writes interpret one name identically, which is what lets an agent write back under
the name it read.

#### AgentKitCore-PathPolicy-DialectMirroring: EmitRelative Mirrors Caller Dialect and Result Location

**Test**: `PathPolicy_EmitRelative_MirrorsTheCallerAndTheResultLocation`

Pins the dialect rule: relative input and a result inside the granted working directory emit a
relative name; absolute input emits an absolute name; discovery establishes the dialect; and a result
outside the anchor is emitted absolutely.

#### AgentKitCore-PathPolicy-DialectMirroring: Ungranted Anchors Emit Absolute Names

**Test**: `PathPolicy_EmitRelative_UngrantedAnchor_IsAlwaysAbsolute`

A discovery request does not make names relative when the working directory is not granted. The
policy exposes that the anchor is ungranted and reports absolute output instead.

#### AgentKitCore-PathPolicy-DiscoveryLocations: Discovery Lists One Location Per Grant

**Test**: `PathPolicy_DiscoveryRoots_ListOneLocationPerGrant`

A rooted grant contributes its resolved location, and an unrestricted grant contributes the working
directory as the walkable discovery location. The set is deduplicated for grouped listings.

#### AgentKitCore-PathPolicy-LastSegmentAlias: A Unique Final Folder Name Resolves to Its Grant

**Test**: `PathPolicy_TryResolveRead_LastSegmentAlias_ResolvesToTheGrant`

A bare segment equal to exactly one grant's final folder name resolves to that grant rather than to a
child of the ungranted working directory, because nothing by that name exists under the working
directory so the alias fires as the fallback. This lets a model use the final name it saw under an
absolute listing header.

#### AgentKitCore-PathPolicy-LastSegmentAlias: Ambiguous Final Folder Names Are Not Aliased

**Test**: `PathPolicy_TryResolveRead_AmbiguousAlias_IsNotAliased_EnumeratesBoth`

Two grants have the same final folder name, and nothing by that name exists under the working
directory so the alias is considered. The bare segment is denied rather than guessed, and the
denial enumerates both real locations so the model can choose an absolute one.

#### AgentKitCore-PathPolicy-AliasIsFallback: A Working-Directory Subfolder Shadows a Same-Named Grant

**Test**: `PathPolicy_TryResolveRead_WorkingDirectorySubfolderShadowsSameNamedGrant`

The working directory contains a real subfolder whose name also matches a different granted
location's final folder name. The bare segment resolves to the working-directory subfolder — the
documented interpretation wins — rather than silently returning the other grant's contents. The
alias is confined to a fallback that fires only when the working-directory interpretation does not
name an existing path, eliminating the silent wrong-target case.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Relative Denial Echoes, Interprets, and Enumerates

**Test**: `PathPolicy_TryResolveRead_RelativeDenial_EchoesInterpretsAndEnumerates`

Asserts the new denial order for a relative escape: `Requested: "..."` echoes the input verbatim,
`Interpreted as:` states the absolute location produced by the working directory, and permitted
locations are listed with access levels.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Absolute Denial Has No Interpretation Clause

**Test**: `PathPolicy_TryResolveRead_AbsoluteDenial_HasNoInterpretationClause`

Asserts an absolute refusal still echoes the requested location and enumerates grants, but does not
claim an interpretation occurred.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Bare Segment Under Empty Grants Produces the Worked Example

**Test**: `PathPolicy_TryResolveRead_BareSegmentUnderEmptyGrants_ProducesTheWorkedExample`

Pins the app-folder anchor example. A `file_list("work")`-style request under an ungranted anchor
echoes `work`, interprets it beneath the working directory, and states that no locations are
permitted.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Omitted Denial Echoes a Stand-In

**Test**: `PathPolicy_TryResolveRead_OmittedDenial_EchoesAStandIn`

When no path was supplied and the working directory itself is not granted, the denial echoes
`(no path — the working directory)` rather than an empty quotation and does not include an
interpretation clause.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Parent-Traversal Denial Normalizes the Interpreted Path

**Test**: `PathPolicy_TryResolveRead_RelativeParentTraversalDenial_InterpretedPathIsNormalized`

A `../outside.md` request that escapes the anchor is denied, and the `Interpreted as:` line reports
a canonical location: the parent segment is collapsed, so the reported interpretation carries no
`..`.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Dot-Slash Denial Normalizes the Interpreted Path

**Test**: `PathPolicy_TryResolveRead_DotSlashDenial_InterpretedPathIsNormalized`

A `./../outside.md` request is denied, and the reported interpretation collapses both the `.` and
the `..` segments, so neither a `..` nor a same-directory `.` navigation survives in the
`Interpreted as:` line.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Nested Traversal Denial Normalizes the Interpreted Path

**Test**: `PathPolicy_TryResolveRead_NestedTraversalDenial_InterpretedPathIsNormalized`

A nested `a/../../b/outside.md` request is denied, and the `Interpreted as:` line equals the
lexically normalized location `Path.GetFullPath(Path.Combine(workingDirectory, request))`, with no
`..` remaining.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Plain Relative Denial Reports the Interpretation Verbatim

**Test**: `PathPolicy_TryResolveRead_PlainRelativeDenial_InterpretedPathReportedVerbatim`

The no-regression pin: a plain relative name needing no normalization is still reported as the
working-directory-combined location exactly as before, confirming normalization did not alter the
no-op case.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Interpreted-Path Normalization Failure Still Denies and Discloses

**Test**: `PathPolicy_TryResolveRead_InterpretedPathNormalizationThrows_StillDeniesAndDiscloses`

The robustness pin for the non-throwing contract. A relative request whose interpretation cannot be
normalized (an embedded null after a parent segment) still returns a denial rather than throwing,
still echoes the requested text, and still enumerates the permitted location.

#### AgentKitCore-PathPolicy-DenialDisclosesAndEnumerates: Alias to a Read-Only Grant Omits the Interpretation Clause

**Test**: `PathPolicy_TryResolveWrite_AliasToReadOnlyGrant_DeniedWithoutInterpretationClause`

A write aliased to a read-only grant is denied because it is read-only, and the denial omits the
`Interpreted as:` line entirely, because the alias branch performs no working-directory
interpretation.

#### AgentKitCore-PathPolicy-EnumerationFiltered: Permitted Files Are Listed

**Test**: `PathPolicy_EnumerateFiles_PermittedFiles_AreListed`

Normal operation: files at the top of the permitted location and nested within it both appear, so
the filtering is not over-broad.

#### AgentKitCore-PathPolicy-EnumerationFiltered: A Refused Directory Lists Nothing

**Test**: `PathPolicy_EnumerateFiles_DeniedDirectory_ReturnsEmpty`

Error path: listing a populated directory outside the permitted location yields an empty result
rather than an exception.

#### AgentKitCore-PathPolicy-EnumerationFiltered: An Omitted Directory Lists the Working Directory

**Test**: `PathPolicy_EnumerateFiles_OmittedDirectory_ListsTheAnchor`

Asserts the first question an agent asks about a workspace it has not seen is answered with the
working directory when that directory is granted.

#### AgentKitCore-PathPolicy-EnumerationFiltered: A Relative Directory Lists That Directory

**Test**: `PathPolicy_EnumerateFiles_RelativeDirectory_ListsThatDirectory`

Asserts a relative directory name narrows enumeration to that subdirectory rather than widening it to
the whole working directory.

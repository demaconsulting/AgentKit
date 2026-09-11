## PathPolicy Unit Verification Design

This document describes the unit-level verification strategy for the `PathPolicy` class.

### Verification Approach

`PathPolicy` is verified through unit tests that exercise the policy against a **real file system
containing a real reparse point**. `RealPathResolver` and `PathRule` are used as real
dependencies rather than substitutes — both are documented dependencies in _PathPolicy Unit
Design_, and substituting either would remove exactly the behavior the tests exist to confirm.

**The suite exercises paths as a model writes them, not only as a host composes them.** One group
of scenarios builds request paths from the rule's resolved `Root`, matching what a caller holds
once a policy has been constructed; those are the containment scenarios, and the absolute form is
the right one for them. A second group states paths the way a model states them — a bare file
name, a leading current-directory token, a nested relative name, and no path at all. That second
group exists because its absence was the reason a defect in relative-path resolution survived a
full suite and four formal reviews: every scenario passed a fixture-absolute path, and not one
passed the spelling a model actually produces.

Two scenarios are written as contrasts rather than as plain assertions, so that they cannot
quietly become vacuous: the naive-prefix scenario asserts that a text-based check _would_ accept
the request before asserting the policy refuses it, and the enumeration scenario asserts that a
raw recursive listing _does_ surface the escaped file before asserting the policy's listing does
not. The relative-path scenario is written the same way, asserting that the resolved location is
beneath the base and is _not_ the location the process directory would have produced.

Unit tests reside in `PathPolicyTests.cs` within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **File system**: A temporary directory tree created per test through `ReparsePointFixture`,
  with real files written into both the permitted location and the location outside it
- **Reparse points**: A directory junction created by `cmd.exe /c mklink /J` on Windows, and a
  directory symbolic link on Linux and macOS. A Windows symbolic link is deliberately not used;
  see _RealPathResolver Unit Verification Design_ for why
- **Unresolvable input**: A path containing an embedded null character provides a portable,
  deterministic way to exercise the "location cannot be determined" path without constructing a
  cyclic link
- **Mocking**: None
- **Isolation**: Each test constructs its own policy and its own fixture; no state is shared

### Acceptance Criteria

A unit test run passes when all thirty-nine scenarios below pass without error or exception beyond
those explicitly asserted. Any escaping path that is permitted, any escaped file that appears in
a listing, any denial message containing a host location or a directory separator, any relative
request resolved against the process working directory, and any exception escaping a refusal
constitutes a failure.

### Test Scenarios

#### AgentKitCore-PathPolicy-ForWorkspace: Naming a Workspace Sets the Base and Both Rules

**Test**: `PathPolicy_ForWorkspace_Root_SetsBaseAndBothRules`

Normal operation for the construction path the documentation recommends. Asserts the base, the
read rule's location and the write rule's location are all the workspace, resolved to their real
location.

#### AgentKitCore-PathPolicy-ForWorkspace: A Workspace Policy Requires a Workspace

**Test**: `PathPolicy_ForWorkspace_NullRoot_ThrowsArgumentException`

Error path: a missing and an empty workspace are both refused, because there is no safe location
to assume in place of one.

#### AgentKitCore-PathPolicy-ForWorkspace: A Workspace Policy Carries the Host's Ceilings

**Test**: `PathPolicy_ForWorkspace_CustomLimits_ExposesSuppliedLimits`

Asserts the shorthand does not quietly substitute the defaults for the budget the host stated.

#### AgentKitCore-PathPolicy-BaseDirectory: A Policy Without a Stated Base Uses the Read Rule's Location

**Test**: `PathPolicy_Constructor_NoBaseDirectory_UsesTheReadRuleRoot`

Asserts that a policy built the ordinary rooted way measures a relative path from the location it
was confined to, rather than silently preserving the process-directory behavior.

#### AgentKitCore-PathPolicy-BaseDirectory: A Workspace Reached Through a Link Is Reported at Its Real Location

**Test**: `PathPolicy_BaseDirectory_RootReachedThroughLink_IsReportedAsItsRealLocation`

Boundary condition: the workspace is named through a genuine reparse point. Asserts the policy
holds the link's target rather than the link, so that every later comparison is real location
against real location.

#### AgentKitCore-PathPolicy-RelativePathAgainstBase: A Bare File Name Resolves Beneath the Workspace

**Test**: `PathPolicy_TryResolveRead_BareFileName_ResolvesBeneathTheBase`

The scenario the workspace base exists for. Asserts the request a model actually writes is
permitted and reads back the expected content.

#### AgentKitCore-PathPolicy-RelativePathAgainstBase: A Current-Directory Prefix Resolves Beneath the Workspace

**Test**: `PathPolicy_TryResolveRead_DotSlashFileName_ResolvesBeneathTheBase`

Asserts the leading token a model often adds reaches the same file the bare name reaches.

#### AgentKitCore-PathPolicy-RelativePathAgainstBase: A Nested Relative Path Resolves Beneath the Workspace

**Test**: `PathPolicy_TryResolveRead_NestedRelativePath_ResolvesBeneathTheBase`

Uses a forward slash deliberately, because that is the separator a model writes regardless of the
host platform.

#### AgentKitCore-PathPolicy-RelativePathAgainstBase: A Relative Path Is Not Measured From the Process Directory

**Test**: `PathPolicy_TryResolveRead_RelativePath_IsNotResolvedAgainstTheProcessDirectory`

The regression scenario for the defect. Asserts the resolved location lies beneath the base
**and** is not the location the process working directory would have produced, so the scenario
names the failure being prevented rather than merely describing the success.

#### AgentKitCore-PathPolicy-RelativePathAgainstBase: An Absolute Path Inside the Workspace Is Permitted

**Test**: `PathPolicy_TryResolveRead_AbsolutePathInsideRoot_ReturnsRealPath`

Asserts that naming a workspace narrows how a bare name is read without withdrawing the absolute
form a host composing paths itself relies on.

#### AgentKitCore-PathPolicy-RelativePathAgainstBase: A Bare File Name Resolves for Writing Too

**Test**: `PathPolicy_TryResolveWrite_BareFileName_ResolvesBeneathTheBase`

Asserts reads and writes interpret one name identically, which is what lets an agent write back
under the name it read.

#### AgentKitCore-PathPolicy-OmittedPathMeansBase: An Omitted Path Denotes the Workspace

**Test**: `PathPolicy_TryResolveRead_OmittedPath_ResolvesToTheBase`

A theory over a missing, an empty and a whitespace request. Asserts each is answered with the
workspace rather than raising, which is the promise that no caller-supplied path is an exception.

#### AgentKitCore-PathPolicy-OmittedPathMeansBase: A Placeholder Word Denotes the Workspace

**Test**: `PathPolicy_TryResolveRead_PlaceholderPath_ResolvesToTheBase`

A theory over the literal words a model's own runtime prints for absence, in two capitalizations.
Asserts each is treated exactly as an omitted argument is.

#### AgentKitCore-PathPolicy-OmittedPathMeansBase: An Omitted Write Path Does Not Throw

**Test**: `PathPolicy_TryResolveWrite_OmittedPath_DoesNotThrow`

Asserts the non-throwing promise holds on the write path as well as the read path.

#### AgentKitCore-PathPolicy-DenyEscapedPath: A File Beneath a Link Outside the Root Is Refused

**Test**: `PathPolicy_TryResolveRead_FileBeneathLinkOutsideRoot_ReturnsDenial`

The enumeration-independent escape scenario. A secret is written outside the permitted location
and a directory link is created inside it. Asserts the request is refused, that no location is
handed back, and that a reason is supplied.

#### AgentKitCore-PathPolicy-DenyEscapedPath: A Request Passing a Naive Prefix Check Is Still Refused

**Test**: `PathPolicy_TryResolveRead_NaivePrefixCheckWouldPass_StillDenied`

The naive-prefix-check scenario. Asserts first that the requested path text genuinely begins with
the permitted location followed by a separator — so a string comparison would accept it — and
then that the policy refuses it anyway. Pins the reason containment cannot be decided on the
requested text.

#### AgentKitCore-PathPolicy-DenyEscapedPath: A Relative Path Climbing Out of the Workspace Is Refused

**Test**: `PathPolicy_TryResolveRead_RelativeParentTraversal_ReturnsDenial`

Asserts that accepting relative requests does not weaken containment: the escape is judged on the
resolved location exactly as an absolute escape is.

#### AgentKitCore-PathPolicy-DenyEscapedPath: A Relative Path Beneath a Link Outside the Root Is Refused

**Test**: `PathPolicy_TryResolveRead_RelativePathBeneathLinkOutsideRoot_ReturnsDenial`

The scenario that pins the resolution order. The per-component reparse-point walk runs on the
path only after it has been made absolute against the workspace, so a relative escape and the
absolute spelling of the same request reach the same decision.

#### AgentKitCore-PathPolicy-DenyEscapedPath: An Absolute Path Outside the Workspace Is Refused

**Test**: `PathPolicy_TryResolveRead_AbsolutePathOutsideRoot_ReturnsDenial`

Asserts containment is unchanged by the introduction of the workspace base.

#### AgentKitCore-PathPolicy-RealPathReported: A Permitted Read Returns Its Real Location

**Test**: `PathPolicy_TryResolveRead_PermittedPath_ReturnsRealPath`

Normal operation: a contained file is permitted, no denial is produced, and the returned location
reads back the expected content.

#### AgentKitCore-PathPolicy-DenialResult: A Refused Read Returns Without Throwing

**Test**: `PathPolicy_TryResolveRead_DeniedPath_ReturnsFalseWithoutThrowing`

Error path: a location outside the permitted one is refused by return value, with a non-empty
reason and no location.

#### AgentKitCore-PathPolicy-DenialMessageRedacted: A Denial Message Omits the Requested Path

**Test**: `PathPolicy_TryResolveRead_DeniedPath_DenialMessageOmitsRequestedPath`

Asserts the denial message contains neither the requested path nor the permitted location, and
contains no directory separator at all — so no host location can be present in any form.

#### AgentKitCore-PathPolicy-DenialMessageRedacted: A Denial Message Contains No Directory Separator

**Test**: `PathPolicy_TryResolveRead_DeniedPath_DenialMessageContainsNoDirectorySeparator`

Asserts the property directly, for both the platform separator and the alternative one, so that
the recovery guidance cannot silently retire the check by adopting an example containing a slash.

#### AgentKitCore-PathPolicy-DenialGuidance: A Denial Message States the Expected Path Form

**Test**: `PathPolicy_TryResolveRead_DeniedPath_DenialMessageStatesTheExpectedPathForm`

Asserts the message names the workspace-relative form and gives a bare file name as its example,
so that a refused agent has something to act on rather than a bare "no" to retry against.

#### AgentKitCore-PathPolicy-UnresolvablePathDenied: A Malformed Path Is Refused

**Test**: `PathPolicy_TryResolveRead_MalformedPath_ReturnsDenial`

Boundary condition: a path containing an embedded null character cannot be interpreted by any
platform. Asserts it is refused with a reason rather than allowed to throw, confirming the
fail-safe reading for input a model controls.

#### AgentKitCore-PathPolicy-IndependentRules: The Write Rule Grants Nothing to a Read

**Test**: `PathPolicy_TryResolveRead_WriteRootOnly_DeniesReadOutsideReadRoot`

Constructs a policy whose read and write rules are rooted at different locations, and asserts that
a location only the write rule permits is refused for reading while remaining permitted for
writing.

#### AgentKitCore-PathPolicy-RealPathReported: A Permitted Write Returns Its Real Location

**Test**: `PathPolicy_TryResolveWrite_PermittedPath_ReturnsRealPath`

Normal operation for a file that does not exist yet: the write is permitted and the real location
the caller may create is returned.

#### AgentKitCore-PathPolicy-DenialResult: A Refused Write Returns Without Throwing

**Test**: `PathPolicy_TryResolveWrite_DeniedPath_ReturnsFalseWithoutThrowing`

Error path for writes, mirroring the read case.

#### AgentKitCore-PathPolicy-IndependentRules: A Readable Location Is Not Thereby Writable

**Test**: `PathPolicy_TryResolveWrite_ReadableButNotWritablePath_ReturnsDenial`

Constructs the read-wide, write-narrow configuration and asserts the same location is permitted
for reading and refused for writing.

#### AgentKitCore-PathPolicy-EnumerationFiltered: Enumeration Across a Link Excludes the Escaped File

**Test**: `PathPolicy_EnumerateFiles_LinkToOutsideRoot_ExcludesEscapedFile`

The enumeration-escape scenario. Asserts first that a raw recursive listing of the permitted
location **does** surface the file that lies outside it — confirming the operating system follows
the link, so the scenario cannot become vacuous — and then that the policy's listing excludes it
while still including a legitimately contained file.

#### AgentKitCore-PathPolicy-EnumerationFiltered: Permitted Files Are Listed

**Test**: `PathPolicy_EnumerateFiles_PermittedFiles_AreListed`

Normal operation: files at the top of the permitted location and nested within it both appear, so
the filtering is not over-broad.

#### AgentKitCore-PathPolicy-EnumerationFiltered: A Refused Directory Lists Nothing

**Test**: `PathPolicy_EnumerateFiles_DeniedDirectory_ReturnsEmpty`

Error path: listing a populated directory outside the permitted location yields an empty result
rather than an exception or a disclosure.

#### AgentKitCore-PathPolicy-EnumerationFiltered: An Omitted Directory Lists the Workspace

**Test**: `PathPolicy_EnumerateFiles_OmittedDirectory_ListsTheBase`

Asserts the first question an agent asks about a workspace it has not seen is answered rather
than refused, and that the answer is the workspace itself.

#### AgentKitCore-PathPolicy-EnumerationFiltered: A Relative Directory Lists That Directory

**Test**: `PathPolicy_EnumerateFiles_RelativeDirectory_ListsThatDirectory`

Asserts a relative directory name narrows the listing to that subdirectory rather than widening
it to the whole workspace, confirming the base resolution applies to enumeration as it does to
direct access.

#### AgentKitCore-PathPolicy-RequiredRules: A Policy Cannot Be Created Without a Read Rule

**Test**: `PathPolicy_Constructor_NullReadRule_ThrowsArgumentNullException`

Asserts an unguarded policy is unrepresentable.

#### AgentKitCore-PathPolicy-RequiredRules: A Policy Cannot Be Created Without a Write Rule

**Test**: `PathPolicy_Constructor_NullWriteRule_ThrowsArgumentNullException`

Asserts both rules are required, not merely the first.

#### AgentKitCore-PathPolicy-CarriesLimits: A Policy Created Without Ceilings Carries the Defaults

**Test**: `PathPolicy_Constructor_NoLimits_UsesDefaultLimits`

Asserts the two-rule constructor yields the **same shared instance** as `ToolLimits.Default`,
rather than merely an equal one, so that the delegation cannot silently start allocating a fresh
set of ceilings that happens to agree today.

#### AgentKitCore-PathPolicy-CarriesLimits: A Policy Exposes the Ceilings the Host Supplied

**Test**: `PathPolicy_Constructor_CustomLimits_ExposesSuppliedLimits`

Normal operation: the host's ceilings are the ceilings the governed tools observe.

#### AgentKitCore-PathPolicy-CarriesLimits: A Policy Cannot Be Created With Missing Ceilings

**Test**: `PathPolicy_Constructor_NullLimits_ThrowsArgumentNullException`

Error path: "unbounded" is not a sensible default, so an explicitly absent set of ceilings is the
same kind of programming error as an absent rule.

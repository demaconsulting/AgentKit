## PathPolicy Unit Verification Design

This document describes the unit-level verification strategy for the `PathPolicy` class.

### Verification Approach

`PathPolicy` is verified through unit tests that exercise the policy against a **real file system
containing a real reparse point**. `RealPathResolver` and `PathRule` are used as real
dependencies rather than substitutes — both are documented dependencies in _PathPolicy Unit
Design_, and substituting either would remove exactly the behavior the tests exist to confirm.

Two scenarios are written as contrasts rather than as plain assertions, so that they cannot
quietly become vacuous: the naive-prefix scenario asserts that a text-based check _would_ accept
the request before asserting the policy refuses it, and the enumeration scenario asserts that a
raw recursive listing _does_ surface the escaped file before asserting the policy's listing does
not.

Request paths are built from the rule's resolved `Root`, matching what a caller holds once a
policy has been constructed.

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

A unit test run passes when all eighteen scenarios below pass without error or exception beyond
those explicitly asserted. Any escaping path that is permitted, any escaped file that appears in
a listing, any denial message containing a host location, and any exception escaping a refusal
constitutes a failure.

### Test Scenarios

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

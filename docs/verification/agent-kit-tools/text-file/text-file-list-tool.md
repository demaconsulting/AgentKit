### TextFileListTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileListTool` class.

#### Verification Approach

Nothing is mocked or stubbed. The unit's central invariant — that enumeration goes through the
access policy and therefore never advertises a file whose reading is refused — is only meaningful
against a real file system containing a real reparse point, because the behavior being guarded
against is the operating system's own willingness to walk through a link. A substituted enumeration
would not follow a link and the scenario would pass no matter what the unit did.

That scenario, `TextFileListTool_List_LinkToOutsideRoot_DoesNotListEscapedFile`, is the security
control of this unit and is the subsystem-level equivalent of the enumeration filtering the access
policy already proves at the Core level. It reads the escaped file through the link on disk before
listing, so a fixture that failed to create the link fails the test rather than making it pass
vacuously.

The remaining scenarios assert the properties that determine whether a listing is usable rather than
merely safe: grouped output under absolute location headers, bare names relative to each header,
dialect mirroring for relative and absolute requests, deterministic order, pattern narrowing, an
empty named listing reported as a fact, a discovery listing that covers every grant — including a
permitted location that currently holds no matching file, reported under its absolute header with a
marker naming its access level — and an oversized listing refused rather than truncated. Policy
denials are passed through with the current disclosure behavior, so the refused request and permitted
locations are visible to the model.

Unit tests reside in `TextFile/TextFileListToolTests.cs`, with the shared reparse-point fixture in
`TextFile/ReparsePointFixture.cs`, within the `DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: Each scenario creates and deletes its own temporary tree containing a working
  directory, any additional granted locations, and a sibling directory outside them
- **Reparse points**: A directory junction on Windows, a symbolic link elsewhere; a failure to
  create one fails the test rather than skipping it
- **Isolation**: Each test constructs its own policy, tool and tree; no state is shared

#### Acceptance Criteria

A unit test run passes when all twenty-eight scenarios below pass without error or exception beyond
those explicitly asserted. An escaped file appearing in a listing, a missing absolute header, names
reported relative to the wrong header, a non-deterministic order, a truncated listing where a refusal
was required, an empty listing reported as a refusal, a discovery listing that omits a permitted
empty location, or renders one in a form a model could mistake for an error or the next header, a
request naming no directory refused or raising rather than discovering permitted locations, an
exception raised at a malformed request, or a policy refusal that does not disclose the permitted
locations constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-ListTool-ToolName: The Name Constant Is Family-Qualified

**Test**: `TextFileListTool_ToolName_Constant_IsTheFamilyQualifiedName`

Asserts the constant is `text_file_list` and begins with the pack's declared family prefix — the
name the read and write tools redirect a model to.

##### AgentKitTools-TextFile-ListTool-ToolName: The Constructed Tool Carries the Name and a Description

**Test**: `TextFileListTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

Normal operation: the tool a model is offered carries the published name and a non-empty
description it can choose by.

##### AgentKitTools-TextFile-ListTool-GuardedConstruction: A Null Policy Throws

**Test**: `TextFileListTool_Create_NullPolicy_ThrowsArgumentNullException`

Error path at construction: a listing tool governed by nothing would advertise the whole host.

##### AgentKitTools-TextFile-ListTool-GuardedConstruction: The Result Is Plain Text, Not a JsonElement

**Test**: `TextFileListTool_List_PermittedDirectory_ResultIsPlainTextNotJsonElement`

Asserts both the positive and the negative type. This unit's delegate is synchronous, so the
scenario also confirms the guard applies to a synchronous tool exactly as to an asynchronous one.

##### AgentKitTools-TextFile-ListTool-PolicyEnumeration: Discovery Lists Relative Names Under the Anchor Header

**Test**: `TextFileListTool_List_Discovery_ListsRelativeNamesUnderTheAnchorHeader`

Normal operation for the first listing an agent makes. With the working directory granted, omitting
the directory produces one absolute working-directory header and bare names beneath it.

##### AgentKitTools-TextFile-ListTool-PolicyEnumeration: A Link Out of the Root Does Not List the Escaped File

**Test**: `TextFileListTool_List_LinkToOutsideRoot_DoesNotListEscapedFile`

The unit's security control. A real reparse point inside the permitted root points at a sibling
directory holding a file; the escaped file is read through the link on disk to prove the link works,
and the listing is then asserted to contain the permitted file and not the escaped one.

##### AgentKitTools-TextFile-ListTool-GroupedListing: An Absolute Request Lists Under the Absolute Header

**Test**: `TextFileListTool_List_AbsoluteDirectory_ListsUnderTheAbsoluteHeader`

Dialect mirroring for an absolute caller: the root is requested by absolute path, and the listing is
grouped under that absolute location with names relative to it.

##### AgentKitTools-TextFile-ListTool-DeterministicOrder: Files Are Listed in a Deterministic Order

**Test**: `TextFileListTool_List_SeveralFiles_AreListedInDeterministicOrder`

Files are created in an order other than their sorted order, and the listing is asserted in ordinal
order, so the same tree always produces the same listing.

##### AgentKitTools-TextFile-ListTool-SearchPattern: A Search Pattern Narrows the Listing

**Test**: `TextFileListTool_List_SearchPattern_LimitsTheListingToMatchingFiles`

Normal operation: only the matching file is listed, which is also how an agent recovers from a
listing refused for exceeding the result ceiling.

##### AgentKitTools-TextFile-ListTool-SearchPattern: No Pattern Lists Every Permitted File

**Test**: `TextFileListTool_List_NoSearchPattern_ListsEveryPermittedFile`

Boundary condition: an absent pattern means everything, which is the least surprising reading for a
model exploring a directory it does not know.

##### AgentKitTools-TextFile-ListTool-EmptyListing: Nothing Matched Is a Result, Not a Refusal

**Test**: `TextFileListTool_List_NoMatches_ReturnsAnEmptyListingNotADenial`

Boundary condition for a **named** directory: asserts both the reported text, `No files matched.`,
and the absence of a denial, because refusing here would tell the model to correct a request that is
already correct. Discovery is distinct and is covered by the discovery-completeness scenarios below.

##### AgentKitTools-TextFile-ListTool-ResultCeiling: A Listing Beyond the Result Ceiling Is Refused

**Test**: `TextFileListTool_List_ListingBeyondTheResultCeiling_ReturnsDenialNamingTheCeiling`

Boundary condition: the ceiling is named in the refusal and no file name appears in the result,
which is what distinguishes a refusal from a truncation.

##### AgentKitTools-TextFile-ListTool-DeniedDirectory: A Directory Outside the Read Grant Is Refused

**Test**: `TextFileListTool_List_DirectoryOutsideTheReadRoot_ReturnsDenial`

Error path: a refusal rather than an empty listing, so the agent learns it may not look there rather
than concluding the location is empty.

##### AgentKitTools-TextFile-ListTool-OmittedDirectory: An Omitted Directory Lists the Anchor

**Test**: `TextFileListTool_List_OmittedDirectory_ListsTheAnchor`

A theory over a missing, an empty and a whitespace directory argument. With the working directory
granted, each spelling discovers the permitted anchor under its absolute header and lists bare names
beneath it.

##### AgentKitTools-TextFile-ListTool-OmittedDirectory: A Placeholder Directory Lists the Anchor

**Test**: `TextFileListTool_List_PlaceholderDirectory_ListsTheAnchor`

A theory over the literal words a model's own runtime prints for absence. Asserts each is treated
exactly as an omitted argument is, because a model whose schema marks an argument optional
frequently sends the word rather than omitting the argument.

##### AgentKitTools-TextFile-ListTool-OmittedDirectory: Omitting the Argument Entirely Does Not Throw

**Test**: `TextFileListTool_List_MissingDirectoryArgument_DoesNotThrow`

The scenario that pins the parameter as optional. A parameter with no default fails inside the
function factory before the tool body is reached, and the model then receives an opaque framework
error rather than anything it can act on. Invokes the tool with no arguments at all and asserts an
ordinary discovery listing comes back.

##### AgentKitTools-TextFile-ListTool-RelativeDirectory: A Bare Relative Directory Lists Under the Anchor

**Test**: `TextFileListTool_List_BareRelativeDirectory_ListsUnderTheAnchor`

Normal operation for the way a model names a subdirectory. Places a file in a subdirectory and
another elsewhere in the working directory, then asserts only the named subdirectory is listed under
the working-directory header with a name relative to the anchor.

##### AgentKitTools-TextFile-ListTool-GroupedListing: Two Grants Discover Under Separate Absolute Headers

**Test**: `TextFileListTool_List_TwoGrants_Discovery_ListsEachUnderItsAbsoluteHeader`

Discovery over multiple grants lists every granted location under its own absolute header. The
working directory entry teaches bare names within the anchor, while the non-anchor entry teaches the
absolute location the model must use to address that grant.

##### AgentKitTools-TextFile-ListTool-DenialDisclosure: A Refusal Discloses the Permitted Location

**Test**: `TextFileListTool_List_DeniedDirectory_DenialDisclosesPermittedLocation`

Disclosure behavior: the refused request is echoed, and the permitted working directory is named
with its `(read-write)` access level so a confined model learns where it may list instead.

##### AgentKitTools-TextFile-ListTool-DiscoveryCompleteness: A Single Empty Location Appears With Its Access Level

**Test**: `TextFileListTool_List_Discovery_SingleEmptyLocation_AppearsWithItsAccessLevel`

The core of the fix: a lone granted read-write location holding no matching file is reported under
its absolute header with the `(no files - read-write)` marker, and explicitly not as `No files
matched.`, so the location's existence is never hidden from a discovering model.

##### AgentKitTools-TextFile-ListTool-DiscoveryCompleteness: An Empty Read-Only Location Names Read-Only

**Test**: `TextFileListTool_List_Discovery_ReadOnlyEmptyLocation_MarkerNamesReadOnly`

The marker reflects the truthful access level: an empty read-only location, discovered alongside a
populated read-write anchor, renders with the `(no files - read-only)` marker so a model does not
attempt a write it would be refused.

##### AgentKitTools-TextFile-ListTool-DiscoveryCompleteness: Empty Locations Appear First, Middle and Last

**Test**: `TextFileListTool_List_Discovery_EmptyLocationsAmongPopulated_EachAppearsInFirstMiddleLast`

Five granted locations ordered so an empty block falls first, in the middle and last among populated
blocks. The exact joined listing is asserted — headers, markers, names and blank-line block
separators — so an empty block can never be confused with an adjacent block's absolute header.

##### AgentKitTools-TextFile-ListTool-DiscoveryCompleteness: Every Location Empty Still Reports Each

**Test**: `TextFileListTool_List_Discovery_EveryLocationEmpty_AllAppearWithMarkers`

Several granted locations, all empty. The result is asserted not to be `No files matched.`; each
header-and-marker block is present and blocks are blank-line separated, proving discovery reports
every permitted location even when none holds a file.

##### AgentKitTools-TextFile-ListTool-DiscoveryCompleteness: A Location With Only Subdirectories Is Empty

**Test**: `TextFileListTool_List_Discovery_LocationWithOnlySubdirectories_AppearsAsEmpty`

A granted location whose only content is a subdirectory matches no *file*, so it renders as an empty
block with its access-level marker — confirming the listing reports files and that subdirectories
alone leave a location empty.

##### AgentKitTools-TextFile-ListTool-DiscoveryCompleteness: A Populated Block Renders Exactly As Before

**Test**: `TextFileListTool_List_Discovery_PopulatedAndEmpty_PopulatedBlockRendersExactlyAsBefore`

Regression lock: a discovery over one populated and one empty grant is asserted to contain the
populated block byte-for-byte as the pre-change tool produced it, proving the empty-location
rendering introduces no populated-output regression.

##### AgentKitTools-TextFile-ListTool-DiscoveryCompleteness: No Grants Still Reports No Files Matched

**Test**: `TextFileListTool_List_Discovery_NoGrants_ReturnsNoFilesMatched`

Boundary condition: a policy carrying no grants produces no block and reports `No files matched.`,
preserving the zero-grants contract — there is genuinely no permitted location to report.

##### AgentKitTools-TextFile-ListTool-DiscoveryCompleteness: Empty Markers Count Toward the Result Ceiling

**Test**: `TextFileListTool_List_Discovery_EmptyMarkersBeyondResultCeiling_ReturnsDenialNamingTheCeiling`

Boundary condition: an empty location whose header and marker exceed a small result ceiling is
refused as `ResourceTooLarge` with the ceiling named, proving markers count toward the ceiling and
are refused rather than truncated.

##### AgentKitTools-TextFile-ListTool-DiscoveryCompleteness: A Pattern Matching Nothing Still Reports the Location

**Test**: `TextFileListTool_List_Discovery_PatternMatchesNothing_LocationStillAppears`

Boundary condition: a populated location narrowed by a pattern that matches nothing is still reported
under its absolute header with its access-level marker, proving emptiness arising from the pattern
hides a location no more than emptiness arising from the location itself.

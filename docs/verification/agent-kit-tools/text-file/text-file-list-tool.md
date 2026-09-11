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
merely safe: relative platform-neutral names, a deterministic order, pattern narrowing, an empty
listing reported as a fact, and an oversized listing refused rather than truncated.

Unit tests reside in `TextFile/TextFileListToolTests.cs`, with the shared reparse-point fixture in
`TextFile/ReparsePointFixture.cs`, within the `DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: Each scenario creates and deletes its own temporary tree containing a permitted
  root and a sibling directory outside it
- **Reparse points**: A directory junction on Windows, a symbolic link elsewhere; a failure to
  create one fails the test rather than skipping it
- **Isolation**: Each test constructs its own policy, tool and tree; no state is shared

#### Acceptance Criteria

A unit test run passes when all twenty scenarios below pass without error or exception beyond those
explicitly asserted. An escaped file appearing in a listing, an absolute name in a listing, a
non-deterministic order, a truncated listing where a refusal was required, an empty listing reported
as a refusal, a request naming no directory refused or raising rather than listing the workspace,
an exception raised at a malformed request, and a refusal containing a host path each
constitute a failure.

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

##### AgentKitTools-TextFile-ListTool-PolicyEnumeration: A Permitted Directory Lists Its Files

**Test**: `TextFileListTool_List_PermittedDirectory_ListsThePermittedFiles`

Normal operation: the exact listing is asserted, so an extra or missing entry fails rather than
being absorbed by a looser check.

##### AgentKitTools-TextFile-ListTool-PolicyEnumeration: A Link Out of the Root Does Not List the Escaped File

**Test**: `TextFileListTool_List_LinkToOutsideRoot_DoesNotListEscapedFile`

The unit's security control. A real reparse point inside the permitted root points at a sibling
directory holding a file; the escaped file is read through the link on disk to prove the link works,
and the listing is then asserted to contain the permitted file and not the escaped one.

##### AgentKitTools-TextFile-ListTool-RelativeNames: Names Are Relative to the Requested Directory

**Test**: `TextFileListTool_List_PermittedDirectory_NamesAreRelativeToTheRequestedDirectory`

Disclosure control and usability: a file one level down is reported as `sub/child.txt`, and the
permitted location does not appear anywhere in the result.

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

Boundary condition: asserts both the reported text and the absence of a denial, because refusing
here would tell the model to correct a request that is already correct.

##### AgentKitTools-TextFile-ListTool-ResultCeiling: A Listing Beyond the Result Ceiling Is Refused

**Test**: `TextFileListTool_List_ListingBeyondTheResultCeiling_ReturnsDenialNamingTheCeiling`

Boundary condition: the ceiling is named in the refusal and no file name appears in the result,
which is what distinguishes a refusal from a truncation.

##### AgentKitTools-TextFile-ListTool-DeniedDirectory: A Directory Outside the Read Root Is Refused

**Test**: `TextFileListTool_List_DirectoryOutsideTheReadRoot_ReturnsDenial`

Error path: a refusal rather than an empty listing, so the agent learns it may not look there rather
than concluding the location is empty.

##### AgentKitTools-TextFile-ListTool-OmittedDirectory: An Omitted Directory Lists the Workspace Root

**Test**: `TextFileListTool_List_OmittedDirectory_ListsTheWorkspaceRoot`

A theory over a missing, an empty and a whitespace directory argument. Asserts each produces the
listing of the workspace root rather than a refusal. This scenario **replaces** an earlier one
that asserted the opposite: an agent exploring a workspace for the first time has no directory
name to give, so refusing the only request it can make sent it guessing at locations it has no
business exploring.

##### AgentKitTools-TextFile-ListTool-OmittedDirectory: A Placeholder Directory Lists the Workspace Root

**Test**: `TextFileListTool_List_PlaceholderDirectory_ListsTheWorkspaceRoot`

A theory over the literal words a model's own runtime prints for absence. Asserts each is treated
exactly as an omitted argument is, because a model whose schema marks an argument optional
frequently sends the word rather than omitting the argument.

##### AgentKitTools-TextFile-ListTool-OmittedDirectory: Omitting the Argument Entirely Does Not Throw

**Test**: `TextFileListTool_List_MissingDirectoryArgument_DoesNotThrow`

The scenario that pins the parameter as optional. A parameter with no default fails inside the
function factory before the tool body is reached, and the model then receives an opaque framework
error rather than anything it can act on — the observed failure this scenario exists to prevent.
Invokes the tool with no arguments at all and asserts an ordinary listing comes back.

##### AgentKitTools-TextFile-ListTool-RelativeDirectory: A Bare Relative Directory Lists That Directory

**Test**: `TextFileListTool_List_BareRelativeDirectory_ListsThatDirectory`

Normal operation for the way a model names a subdirectory. Places a file in a subdirectory and
another elsewhere in the workspace, and asserts only the named subdirectory is listed — so the
relative name narrows the listing rather than being ignored.

##### AgentKitTools-TextFile-ListTool-DenialRedaction: A Refusal Contains No Host Detail

**Test**: `TextFileListTool_List_DeniedDirectory_DenialTextContainsNoHostDetail`

Disclosure control: asserts the refusal contains neither the permitted location, the requested
location, nor a directory separator. The absence of a separator is also what keeps the recovery
guidance the refusal now carries from reintroducing host layout.

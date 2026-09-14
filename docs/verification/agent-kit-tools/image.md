## Image Subsystem Verification Design

This document describes the subsystem-level verification strategy for the Image tool family.

### Verification Approach

The subsystem is verified through integration tests that exercise the family the way an application
does: composed through the AgentKitCore `ToolPackBuilder` under one access policy and a declared
host capability, then invoked through the published tool list by name and argument dictionary,
exactly as an agent runtime invokes it. Nothing is mocked. The access policy and the file system
are real, because the properties under verification — that a path outside the permitted location
cannot be read, that image content reaches a caller as content rather than as serialized JSON, and
that a non-vision host is never even asked for the family — are properties of the real thing and a
test double would prove only that the double behaves.

The scenarios here assert what belongs to the family as a whole: one family prefix, one policy
governing the tool, content delivered unserialized, refusals that are returned rather than thrown,
policy refusals that disclose the permitted location, and the capability gate withholding the family without
consulting the pack. The algorithm of any single tool is verified in that unit's own document.

Two scenarios carry the weight of the increment. The first proves the guarded construction path is
load-bearing: image content invoked through the composed tool arrives as a `List<AIContent>` and
specifically not as a `JsonElement`, which is the difference between the model seeing the image and
fabricating a description of one it never received. The second proves the capability gate withholds
the family by *not consulting* the pack, using a recording decorator that would set a flag the
moment its `CreateTools` were called — a distinction an empty tool list alone cannot make, because a
pack that was consulted and returned nothing and a pack that was never consulted both yield an empty
list.

Subsystem tests reside in `Image/ImageTests.cs`, with the capability-gate recording decorator in
`Image/RecordingToolPack.cs` and the shared temporary-directory test fixture reused from
`TextFile/TempDirectoryFixture.cs`, all within the `DemaConsulting.AgentKit.Tools.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: Each scenario that reads a file creates its own temporary directory tree — a
  permitted root and a sibling directory outside it — writing binary files directly, and deletes it
  afterwards
- **Isolation**: Each test constructs its own policy, composition and temporary tree; no state is
  shared between tests

### Acceptance Criteria

A subsystem test run passes when all thirteen scenarios below pass without error or exception beyond
those explicitly asserted. A tool published outside the family prefix, image content arriving as a
`JsonElement`, a family registered for a non-vision host, a pack consulted despite an unmet
capability, a refusal raised as an exception rather than returned, a policy refusal that fails to
disclose the permitted location, a relative name that is not resolved against the workspace, a permitted read that
fails, and a truncated result where a refusal was required each
constitute a failure.

### Test Scenarios

#### AgentKitTools-Image-FamilyComposition: The Family Publishes the Read Tool

**Test**: `Image_Family_ComposedThroughBuilder_PublishesTheReadTool`

Normal operation: composes the pack through a `ToolPackBuilder` on a vision host and asserts the
read tool name, confirming the family is attached as one unit and publishes what it promises.

#### AgentKitTools-Image-FamilyComposition: A Host Declaring Vision Receives the Family

**Test**: `Image_Family_HostDeclaringVision_ReceivesTheFamily`

Normal operation: a host that declares the vision capability receives the family's tools, confirming
the gate admits a host that meets the requirement.

#### AgentKitTools-Image-CapabilityGated: A Host Without Vision Receives No Tools

**Test**: `Image_Family_HostWithoutVision_ReceivesNoTools`

Boundary condition: a host that declares no capability receives none of the family's tools,
confirming the family is withheld from a host that cannot present its content.

#### AgentKitTools-Image-CapabilityGated: The Pack Is Not Consulted Without Vision

**Test**: `Image_Family_HostWithoutVision_PackIsNotConsulted`

Security control: the real pack is wrapped in a recording decorator and added to a builder that
declares no capability. After building, the decorator's consultation flag is asserted false and the
tool list empty. The recording decorator is what distinguishes "not consulted" from "consulted but
produced nothing" — an empty list is produced by both, and only the first is the gate's promise.

#### AgentKitTools-Image-GuardedConstruction: Every Tool Carries a Validated Name and a Description

**Test**: `Image_Family_EveryTool_CarriesAValidatedNameAndDescription`

Runs each published name through the naming convention's own validation and asserts a non-empty
description, confirming no tool is offered to a model that the model cannot identify or choose.

#### AgentKitTools-Image-GuardedConstruction: A Result Reaches the Caller Unserialized

**Test**: `Image_Family_ToolResult_ReachesTheCallerUnserialized`

The load-bearing proof of the increment: reads a permitted image through the composed tool and
asserts the result is a `List<AIContent>` and specifically not a `JsonElement`, then that the
content's bytes are the real file's. Without the guarded construction path the content would be
flattened into JSON, the provider would never receive the image, and the failure would be silent.

#### AgentKitTools-Image-PolicyGoverned: A Permitted Image Is Returned as Image Content

**Test**: `Image_Family_PermittedImage_IsReturnedAsImageContent`

Normal operation: reads a permitted PNG and asserts the returned data content carries the
`image/png` media type and the file's real bytes, so the model is handed the image the request
named.

#### AgentKitTools-Image-PolicyGoverned: A Path Outside the Root Is Refused

**Test**: `Image_Family_PathOutsideRoot_IsRefused`

Error path and security control: a request for a location outside the permitted one is refused as
`PathNotPermitted`, confirming the read decision governs the composed tool.

#### AgentKitTools-Image-PolicyGoverned: A Relative Path From a Model Is Resolved Against the Workspace

**Test**: `Image_Family_RelativePathFromAModel_IsResolvedAgainstTheWorkspace`

Normal operation for the request a model actually makes: the image is asked for by name alone and
the real bytes come back. The family shares the access policy the text file family uses, so a name
a text file listing reported is directly usable here; a family that read names differently would
make a discovered name unusable.

#### AgentKitTools-Image-DenialsAreResults: A Refused Request Returns a Result

**Test**: `Image_Family_DeniedRequest_ReturnsAResultWithoutThrowing`

Error path: a request for a location outside the permitted one returns text naming a denial reason
rather than raising an exception, confirming a refused agent is told why rather than stranded.

#### AgentKitTools-Image-DenialsAreResults: A Refusal Discloses the Permitted Location

**Test**: `Image_Family_DenialText_DisclosesPermittedLocation`

Disclosure behavior: asserts the refusal names the permitted location so a confined model learns
where it may work. The transcript leaves the process, so this disclosure is a deliberate control —
the rule that once redacted these refusals is dropped.

#### AgentKitTools-Image-DenialsAreResults: An Unsupported Type Is Refused, Stating What the File Is

**Test**: `Image_Family_UnsupportedType_IsRefusedWithRedirectWhereUseful`

Error path: an `.svg` — which genuinely *is* text — is refused as an unsupported type, the refusal
states that, and it names `text_file_read` as the reader for that kind of content. The naming is a
classification of the file rather than a prescribed way around the refusal, on the same basis as the
text file read tool's own binary-content refusal naming `image_read`.

#### AgentKitTools-Image-ObservesPolicyLimits: A File Beyond the Binary Ceiling Is Refused, Not Truncated

**Test**: `Image_Family_FileBeyondTheBinaryCeiling_IsRefusedNotTruncated`

Boundary condition: composes the family under a policy carrying a small binary ceiling and asserts
the result is a refusal naming that ceiling, containing no content.

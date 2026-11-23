### ImageViewTool Unit Verification Design

This document describes the unit-level verification strategy for the `ImageViewTool` class.

#### Verification Approach

Every scenario invokes the constructed tool exactly as a runtime would — through
`AIFunction.InvokeAsync` with named arguments — over real files in a temporary tree, drawn by the
real rendering library, because what the tool promises is what a model receives: a picture, a
report it can act on, or a denial. The picture is asserted as image content rather than serialized
JSON, decoded, and its corner checked for opacity.

Unit tests reside in `Image/ImageViewToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project, using `TempDirectoryFixture.cs`,
`DirectoryLink.cs`, `Image/ImageTestImages.cs` and `Rendering/RenderSamples.cs`.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: The CanvasNet Viewer, in process
- **File system**: A temporary tree per test, with a permitted root and a sibling outside it; the
  link scenario adds a real link (an NTFS junction on Windows, a symbolic link elsewhere) and fails
  rather than skips when it cannot be created
- **Isolation**: Each test builds its own tool over a fresh renderer

#### Acceptance Criteria

A unit test run passes when all fifteen scenarios below pass without error or exception beyond
those explicitly asserted. A supported file not returned as an opaque PNG, a wrong page or page
count, a width not applied, a file outside the policy read or distinguished by its existence, a
file read through a link that leaves the grant, a
malformed request not refused with its reason, a ceiling not applied, a broken file not reported
with its location or naming a help topic or a host path, a note missing from the caption, or a
reference outside the grant drawn each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Image-ViewTool-ToolName: The Name Is the Family-Qualified Name

**Test**: `ImageViewTool_ToolName_Constant_IsTheFamilyQualifiedName`

The constant is `image_view` and carries the family prefix.

##### AgentKitToolsVisual-Image-ViewTool-GuardedConstruction: The Tool Carries Its Name and a Description

**Test**: `ImageViewTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

The built tool carries the name and a description, and a null policy or renderer is refused.

##### AgentKitToolsVisual-Image-ViewTool-DrawsEachFormat: Each Supported File Is Returned as a PNG Picture

**Test**: `ImageViewTool_View_SupportedFile_ReturnsAPngPicture`

Run as a theory over an SVG, a Mermaid, PlantUML, Vega-Lite and OpenSCAD source, a PDF and a PNG,
each named relative to the workspace: the result is a caption naming the format followed by
`image/png` content whose corner is opaque.

##### AgentKitToolsVisual-Image-ViewTool-PageSelection: The Second Page Is Drawn and Counted

**Test**: `ImageViewTool_View_SecondPage_CaptionStatesThePageOfTheCount`

Page 2 of a two-page PDF is captioned `Rendered pdf page 2 of 2 as PNG`.

##### AgentKitToolsVisual-Image-ViewTool-PageSelection: A Page Out of Range Is Reported

**Test**: `ImageViewTool_View_PageOutOfRange_IsReported`

Page 3 is reported as `page-out-of-range` naming the two pages; page 0 is refused as
`InvalidRequest`.

##### AgentKitToolsVisual-Image-ViewTool-WidthApplied: A Requested Width Is Applied

**Test**: `ImageViewTool_View_RequestedWidth_IsApplied`

A width of 300 draws a 200 by 100 SVG at 300 by 150; a width of 0 is refused.

##### AgentKitToolsVisual-Image-ViewTool-InputCheckedBeforeRead: A File Outside the Policy Is Refused Before Any Read

**Test**: `ImageViewTool_View_PathOutsideThePolicy_IsRefusedBeforeAnyRead`

An existing and a missing file outside the grant are both refused as `PathNotPermitted`, with texts
identical apart from the name, so the refusal reveals nothing about existence.

##### AgentKitToolsVisual-Image-ViewTool-InputLinkConfined: A File Reached Through a Link Leaving the Grant Is Refused

**Test**: `ImageViewTool_View_InputThroughLinkLeavingTheGrant_IsRefused`

A drawable SVG outside the grant is reached through a link spelled inside it. Viewing the spelled
path returns the policy's `PathNotPermitted` refusal rather than a picture, and the refusal does
not name the outside directory.

##### AgentKitToolsVisual-Image-ViewTool-RefusesMalformedRequests: Malformed Requests Are Refused

**Test**: `ImageViewTool_View_MalformedRequests_AreRefused`

No path and a directory are refused as `InvalidRequest`; a missing file as `TargetNotFound`.

##### AgentKitToolsVisual-Image-ViewTool-RefusesMalformedRequests: An Unsupported Type Is Refused

**Test**: `ImageViewTool_View_UnsupportedType_IsRefused`

A `.docx` is refused as `UnsupportedMediaType`, before any render.

##### AgentKitToolsVisual-Image-ViewTool-ObservesBinaryCeiling: A File Beyond the Ceiling Is Refused

**Test**: `ImageViewTool_View_FileBeyondTheBinaryCeiling_IsRefused`

Under a 64-byte ceiling, an SVG larger than that is refused as `ResourceTooLarge` naming 64 bytes.

##### AgentKitToolsVisual-Image-ViewTool-ObservesBinaryCeiling: A Picture Beyond the Ceiling Is Refused

**Test**: `ImageViewTool_View_PictureBeyondTheBinaryCeiling_IsRefused`

Under a 1,000-byte ceiling, a small SVG fits but its drawing does not, and the picture is refused as
`ResourceTooLarge` naming the rendered picture.

##### AgentKitToolsVisual-Image-ViewTool-ShapedFailure: A Broken File Is Reported With the Offending Line

**Test**: `ImageViewTool_View_BrokenFile_IsReportedWithTheOffendingLine`

A Mermaid file with a malformed arrow is reported as `mermaid-error/mermaid-syntax-error` at line 2,
column 5, quoting the line from the file, naming no help topic and no part of the temporary
directory's absolute path.

##### AgentKitToolsVisual-Image-ViewTool-ShapedFailure: A Reference Outside the Grant Fails the View

**Test**: `ImageViewTool_View_ReferenceOutsideTheGrant_FailsTheView`

An SVG embedding a picture outside the grant is reported as `file-access-denied`.

##### AgentKitToolsVisual-Image-ViewTool-CaptionNotes: The Caption Lists the Render's Notes

**Test**: `ImageViewTool_View_RenderWithNotes_CaptionListsThem`

An OpenSCAD model calling an unknown module is drawn, and the caption carries
`Notes: warning scad-warning`.

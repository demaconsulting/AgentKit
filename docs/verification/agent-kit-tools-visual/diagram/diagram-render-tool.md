### DiagramRenderTool Unit Verification Design

This document describes the unit-level verification strategy for the `DiagramRenderTool` class.

#### Verification Approach

Every scenario invokes the constructed tool exactly as a runtime would — through
`AIFunction.InvokeAsync` with named arguments — drawn by the real rendering library, over a real
temporary tree wherever a file is referenced or saved, because what the tool promises is what a model
receives: a picture, a description, a report it can act on, or a denial. A picture is asserted as
image content rather than serialized JSON, decoded, and its corner checked for opaque white; a
description is asserted on its JSON properties.

Unit tests reside in `Diagram/DiagramRenderToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project, using `TempDirectoryFixture.cs`,
`Image/ImageTestImages.cs` and `Rendering/RenderSamples.cs`.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: The CanvasNet Viewer and its help catalog, in process
- **File system**: A temporary tree per test that references or saves a file, with a permitted root
  and a sibling outside it
- **Isolation**: Each test builds its own tool over a fresh renderer

#### Acceptance Criteria

A unit test run passes when all seventeen scenarios below pass without error or exception beyond
those explicitly asserted. A language not drawn as an opaque PNG, a saved file that differs from the
picture or is not replaced, an unusable destination written, a text preview without its description
or its required `save_as`, a reference outside the grant drawn, a malformed or oversized request not
refused with its reason, a broken source not reported with its location, caret and help topic, an
empty picture shown, a note missing from the caption, a width not applied or not reduced, or a slow
render not reported as a timeout each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Diagram-RenderTool-ToolName: The Name Is the Family-Qualified Name

**Test**: `DiagramRenderTool_ToolName_Constant_IsTheFamilyQualifiedName`

The constant is `diagram_render` and carries the family prefix.

##### AgentKitToolsVisual-Diagram-RenderTool-GuardedConstruction: Each Preview Describes Its Own Result

**Test**: `DiagramRenderTool_Create_EachPreview_DescribesItsOwnResult`

The image-preview tool's description says it returns the picture and its schema calls `save_as`
optional; the text-preview tool's says it returns a description and its schema calls `save_as`
required; both name the offered languages. Null arguments, an empty language list and an undefined
preview mode are refused.

##### AgentKitToolsVisual-Diagram-RenderTool-DrawsEachLanguage: Each Language Is Returned as an Opaque PNG

**Test**: `DiagramRenderTool_Render_EachLanguage_ReturnsAnOpaquePngPicture`

Run as a theory over a Mermaid, PlantUML, SVG, Vega-Lite and OpenSCAD source: the result is a caption
`Rendered <language> as PNG, …` followed by `image/png` content whose corner pixel is opaque white.

##### AgentKitToolsVisual-Diagram-RenderTool-SaveAs: The Picture Is Saved and Named

**Test**: `DiagramRenderTool_Render_SaveAs_SavesThePictureAndNamesIt`

Saving to `diagrams/flow.png` names that path in the caption, and the saved bytes are the returned
picture.

##### AgentKitToolsVisual-Diagram-RenderTool-SaveAs: An Existing File Is Replaced

**Test**: `DiagramRenderTool_Render_SaveAsExistingFile_ReplacesIt`

Saving over a file holding text leaves a PNG there and no other file in the folder.

##### AgentKitToolsVisual-Diagram-RenderTool-SaveAsCheckedFirst: Unusable Destinations Write Nothing

**Test**: `DiagramRenderTool_Render_SaveAsRefusals_WriteNothing`

A `.jpg` is refused as `InvalidRequest`, `../outside/flow.png` and a save under a read-only policy as
`PathNotPermitted`, and a missing folder as `TargetNotFound`; no file is written anywhere.

##### AgentKitToolsVisual-Diagram-RenderTool-TextPreviewDescribes: Text Preview Requires save_as

**Test**: `DiagramRenderTool_Render_TextPreviewWithoutSaveAs_IsRefused`

A text-preview render without `save_as` is refused as `InvalidRequest` saying it is required.

##### AgentKitToolsVisual-Diagram-RenderTool-TextPreviewDescribes: Text Preview Saves and Describes the Picture

**Test**: `DiagramRenderTool_Render_TextPreview_SavesAndDescribesThePicture`

A text-preview render of a Mermaid flowchart saves `flow.png` and returns JSON naming `savedTo`,
`format` and a positive `width`, features carrying the labels `Draft` and `Publish`, and at least one
dominant color. Also evidence for `AgentKit-OTS-CanvasNet-Positions`.

##### AgentKitToolsVisual-Diagram-RenderTool-InlineReferencesConfined: A Reference Inside the Grant Is Drawn

**Test**: `DiagramRenderTool_Render_InlineReferenceInsideTheGrant_IsDrawn`

An SVG embedding `ok.png` from the working directory is drawn with no note.

##### AgentKitToolsVisual-Diagram-RenderTool-InlineReferencesConfined: A Reference Outside the Grant Fails the Render

**Test**: `DiagramRenderTool_Render_InlineReferenceOutsideTheGrant_FailsTheRender`

An SVG embedding `../outside/secret.png` fails as `file-access-denied`, names the
`svg/diagnostics` help topic and no part of the temporary directory's absolute path. Under a policy
granting only a subfolder of the working directory, an SVG embedding a picture beside that subfolder
is drawn without it, with an `svg-image-unresolved` note saying it was not drawn.

##### AgentKitToolsVisual-Diagram-RenderTool-RefusesMalformedRequests: Malformed Requests Are Refused

**Test**: `DiagramRenderTool_Render_MalformedRequests_AreRefused`

For a tool offering Mermaid alone, an unknown language (whose refusal lists `mermaid`), an unoffered
language, a blank source and a width of 0 are each refused as `InvalidRequest`.

##### AgentKitToolsVisual-Diagram-RenderTool-ObservesCeilings: Sources and Pictures Beyond the Ceilings Are Refused

**Test**: `DiagramRenderTool_Render_BeyondTheCeilings_IsRefused`

Under a 64-byte read ceiling the SVG source is refused as `ResourceTooLarge` naming 64 bytes; under a
1,000-byte binary ceiling its picture is refused as `ResourceTooLarge` naming the rendered picture.

##### AgentKitToolsVisual-Diagram-RenderTool-ShapedFailure: A Broken Source Is Shaped With Its Location and Help Topic

**Test**: `DiagramRenderTool_Render_BrokenSource_IsShapedWithItsLocationAndHelpTopic`

Run as a theory over a broken source in each language: the report opens with the language's code and
the line, quotes that line, places a caret under it, and ends with
`Help: diagram_help topic '<tag>/diagnostics'.`

##### AgentKitToolsVisual-Diagram-RenderTool-ShapedFailure: An Empty Picture Is Reported as a Failure

**Test**: `DiagramRenderTool_Render_EmptyPicture_IsReportedAsAFailure`

An SVG that draws nothing is reported as `empty-render`.

##### AgentKitToolsVisual-Diagram-RenderTool-CaptionNotes: The Caption Lists the Render's Notes

**Test**: `DiagramRenderTool_Render_RenderWithNotes_CaptionListsThem`

An OpenSCAD model calling an unknown module is drawn, and the caption carries
`Notes: warning scad-warning`.

##### AgentKitToolsVisual-Diagram-RenderTool-WidthApplied: A Width Is Applied Up to the Largest Side

**Test**: `DiagramRenderTool_Render_RequestedWidth_IsAppliedUpToTheLargestSide`

With a largest side of 400 pixels, a 200 by 100 SVG asked for at 300 is drawn at 300 by 150, and
asked for at 5000 is drawn at 400 by 200.

##### AgentKitToolsVisual-Diagram-RenderTool-Timeout: A Slow Render Is Reported as a Timeout

**Test**: `DiagramRenderTool_Render_BeyondTheTimeout_IsReportedAsATimeout`

With a 100-millisecond timeout, an OpenSCAD model that loops tens of millions of times without
drawing is reported as `render-timeout` with advice to simplify the source. Only the outcome is
asserted, never a duration.

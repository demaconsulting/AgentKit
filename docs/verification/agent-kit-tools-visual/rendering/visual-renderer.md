### VisualRenderer Unit Verification Design

This document describes the unit-level verification strategy for the `VisualRenderer` class.

#### Verification Approach

Every scenario draws through the real rendering library: the renderer's value is in how it drives
that library — the background it forces, the sizes it requests, the budgets it passes on, the
outcomes it promotes — and a substitute library would verify none of it. Sizes are asserted exactly,
because they are deterministic for a vector source of a known size.

**No scenario asserts a duration.** The timeout scenario asserts the `render-timeout` outcome only,
for a model that loops without drawing far longer than its 100 ms timeout. The render a scenario
must hold in progress is held **deterministically** by a file-access delegate that blocks until the
test releases it, ignoring the render's token — so the grace-period abandonment and the gate are
exercised by a render that is known not to stop, rather than by one assumed to be slow.

Unit tests reside in `Rendering/VisualRendererTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project, using `Rendering/RenderSamples.cs` and
`Image/ImageTestImages.cs`.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: The CanvasNet Viewer, in process
- **File system**: A temporary tree for scenarios that reference a file
- **Isolation**: Each test creates its own renderer, so no gate is shared between tests

#### Acceptance Criteria

A unit test run passes when all seventeen scenarios below pass without error or exception beyond
those explicitly asserted. A transparent background, a budget or limit not applied, a size other than
the one specified, an enlarged raster, a timeout not reported, an abandoned render not reported or
releasing its gate place early, a caller's cancellation reported as a timeout, a note lost, a refused
or empty picture returned as a success, a blank raster refused, the wrong page or page count, a
description missing or produced unasked, or a warm-up failure each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Rendering-VisualRenderer-OpaqueBackground: An SVG Is Drawn on Opaque White

**Test**: `VisualRenderer_RenderAsync_Svg_IsDrawnOnOpaqueWhite`

The blank corner of an SVG decodes as white with alpha 255.

##### AgentKitToolsVisual-Rendering-VisualRenderer-PixelBudget: The Pixel Budget Bounds the Render

**Test**: `VisualRenderer_RenderAsync_PictureOverThePixelBudget_FailsAsBudgetExceeded`

Under a policy whose pixel budget is 1,000, a diagram fails as `budget-exceeded`.

##### AgentKitToolsVisual-Rendering-VisualRenderer-SideBounded: A Requested Width Is Reduced to the Largest Side

**Test**: `VisualRenderer_RenderAsync_WidthBeyondTheLargestSide_IsReduced`

A 5000-pixel request under a largest side of 300 draws a 200 by 100 SVG at 300 by 150.

##### AgentKitToolsVisual-Rendering-VisualRenderer-SideBounded: A Natural Size Beyond the Largest Side Is Reduced

**Test**: `VisualRenderer_RenderAsync_NaturalSizeBeyondTheLargestSide_IsDrawnAtTheLargestSide`

A 1000 by 500 SVG under a largest side of 400 is drawn at 400 by 200.

##### AgentKitToolsVisual-Rendering-VisualRenderer-SmallVectorEnlarged: A Small Vector Picture Is Drawn Larger

**Test**: `VisualRenderer_RenderAsync_SmallVectorPicture_IsDrawnAtTheSmallestPreviewSide`

A 200 by 100 SVG with no width requested is drawn at the default smallest preview side, 768 by 384.

##### AgentKitToolsVisual-Rendering-VisualRenderer-RasterNeverEnlarged: A Raster Is Never Enlarged

**Test**: `VisualRenderer_RenderAsync_RasterImage_IsNeverEnlarged`

A 400 by 200 PNG is drawn at its own size with no width, at 100 by 50 for a width of 100, and at its
own size for a width of 1600.

##### AgentKitToolsVisual-Rendering-VisualRenderer-TriangleLimit: The Triangle Limit Refuses a Model

**Test**: `VisualRenderer_RenderAsync_ModelOverTheTriangleLimit_IsRefused`

A sphere drawn under the default limit succeeds, and under a limit of 100 fails as `scad-error`
naming the limit — so the configured limit, not the model, decided.

##### AgentKitToolsVisual-Rendering-VisualRenderer-Timeout: A Render Beyond the Timeout Times Out

**Test**: `VisualRenderer_RenderAsync_RenderBeyondTheTimeout_FailsAsRenderTimeout`

An OpenSCAD model that loops tens of millions of times without drawing fails as `render-timeout`
under a 100 ms timeout.

##### AgentKitToolsVisual-Rendering-VisualRenderer-AbandonAndGate: A Render Ignoring Cancellation Is Abandoned and Holds the Gate

**Test**: `VisualRenderer_RenderAsync_RenderIgnoringCancellation_IsAbandonedAndHoldsTheGate`

With one gate place and a 3-second timeout, a render is started and the test waits until it has
reached the blocking delegate. A second render then times out waiting for the gate; the held render
is reported as `render-timeout` while it is still inside the library — abandoned, not finished —
and once it is released and ends, a third render succeeds. Every wait the test makes is bounded, so
a regression fails rather than hangs the run.

##### AgentKitToolsVisual-Rendering-VisualRenderer-CallerCancellation: The Caller's Cancellation Is Thrown

**Test**: `VisualRenderer_RenderAsync_CallerCancels_ThrowsOperationCanceled`

A token already canceled makes the render throw `OperationCanceledException`.

##### AgentKitToolsVisual-Rendering-VisualRenderer-LenientNotes: An Unsupported Construct Is a Note

**Test**: `VisualRenderer_RenderAsync_UnsupportedFeature_IsReportedAsANote`

An OpenSCAD model calling an unknown module beside a cube is drawn, with a `warning scad-warning`
note at line 1 naming the module.

##### AgentKitToolsVisual-Rendering-VisualRenderer-RefusedPicturePromoted: A Refused Picture Fails the Render

**Test**: `VisualRenderer_RenderAsync_RefusedPicture_IsPromotedToAFailure`

An SVG escaping to a picture outside the grant fails as `file-access-denied` with the detail
`image-access-denied`, and the outcome keeps the note that reported the refusal.

##### AgentKitToolsVisual-Rendering-VisualRenderer-EmptyPicturePromoted: An Empty Picture Fails the Render

**Test**: `VisualRenderer_RenderAsync_EmptyPicture_IsPromotedToAFailure`

Run as a theory over an SVG that draws nothing, the same SVG at a requested width of 30 pixels,
and an unsupported Mermaid diagram type drawn as an empty 16 by 16 square; each is caught by its
ink and fails as `empty-render`. With the width requested the message names it; without one it
does not.

##### AgentKitToolsVisual-Rendering-VisualRenderer-EmptyPicturePromoted: A Small Picture With Ink Is Drawn

**Test**: `VisualRenderer_RenderAsync_SmallPictureWithInk_IsDrawn`

Run as a theory over a 24 by 24 SVG icon with no width requested, drawn at 768 pixels; a
two-circle SVG at a requested width of 30 pixels; and a two-node Mermaid flowchart at a requested
width of 32 pixels. Each is drawn at the expected width rather than reported as empty, because
emptiness is judged by ink alone.

##### AgentKitToolsVisual-Rendering-VisualRenderer-EmptyPicturePromoted: A Blank Raster Is Drawn

**Test**: `VisualRenderer_RenderAsync_BlankRasterImage_IsDrawn`

A uniformly white PNG is returned as drawn, because it is its file's true content.

##### AgentKitToolsVisual-Rendering-VisualRenderer-PageSelection: A Document Page Is Drawn With the Page Count

**Test**: `VisualRenderer_RenderAsync_DocumentPage_IsDrawnWithThePageCount`

Page 2 of a two-page PDF reports page 2 of 2; page 3 fails as `page-out-of-range`.

##### AgentKitToolsVisual-Rendering-VisualRenderer-Description: A Description Lists Features and Colors

**Test**: `VisualRenderer_RenderAsync_DescriptionRequested_ListsFeaturesAndColors`

A flowchart drawn with a description lists a feature carrying the label `Publish` and dominant colors
in the form `#rrggbb nn%`; drawn without one, it lists neither.

##### AgentKitToolsVisual-Rendering-VisualRenderer-WarmUp: Warming Up Completes

**Test**: `VisualRenderer_WarmUpAsync_KnownAndUnknownFormats_Completes`

Warming up every known format plus an unknown name completes successfully, over the five formats the
renderer knows.

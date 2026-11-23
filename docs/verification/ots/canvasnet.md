## CanvasNet Verification

This document provides the verification evidence for the `CanvasNet` OTS software item.

### Required Functionality

`CanvasNet` supplies the raster imaging capability the region tools are built on, and through its
Viewer the drawing of documents and diagrams the view tool and the Rendering subsystem are built
on: reading the pixel
dimensions an image file declares in its header without decoding any pixel data — including for a
file whose declared dimensions exceed what it will decode — reporting, from the header alone,
whether a full decode is expected to succeed, decoding PNG and JPEG files into an
addressable pixel buffer including a PNG whose pixels are stored as palette indices, rejecting PNG
pixel data that would inflate beyond the declared size without inflating it, copying a
rectangular sub-region of such a buffer, and
encoding a buffer back to a form that reproduces every pixel exactly when decoded again. The Viewer
must draw each supported format at a requested size, background and page; consult a caller-supplied
file-access policy for every referenced file; never fetch a network reference; honor a pixel budget,
a triangle limit and a cancellation token; and report source errors with their locations and
unsupported features as notes.

### Verification Approach

Like the other runtime library dependencies, and unlike the build-and-verify pipeline tools, this
OTS item has no self-validation command. Per `software-items.md`, an OTS library is verified
through integration tests proving the required functionality works, so the evidence is AgentKit's
own tests: each exercises the library through the tool that consumes it, invoked the way a runtime
invokes it, against files the test itself constructs so that the expected answer is known
independently of the library.

The scenarios are chosen so that each observes something only the required capability could
produce. A caption stating an image's real size is evidence the header was read. A refusal naming
a decode budget, issued for a file that declares its size and then carries no pixel data at all,
is evidence the header was read *without* decoding — a library that decoded first would have
failed differently. These tests run on every platform with no platform filter, so a single-OS run
satisfies them.

### Test Scenarios

#### ImageReadTool_Read_SupportedPng_CaptionStatesThePixelDimensions

**Scenario**: A PNG of a known size is read through the image read tool.

**Expected**: The caption states that size, which the tool obtained from the file's header rather
than from the test.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-HeaderProbe`.

#### ImageReadTool_Read_SupportedJpeg_CaptionStatesThePixelDimensions

**Scenario**: A JPEG of a known size is read through the image read tool.

**Expected**: The caption states that size, obtained by a bounded scan of the file's leading
marker segments.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-HeaderProbe`.

#### ImageCropTool_Crop_HeaderDeclaringMorePixelsThanTheDecodeBudget_IsRefusedNamingTheBudget

**Scenario**: A file declaring eight thousand pixels on each side, and carrying no pixel data at
all, is offered to the image crop tool.

**Expected**: A refusal naming the host's decode budget and the declared size, and specifically not
the undecodable refusal — so the header was read, and reported, without any attempt to decode.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-HeaderProbe`.

#### ImageCropTool_Crop_PalettizedPng_ReturnsCroppedImageContent

**Scenario**: A region is taken from a PNG whose pixels are stored as palette indices.

**Expected**: The region decodes successfully and its first pixel is the palette's color rather
than a raw index, proving the indices were resolved through the file's own palette.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-Decode`.

#### ImageCropTool_Crop_PermittedJpeg_ReturnsPngContent

**Scenario**: A region is taken from a JPEG.

**Expected**: The region decodes successfully, with the requested dimensions, proving the second
format decodes into the same addressable buffer.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-Decode`.

#### ImageCropTool_Crop_PngInflatingBeyondItsDeclaredSize_IsRefusedWithoutInflatingIt

**Scenario**: A PNG declaring sixteen by sixteen pixels, a fraction of the binary ceiling in size,
whose pixel data inflates to half a gigabyte, is offered to the image crop tool, and the decode step
the tool performs is then repeated on the same bytes while the calling thread's allocation is
measured — that step is synchronous, so the per-thread counter is unaffected by concurrent tests.

**Expected**: The undecodable refusal naming the declared size, with the decode step having allocated
less than a quarter of the inflated size — so the decoder rejected the pixel data without inflating it.
The same scenario run against the 0.1.0 release allocated over a gigabyte and failed, which is
what shows the scenario observes the bound rather than passing regardless of it.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-BoundedInflate`.

#### ImageCropTool_Crop_ReturnedRegion_CarriesTheSourcePixelsExactly

**Scenario**: A region whose origin is not the image's own is taken from a source built with a
distinct value in every channel of every pixel, and the returned region is decoded again.

**Expected**: Every channel of every pixel of the result equals the source pixel it came from,
including the alpha channel, proving both that the sub-region copy addresses the right pixels and
that the encoding alters none of them.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-Crop`, `AgentKit-OTS-CanvasNet-Encode`.

#### ImageCropTool_Crop_Adam7InterlacedPng_IsRefusedNamingTheDimensions

**Scenario**: A complete, well-formed PNG declaring Adam7 interlacing — today the one well-formed
input this library does not decode — is offered to the image crop tool.

**Expected**: A refusal stating the file is well formed but uses a feature the tool does not
decode, naming the size the header declared, and specifically **not** the undecodable refusal —
so the feasibility was reported from the header, before any decode was attempted, and the
consuming tool needed no knowledge of the PNG format to say so.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-DecodeFeasibility`.

#### Viewer scenarios

The Viewer requirements are evidenced by the Rendering subsystem's and the view tool's own tests,
which draw through the real library:

- **Drawing** — `ImageViewTool_View_SupportedFile_ReturnsAPngPicture` draws an SVG, Mermaid,
  PlantUML, Vega-Lite and OpenSCAD source, a PDF, a PPTX slide, a VSDX page and a PNG (the PPTX and
  VSDX packages are built in test code: a one-slide deck with its layout, master and theme, and a
  one-page drawing in the Visio 2012 namespace, each holding one filled shape and no text, so the
  result does not depend on installed fonts); `VisualRenderer_RenderAsync_Svg_IsDrawnOnOpaqueWhite`
  asserts the requested background; `VisualRenderer_RenderAsync_WidthBeyondTheLargestSide_IsReduced`
  the requested width; `VisualRenderer_RenderAsync_DocumentPage_IsDrawnWithThePageCount` the
  requested page and the page count.
- **File access** — the `VisualAccess_ToFileAccess_*` scenarios observe that a picture, an include
  and a data file refused by the policy are not drawn.
- **No network** — `VisualAccess_ToFileAccess_NetworkReference_IsNeverFetched` and
  `AgentKitToolsVisual_Rendering_NetworkReferences_AreNeverFetched` observe that a local listener
  receives no request.
- **Budgets and cancellation** — the pixel-budget, triangle-limit and timeout scenarios of
  `VisualRendererTests` observe each refusal.
- **Diagnostics** — `RenderErrorShaper_Shape_SyntaxError_StatesCodeLineColumnAndCaret` observes the
  location of a broken source in each format, and
  `VisualRenderer_RenderAsync_UnsupportedFeature_IsReportedAsANote` a note with its code and line.
- **Help catalog** — `DiagramHelpTool_Help_Topic_ReturnsTheCardWithItsExampleSources` reads a card
  with its example source and by an alias; `DiagramHelpTool_Help_HostOrientedCards_AreHidden` and
  `DiagramHelpTool_Help_UnofferedLanguage_IsNotServed` observe that a card hidden by the tag filter
  is neither found nor listed.
- **Positions** — `DiagramRenderTool_Render_TextPreview_SavesAndDescribesThePicture` observes the
  node labels of a Mermaid flowchart and its dominant colors in a text-preview description.

### Requirements Coverage

- **`AgentKit-OTS-CanvasNet-HeaderProbe`**:
  ImageReadTool_Read_SupportedPng_CaptionStatesThePixelDimensions,
  ImageReadTool_Read_SupportedJpeg_CaptionStatesThePixelDimensions,
  ImageCropTool_Crop_HeaderDeclaringMorePixelsThanTheDecodeBudget_IsRefusedNamingTheBudget
- **`AgentKit-OTS-CanvasNet-Decode`**:
  ImageCropTool_Crop_PalettizedPng_ReturnsCroppedImageContent,
  ImageCropTool_Crop_PermittedJpeg_ReturnsPngContent
- **`AgentKit-OTS-CanvasNet-BoundedInflate`**:
  ImageCropTool_Crop_PngInflatingBeyondItsDeclaredSize_IsRefusedWithoutInflatingIt
- **`AgentKit-OTS-CanvasNet-Crop`**:
  ImageCropTool_Crop_ReturnedRegion_CarriesTheSourcePixelsExactly
- **`AgentKit-OTS-CanvasNet-Encode`**:
  ImageCropTool_Crop_ReturnedRegion_CarriesTheSourcePixelsExactly
- **`AgentKit-OTS-CanvasNet-DecodeFeasibility`**:
  ImageCropTool_Crop_Adam7InterlacedPng_IsRefusedNamingTheDimensions
- **`AgentKit-OTS-CanvasNet-ViewerRender`**:
  ImageViewTool_View_SupportedFile_ReturnsAPngPicture,
  VisualRenderer_RenderAsync_Svg_IsDrawnOnOpaqueWhite,
  VisualRenderer_RenderAsync_WidthBeyondTheLargestSide_IsReduced,
  VisualRenderer_RenderAsync_DocumentPage_IsDrawnWithThePageCount
- **`AgentKit-OTS-CanvasNet-FileAccessPolicy`**:
  VisualAccess_ToFileAccess_ParentFolderEscape_IsRefused,
  VisualAccess_ToFileAccess_LinkEscape_IsRefused,
  VisualAccess_ToFileAccess_DenyPatternInsideTheGrant_IsRefused,
  VisualAccess_ToFileAccess_IncludedFileOutside_IsRefused
- **`AgentKit-OTS-CanvasNet-NoNetwork`**:
  VisualAccess_ToFileAccess_NetworkReference_IsNeverFetched,
  AgentKitToolsVisual_Rendering_NetworkReferences_AreNeverFetched
- **`AgentKit-OTS-CanvasNet-PixelBudget`**:
  VisualRenderer_RenderAsync_PictureOverThePixelBudget_FailsAsBudgetExceeded,
  VisualRenderer_RenderAsync_ModelOverTheTriangleLimit_IsRefused
- **`AgentKit-OTS-CanvasNet-Cancellation`**:
  VisualRenderer_RenderAsync_RenderBeyondTheTimeout_FailsAsRenderTimeout
- **`AgentKit-OTS-CanvasNet-Diagnostics`**:
  RenderErrorShaper_Shape_SyntaxError_StatesCodeLineColumnAndCaret,
  VisualRenderer_RenderAsync_UnsupportedFeature_IsReportedAsANote
- **`AgentKit-OTS-CanvasNet-HelpCatalog`**:
  DiagramHelpTool_Help_Topic_ReturnsTheCardWithItsExampleSources,
  DiagramHelpTool_Help_HostOrientedCards_AreHidden,
  DiagramHelpTool_Help_UnofferedLanguage_IsNotServed
- **`AgentKit-OTS-CanvasNet-Positions`**:
  DiagramRenderTool_Render_TextPreview_SavesAndDescribesThePicture

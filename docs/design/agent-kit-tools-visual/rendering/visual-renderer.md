### VisualRenderer

![AgentKit Tools Visual Rendering Structure](RenderingView.svg)

The `VisualRenderer` class draws a document or diagram to an opaque PNG within a pack's time,
concurrency and size bounds, and reports everything the render said about the picture.

#### Purpose

To be the one place every drawing of a pack passes through, so that every bound, the opaque
background and the promotions of misleading pictures hold for every tool alike.

#### Data Model

One instance per pack, created by the pack from its `VisualOptions`. Its only mutable state is the
render gate, a `SemaphoreSlim` of `MaxConcurrentRenders` places; it is safe for concurrent use.

| Member | Type | Value |
| --- | --- | --- |
| `Options` | `VisualOptions` | The pack's options |
| `NearEmptyInkFraction` | `double` | 0.0005 |
| `MaxFeatures` | `int` | 50 |
| `MaxDominantColors` | `int` | 4 |
| `MaxContentBytes` | `int` | 16 MiB, the largest content the library accepts as a stream |
| `Grace` | `TimeSpan` | 2 seconds |
| `White` | `Rgba32` | Opaque white, the background of every picture |
| `WarmUpFormats` | collection | `mermaid`, `plantuml`, `svg`, `vega-lite`, `openscad` |

#### Key Methods

##### RenderAsync(RenderRequest request, CancellationToken cancellationToken)

Draws one document or diagram.

**Algorithm — timeout and gate:**

1. Start a clock and wait for a place in the gate for at most `RenderTimeout`. A render that cannot
   start in time has timed out like one that cannot finish: `render-timeout`.
2. Create a cancellation source linked to the caller's token, canceled after what remains of the
   timeout, and start the drawing on the thread pool.
3. Attach a continuation to the drawing that observes its exception, disposes the cancellation
   source and releases the gate place — so the place is released when the drawing ends, however
   long after the caller stopped waiting.
4. Wait for the drawing for at most what remains of the timeout plus the grace period. A drawing
   that has not ended by then is abandoned and `render-timeout` is returned; a cancellation that is
   the timeout's rather than the caller's is also `render-timeout`; the caller's own cancellation is
   thrown.

The library honors the token, so the grace-period abandonment is defense in depth: it bounds what
a caller waits even if a drawing stopped noticing its token, and the gate keeps such drawings from
accumulating.

**Algorithm — the drawing:**

1. Clamp a requested width to 1..`MaxRenderSide`. Draw once: a vector source at the requested
   width, a raster at its own size. Every call sets an opaque white background, lenient drawing,
   `MaxDecodedPixels` = `ToolLimits.MaxImagePixels`, the triangle limit, the page, the folder, the
   file-access policy, the token, and positions when a description is asked for. A DPI is never set.
2. Decide whether to draw again, at most once, setting the longer side (width when wider than tall,
   otherwise height): a raster wider than a requested width is reduced to it; a picture whose longer
   side exceeds `MaxRenderSide` is drawn at that side; a vector picture with no requested width
   whose longer side is below `MinPreviewSide` is drawn at that side. A vector source is drawn
   again rather than resampled, so it stays sharp; a raster is never enlarged.
3. Finish: format every note; promote, or encode.

**Promotions to failure:**

- A note with the code `image-access-denied` — an embedded picture the file-access policy refused —
  makes the render a `file-access-denied` failure with the detail `image-access-denied`, carrying
  every note. A picture with a hole the model cannot see the reason for would be described as
  complete.
- A vector picture whose final picture has less than `NearEmptyInkFraction` of its pixels
  different from its top-left pixel, whatever its natural or requested size, is an `empty-render`
  failure carrying every note; with a requested width, the message names it and says a larger
  width, or none, may show the picture. Emptiness is judged by ink alone, because a small icon or
  a small requested width is not empty. A raster image is never promoted: a blank photograph is
  the true content of its file.

**Encoding:** a picture that still carries transparency is flattened onto white, and the picture is
encoded as RGB PNG. With a description asked for, the outcome lists at most `MaxFeatures` features
of a Mermaid diagram or Vega-Lite chart (`kind id "text" from->to`), marking truncation, and at most
`MaxDominantColors` colors (`#rrggbb nn%`).

**Notes** are formatted as `severity code[/detail] at line L, column C: message`, with the
library's location prefix and leading detail code removed from the message and its host-oriented
phrases rewritten. The note's detail is used only when it is a code: the library also uses the
detail of a refused file for a record of its requested and real paths, which is host detail and is
never shown.

**Any exception** the drawing raises other than the timeout's cancellation is classified by
`RenderErrorShaper` and returned as a failure. The content is model-authored and the library a
prerelease, so a defect it raises on hostile input must become a failure the model is told about,
not the end of the agent's turn.

##### WarmUpAsync(IEnumerable&lt;string&gt; formats, CancellationToken cancellationToken)

Draws a small embedded sample of each named format it knows, through `RenderAsync` — so through the
gate and the timeout — under a file-access policy that refuses every file, and discards the
results. Unknown names are ignored, and a failed sample is ignored: warming up is an optimization.

##### IsRaster(string fileName)

True for the raster extensions the library draws (`bmp`, `gif`, `j2c`, `j2k`, `jp2`, `jpeg`, `jpg`,
`png`, `tif`, `tiff`), which are never enlarged nor promoted for being empty.

#### Error Handling

`ArgumentNullException` for a null request, options or format list. `OperationCanceledException`
for the caller's own cancellation. Every other failure is an outcome.

#### Dependencies

`VisualOptions` for the bounds; `RenderRequest`, `RenderOutcome` and `RenderFailure`;
`RenderErrorShaper` to classify an exception and clean a message; `VisualAccess` for the warm-up's
names. From the CanvasNet Viewer and core: `CanvasNetViewer.Render`, `ViewerRenderOptions`,
`ScadViewOptions`, `PositionSelection`, `ImageSourceDiagnostic`, `Surface`, `PngCodec`, `Rgba32`.

#### Callers

`ImagePack` creates one and calls `WarmUpAsync`; `ImageViewTool` calls `RenderAsync`.

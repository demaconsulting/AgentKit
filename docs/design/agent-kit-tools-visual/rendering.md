## Rendering

![AgentKit Tools Visual Rendering Structure](RenderingView.svg)

The Rendering subsystem holds the internal pieces every visual tool that draws shares: the
confinement of the files a drawn document references, the bounded renderer, the shaping of failures
into text a model can act on, and the saving of a picture under the write grant.

### Overview

A visual tool that draws a document hands the rendering library content written by, or chosen by, a
model. Four things must then hold for every such tool alike, and holding them in one subsystem is
what keeps them alike:

1. **Every file the document references is confined** to what the agent may read, by a file-access
   policy mapped from the access policy, because the rendering library opens those files itself and
   reads any local file by default.
2. **Every render is bounded** by the pixel budget, the binary ceiling, the largest render side, the
   triangle limit, the timeout and the pack's concurrency bound, and is drawn on an opaque white
   background.
3. **Every failure is shaped** into text with a stable code, a location, the offending line with a
   caret, model-facing advice and every note — with host paths removed — and a picture that would
   mislead a model is promoted to a failure.
4. **Every saved picture** goes only where the write decision permits, replacing an earlier one in
   one step.

The subsystem contains four units and three supporting types:

| Unit | Responsibility |
| --- | --- |
| `VisualAccess` | Maps the access policy onto the rendering library's file-access policy |
| `VisualRenderer` | Draws within the bounds on opaque white, promoting misleading pictures |
| `RenderErrorShaper` | Classifies, locates and words a failure for a model |
| `RenderDestination` | Saves a picture under the write grant, replacing atomically |

`RenderRequest`, `RenderOutcome` and `RenderFailure` are supporting types rather than units: each is
an immutable carrier of values between the units above, with no behavior a requirement would
promise beyond what the units promise observably. They are documented here and covered by this
subsystem's review-set.

- **RenderRequest** — the bytes to draw, the file name that chooses the format, the folder relative
  references resolve against (or none), the file-access policy, the policy's limits, the requested
  width and page, and whether a description of the picture is wanted. The content is always bytes
  the tool already holds, never a path, so the bytes judged and the bytes drawn are the same and the
  library's messages name the content only by its file name.
- **RenderOutcome** — exactly one of an opaque PNG (with its size, format, page, page count, notes,
  and when asked the features and dominant colors) or a `RenderFailure`. Its lists are private
  copies exposed read-only.
- **RenderFailure** — a code, an optional finer code, the library's message without its framing,
  the library's hint, the location (file, line, column, JSON pointer) and the notes.

### Interfaces

Nothing in the subsystem is public. It is consumed by `ImageViewTool`, which draws through
`VisualRenderer`, confines through `VisualAccess` and shapes through `RenderErrorShaper`; by
`DiagramRenderTool`, which does the same for inline source, resolving its references through
`VisualAccess.InlineBaseFolder`, naming a help topic through `RenderErrorShaper` and saving through
`RenderDestination`; and by `ImagePack` and `DiagramPack`, each of which creates its own renderer.

| Interface | Direction | Format | Constraints |
| --- | --- | --- | --- |
| `VisualAccess.ToFileAccess` | Outbound | CanvasNet `FileAccessPolicy` | Never `AllowAll` |
| `VisualAccess.InlineBaseFolder` | Outbound | `string?` | Null when the working directory is unreadable |
| `VisualRenderer.RenderAsync` | Outbound | `RenderOutcome` | Throws only the caller's cancellation |
| `VisualRenderer.WarmUpAsync` | Outbound | `Task` | Through the gate and timeout |
| `RenderErrorShaper.Shape` | Outbound | `string` | No host path; help topic only when served |
| `RenderDestination.TryResolve`/`TryWriteAsync` | Outbound | Denial or destination; `bool` | Write decision alone |
| `PathPolicy`, `ToolLimits` | Inbound | AgentKitCore | Supplied by the calling tool |
| `VisualOptions` | Inbound | Public record | Supplied by the pack |
| CanvasNet Viewer | Inbound | OTS library | Bytes in; picture, notes or exception out |

### Design

**The rendering library never judges its input; the calling tool does.** The library applies its
file-access policy only to the files a document references, never to the file it is asked to draw,
so each tool takes the read decision for its input itself and hands the subsystem bytes. That
division is why `RenderRequest` carries bytes and a name rather than a path.

**Lenient rendering with every note reported.** The library can stop at the first construct it does
not draw, or draw without it and say so. The second is chosen: a lenient render's notes carry more
for a model to act on — an unsupported Mermaid diagram type is reported with the list of types that
are supported — and the picture shows the rest. Two outcomes of a lenient render would mislead a
model and are promoted to failures by `VisualRenderer`: a picture whose embedded image the
file-access policy refused, and a picture that is empty or almost empty.

**Defense in depth around cancellation.** The timeout is first a cancellation token the library
honors. The caller also stops waiting a grace period later, and the pack's render gate keeps an
abandoned render's place until it ends; those two exist so that no render that stopped noticing its
token could hold a caller or accumulate, not because the library is known to ignore it.

**One mapping for every reference kind.** The same file-access policy governs an SVG's pictures, a
Mermaid node's picture, a Vega-Lite chart's image marks and data file, a PlantUML include and an
OpenSCAD include, at every level of nesting, because the library consults one policy for all of
them. The subsystem does not inspect documents for references itself.

**Opaque white is not optional.** The library's default background is transparent, which a vision
model reads as dark text on black. The renderer sets white for every render and flattens again any
picture that still carries transparency, so no tool can return a transparent picture.

**Known limitations of the pinned library** are recorded in the system design; see *AgentKitToolsVisual
System Design*, *Known Limitations*.

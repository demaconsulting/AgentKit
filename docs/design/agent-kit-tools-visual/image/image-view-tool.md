### ImageViewTool

![AgentKit Tools Visual Image Structure](ImageView.svg)

The `ImageViewTool` class publishes the `image_view` tool: it draws one document, drawing or diagram
file the access policy permits the agent to read as a PNG picture the model can look at.

#### Purpose

A provider renders a few raster types and PDF, which `image_read` hands it byte for byte. An agent's
workspace also holds vector drawings, slide decks, VSDX drawings and diagram sources a provider
cannot render. This tool draws such a file and returns the drawing, so the two meanings of "look at
this file" — pass the bytes through, or draw them — each have their own tool and never disagree.

#### Data Model

The class is static. A constructed tool captures only the immutable policy and the pack's renderer,
which is itself safe for concurrent use.

| Member | Type | Value |
| --- | --- | --- |
| `ToolName` (constant) | `string` | `image_view` |
| Parameters | `path`, `page`, `width` | `page` defaults to 1; `width` is optional |

#### Key Methods

##### Create(PathPolicy policy, VisualRenderer renderer)

Builds the tool through `GuardedToolFactory`, so the picture reaches the caller as image content
rather than a serialized copy of it. Internal: an application obtains the tool by attaching
`ImagePack`, which supplies the renderer whose gate every drawing of the pack shares.

##### IsViewable(string path)

True when the file's name ends in one of `CanvasNetViewer.Extensions` (case-insensitive) — matched
on the end of the name because one supported extension, `.vl.json`, has two parts.

##### The view, in order

1. A missing path, a page below 1 or a width below 1 is refused as `InvalidRequest`.
2. `PathPolicy.TryResolveRead(path)`: the rendering library never judges the file it is asked to
   draw, so this is the input's only judgment, and it is taken before anything is learned about the
   file. The policy's refusal is returned unchanged as `PathNotPermitted`, identical whether or not
   the file exists.
3. A directory is refused as `InvalidRequest`.
4. A name `IsViewable` rejects is refused through `ImageMediaTypes.DenyUnsupportedType`, so the
   family answers "what is this file" in one voice.
5. A missing file is refused as `TargetNotFound`.
6. The file is opened once; its length, read from that handle, must not exceed the lower of the
   binary ceiling and the largest content the library accepts (16 MiB), or it is refused as
   `ResourceTooLarge` naming the ceiling; then exactly that many bytes are read. An unreadable file
   is `InvalidRequest`.
7. Those bytes, never the path, are drawn by `VisualRenderer` with the file's name, the file's own
   folder as the base for references, the file-access policy `VisualAccess` maps from the access
   policy, the policy's limits, the requested width and the requested page. Giving the library
   bytes keeps the bytes judged and the bytes drawn the same, and keeps the input's absolute path
   out of every message the library composes.
8. A failure is returned as `ToolResult.Text` of the text `RenderErrorShaper` composes, quoting the
   offending line when the file is a text format no larger than the read ceiling, and naming no help
   topic — this family has no help catalog. A failure is text rather than a denial because the model
   can fix what it wrote; path, permission and size problems remain denials.
9. A drawn picture larger than the binary ceiling is refused as `ResourceTooLarge`: it is content
   returned to the provider, and the guarded construction path does not enforce that ceiling.
10. Otherwise `ToolResult.Image(png, "image/png", caption)`, the caption being
    `Rendered <format>[ page P of N] as PNG, WxH pixels.[ Notes: …]` — the page part only for a
    document with more than one page — with host paths scrubbed.

#### Error Handling

`ArgumentNullException` from `Create` for a null policy or renderer. Every condition a model can
provoke is a returned result; only the caller's own cancellation is thrown.

#### Dependencies

`PathPolicy`, `GuardedToolFactory` and `ToolResult` from AgentKitCore; `ImageMediaTypes` for the
unsupported-type refusal; the Rendering subsystem's `VisualRenderer`, `VisualAccess` and
`RenderErrorShaper`; `CanvasNetViewer.Extensions` from the CanvasNet Viewer.

#### Callers

`ImagePack.CreateTools` creates the tool; a model calls it.

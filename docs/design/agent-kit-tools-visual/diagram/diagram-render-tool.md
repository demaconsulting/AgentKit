### DiagramRenderTool

![AgentKit Tools Visual Diagram Structure](DiagramView.svg)

The `DiagramRenderTool` class publishes the `diagram_render` tool: it draws diagram source the model
supplies inline and shows the model the result, optionally saving the picture under the write grant.

#### Purpose

A model checks a drawing by looking at it, or, when it cannot see, by what the drawing contains. The
tool draws source in any offered language within the pack's bounds and confinement and returns the
one of those two the host can use, or a report the model can fix the source from.

#### Data Model

The class is static. A constructed tool captures the immutable policy, the pack's renderer, the
preview mode, the offered languages and the filtered help catalog.

| Member | Type | Value |
| --- | --- | --- |
| `ToolName` (constant) | `string` | `diagram_render` |
| Parameters | `format`, `source`, `save_as`, `width` | `save_as` optional in image preview, required in text preview |

#### Key Methods

##### Create(PathPolicy policy, VisualRenderer renderer, DiagramPreview preview, IReadOnlyList\<string\> formats, HelpCatalog catalog)

Builds the tool through `GuardedToolFactory` from one of two delegates, one per preview mode, each
with its own tool description and its own `save_as` parameter description. A parameter description
is a compile-time attribute, so separate delegates are what keep one declaration from calling
`save_as` optional in one place and required in another. Refuses null arguments, an empty language
list and an undefined preview mode as programming errors. Internal: the pack supplies the renderer,
the languages and the catalog.

##### The render, in order

1. A language that is not offered (matched case-insensitively) is refused as `InvalidRequest`
   listing the offered ones; a blank source and a width below 1 are refused as `InvalidRequest`.
2. The source's UTF-8 length must not exceed the lower of the policy's `MaxReadBytes` and the
   largest content the library accepts (16 MiB), or it is refused as `ResourceTooLarge` naming the
   ceiling.
3. In text preview a missing `save_as` is refused as `InvalidRequest`. A named `save_as` is resolved
   by `RenderDestination.TryResolve` — `.png`, the write decision alone, not a directory, an existing
   parent — and its refusal returned unchanged. All of this happens before anything is drawn.
4. `VisualRenderer` draws the UTF-8 bytes as `DiagramFormats.FileName(format)` (for example
   `diagram.mmd`), with `VisualAccess.InlineBaseFolder(policy)` as the base for relative references
   (the working directory when readable, otherwise none, so they fail closed),
   `VisualAccess.ToFileAccess(policy)`, the policy's limits, the requested width, and a description
   of the picture's features and colors asked for in text preview.
5. A failure is returned as `ToolResult.Text` of the text `RenderErrorShaper` composes with the
   source in hand and a `HelpReference` to `diagram_help`, the language's help tag and the pack's
   catalog, so the report names `<tag>/diagnostics` only when that topic is served. A failure is text
   rather than a denial because the model can fix what it wrote.
6. A picture larger than the policy's `MaxBinaryBytes` is refused as `ResourceTooLarge`: it is
   content returned or written, and the guarded construction path does not enforce that ceiling.
7. A named destination is written by `RenderDestination.TryWriteAsync`, which replaces an earlier
   file atomically; a write the file system refused is `InvalidRequest` stating the picture could
   not be saved.
8. Image preview returns `ToolResult.Image(png, "image/png", caption)`, the caption being
   `Rendered <format> as PNG, WxH pixels[, saved to <path>].[ Notes: …]`. Text preview returns
   `ToolResult.Structured` of `savedTo`, `format`, `width`, `height`, `notes`, `features` (at most
   50, each `kind id "text" from->to`), `featuresTruncated` and `dominantColors` (at most 4, each
   `#rrggbb nn%`). Host paths are scrubbed from the caption and notes.

#### Error Handling

`ArgumentNullException`, `ArgumentException` and `ArgumentOutOfRangeException` from `Create`. Every
condition a model can provoke is a returned result; only the caller's own cancellation is thrown.

#### Dependencies

`PathPolicy`, `GuardedToolFactory` and `ToolResult` from AgentKitCore; `ImageMediaTypes.Png`; the
Rendering subsystem's `VisualRenderer`, `VisualAccess`, `RenderErrorShaper` and `RenderDestination`;
`DiagramHelpTool` and `DiagramFormats` in this subsystem; `HelpCatalog` from the CanvasNet Viewer.

#### Callers

`DiagramPack.CreateTools` creates the tool; a model calls it.

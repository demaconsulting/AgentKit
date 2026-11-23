## Diagram

![AgentKit Tools Visual Diagram Structure](DiagramView.svg)

The Diagram subsystem is the diagram tool family: it lets an agent draw diagrams from source it
writes, check what it drew, fix it and save the picture, with the drawing help for each language
served a topic at a time.

### Overview

An agent writing documentation can produce the figures it can write as source — Mermaid and
PlantUML diagrams, SVG figures, Vega-Lite charts and OpenSCAD parts. Writing the source is only
half of drawing: the agent must also see what the source produced, learn why a source did not draw,
and keep the picture where the document can reference it. The family provides that loop:

1. **Read the help.** `diagram_help` returns the index of topics, then the card of the diagram type
   the agent is drawing, with its example sources.
2. **Draw.** `diagram_render` draws source given inline and returns the picture to a model that can
   see, or a description of it in words to a model that cannot.
3. **Fix.** A source that does not draw is reported with its location, the offending line with a
   caret, and the `diagram_help` topic of the language's diagnostics.
4. **Save.** `save_as` writes the picture as a PNG under the write grant, replacing the earlier
   rendering of the same name, so the loop can save on every attempt.

The subsystem contains four units and one supporting type:

| Unit | Responsibility |
| --- | --- |
| `DiagramPack` | The pack: prefix, preview mode, tool creation, instructions, warm-up |
| `DiagramPackOptions` | The rendering bounds and the offered languages, validated and copied |
| `DiagramHelpTool` | `diagram_help`: the filtered help catalog, a topic at a time |
| `DiagramRenderTool` | `diagram_render`: inline source to a picture or a description |

`DiagramPreview` is the public enum of the two preview modes; it is part of the `DiagramPack` unit,
which it configures. `DiagramFormats` is an internal supporting table rather than a unit: it lists
the known languages with the file name each is drawn as and the help tag its cards carry, so that
the options, the renderer, the warm-up and the help topic a failure names cannot disagree about
which languages exist. It has no behavior a requirement would promise beyond what the units promise
observably, and is covered by this subsystem's review-set.

| Language | File name drawn as | Help tag |
| --- | --- | --- |
| `mermaid` | `diagram.mmd` | `mermaid` |
| `plantuml` | `diagram.puml` | `plantuml` |
| `svg` | `diagram.svg` | `svg` |
| `vega-lite` | `diagram.vl.json` | `vega-lite` |
| `openscad` | `diagram.scad` | `scad` |

### Interfaces

| Interface | Direction | Format | Constraints |
| --- | --- | --- | --- |
| `DiagramPack` | Outbound | AgentKitCore pack contract | Prefix `diagram`; Vision for image preview |
| `DiagramPack.For(host, options)` | Inbound | Factory | Image preview iff Vision |
| `DiagramPack.Instructions` | Outbound | `string` | Offered to the host, never injected |
| `DiagramPack.WarmUpAsync` | Inbound | `Task` | Optional; host-initiated only |
| `DiagramPackOptions` | Inbound | Public record | Each value checked where set |
| `diagram_help(topic?)` | Model-facing | Text or `TargetNotFound` | No path, no file |
| `diagram_render(format, source, save_as?, width?)` | Model-facing | Picture, description or text | Inline source |
| Rendering subsystem | Inbound | Internal | Renderer, access, shaper, destination |
| CanvasNet Viewer help catalog | Inbound | OTS library | Filtered by tag |

### Design

**Source is inline, never a path.** A model drawing a diagram is writing its source anyway, and
taking it inline lets one call draw and the next call redraw without a file write in between. A
source file already in the workspace is drawn by `image_view`. Inline source has no folder of its
own, so its relative references resolve against the working directory when the policy permits
reading it, and against no folder otherwise; every referenced file is judged by the file-access
policy `VisualAccess` maps from the access policy.

**The preview mode follows the host.** Image preview returns the picture and requires the Vision
capability; text preview returns a description — the size, the notes, the features the library
reports (nodes, edges, labels, marks) and the dominant colors — and requires nothing, so a model
that cannot see can still draw a figure for a document. `DiagramPack.For` chooses the mode from the
same capabilities the application declares on its builder. The mode is a constructor argument
rather than an option, so `For` never overrides a value its caller set.

**Publication follows the policy only where it must.** `diagram_help` touches no file. Image-preview
`diagram_render` returns its picture without writing, so its optional `save_as` is answered by the
ordinary refusal under a read-only policy. Text-preview `diagram_render` must save, so under a
policy that permits no writing anywhere its only outcome would be a refusal, and the pack withholds
it — the rule every AgentKit pack applies.

**The help is the library's own, filtered once per pack.** Every card is served unless one of its
tags is excluded: a language the pack does not offer, a format the family never draws from source
(`pdf`, `pptx`, `vsdx`, `raster`), or a card written for the application (`hosting`,
`file-access`, `limits`). A card about two languages is hidden when either is not offered — the
library's filter is by tag, and a partly relevant card is a small loss beside a card naming a
language the render tool refuses. A shaped failure names `<tag>/diagnostics` only when the pack's
catalog serves it.

**One gate per pack.** The pack creates one `VisualRenderer`, whose render gate bounds how many
drawings run at once for every composition the pack is added to.

**Instructions are offered, never injected.** `DiagramPack.Instructions` names the offered
languages and the draw, look, fix loop for the pack's preview mode, in about eighty tokens. An
application places it in its agent's instructions if it wants to; the pack never does.

### Known Limitations

The pinned rendering library's known behaviors are recorded in *AgentKitToolsVisual System Design*,
*Known Limitations*. Those a model meets through `diagram_render`:

- Malformed Vega-Lite JSON is reported as nesting too deep
  ([CanvasNet #163](https://github.com/demaconsulting/CanvasNet/issues/163)); the failure is still
  classified as a Vega-Lite error with its location.
- A bad PlantUML line can make the diagram be detected as another kind
  ([CanvasNet #164](https://github.com/demaconsulting/CanvasNet/issues/164)); the location still
  points at the line to fix.
- A Vega-Lite encoding without a type draws without a diagnostic
  ([CanvasNet #165](https://github.com/demaconsulting/CanvasNet/issues/165)).
- Format failures carry no stable code or structured location
  ([CanvasNet #166](https://github.com/demaconsulting/CanvasNet/issues/166)), which is why the
  Rendering subsystem shapes them.
- The Vega-Lite, Mermaid and PlantUML budgets cannot be set through the render options
  ([CanvasNet #167](https://github.com/demaconsulting/CanvasNet/issues/167)); the pixel budget, the
  binary ceiling, the largest side and the timeout still bound every drawing.
- An unresolvable relative reference — for example in inline source when the working directory may
  not be read — is left out of the picture with a note rather than failing the render; only a
  reference the file-access policy refused fails it.

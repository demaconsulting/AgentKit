# Images, Documents and Diagrams

The image and diagram families ship in their own package, `DemaConsulting.AgentKit.Tools.Visual`, so
an application that attaches neither takes on no imaging dependency. This chapter covers what the two
packs offer, how to choose between their tools, the bounds every drawing runs within, and how to move
an application from the earlier `ImagePack` in `DemaConsulting.AgentKit.Tools`.

```bash
dotnet add package DemaConsulting.AgentKit.Tools.Visual
```

## Choosing a Pack

| Pack | Tools | Requires |
| --- | --- | --- |
| `ImagePack` | `image_read`, `image_crop`, `image_auto_crop`, `image_view` | `Vision` |
| `DiagramPack` | `diagram_help`, `diagram_render` | `Vision` in image preview, nothing in text preview |

`ImagePack` lives in the `DemaConsulting.AgentKit.Tools.Visual.Image` namespace and `DiagramPack` in
`DemaConsulting.AgentKit.Tools.Visual.Diagram`.

Attach `ImagePack` when the agent should look at files the user already has — photographs,
screenshots, scanned pages, slide decks, drawings. Attach `DiagramPack` when the agent should draw
figures of its own from source it writes. The two are independent: either can be attached without
the other, and both draw through the same rendering library under the same confinement.

```csharp
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Diagram;
using DemaConsulting.AgentKit.Tools.Visual.Image;

var workspace = Path.GetFullPath("workspace");
var policy = new PathPolicy(workspace, [PathRule.ReadWrite(workspace)]);

var host = HostCapabilities.Vision;
var diagrams = DiagramPack.For(host);

var tools = new ToolPackBuilder(policy)
    .WithHostCapabilities(host)
    .Add(new ImagePack())
    .Add(diagrams)
    .Build();
```

## Reading or Drawing a File

The image family offers two ways to show the model a file, and they answer different questions.

- **`image_read` hands the file's bytes to the provider unchanged.** It accepts PNG, JPEG, GIF, WebP
  and PDF, the formats providers decode themselves, and states the image's pixel dimensions — the
  coordinate space `image_crop` takes its region in. Use it when the model must see the file exactly
  as it is, or when a region will be cut from it next.
- **`image_view` draws the file and returns the drawing as a PNG.** It accepts SVG, PDF pages, PPTX
  slides, VSDX pages, Mermaid (`.mmd`, `.mermaid`), PlantUML (`.puml`, `.plantuml`, `.pu`),
  Vega-Lite (`.vl`, `.vl.json`) and OpenSCAD (`.scad`) sources, and raster images. It takes an
  optional `page`, counted from 1, and an optional `width` in pixels, and its caption names the
  format, the page of the page count, the size and anything the drawing left out. Use it for content
  a provider cannot decode, and for a single page of a long document.

Each tool's refusal points at the other where that helps. `image_read` on an `.svg` file is refused
with `UnsupportedMediaType` naming `image_view`, because an SVG is vector content to be drawn rather
than a raster image to be passed through. `image_crop` and `image_auto_crop` on an `.svg` file are
refused naming no tool, because drawing it returns the whole picture, not the region asked for.

## Preview Modes

`diagram_render` draws diagram source the model supplies inline — never a path — and shows the model
what it drew in one of two ways, chosen when the pack is created:

- **`DiagramPreview.Image`** returns the picture for the model to look at. The pack then requires
  the `Vision` host capability, and `save_as` is optional.
- **`DiagramPreview.Text`** saves the picture and returns a description of it in words: its size,
  up to fifty of the labels, shapes and edges it contains (with a flag saying when there were more),
  and up to four dominant colors with their share of the picture. The pack requires no capability,
  so a model that cannot see can still draw, and `save_as` is required. Under a policy that permits
  no writing anywhere, `diagram_render` is not published in this mode, because its only outcome
  would be a refusal; `diagram_help` still is.

`DiagramPack.For(host)` chooses image preview for a host that declares `Vision` and text preview
otherwise. Pass it the same capabilities declared on the `ToolPackBuilder`, so the pack is never
withheld for a capability it does not need. The mode is a constructor argument,
`new DiagramPack(DiagramPreview.Text)`, rather than an option, so `For` never overrides a value the
caller set.

## Choosing the Languages

`DiagramPackOptions.Formats` names the languages the pack offers: by default `mermaid`, `plantuml`,
`svg`, `vega-lite` and `openscad`. Names are matched case-insensitively. An empty list, an unknown
name or a name listed twice is refused with an `ArgumentException` where the list is set, and the
list is copied, so changing the array afterwards changes nothing.

```csharp
var diagrams = new DiagramPack(
    DiagramPreview.Image,
    new DiagramPackOptions { Formats = ["mermaid", "plantuml"] });
```

The configured list reaches the model in three places: the `diagram_render` description, the pack's
`Instructions`, and the refusal of a language that is not offered, which lists the ones that are.

## Drawing Help

`diagram_help` serves the rendering library's drawing help a topic at a time. With no topic it
returns an index of the root topics and their summaries; with a topic it returns that topic's card,
the complete source of each of its examples and the identifiers of its child topics. An unknown topic
is refused with `TargetNotFound` and up to eight topic identifiers that contain the text asked for.

The help is filtered to what the model can use:

- Cards for a language the pack does not offer are hidden. A card tagged with two languages is
  hidden when either is not offered.
- Cards about document formats `diagram_render` does not draw — PDF, PPTX, VSDX and raster images —
  are hidden.
- Cards written for the application rather than the model — hosting options, file-access
  configuration and resource limits — are never served.

A drawing that fails names the help topic for its language's diagnostics, for example
`mermaid/diagnostics`, only when that topic exists in the filtered help.

## Saving a Picture

`diagram_render` writes the picture when `save_as` names a destination:

- The destination must end in `.png`, matched case-insensitively.
- It is judged by the policy's write decision, independently of anything read, and refused with the
  ordinary refusal naming the writable locations when no read-write grant covers it.
- Its folder must already exist; no folder is created.
- These checks run before anything is drawn, so a refused destination costs no rendering time.
- **An existing file of the same name is replaced**, so a model can save on every attempt while it
  fixes a drawing. The picture is written to a temporary file in the same folder and then moved over
  the destination, so a reader sees either the earlier picture or the new one, never a partial file;
  the temporary file is removed if anything fails.

The caption, or in text preview the `savedTo` field, names the saved file in the policy's path
dialect. This differs from `image_crop` and `image_auto_crop`, whose `destination` never replaces an
existing file: a cropped figure is a one-off product, while a diagram is redrawn until it is right.

## Referenced Files

A document or diagram can reference other files — an embedded picture, an included PlantUML file, a
Vega-Lite data file. Both drawing tools keep those references inside the application's grants:

- **The input file is checked like any other read**, by the policy, before anything is learned about
  it, and a refusal is the policy's own.
- **Every referenced file must be readable under the policy**, deny patterns included. A reference
  that escapes by `..`, an absolute path, a `file:` URI, a symbolic link or a junction is refused,
  and the refusal states that the file lies outside the locations the agent may read without
  disclosing where on the host it is.
- **Nothing is fetched from the network.** An `http:` or `https:` reference is never followed.
- **A relative reference resolves against the referencing file's folder.** For inline source, which
  has no folder, it resolves against the policy's working directory when the agent may read it.
  When the working directory is not readable there is no folder to resolve against, so the picture
  is drawn without the referenced content and the result carries an `svg-image-unresolved` note
  rather than failing.
- **A refused reference fails the drawing** with `file-access-denied`, rather than returning a
  picture with a hole in it.
- On Linux the real location of a referenced file is read from `/proc`; where `/proc` is not
  mounted, every reference is refused.

## Failures a Model Can Fix

A source or file that cannot be drawn is not refused; it is reported as text the model can act on,
in this shape:

```text
Render failed (<code>) at line <line>, column <column>: <message>
  <line> | <the offending line>
         |      ^
Hint: <advice the model can act on>
Notes: <anything else the drawing reported>
Help: diagram_help topic '<language>/diagnostics'.
```

The first line carries an error code — `file-access-denied`, `budget-exceeded`,
`unsupported-feature`, `corrupt-input`, `render-timeout`, `empty-render`, a language's own
`<language>-error`, or `operation-failed` — and the line and column where the library reported them.
The excerpt shows the offending line with a caret under the column, hints written for a command-line
user are rewritten for a model, and any notes the drawing produced are listed. A drawing that came
out empty, or nearly so, is reported as `empty-render` rather than shown; a raster image is shown
as it is, blank or not. Path, permission and size
problems remain ordinary refusals.

## Limits

Every drawing is bounded by the policy's `ToolLimits` and by the pack's `VisualOptions`. The
`ToolLimits` values keep their usual meaning:

- `MaxImagePixels` is the pixel budget the rendering library decodes within.
- `MaxBinaryBytes` bounds the input file `image_view` reads, which is never more than 16 MiB, and
  the PNG either tool returns or saves.
- `MaxReadBytes` bounds the length of inline source `diagram_render` accepts.

`VisualOptions` carries the bounds that are specific to drawing:

| Option | Default | Range | Meaning |
| --- | --- | --- | --- |
| `RenderTimeout` | 30 seconds | above 0, at most 10 minutes | Time for one drawing, waiting included |
| `MaxRenderSide` | 2048 | 16 to 8192 | Largest width or height, in pixels |
| `MinPreviewSide` | 768 | 1 to 8192, at most `MaxRenderSide` | Side a small vector drawing is enlarged to |
| `ScadMaxTriangles` | 500,000 | at least 1 | Triangle limit for an OpenSCAD model |
| `MaxConcurrentRenders` | 1 | at least 1 | Drawings one pack instance runs at a time |

The render timeout includes any wait for another drawing to finish, and a small vector drawing is
enlarged so its labels can be read.

A value outside its range is refused with an `ArgumentOutOfRangeException` where it is set. The one
relation between two values, `MinPreviewSide` not exceeding `MaxRenderSide`, is checked when a pack
is created and refused with an `ArgumentException`. Raster images are never enlarged.

```csharp
var options = new VisualOptions { RenderTimeout = TimeSpan.FromSeconds(10), MaxRenderSide = 1600 };

var images = new ImagePack(options);
var diagrams = DiagramPack.For(host, new DiagramPackOptions { Visual = options });
```

The concurrency bound is held per pack **instance**, and an instance shares it with every
composition it is added to, so one instance serving many agents still runs at most
`MaxConcurrentRenders` drawings at a time. That gate is the packs' only state, and both packs are
safe for concurrent use.

## Warming Up

The first drawing of each language in a process pays for loading and initializing its library, which
can take a second or more. `ImagePack.WarmUpAsync` and `DiagramPack.WarmUpAsync` draw a small sample
of each format once, through the pack's own gate and timeout, so that cost is paid before a model is
waiting:

```csharp
await diagrams.WarmUpAsync(cancellationToken);
```

Warming up is optional and never called by the packs themselves. The samples reference no file and
are discarded, and a sample that fails is ignored: the first real drawing that meets the same failure
reports it.

## Instructions for the Agent

`DiagramPack.Instructions` is a short paragraph, about eighty tokens, describing the draw, look, fix
loop and naming the offered languages; in text preview it also says that `save_as` is required and
that a description comes back instead of the picture. **The pack never adds it to any
instructions.** An application that wants it appends it to its own instructions explicitly:

```csharp
var instructions = "You write documentation for this project. " + diagrams.Instructions;
```

Read the paragraph from the same pack instance that is added to the builder, so it describes the
preview mode the tools were composed in. The document assistant sample does exactly this, followed
by a sentence of its own about where to keep the source and the picture; see *Sample: Document
Assistant*.

## Delivering Pictures to the Model

Both packs return pictures as ordinary image tool results. On an `IChatClient` provider that drops
an image in a tool result — Ollama through OllamaSharp is one — the agent factory's image-promoting
decorator delivers it; see *Delivering Tool Images to the Model*. The GitHub Copilot runtime
delivers tool images directly. Text preview needs no image delivery at all.

## Known Limitations

`DemaConsulting.AgentKit.Tools.Visual` draws through the CanvasNet libraries, currently a prerelease
of version 0.2.0; a stable release of the package waits on a stable CanvasNet 0.2.0. The following
behaviors of the library are known and tracked upstream:

- Truncated or otherwise malformed Vega-Lite JSON is reported as nesting deeper than the library's
  limit rather than as a syntax error
  ([CanvasNet #163](https://github.com/demaconsulting/CanvasNet/issues/163)).
- One bad message line in a PlantUML sequence diagram can make the source be read as a deployment
  diagram, so the reported error points at a valid line
  ([CanvasNet #164](https://github.com/demaconsulting/CanvasNet/issues/164)).
- A Vega-Lite encoding field without a `type` draws a very narrow chart with no diagnostic. In image
  preview the model sees the narrow picture; in text preview the reported size shows it
  ([CanvasNet #165](https://github.com/demaconsulting/CanvasNet/issues/165)).
- Source errors carry no stable error code or structured line and column. AgentKit classifies them
  by exception type and reads the location from the message, so an occasional error is reported as
  `operation-failed` or without a location
  ([CanvasNet #166](https://github.com/demaconsulting/CanvasNet/issues/166)).
- Only OpenSCAD has a settable size budget (`ScadMaxTriangles`); Vega-Lite, Mermaid and PlantUML
  drawings are bounded by the render timeout and the pixel budget
  ([CanvasNet #167](https://github.com/demaconsulting/CanvasNet/issues/167)).
- The library draws Mermaid, PlantUML and OpenSCAD on a transparent background by default, which
  hides dark labels from some vision models. AgentKit always draws on opaque white, so returned and
  saved pictures are unaffected
  ([CanvasNet #168](https://github.com/demaconsulting/CanvasNet/issues/168)).

## Migrating from `DemaConsulting.AgentKit.Tools`

`ImagePack` and the image tools moved from `DemaConsulting.AgentKit.Tools` to the new
`DemaConsulting.AgentKit.Tools.Visual` package. **This is a breaking change** for an application
that attaches the image family:

1. **Add the package.** `dotnet add package DemaConsulting.AgentKit.Tools.Visual`.
   `DemaConsulting.AgentKit.Tools` no longer contains the image family and no longer brings the
   CanvasNet dependency.
2. **Change the namespace.** `using DemaConsulting.AgentKit.Tools.Image;` becomes
   `using DemaConsulting.AgentKit.Tools.Visual.Image;`. The type names — `ImagePack`,
   `ImageReadTool`, `ImageCropTool`, `ImageAutoCropTool`, `ImageMediaTypes` — are unchanged.
3. **Nothing else needs to change in code.** `new ImagePack()` still compiles; the constructor now
   takes optional `VisualOptions`. The tool names `image_read`, `image_crop` and `image_auto_crop`
   and their parameters are unchanged.

Two behaviors change and may be visible to a model or a test:

- The pack now also publishes **`image_view`**, so a composition with the image family offers one
  more tool than before.
- **`image_read` on an `.svg` file is refused naming `image_view`** instead of `text_file_read`, and
  `image_crop` and `image_auto_crop` refuse an `.svg` file naming no tool. An application whose
  instructions or tests mention the old redirection should be updated. `text_file_read` still
  redirects a raster image or PDF to `image_read`.

The samples changed accordingly: the document assistant references the new package, imports
`DemaConsulting.AgentKit.Tools.Visual.Image`, adds the diagram pack, and accepts `--no-diagrams`.

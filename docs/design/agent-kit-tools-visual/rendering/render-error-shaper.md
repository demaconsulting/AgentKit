### RenderErrorShaper

![AgentKit Tools Visual Rendering Structure](RenderingView.svg)

The `RenderErrorShaper` class turns a failed render into text a model can act on.

#### Purpose

A model fixes what it drew by reading why the render failed. The rendering library raises a format's
syntax error as a general "could not be drawn" exception wrapping the format library's own
exception, with no stable code and the location only inside the message
([CanvasNet #166](https://github.com/demaconsulting/CanvasNet/issues/166)); its message is framed
for a developer, may name a host path, and its hints are written for a host ("add the folder to the
allowed roots") or a command line ("without --strict"). Shaping the failure is what makes the draw,
read the error, fix, draw again loop converge on the first retry.

#### Data Model

The class is static and stateless. It defines two codes of its own beside the library's
`ErrorCodes`: `render-timeout` and `empty-render`. Two supporting records carry what a caller knows:

- **ShapeContext** — `Policy`, `InputFolder`, `SourceText`, `SourceFileName` and `Help`: the host
  folders to scrub, the source the caret is drawn under, and the help tool, if any.
- **HelpReference** — `ToolName`, `Tag` and `TopicExists`: a help tool a failure may point to, and
  whether it serves a topic.

#### Key Methods

##### Classify(Exception exception)

**Algorithm:** collects the exception and its inner exceptions, outermost first, and decides the
code **by type, never by message text**, most specific first:

| In the chain | Code |
| --- | --- |
| `FileReferenceException`, refused (denied, network, invalid path, no real path) | `file-access-denied` |
| `FileReferenceException`, too large or too many files | `budget-exceeded` |
| `FileReferenceException`, not a regular file or cannot read | `input-unreadable` |
| `ViewerException` with a code other than `operation-failed` | its `Code` |
| `ImageSourceBudgetException`, `RenderBudgetExceededException` | `budget-exceeded` |
| `UnsupportedImageFeatureException`, any `*UnsupportedFeatureException` | `unsupported-feature` |
| a Mermaid, PlantUML, Vega-Lite or OpenSCAD library exception | `<format>-error` |
| `InvalidDataException`, `XmlException` | `corrupt-input` |
| anything else | `operation-failed` |

The format codes are `mermaid-error`, `plantuml-error`, `vega-lite-error` and `scad-error`.

Message text is not a stable contract and is sometimes misleading: malformed Vega-Lite JSON is
reported as nesting deeper than a limit
([CanvasNet #163](https://github.com/demaconsulting/CanvasNet/issues/163)), which a classifier
reading words would report as a budget problem.

The message is cleaned by `CleanMessage`. The location is read from the format library's typed
exception where there is one — Mermaid, PlantUML, Vega-Lite (with its JSON pointer) and OpenSCAD
exceptions carry it — and otherwise from the message prefix. The hint is the `ViewerException`'s or
the `FileReferenceException`'s, if any.

##### CleanMessage(string message, out file, out line, out column, out pointer, out detail)

Removes, in order, the framing `The image '<name>' could not be drawn by the '<source>' source: `,
the uniform location prefix `file:line:column[ /pointer]: ` (the file may be drive-qualified), and a
leading detail code such as `mermaid-syntax-error: `, reporting each. When there is no prefix, an
XML reader's `(line N, column M)` inside the text is reported as the location. Every expression has
a match timeout.

##### Shape(RenderFailure failure, ShapeContext context)

Composes, line by line:

```text
Render failed (<code>[/<detail>]) at line L, column C[ (<pointer>)]: <message>
  L | <the source line>
    |     ^
Hint: <advice for the model>
Notes: <note>; <note>
Help: <help tool> topic '<tag>/diagnostics'.
```

- The **excerpt** is shown only when the source text is in hand and the failure's file, if named, is
  the source's own — a failure inside an included file names that file instead. A line longer than
  160 characters is shown as a window around the column; tabs become spaces so the caret aligns.
- The **hint** is this unit's own for the codes a model can fix in only one way
  (`file-access-denied`, `budget-exceeded`, `size-limit`, `unsupported-feature`, `render-timeout`,
  `empty-render`); otherwise the library's hint, unless it contains `--`, "allowed roots", "strict"
  or names the render options, in which case none.
- The **help line** appears only when the context names a help tool that serves the topic
  `<tag>/diagnostics`; a tool without a help catalog passes none, and pointing a model at a topic the
  help tool would refuse is worse than naming none.
- Every message and note passes through `Rewrite`, and the whole text through `Scrub`.

##### Rewrite(string text) and Scrub(string text, PathPolicy policy, string? inputFolder)

`Rewrite` replaces "by the file-access policy '<name>'" with "by the access policy", "the allowed
roots" with "the locations the agent may read", and removes a trailing "Hint: …" the library
appended. `Scrub` replaces, longest first and in both separator spellings, the working directory,
every grant root and the input's folder with their form relative to the working directory (`.` for
the working directory itself) or `<outside>` when they lie outside it. A path the model wrote itself
— an absolute reference in its own document — may still appear, because the model supplied it.

#### Error Handling

`ArgumentNullException` for a null argument. Nothing else can fail; the expressions are bounded by a
timeout.

#### Dependencies

`PathPolicy` from AgentKitCore for the host folders. From the CanvasNet libraries: `ErrorCodes`,
`ViewerException`, `FileReferenceException`, `FileReferenceFailureKind`, `ImageSourceBudgetException`,
`UnsupportedImageFeatureException`, `RenderBudgetExceededException`, and the typed exceptions of the
Mermaid, PlantUML, Vega-Lite and OpenSCAD libraries.

#### Callers

`VisualRenderer` classifies an exception and cleans a note's message; `ImageViewTool` shapes a failure
and scrubs a caption.

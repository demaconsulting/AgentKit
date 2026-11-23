### DiagramHelpTool

![AgentKit Tools Visual Diagram Structure](DiagramView.svg)

The `DiagramHelpTool` class publishes the `diagram_help` tool: the drawing help for the offered
languages, one topic at a time.

#### Purpose

The rendering library carries a help catalog of cards — one per language, per diagram type and per
subject such as style or composition — written to be read by a model before drawing. All of it at
once is about seventy thousand tokens. Serving the index first, then one card, lets a model read
only what the diagram in hand needs. Serving the library's own catalog rather than a copy keeps the
help in step with what the library draws.

#### Data Model

The class is static. A constructed tool captures only the pack's filtered `HelpCatalog`, which is
immutable.

| Member | Type | Value |
| --- | --- | --- |
| `ToolName` (constant) | `string` | `diagram_help` |
| `MaxSuggestions` (internal) | `int` | 8 |
| Parameter | `topic` | optional |

#### Key Methods

##### CatalogFor(IReadOnlyList\<string\> formats)

Returns `HelpCatalog.Default.Filter(include: [], exclude: …)`, every card included unless one of its
tags is excluded. The excluded tags are the help tags of the known languages not offered, the
document formats the family never draws from source (`pdf`, `pptx`, `vsdx`, `raster`), and the tags
of cards written for the application (`hosting`, `file-access`, `limits`). A hidden card is not found
by its id or an alias and is not listed among another card's topics.

##### Create(HelpCatalog catalog, IReadOnlyList\<string\> formats)

Builds the tool through `GuardedToolFactory`, with a description naming the offered languages.
Internal: the pack filters the catalog and supplies it.

##### Help(topic)

- **No topic**: `Diagram help topics for <languages>:`, then one line per visible top-level topic —
  its id padded to a column, then its summary — then a line inviting a call with a topic id.
- **A topic**: `catalog.Find(topic.Trim())`, which matches ids and aliases case-insensitively. The
  answer is the library's `RenderText` of the card — its summary, body, the topics below it and the
  topics beside it — followed, when the card has examples, by `## Example sources` and each example's
  name, file name, description and full content in a code fence longer than any run of backticks in
  the content. The library's card names its examples without carrying them.
- **An unknown topic**: `Denied(TargetNotFound)` with `No help topic '<topic>'.` (the echo limited
  to 80 characters) and either `Did you mean: …?` listing up to eight visible card ids containing
  the topic or its last segment, or a pointer to the index.

#### Error Handling

`ArgumentNullException` from `CatalogFor` and `Create`. Every condition a model can provoke is a
returned result.

#### Dependencies

`GuardedToolFactory` and `ToolResult` from AgentKitCore; `HelpCatalog` from the CanvasNet Viewer;
`DiagramFormats` for the help tags.

#### Callers

`DiagramPack` builds the catalog and the tool; `DiagramRenderTool` asks the same catalog whether a
diagnostics topic is served before naming it; a model calls the tool.

## Diagram Subsystem Verification Design

This document describes the subsystem-level verification strategy for the Diagram subsystem.

### Verification Approach

The subsystem is verified by scenarios that compose the family through a `ToolPackBuilder` and use
both of its tools the way a model does — read the help, draw, read the topic a failure names, fix and
draw again — so the hand-offs the unit tests cannot see are exercised: the pack's preview mode
reaching the render tool, and a failure's help topic being one the help tool serves. Every scenario
draws through the real rendering library and reads its real help catalog.

Subsystem tests reside in `Diagram/DiagramTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project, drawing the sources in
`Rendering/RenderSamples.cs`.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: The CanvasNet Viewer and its help catalog, in process; no external services
- **File system**: A temporary tree for the scenario that saves a picture; the others touch no file
- **Isolation**: Each test composes its own pack and policy; no state is shared

### Acceptance Criteria

A subsystem test run passes when all four scenarios below pass without error or exception beyond
those explicitly asserted. A family not published as help then render, a host without vision not
receiving the text preview or receiving image preview, a failure naming a topic the help tool does
not serve, a fixed source not drawn, or help that does not narrow from the index to a diagram type
each constitute a failure.

### Test Scenarios

#### AgentKitToolsVisual-Diagram-FamilyComposition: The Family Composed Through a Builder Publishes Both Tools

**Test**: `Diagram_Family_ComposedThroughBuilder_PublishesBothTools`

Composes the pack chosen for a vision host on a vision builder and asserts the list is exactly
`diagram_help`, `diagram_render`.

#### AgentKitToolsVisual-Diagram-VisionOrTextPreview: A Host Without Vision Receives the Text Preview

**Test**: `Diagram_Family_HostWithoutVision_ReceivesTheTextPreview`

On a builder declaring nothing over a writable temporary tree, the pack chosen for that host
publishes both tools, and its render tool saves a Mermaid flowchart and returns a description naming
the node label `Draft`; an image-preview pack on the same builder contributes no tools.

#### AgentKitToolsVisual-Diagram-DrawFixRedraw: A Broken Source Names a Served Topic and the Fix Draws

**Test**: `Diagram_DrawFixRedraw_BrokenSource_NamesAServedTopicAndTheFixDraws`

Draws a Mermaid source with a malformed arrow, reads the help topic the report names — asserted to be
`mermaid/diagnostics` — through `diagram_help` and finds `mermaid-syntax-error` in it, then draws the
corrected source and asserts a picture is returned.

#### AgentKitToolsVisual-Diagram-ProgressiveHelp: The Help Narrows From the Index to a Diagram Type

**Test**: `Diagram_ProgressiveHelp_IndexThenTopic_NarrowsToTheDiagramType`

Reads the index, the `plantuml` card and the `plantuml/sequence` card, and asserts the index names
the language, the language's card lists the sequence topic, the sequence card carries an example
source, and the index is shorter than the two cards together.

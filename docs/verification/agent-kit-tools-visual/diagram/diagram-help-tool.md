### DiagramHelpTool Unit Verification Design

This document describes the unit-level verification strategy for the `DiagramHelpTool` class.

#### Verification Approach

Every scenario invokes the constructed tool exactly as a runtime would, over the rendering library's
real help catalog filtered as a pack filters it, because what the tool promises is the text a model
reads. Assertions are on the ids, headings and fences in that text rather than on whole cards, which
the library's authors may reword.

Unit tests reside in `Diagram/DiagramHelpToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: The CanvasNet Viewer's help catalog, in process
- **Isolation**: Each test builds its own tool

#### Acceptance Criteria

A unit test run passes when all seven scenarios below pass without error or exception beyond those
explicitly asserted. A wrong name, a description not naming the languages, an index listing nested
or host-oriented topics, a card without its example source, an alias not found, a hidden card served
or listed, an unknown topic without suggestions or pointer, or help served for a language not
offered each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Diagram-HelpTool-ToolName: The Name Is the Family-Qualified Name

**Test**: `DiagramHelpTool_ToolName_Constant_IsTheFamilyQualifiedName`

The constant is `diagram_help` and carries the family prefix.

##### AgentKitToolsVisual-Diagram-HelpTool-GuardedConstruction: The Tool Carries Its Name and the Languages

**Test**: `DiagramHelpTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

The tool built for Mermaid and SVG carries the name and a description naming `mermaid, svg`; a null
catalog or list is refused.

##### AgentKitToolsVisual-Diagram-HelpTool-Index: No Topic Returns the Index

**Test**: `DiagramHelpTool_Help_NoTopic_ReturnsTheIndexOfTopLevelTopics`

The index has a `mermaid` line with its summary and a `scad` line, no nested topic, no `files`,
`limits` or `pdf` line, a pointer to asking with a topic id, and fewer than 4,000 characters.

##### AgentKitToolsVisual-Diagram-HelpTool-TopicCard: A Topic Returns Its Card With Its Example Sources

**Test**: `DiagramHelpTool_Help_Topic_ReturnsTheCardWithItsExampleSources`

`mermaid/flowchart` returns the card headed `# Flowcharts` followed by `## Example sources` and a
`mermaid` fence holding a flowchart; `Mermaid/Graph` (an alias, in other case, with spaces) returns
the same text. Also evidence for `AgentKit-OTS-CanvasNet-HelpCatalog`.

##### AgentKitToolsVisual-Diagram-HelpTool-HostCardsHidden: Host-Oriented Cards Are Hidden

**Test**: `DiagramHelpTool_Help_HostOrientedCards_AreHidden`

The `mermaid` card lists `mermaid/flowchart` but not `mermaid/options`; `mermaid/options` and
`files` are refused as `TargetNotFound`.

##### AgentKitToolsVisual-Diagram-HelpTool-UnknownTopicSuggests: An Unknown Topic Is Refused With Suggestions

**Test**: `DiagramHelpTool_Help_UnknownTopic_IsRefusedWithSuggestions`

`flowchart` is refused as `TargetNotFound` suggesting `mermaid/flowchart`; `zzzz` is refused pointing
to the index.

##### AgentKitToolsVisual-Diagram-HelpTool-OfferedLanguagesOnly: An Unoffered Language Is Not Served

**Test**: `DiagramHelpTool_Help_UnofferedLanguage_IsNotServed`

For a pack offering Mermaid alone, the index is headed `Diagram help topics for mermaid:` and names
neither `plantuml` nor `scad`, and `plantuml` is refused as `TargetNotFound`.

### DiagramPack Unit Verification Design

This document describes the unit-level verification strategy for the `DiagramPack` class and its
`DiagramPreview` enum.

#### Verification Approach

The pack is constructed directly and its tools created from real policies; the scenarios that
exercise a created tool invoke it exactly as a runtime would, drawn by the real rendering library.

Unit tests reside in `Diagram/DiagramPackTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project, using `TempDirectoryFixture.cs` and
`Rendering/RenderSamples.cs`.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: The CanvasNet Viewer, in process
- **File system**: A temporary tree for the scenario that saves a picture
- **Isolation**: Each test constructs its own pack

#### Acceptance Criteria

A unit test run passes when all thirteen scenarios below pass without error or exception beyond
those explicitly asserted. A wrong prefix, a capability that does not follow the preview mode, a
preview mode not chosen from the host, options not applied or invalid arguments accepted, tools
missing, misnamed or withheld wrongly, a null policy accepted, a destination outside the policy
written, instructions that do not name the languages or that appear in a tool, or a pack that
cannot draw after warming up each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Diagram-Pack-FamilyPrefix: The Prefix Is diagram

**Test**: `DiagramPack_FamilyPrefix_Constant_IsDiagram`

The constant is `diagram`.

##### AgentKitToolsVisual-Diagram-Pack-FamilyPrefix: The Contract Reports the Constant

**Test**: `DiagramPack_FamilyPrefix_Contract_ReportsTheDeclaredConstant`

The prefix read through `IToolPack` equals the constant.

##### AgentKitToolsVisual-Diagram-Pack-PreviewFollowsHost: The Capabilities Follow the Preview Mode

**Test**: `DiagramPack_RequiredCapabilities_FollowThePreviewMode`

Image preview requires `Vision`, text preview requires `None`, and the default pack is in image
preview.

##### AgentKitToolsVisual-Diagram-Pack-PreviewFollowsHost: For Chooses the Preview Mode From the Host

**Test**: `DiagramPack_For_HostCapabilities_ChoosesThePreviewMode`

`For(Vision)` and `For(Vision | Delegation)` are in image preview; `For(None)` is in text preview.

##### AgentKitToolsVisual-Diagram-Pack-AcceptsOptions: The Options Are Applied

**Test**: `DiagramPack_Constructor_Options_AreApplied`

The options given to the constructor and to `For` are the options the pack carries; with none, the
pack carries `DiagramPackOptions.Default`.

##### AgentKitToolsVisual-Diagram-Pack-AcceptsOptions: Invalid Arguments Are Refused

**Test**: `DiagramPack_Constructor_InvalidArguments_AreRefused`

An undefined preview mode is refused with `ArgumentOutOfRangeException`; a smallest preview side of
600 above a largest render side of 500 is refused with `ArgumentException` by the constructor and by
`For`.

##### AgentKitToolsVisual-Diagram-Pack-RegistersHelpAndRenderTools: Both Tools Are Created

**Test**: `DiagramPack_CreateTools_Policy_CreatesTheHelpAndRenderTools`

Under a read-write policy, each preview mode creates `diagram_help` then `diagram_render`, no element
null and each carrying `diagram_`. Also evidence for
`AgentKitToolsVisual-Diagram-Pack-ToolsCarryFamilyPrefix`.

##### AgentKitToolsVisual-Diagram-Pack-TextPreviewRenderRequiresWriteGrant: Text Preview Withholds the Render Tool Under a Read-Only Policy

**Test**: `DiagramPack_CreateTools_ReadOnlyPolicy_WithholdsTheTextPreviewRenderTool`

Under a read-only policy, image preview creates both tools and text preview only `diagram_help`.

##### AgentKitToolsVisual-Diagram-Pack-RequiresPolicy: A Null Policy Is Refused

**Test**: `DiagramPack_CreateTools_NullPolicy_ThrowsArgumentNullException`

`CreateTools(null)` throws `ArgumentNullException`.

##### AgentKitToolsVisual-Diagram-Pack-PolicyGovernsCreatedTools: The Supplied Policy Governs the Render Tool

**Test**: `DiagramPack_CreateTools_SuppliedPolicy_GovernsTheRenderTool`

Over a temporary tree, a save inside the grant is written and a save to `../outside` is refused as
`PathNotPermitted`.

##### AgentKitToolsVisual-Diagram-Pack-InstructionsOffered: The Instructions Name the Languages and Follow the Mode

**Test**: `DiagramPack_Instructions_NameTheLanguagesAndFollowThePreviewMode`

For a pack offering Mermaid and Vega-Lite, both modes' instructions name `mermaid, vega-lite` and
`diagram_help` and not `plantuml`; image preview tells the model to look at the picture, text
preview that `save_as` is required and a description is returned.

##### AgentKitToolsVisual-Diagram-Pack-InstructionsOffered: The Instructions Are Not Injected

**Test**: `DiagramPack_Instructions_AreNotInjectedIntoTheTools`

No created tool's description contains the instruction text.

##### AgentKitToolsVisual-Diagram-Pack-WarmUp: The Pack Draws After Warming Up

**Test**: `DiagramPack_WarmUpAsync_Pack_CompletesAndTheRenderToolStillDraws`

A pack offering Mermaid and SVG completes its warm-up and then draws a Mermaid flowchart.

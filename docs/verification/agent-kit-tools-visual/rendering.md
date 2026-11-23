## Rendering Subsystem Verification Design

This document describes the subsystem-level verification strategy for the Rendering subsystem.

### Verification Approach

The subsystem is verified by scenarios that pass the output of one unit to the next the way a visual
tool does — the mapped policy into the renderer, the renderer's failure into the shaper, the
renderer's picture to the destination — so the hand-offs the unit tests cannot see are exercised.
Every scenario draws through the real rendering library against a real temporary directory tree:
confinement is a security control, and a simulated library or file system would prove only that the
simulation was written consistently.

Subsystem tests reside in `Rendering/RenderingTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project. The sources, documents and helpers every
rendering test draws are in `Rendering/RenderSamples.cs`; the directory-link helper is
`DirectoryLink.cs` and the counting HTTP listener `Rendering/CountingListener.cs`.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: The CanvasNet Viewer, in process; no external services
- **File system**: A temporary tree per test, with a permitted root and a sibling outside it
- **Isolation**: Each test creates its own tree, policy and renderer; no state is shared

### Acceptance Criteria

A subsystem test run passes when all four scenarios below pass without error or exception beyond
those explicitly asserted. A confined reference that is not drawn, a picture with a transparent
pixel, a broken source not reported with its code and location, a refused reference whose report
carries a host path, and a saved picture whose bytes differ from the drawn one each constitute a
failure.

### Test Scenarios

#### AgentKitToolsVisual-Rendering-ReferencesConfined: A Confined Reference Is Drawn on Opaque White

**Test**: `Rendering_Pipeline_ConfinedReference_IsDrawnOnOpaqueWhite`

Maps a rooted policy, draws an SVG embedding a picture beside it inside the grant with the document's
folder as the base, and asserts the render succeeded and the picture's last pixel is fully opaque.
Also evidence for `AgentKitToolsVisual-Rendering-BoundedRender`.

#### AgentKitToolsVisual-Rendering-ActionableFailures: A Broken Source Becomes Actionable Text

**Test**: `Rendering_Pipeline_BrokenSource_BecomesActionableText`

Draws a Mermaid source with a malformed arrow and shapes the failure with the source in hand;
asserts the text opens with `mermaid-error/mermaid-syntax-error` at line 2, column 5 and quotes the
offending line.

#### AgentKitToolsVisual-Rendering-ReferencesConfined: A Reference Outside the Grant Fails Without Host Paths

**Test**: `Rendering_Pipeline_ReferenceOutsideTheGrant_FailsWithoutHostPaths`

Draws an SVG escaping to a picture outside the grant, shapes the failure, and asserts a
`file-access-denied` report containing no part of the temporary directory's absolute path — which
the library's own refusal record does carry, so this scenario also guards against that record
reaching a note. Also evidence for `AgentKitToolsVisual-Rendering-ActionableFailures`.

#### AgentKitToolsVisual-Rendering-SavedUnderWriteGrant: A Drawn Picture Is Saved to a Permitted Destination

**Test**: `Rendering_Pipeline_DrawnPicture_IsSavedToAPermittedDestination`

Draws a PlantUML diagram, resolves `diagram.png` through the write decision, writes it, and asserts
the saved bytes are the drawn picture and its corner pixel is opaque white.

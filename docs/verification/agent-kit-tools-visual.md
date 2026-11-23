# AgentKitToolsVisual System Verification Design

This document describes the system-level verification strategy for the AgentKit Tools Visual
package.

## Verification Approach

The AgentKit Tools Visual system is verified through system-level integration tests that exercise
the package as a whole from the perspective of a consumer. Tests compose the package through its
public integration surface — the AgentKitCore pack contract — and assert on observable outputs,
without relying on knowledge of internal implementation details. No mocking or stubbing is
required at the system level.

The system under verification is the package's integration surface: the promise that its families are composed through
the AgentKitCore `ToolPackBuilder` under one access policy, that a composition to which no family has been attached is
well defined and contributes no tools, that attaching the image family contributes exactly that family's tools only when
the host declares the Vision capability, that each image tool is published under a policy that permits no writing, that
the diagram family is contributed in image preview to a vision host and in text preview to a host that cannot see, and
withholds its text-preview render tool where nothing may be written, that a path denial from the package's path-taking
tools discloses no host absolute path, that a file a viewed document or inline diagram source references from outside
the grants is never drawn, and that a render never fetches anything from the network. The Image and Diagram families and
the Rendering subsystem have their own subsystem and unit verification; see *Image Subsystem Verification Design*,
*Diagram Subsystem Verification Design* and *Rendering Subsystem Verification Design*. The rendering requirements that
are verified below the system level — the bounds, the shaped failures and the saved pictures — are covered by the
Rendering subsystem's and units' scenarios, which the system requirements list as their tests.

System tests reside in `AgentKitToolsVisualTests.cs`, with the temporary-directory fixture in `TempDirectoryFixture.cs`,
within the `DemaConsulting.AgentKit.Tools.Visual.Tests` project, with the directory-link helper in `DirectoryLink.cs`
and the counting listener in `Rendering/CountingListener.cs`. The package's documented examples are compiled against its
real API by `XmlDocExampleTests.cs` in the same project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: The path-denial and referenced-file scenarios use a real temporary directory
  tree, including a directory junction (Windows) or symbolic link (elsewhere); the composition
  scenarios run entirely in memory
- **Network**: The network scenario starts an HTTP listener on a free loopback port and counts the
  connections it receives; nothing leaves the machine
- **Isolation**: Each test method constructs its own policy, builder and fixture; no state is
  shared between tests

## External Interface Simulation

No external interface is simulated at the system level. The composition scenarios run against the
in-memory AgentKitCore pack contract, so there is nothing to substitute, and the path-denial
scenario uses a real temporary directory tree, because path containment is a security control and
a simulated file system would prove only that the simulation was written consistently.

## System-Level Test Scenarios

### Composition: An Empty Builder Contributes No Tools

**Test**: `AgentKitToolsVisual_SystemComposition_EmptyBuilder_ContributesNoTools`

Verifies that the package composes through the AgentKitCore pack contract and that a composition
to which no family has been attached contributes no tools. Constructs a real access policy from one
unrestricted rule, builds a `ToolPackBuilder` governed by that policy without adding any family,
and asserts the resulting tool list is empty. This test is also the evidence for the platform and
framework requirements, filtered by the operating system and runtime the result came from.

### Composition: The Image Family Is Contributed to a Vision Host

**Test**: `AgentKitToolsVisual_SystemComposition_ImagePack_ContributesTheImageFamily`

Verifies that attaching the Image pack to a host that declares the Vision capability contributes
that family's tools, under the one family prefix the pack claims. Constructs a real access policy,
declares Vision on a `ToolPackBuilder`, adds `ImagePack`, and asserts the composed list is exactly
`image_read`, `image_crop`, `image_auto_crop` and `image_view`.

### Composition: The Image Family Is Withheld From a Non-Vision Host

**Test**: `AgentKitToolsVisual_SystemComposition_ImagePackWithoutVision_ContributesNoTools`

Verifies that attaching the Image pack to a host that declares no capability contributes no tools.
Constructs a real access policy, adds `ImagePack` to a `ToolPackBuilder` that declares nothing, and
asserts the composed list is empty, so a model that cannot see is not offered a tool that returns
content it could only fabricate around.

### Composition: A Read-Only Policy Publishes Each Image Tool

**Test**: `AgentKitToolsVisual_SystemComposition_ReadOnlyPolicy_PublishesEveryImageTool`

Verifies policy-derived publication for this package. Composes the image family on a vision host
over a policy whose only grant is read-only, and asserts the composed list is exactly `image_read`,
`image_crop`, `image_auto_crop` and `image_view`, in that order. Each image tool can succeed without writing, so
none is withheld; the exact ordered list is asserted because both which tools survive and the order
a model sees them in are observable.

### Composition: The Diagram Family Is Contributed in Image Preview to a Vision Host

**Test**: `AgentKitToolsVisual_SystemComposition_DiagramPack_VisionHost_ContributesImagePreview`

Composes `DiagramPack.For(Vision)` on a vision host and asserts the pack is in image preview, the
composed list is exactly `diagram_help` and `diagram_render`, and the render tool's description says
it returns the picture.

### Composition: The Diagram Family Is Contributed in Text Preview to a Non-Vision Host

**Test**: `AgentKitToolsVisual_SystemComposition_DiagramPack_NonVisionHost_ContributesTextPreview`

Composes `DiagramPack.For(None)` on a host that declares nothing and asserts the pack is in text
preview, the composed list is exactly `diagram_help` and `diagram_render`, and the render tool's
description says it returns a description of the picture.

### Composition: A Read-Only Policy Withholds the Text-Preview Render Tool

**Test**: `AgentKitToolsVisual_SystemComposition_DiagramPack_ReadOnlyTextPreview_WithholdsRender`

Composes the text-preview diagram family over a policy whose only grant is read-only and asserts the
composed list is exactly `diagram_help`: the render tool would require a destination nothing may be
written to.

### Path Denials: No Image Tool Discloses a Host Absolute Path

**Test**: `AgentKitToolsVisual_PathDenials_DiscloseNoHostAbsolutePath`

Composes the image family on a vision host over a read-only working directory and asks
`image_read`, `image_crop`, `image_auto_crop` and `image_view` each for a file beside it, reached with
`../outside`. Asserts each result is a `PathNotPermitted` denial that names the working directory
as `. (read-only)` and contains neither the working directory's absolute location nor the temporary
base directory. Then composes the diagram family over a read-write grant of the same directory and
names `../outside/x.png` as `diagram_render`'s `save_as`, asserting the same of its denial with the
grant named as `. (read-write)`.

### Referenced Files: A File Outside the Grants Is Never Drawn

**Test**: `AgentKitToolsVisual_ReferencedFiles_OutsideTheGrants_AreNeverDrawn`

Composes the image family on a vision host over a read-only grant holding four SVG documents, each
embedding a picture by a different route: one inside the grant, one by a parent-folder escape, one
by an absolute path outside the grant, and one through a directory link inside the grant pointing
outside it. Asserts `image_view` draws the first and reports each of the other three as a
`file-access-denied` failure, so the access policy reaches every file a render opens, whichever way
the document names it.

### Referenced Files: A File Inline Diagram Source References Outside the Grants Is Never Drawn

**Test**: `AgentKitToolsVisual_ReferencedFiles_InlineDiagramOutsideTheGrants_IsNeverDrawn`

Composes the diagram family on a vision host over a read-only grant and draws three inline SVG
sources through `diagram_render`, embedding a picture inside the grant, one outside it by a
parent-folder escape, and the same one by its absolute path. Asserts the first is drawn and each of
the other two is reported as a `file-access-denied` failure, the parent-folder one naming no part of
the outside folder's absolute path.

### Rendering: A Network Reference Is Never Fetched

**Test**: `AgentKitToolsVisual_Rendering_NetworkReferences_AreNeverFetched`

Starts an HTTP listener on a free loopback port, composes the image family over a read-only grant
holding an SVG embedding a picture from the listener and a PlantUML diagram including a file from
it, views both through `image_view`, and asserts the listener received no request at all.

### Documentation: The Package's Examples Compile

**Test**: `AgentKitToolsVisual_XmlDocExamplesCompile`

Extracts each example from the package's shipped XML documentation file and compiles it with
Roslyn against the real compiled API, so a documented example that does not compile fails the
build rather than misleading a reader.

## Supplementary Live Evidence

The opt-in live suites exercise the visual tools against real models; they are supplementary
evidence and not linked to ReqStream, because the pipeline that builds the trace matrix has no model
to run them against. Over Ollama, a model reads the diagram help, draws a Mermaid source carrying an
injected error, fixes it from the shaped failure and names what the picture shows; and a model views
an SVG figure and a PDF page while a figure reaching outside the sandbox is refused. Over the
Copilot runtime, a picture `diagram_render` draws reaches the model as binary content. See the
*Supplementary Live Verification* sections of the AgentKitAgentsChatClient system verification
(scenarios L5 and L6) and the AgentKitAgentsCopilot system verification (scenario L8b).

## Acceptance Criteria

A system-level test run passes when the scenarios above pass without error or exception beyond
those explicitly asserted. Any unexpected exception, a non-empty tool list from an empty
composition, the image family not contributing its tools to a vision host, the image family
contributed to a host that did not declare Vision, an image tool withheld under a read-only policy,
the diagram family contributed in the wrong preview mode or its text-preview render tool published
under a read-only policy, a path denial disclosing a host absolute path, a referenced file outside
the grants being drawn, a
request reaching the network listener, a documented example that does not compile, or a failure to
compose through the AgentKitCore pack contract constitutes a failure.

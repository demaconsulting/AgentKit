# AgentKitToolsVisual System Design

This document provides the system-level design for the AgentKit Tools Visual package.

![AgentKit Tools Visual Structure](AgentKitToolsVisualView.svg)

## Architecture

The AgentKit Tools Visual system is an optional capability package: a collection of guarded visual
tool families an application attaches to an agent through the AgentKitCore contract. It supplies no
policy primitives, no guarded construction path and no pack contract of its own — those belong to
Core, and each family this package publishes is composed the same way any other AgentKit pack is
composed: through one `ToolPackBuilder` governed by one access policy.

AgentKitToolsVisual is a **peer** of the other capability packages, not a layer above or beneath
them. It depends on Core alone and never on AgentKitTools, so an application can attach the visual
tools with or without the general-purpose families. The package exists as a separate unit of
distribution for one reason: its families need an imaging library, and keeping that library here
means it reaches only the applications that choose the visual tools, while AgentKitTools carries no
imaging dependency at all.

The system contains two subsystems and one unit:

- The **Image** subsystem is the image tool family, publishing `image_read`, `image_crop`,
  `image_auto_crop` and `image_view` under the `image` family prefix and attached to an application
  as one pack. It is gated on a host capability — it is registered only for a host that declares it
  can present visual content to a model — because its tools return image and PDF content that a
  non-vision host could not use. `image_read` stays a byte-exact pass-through of what a provider
  renders itself; `image_view` draws the documents, drawings and diagram sources a provider cannot
  render. See *Image Subsystem Design*.
- The **Rendering** subsystem holds the internal pieces every tool that draws shares: the mapping
  of the access policy onto the rendering library's file-access policy (`VisualAccess`), the
  renderer that applies the bounds, the opaque background and the promotions (`VisualRenderer`), the
  shaping of failures into text a model can act on (`RenderErrorShaper`), and the saving of a
  picture under the write grant (`RenderDestination`). Nothing in it is public. See *Rendering
  Subsystem Design*.
- **VisualOptions** is the public record of rendering bounds a host gives a pack: the render
  timeout, the largest render side, the smallest preview side, the triangle limit of a 3D model and
  the number of renders a pack runs at once. See *VisualOptions Unit Design*.

## External Interfaces

The system's public API is the pack type each family publishes, together with the public tool and
media-type classes that name the family's tools and the content types it reads; it defines no
policy primitive, construction path or pack contract of its own. Its families are composed into an
application through the AgentKitCore pack contract:

- **IToolPack** (from AgentKitCore) — each family this package provides implements the Core pack
  contract, publishing its tools as one capability-gated family under a family prefix no other
  pack claims.
- **ToolPackBuilder** (from AgentKitCore) — an application composes this package's families, with
  any other AgentKit packs, into one ordered tool list governed by a single `PathPolicy`.

An application adds `ImagePack` to give a vision-capable agent the image family, optionally with
`VisualOptions` bounding its drawings, and may call `WarmUpAsync` once at start-up so the rendering
library's first-use cost is not paid while a model waits. A composition to
which no family has been added remains well defined — an empty `ToolPackBuilder` built with a valid
policy yields an empty tool list — and the image family on a host that has not declared Vision
contributes no tools, because the composition does not ask its pack for them.

| Interface | Direction | Format | Constraints |
| --- | --- | --- | --- |
| `IToolPack` | Outbound | AgentKitCore pack contract | Implemented by each family |
| `ToolPackBuilder` | Inbound/Outbound | AgentKitCore composition | Governed by one `PathPolicy` |
| `ImagePack` | Outbound | AgentKitCore pack contract | Prefix `image`; requires Vision |
| `ImagePack(VisualOptions?)` | Inbound | `VisualOptions` record | Each value range-checked where set |
| `ImagePack.WarmUpAsync` | Inbound | `Task` | Optional; host-initiated only |

## Dependencies

The package takes exactly one project dependency, `DemaConsulting.AgentKit.Core`, and one runtime
NuGet dependency of its own. Core supplies the policy primitives, the guarded construction path, the
result constructors and the pack contract each family in this package is built on.
`Microsoft.Extensions.AI.Abstractions` — the package that defines the `AIFunction` a tool is —
reaches this package transitively through Core rather than as a direct dependency; see *OTS
Integration Design* (`docs/design/ots.md`) and *Microsoft.Extensions.AI.Abstractions Design*.

The one direct runtime NuGet dependency is `CanvasNet.Viewer`, which brings the CanvasNet core and
its format libraries (SVG, PDF, PPTX, VSDX, Mermaid, PlantUML, Vega-Lite, OpenSCAD) transitively at
the same version. The region tools use the core to read what an image declares about itself and to
extract and re-encode a region of one; the Rendering subsystem uses the Viewer to draw documents and
diagrams. It is taken **without** `PrivateAssets` because it is needed at run time by any
application that attaches the family. It was chosen on the property that its decoders are safe
managed code and its packages carry no native binaries, which is what makes parsing hostile input
consistent with this library's thesis. It is a prerelease pin, and a stable release of this package
waits on a stable CanvasNet 0.2.0; see *CanvasNet Design*.

The package does **not** depend on AgentKitTools. The image family's refusal for an `.svg` file
names `image_view`, a tool of this same family, so no tool of the text family is named and no
project reference to AgentKitTools is needed.

The dependency runs in exactly one direction: Tools Visual depends on Core, never the reverse, and
no other capability package depends on Tools Visual.

The following OTS items are used for building and verifying this system and are not consumed at
runtime; see *OTS Integration Design* (`docs/design/ots.md`) and each item's dedicated design
document for details:

- **ApiMark** — generates the package's API reference; see *ApiMark Design*
- **FileAssert** — validates generated documents against acceptance criteria; see
  *FileAssert Design*
- **ReqStream** — enforces requirements-to-test traceability; see *ReqStream Design*
- **ReviewMark** — enforces file review coverage and currency; see *ReviewMark Design*
- **SysML2Tools** — lints the architecture model and renders its diagrams; see *SysML2Tools Design*
- **xUnit** — executes unit and integration tests; see *xUnit Design*

## Risk Control Measures

This system relies on the path-containment controls AgentKitCore provides — path normalization,
the working directory paired with read-only or read-write grants, the single containment decision
and the disclosing returned denial — and composes through them unchanged by constructing its tools
through Core's guarded construction path and governing them with the one access policy the
composing application supplies.

For the Image family, the containment control is the `PathPolicy` read decision applied to each
read and the write decision applied to each destination a crop names, judged independently of one
another. The family adds two controls of its own:

- **Vision capability gate** — the family is withheld from a host that has not declared it can
  present visual content, by not asking the pack for its tools, so a model that cannot see an image
  is not offered a tool that returns one it could only fabricate a description of.
- **Decode budget from the header** — an image's decode cost is judged from the dimensions its
  header declares, against the policy's `ToolLimits.MaxImagePixels`, before any pixel data is
  allocated, so a small file cannot provoke a large allocation.

Drawing a document adds controls of its own, all held in the Rendering subsystem:

- **The input is judged first** — the rendering library never judges the file it is asked to draw,
  so `image_view` takes the read decision for it before anything is learned about it, and draws
  the bytes it read rather than handing the library a path.
- **Referenced files are confined** — every picture, include and data file a document references
  is judged by a file-access policy mapped from the access policy: the grant roots (path as written
  and real path), no links below the starting folder, and AgentKit's own read decision, which
  applies deny patterns. The library's allow-everything default is never used, and nothing is
  fetched from the network.
- **Every render is bounded** — by the pixel budget, the binary ceiling, the largest render side,
  the triangle limit and the render timeout, which counts the wait for the render gate. The timeout
  is a cancellation the library honors; as defense in depth, the caller also stops waiting a short
  grace period later, and the gate keeps an abandoned render's place until it ends, so abandoned
  renders cannot accumulate.
- **Nothing misleading reaches the model** — pictures are drawn on opaque white, an empty picture
  or one whose embedded image was refused is reported as a failure, and failures are shaped into
  text with a code, a location, a caret, model-facing advice and every note, with host paths
  removed.

## Data Flow

**Composition path:**

1. **Input**: An access policy at builder construction, and the visual families the application
   wishes to attach from this package
2. **Registration check**: Each family is registered only when the host provides each capability
   it requires, as Core's composition dictates
3. **Creation**: Only a registered family is asked for its tools, and it is handed the one policy
   the builder holds
4. **Output**: One ordered tool list, governed by the single policy — the tools of each family the
   application attached, in the order it attached them

**Drawing path:**

1. **Input**: A path the model names, and optionally a page and a width
2. **Read decision**: The access policy permits the file, or the policy's refusal is returned
3. **Read**: The file is read once, within the binary ceiling
4. **Render**: The renderer draws those bytes under the mapped file-access policy, with the file's
   own folder as the base its references resolve against, within every bound
5. **Output**: An opaque PNG with a caption, or the shaped failure text, or a denial

### Policy-derived publication

The AgentKit packs follow one rule: a pack withholds a tool whose writes the path policy governs
when the policy permits no writing anywhere, and publishes everything else.

| Family | Filters? | Needs a write grant | Read-only total |
| --- | --- | ---: | ---: |
| Image | No | none | 4 → **4** |

All four image tools succeed under a read-only policy. Reading or drawing a file is a read, and
taking a region without a destination returns image content rather than writing a file. The optional
`destination` that `image_crop` and `image_auto_crop` both carry does need a write grant, but the
rule gates a tool on the writes it can only perform, not on whether each argument can be used; see
*Image Pack Design*, *Image Crop Tool Design* and *Image Auto Crop Tool Design*.

## Design Constraints

- **Peer, not a layer**: The package depends on AgentKitCore, the Base Class Library and the one
  runtime imaging dependency its families take; it does not depend on AgentKitTools, and no other
  capability package depends on it
- **Composed through Core**: Each family is published through Core's `IToolPack` contract and
  composed through `ToolPackBuilder`, so an application attaches this package's families the same
  way it attaches any other AgentKit pack
- **Compliance**: All functionality must be traceable to requirements
- **Quality**: Zero warnings, full test coverage, complete documentation
- **Portability**: Compatible across supported .NET platforms

### Known Limitations

The rendering library is a prerelease, and the following behaviors of the pinned version are known
and handled here rather than fixed here:

- **Format failures carry no stable code or structured location**
  ([CanvasNet #166](https://github.com/demaconsulting/CanvasNet/issues/166)). This is why the
  package shapes failures itself, classifying by exception type and reading the location from the
  typed exception or the message prefix.
- **Malformed Vega-Lite JSON is reported as nesting too deep**
  ([CanvasNet #163](https://github.com/demaconsulting/CanvasNet/issues/163)). The shaped failure is
  classified correctly by type, as a Vega-Lite error, but its message still states the misleading
  cause.
- **A bad PlantUML line can make the diagram be detected as a deployment diagram**
  ([CanvasNet #164](https://github.com/demaconsulting/CanvasNet/issues/164)), so the message may
  name the wrong diagram kind; the location still points at the line to fix.
- **A Vega-Lite encoding without a type renders without a diagnostic**
  ([CanvasNet #165](https://github.com/demaconsulting/CanvasNet/issues/165)). The picture is
  returned as drawn; it is caught only if it is empty enough for the near-empty check.
- **The Vega-Lite, Mermaid and PlantUML budgets cannot be set through the render options**
  ([CanvasNet #167](https://github.com/demaconsulting/CanvasNet/issues/167)). The library's own
  defaults apply to those formats; the pixel budget, the binary ceiling, the largest side and the
  timeout still bound every render.
- **The default background is transparent**
  ([CanvasNet #168](https://github.com/demaconsulting/CanvasNet/issues/168)), which a vision model
  reads as dark text on black. Every render therefore sets an opaque white background.
- **A platform that cannot report the real path of an opened file** (Linux without `/proc`) has
  every referenced file refused, because the mapped policy needs the real path; it fails closed.

### Platform Support

The library targets the following frameworks:

| Target Framework | Runtime / Environment |
| --- | --- |
| `net8.0` | .NET 8 LTS |
| `net9.0` | .NET 9 |
| `net10.0` | .NET 10 |

The library is supported on the following operating systems:

- **Windows** — primary developer and CI platform
- **Linux** — CI/CD and containerized environments
- **macOS** — developer workstations using Apple platforms

Portability is achieved by restricting the implementation to Base Class Library (BCL) APIs
available across the target frameworks, to the provider-neutral
`Microsoft.Extensions.AI.Abstractions` surface reached through AgentKitCore, and to one runtime
imaging dependency whose package carries no native binaries, whose decoders are safe managed code
and that targets each framework listed above; see *CanvasNet Design*.

### Integration Patterns

- **NuGet Packaging**: Standard .NET library packaging and distribution, packed with the
  repository README and icon like the other AgentKit packages
- **CI/CD Integration**: Automated build, test, and quality validation
- **Requirements Traceability**: All features linked to passing tests
- **Review Management**: Systematic file review using ReviewMark patterns

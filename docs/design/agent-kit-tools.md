# System Design

This document provides the system-level design for the AgentKit Tools.

![AgentKit Tools Structure](AgentKitToolsView.svg)

## Architecture

The AgentKit Tools system is a general-purpose capability package: a collection of guarded tool
families an application attaches to an agent through the AgentKitCore contract. It supplies no
policy primitives, no guarded construction path and no pack contract of its own — those belong to
Core, and every family this package publishes is composed the same way any other AgentKit pack is
composed: through one `ToolPackBuilder` governed by one access policy.

AgentKitTools is a **peer** of the other capability packages an application may attach, not a base
layer beneath them. It depends on Core and adds tool families; nothing in it is structured so that
another capability package would depend on Tools rather than on Core directly. This keeps the
package set flat: applications attach the families they want, in any combination, without a Tools
dependency being forced upon a family that does not need it.

The system contains the **TextFile** subsystem: the text file tool family, publishing
`text_file_read`, `text_file_write` and `text_file_list` under the `text_file` family prefix and
attached to an application as one pack. It was scaffolded ahead of that family so that its build,
tests, requirements traceability and review coverage were established before any family was added.

The system also contains the **Image** subsystem: the image tool family, publishing `image_read`
under the `image` family prefix and attached to an application as one pack. Unlike the text file
family, it is gated on a host capability — it is registered only for a host that declares it can
present visual content to a model — because its tool returns image and PDF content that a
non-vision host could not use. The remaining tool families this package will provide are introduced
in subsequent increments, each as its own subsystem with its own units, requirements, design,
verification and review set.

## External Interfaces

The system's public API is the pack type each family publishes; it defines no policy primitive,
construction path or pack contract of its own. Its tool families are composed into an application
through the AgentKitCore pack contract:

- **IToolPack** (from AgentKitCore) — each family this package provides implements the Core pack
  contract, publishing its tools as one capability-gated family under a family prefix no other
  pack claims.
- **ToolPackBuilder** (from AgentKitCore) — an application composes this package's families, with
  any other AgentKit packs, into one ordered tool list governed by a single `PathPolicy`.

An application attaches a family by adding that family's pack: `TextFilePack` is the type an
application adds to give an agent the text file family, and `ImagePack` is the type it adds to give
a vision-capable agent the image family. A composition to which no family has been added remains
well defined — an empty `ToolPackBuilder` built with a valid policy yields an empty tool list — and
a family whose required capability the host has not declared, such as the image family on a
non-vision host, contributes no tools because the composition never asks its pack for them.

| Interface         | Direction        | Format                     | Constraints                       |
|-------------------|------------------|----------------------------|-----------------------------------|
| `IToolPack`       | Outbound         | AgentKitCore pack contract | Implemented by each family        |
| `ToolPackBuilder` | Inbound/Outbound | AgentKitCore composition   | Governed by one `PathPolicy`      |
| `TextFilePack`    | Outbound         | AgentKitCore pack contract | Prefix `text_file`; no capability |
| `ImagePack`       | Outbound         | AgentKitCore pack contract | Prefix `image`; requires Vision   |

## Dependencies

The AgentKit Tools takes exactly one project dependency, `DemaConsulting.AgentKit.Core`, and no
runtime NuGet dependency of its own. Core supplies the policy primitives, the guarded construction
path, the result constructors and the pack contract every family in this package is built on.
`Microsoft.Extensions.AI.Abstractions` — the package that defines the `AIFunction` a tool is —
reaches this package transitively through Core rather than as a direct dependency, so a tool
family composes through the same currency Core publishes without this package choosing a provider
or restating a dependency Core already owns. That abstraction is the one OTS runtime library the
software depends on; its integration is recorded in _OTS Integration Design_
(`docs/design/ots.md`) and its dedicated _Microsoft.Extensions.AI.Abstractions Design_, where the
transitive path through Core is documented.

The dependency runs in exactly one direction: Tools depends on Core, never the reverse, and no
other capability package depends on Tools. The package is a peer of the other packs an application
attaches.

The following OTS items are used for building and verifying this system and are not consumed at
runtime; see _OTS Integration Design_ (`docs/design/ots.md`) and each item's dedicated design
document for details:

- **BuildMark** — generates build-notes documentation; see _BuildMark Design_
- **FileAssert** — validates generated documents against acceptance criteria; see
  _FileAssert Design_
- **Pandoc** — converts Markdown documentation to HTML; see _Pandoc Design_
- **ReqStream** — enforces requirements-to-test traceability; see _ReqStream Design_
- **ReviewMark** — enforces file review coverage and currency; see _ReviewMark Design_
- **SarifMark** — converts CodeQL SARIF results to markdown; see _SarifMark Design_
- **SonarMark** — generates SonarCloud quality reports; see _SonarMark Design_
- **SysML2Tools** — lints the architecture model and renders its diagrams; see _SysML2Tools Design_
- **VersionMark** — captures and publishes tool-version information; see _VersionMark Design_
- **WeasyPrint** — converts HTML documentation to PDF; see _WeasyPrint Design_
- **xUnit** — executes unit and integration tests; see _xUnit Design_

## Risk Control Measures

This system defines no risk control measures of its own at this stage. Every path-containment
control an agent relies on — real-location path resolution, the one required working directory
paired with zero-or-more read-only or read-write access grants, the single containment decision,
the disclosing returned denial that echoes the request and enumerates the permitted locations, and
the unrepresentable unguarded policy — lives in AgentKitCore, which this package composes through unchanged. Each tool family
this package adds will inherit those controls by constructing its tools through Core's single
guarded construction path and governing them with the one access policy the composing application
supplies; the risk controls specific to a family are described in that family's design when the
family is introduced. For the TextFile family, that containment control is the `PathPolicy`
decision applied to every read, every write and every enumeration it performs — the read decision
for reads and listings, the write decision for writes — with enumeration going through the policy
so a listing can never advertise a file a read would refuse. For the Image family, the containment
control is that same `PathPolicy` read decision applied to every read, and it adds a second control
of its own: the Vision capability gate, which withholds the family from a host that has not declared
it can present visual content — withholding it by never asking the pack for its tools — so a model
that cannot see an image is never offered a tool that returns one it could only fabricate a
description of.

## Data Flow

**Composition path:**

1. **Input**: An access policy at builder construction, and the tool families the application
   wishes to attach from this package
2. **Registration check**: Each family is registered only when the host provides every capability
   it requires, exactly as Core's composition dictates
3. **Creation**: Only a registered family is asked for its tools, and it is handed the one policy
   the builder holds
4. **Output**: One ordered tool list, governed by the single policy — the tools of every family the
   application attached, in the order it attached them

## Design Constraints

- **Peer, not a layer**: The package depends on AgentKitCore and the Base Class Library only, and
  no other capability package depends on it; a family it provides is attached alongside other
  packs, never beneath them
- **Composed through Core**: Every family is published through Core's `IToolPack` contract and
  composed through `ToolPackBuilder`, so an application attaches this package's families the same
  way it attaches any other AgentKit pack
- **Compliance**: All functionality must be traceable to requirements
- **Quality**: Zero warnings, full test coverage, complete documentation
- **Portability**: Compatible across supported .NET platforms

### Platform Support

The library targets the following frameworks, enabling broad compatibility across modern .NET
runtimes:

| Target Framework | Runtime / Environment |
|------------------|-----------------------|
| `net8.0`         | .NET 8 LTS            |
| `net9.0`         | .NET 9                |
| `net10.0`        | .NET 10               |

The library is supported on the following operating systems:

- **Windows** — primary developer and CI platform
- **Linux** — CI/CD and containerized environments
- **macOS** — developer workstations using Apple platforms

Portability is achieved by restricting the implementation to Base Class Library (BCL) APIs
available across all target frameworks and to the provider-neutral
`Microsoft.Extensions.AI.Abstractions` surface reached through AgentKitCore. No platform-specific
native interop, OS-specific APIs, or framework-version-specific features are used.

### Integration Patterns

- **NuGet Packaging**: Standard .NET library packaging and distribution
- **CI/CD Integration**: Automated build, test, and quality validation
- **Requirements Traceability**: All features linked to passing tests
- **Review Management**: Systematic file review using ReviewMark patterns

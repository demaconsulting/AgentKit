# AgentKit

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

DEMA Consulting libraries of hardened, provider-neutral agent tools for .NET.

AgentKit provides hardened, provider-neutral agent tools: an application attaches a
permission-governed set of tools to the agent framework of its choice, bounded by a policy the
application configures and a tool cannot omit.

> **Status**: Early development. The Core contract — path policy, tool limits, guarded tool
> construction, tool results, and the tool pack contract — is implemented, and two guarded tool
> families, text file and image, are built on it in `DemaConsulting.AgentKit.Tools`. Neither
> package is published to NuGet yet, and the public API is not yet stable.

## Capabilities

- **Guarded tool families**: each family is bound at construction to a policy that constrains what
  it may touch. Two families ship today — **text file** (read, write, list) and **image** (read
  images and PDF documents for a vision-capable agent) — in `DemaConsulting.AgentKit.Tools`.
  File system, transfer buffer, work queue, user interaction, and sub-agent delegation families
  are planned.
- **Capability packs**: adapting other libraries, such as document extraction and speech,
  into guarded agent tools (planned)
- **Provider neutrality**: tools are `AIFunction` instances, so they work with Microsoft
  Agent Framework, the GitHub Copilot SDK, and any `IChatClient` implementation

AgentKit does not provide an agent runtime, context-window management, or provider
abstraction. Microsoft Agent Framework supplies those.

## Packages

- **`DemaConsulting.AgentKit.Core`** — policy primitives, guarded tool construction, tool result
  helpers, and the tool-pack contract.
- **`DemaConsulting.AgentKit.Tools`** — ready-made guarded tool families (text file and image),
  each composed onto a policy through the pack contract.

Additional provider and tool packages will be added as the architecture is implemented.

## Engineering Practices

- **Multi-Platform Support**: Builds and runs on Windows, Linux, and macOS
- **Multi-Runtime Support**: Targets .NET 8, 9, and 10
- **xUnit v3**: Modern unit testing with xUnit framework version 3
- **Comprehensive CI/CD**: GitHub Actions workflows with quality checks and builds
- **Linting Enforcement**: markdownlint, cspell, and yamllint enforced on every CI run
- **Continuous Compliance**: Compliance evidence generated automatically on every CI run, following
  the [Continuous Compliance][link-continuous-compliance] methodology
- **SonarCloud Integration**: Quality gate and security analysis on every build
- **Documentation Generation**: Automated build notes, user guide, code quality reports,
  requirements, justifications, and trace matrix
- **Requirements Traceability**: Requirements linked to passing tests with auto-generated trace matrix

## Installation

Install the libraries using the .NET CLI:

```bash
dotnet add package DemaConsulting.AgentKit.Core
dotnet add package DemaConsulting.AgentKit.Tools
```

## Usage

`DemaConsulting.AgentKit.Core` currently provides the contract that other AgentKit packages — and
an application's own tools — are built against:

- **Path policy**: an independent read rule and write rule, each unrestricted or confined to a
  location, each with its own denied patterns, and all containment decisions resolving symbolic
  links and directory junctions at every path component
- **Tool limits**: ceilings on bytes read, result size returned to the model, and attachments per
  turn, carried with the policy so every tool observes the same budget
- **Guarded tool construction**: the only supported way to build a tool, so the safety conventions
  cannot be forgotten
- **Tool results**: text, structured data, binary, and image results, and refusals that carry a
  reason
- **Tool pack contract**: composition of packs into the tool list an application offers a model,
  gated on host capability

`DemaConsulting.AgentKit.Tools` ships ready-made guarded tool families built on this contract. An
application composes a policy, adds the packs it wants, declares what its host supports, and
receives the tool list to hand to its agent framework of choice:

```csharp
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.Image;
using Microsoft.Extensions.AI;

var policy = PathPolicy.ForWorkspace("/workspace");

IReadOnlyList<AIFunction> tools = new ToolPackBuilder(policy)
    .WithHostCapabilities(HostCapabilities.Vision)
    .Add(new TextFilePack())
    .Add(new ImagePack())
    .Build();

// Hand `tools` to ChatOptions.Tools, an IChatClient, or Microsoft Agent Framework.
```

`ForWorkspace` names the workspace once: it becomes the permitted read location, the permitted
write location, and the location a relative path is interpreted against. That last part matters
because a model asks for `notes.txt`, not for its absolute location — a policy that measured that
name from wherever the host process was started would refuse every legitimate request. Absolute
paths remain expressible and remain subject to the same containment decision, and a request naming
no path at all means the workspace itself. Use a `PathPolicy` constructor directly when reads and
writes need different locations.

The policy is a guardrail, not a sandbox: a tool cannot express an operation the policy forbids,
but AgentKit does not replace OS-level isolation for untrusted code. Because the image family
requires the `Vision` host capability, `ImagePack` contributes its tool only when the host
declares that capability; a host that does not is never offered `image_read`.

Providers differ in where they accept images. Some deliver an image a tool returned straight to
the model; others accept images only on messages and silently discard one that arrives in a tool
response, after which the model describes a picture it never received. A host targeting such a
provider wraps its chat client in `ImagePromotingChatClient`, beneath the function-invocation loop,
and the image is carried onto a user message instead.

## Documentation

Generated documentation includes:

- **Build Notes**: Release information and changes
- **User Guide**: Comprehensive usage documentation
- **Code Quality Report**: CodeQL and SonarCloud analysis results
- **Requirements**: Functional and non-functional requirements
- **Requirements Justifications**: Detailed requirement rationale
- **Trace Matrix**: Requirements to test traceability

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup, coding
standards, and the pull request process.

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Badge References -->
[badge-forks]: https://img.shields.io/github/forks/demaconsulting/AgentKit?style=plastic
[badge-stars]: https://img.shields.io/github/stars/demaconsulting/AgentKit?style=plastic
[badge-contributors]: https://img.shields.io/github/contributors/demaconsulting/AgentKit?style=plastic
[badge-license]: https://img.shields.io/github/license/demaconsulting/AgentKit?style=plastic
[badge-build]: https://img.shields.io/github/actions/workflow/status/demaconsulting/AgentKit/build_on_push.yaml?style=plastic
[badge-quality]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_AgentKit&metric=alert_status
[badge-security]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_AgentKit&metric=security_rating
[badge-nuget]: https://img.shields.io/nuget/v/DemaConsulting.AgentKit.Core?style=plastic

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/AgentKit/network/members
[link-stars]: https://github.com/demaconsulting/AgentKit/stargazers
[link-contributors]: https://github.com/demaconsulting/AgentKit/graphs/contributors
[link-license]: https://github.com/demaconsulting/AgentKit/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/AgentKit/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentKit
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentKit
[link-nuget]: https://www.nuget.org/packages/DemaConsulting.AgentKit.Core
[link-continuous-compliance]: https://github.com/demaconsulting/ContinuousCompliance
[link-contributing]: https://github.com/demaconsulting/AgentKit/blob/main/CONTRIBUTING.md

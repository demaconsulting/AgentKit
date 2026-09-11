# AgentKit

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

DEMA Consulting libraries for composing and running AI agent systems in .NET.

AgentKit provides hardened, provider-neutral agent tools: an application attaches a
permission-governed set of tools to the agent framework of its choice, bounded by a policy the
application configures and a tool cannot omit.

> **Status**: Early development. The Core contract — path policy, tool limits, guarded tool
> construction, tool results, and the tool pack contract — is implemented; the tool families built
> on it are not yet published, and the public API is not yet stable.

## Planned Capabilities

- **Provider back-ends**: GitHub Copilot SDK, and any `Microsoft.Extensions.AI` `IChatClient`
  implementation such as OllamaSharp or Azure AI Foundry
- **Tool library**: file system, text file, image file, todo list, and sub-agent tools, made safe
  through permission policies and path allow-listing, and extensible by the target application
- **Context-window management**: preserved seed prompts, auto-summarization, and RAG-backed memory
  in future

## Packages

- **`DemaConsulting.AgentKit.Core`** — policy primitives, guarded tool construction, tool result
  helpers, and the tool-pack contract.

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

Install the library using the .NET CLI:

```bash
dotnet add package DemaConsulting.AgentKit.Core
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
- **Tool results**: text, binary, and image results, and refusals that carry a reason
- **Tool pack contract**: composition of packs into the tool list an application offers a model,
  gated on host capability

Ready-made tool families will ship in `DemaConsulting.AgentKit.Tools`, which is not yet published.
Usage examples will follow with that package.

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

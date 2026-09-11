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

AgentKit lets an application select a provider back-end, attach a permission-governed set of tools,
configure a context-window management policy, and spawn agents the application can interact with.

> **Status**: Early development. The repository is scaffolded and the architecture is being
> established; the public API is not yet stable and the `Demo` type is a placeholder.

## Planned Capabilities

- **Provider back-ends**: GitHub Copilot SDK, and any `Microsoft.Extensions.AI` `IChatClient`
  implementation such as OllamaSharp or Azure AI Foundry
- **Tool library**: file system, text file, image file, todo list, and sub-agent tools, made safe
  through permission policies and path allow-listing, and extensible by the target application
- **Context-window management**: preserved seed prompts, auto-summarization, and RAG-backed memory
  in future

## Packages

- **`DemaConsulting.AgentKit.Core`** — core abstractions, agent runtime, tool and permission model,
  and context management.

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

The agent composition API is still being designed. The package currently exposes only the
placeholder `Demo` type:

```csharp
using DemaConsulting.AgentKit.Core;

var demo = new Demo();
var result = demo.DemoMethod("World"); // result = "Hello, World!"
```

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

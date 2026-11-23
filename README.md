# ![AgentKit: Safe agent tools for .NET][hero]

<!-- IMPORTANT: All links in this file must be absolute URLs.
     This file is distributed in packages and relative links will not resolve. -->

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

## Overview

AgentKit is a family of .NET libraries that gives an AI agent provider-neutral tools confined to what the
application grants. The application decides which tools an agent receives and the path grants and limits they
carry; each tool checks every call against them and refuses one that falls outside, with a reason the model can act
on. AgentKit also provides an agent session that compacts its own context, so a long-running agent keeps working
within the model's context window on each supported provider.

## Features

- 🛡️ **Application-Set Controls**: Path grants, tool limits, and host capabilities; a path outside the grants is refused
- 🧰 **Ready-Made Tool Families**: Text file, file, Markdown, todo, memory, agent delegation, plus image and diagram
- 🧩 **Custom Tools**: Build your own tools through the same guarded factory and publish them as packs
- 🔌 **Provider-Neutral**: Plain `AIFunction` tools that keep their limits on any `IChatClient` or on GitHub Copilot
- 🧠 **Self-Compacting Sessions**: Tiered compaction keeps a long conversation within the window the provider runs
- 🗄️ **Large Output Retention**: Opt-in; oversized tool output is held in memory and paged through by reference
- 🖼️ **Tool Image Delivery**: Tool-returned images reach the model on providers that would otherwise drop them
- 🎚️ **Model Catalog and Settings**: List models by capability; effort, tier, and window are checked at session start
- 🧪 **Testable Offline**: An in-memory provider session exercises the whole session lifecycle without a model
- 📚 **Agent-Readable API Reference**: Every package ships a gradually-disclosed Markdown API reference
- 🖥️ **Multi-Platform Support**: Builds and runs on Windows, Linux, and macOS, targeting .NET 8, 9, and 10
- 📋 **Continuous Compliance**: CI generates evidence using [Continuous Compliance][link-continuous-compliance]

## Installation

Install the core libraries and the adapter for your provider using the .NET CLI:

```bash
dotnet add package DemaConsulting.AgentKit.Core
dotnet add package DemaConsulting.AgentKit.Tools
dotnet add package DemaConsulting.AgentKit.Tools.Visual        # optional: image and diagram tools
dotnet add package DemaConsulting.AgentKit.Agents.ChatClient   # any IChatClient provider
```

For the GitHub Copilot runtime, add `DemaConsulting.AgentKit.Agents.Copilot` instead of the `IChatClient` adapter and
install the [GitHub Copilot CLI][link-copilot-cli] 1.0.113 or later (`winget install GitHub.Copilot`,
`brew install --cask copilot-cli` or `npm install -g @github/copilot`; `copilot --no-auto-update --version` shows the
version AgentKit runs). AgentKit starts the installed CLI by default; to ship the SDK's bundled runtime instead, pass
`runtimeSource: CopilotRuntimeSource.Bundled` and reference `Microsoft.Copilot.SDK` 2.0.1 directly from the host
application. On Ollama, also add `DemaConsulting.AgentKit.Agents.Ollama` so a session is told the context window the
instance is actually running.

## Usage

Decide what the agent may touch, decide which tools it receives, and hand both to a provider. The example runs on a
local Ollama model through `OllamaSharp` (`dotnet add package OllamaSharp`); any other `IChatClient` drops in on the
same line:

```csharp
using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;
using OllamaSharp;

// What the agent may touch: relative paths anchor to /workspace, which is granted read-write.
var policy = new PathPolicy("/workspace", [PathRule.ReadWrite("/workspace")]);

// Which tools it receives: two of the shipped families, composed onto that policy.
IList<AIFunction> tools =
[
    .. new ToolPackBuilder(policy)
        .Add(new TextFilePack())
        .Add(new FilePack())
        .Build()
];

// Any IChatClient works (OpenAI, Azure OpenAI, ...); this one is a local Ollama model.
IChatClient chatClient = new OllamaApiClient(new Uri("http://localhost:11434"), "qwen3.5:9b");

// A Microsoft Agent Framework agent over that client.
var agent = ChatClientAgentFactory.Create(
    chatClient,
    tools,
    instructions: "You are a document assistant. Use the tools provided.");

var response = await agent.RunAsync("List the files in the workspace and summarize the largest one.");
Console.WriteLine(response.Text);
```

Every tool in that list observes the same policy: a request that names a path outside `/workspace` is refused
with a reason the model can act on. The policy is a path guardrail, not an OS sandbox: a link inside a grant is not a
boundary, so read *Path Policy* in the [User Guide][link-releases] for the link and race caveats. To keep a long
conversation alive past the model's context window, run the agent on a `CompactingAgentSession`, as the User Guide
describes.

## Architecture

![AgentKit architecture: your application adds AgentKit.Tools packs, which are built through the AgentKit.Core tool safety contract that enforces its controls, and builds an agent or session through an AgentKit.Agents adapter, which feeds the Core session lifecycle: Agents.ChatClient builds on Microsoft Agent Framework and any IChatClient provider, Agents.Copilot provides its own AIAgent on a Copilot runtime it starts and owns through the Microsoft Copilot SDK, and Agents.Ollama reads an Ollama server's window, none of which is part of AgentKit][img-architecture]

Your application chooses the tools and sets their controls; `AgentKit.Core` enforces those controls
for every tool and runs the self-compacting session that each provider adapter plugs into.

## Building

```pwsh
pwsh ./build.ps1
```

## User Guide

The AgentKit User Guide, covering the safety model, every tool family, building an agent on each
provider, and sessions with context compaction, is available on the
[AgentKit releases page][link-releases].

## Samples

Three runnable [samples][link-samples] show AgentKit end to end:

- 📄 **[Document Assistant][link-sample-document]**: Consuming the shipped tools under a two-location policy
- 🔬 **[Research Assistant][link-sample-research]**: Planning, memory, and delegation on a self-compacting session
- 🛠️ **[Custom Tools][link-sample-custom]**: Writing your own guarded tools and publishing them as packs

## Documentation

Each release publishes build notes, the User Guide, requirements, a trace matrix, the software and verification design
documents, the code review plan and report, and a code quality report. Each NuGet package also ships its API reference
in its `api/` folder: an index, then a page per namespace, type, and member, with every example compiled against the
real API by the build.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup, coding
standards, and the pull request process, and review the [Code of Conduct][link-code-of-conduct].

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

## Support

- 🐛 **Report Bugs**: [GitHub Issues][link-issues]
- 💡 **Request Features**: [GitHub Issues][link-issues]

## Security

For security concerns and vulnerability reporting, see the [Security Policy][link-security-policy].

<!-- Image References: absolute URLs, because this README is also the NuGet package readme -->
[hero]: https://raw.githubusercontent.com/demaconsulting/AgentKit/main/docs/images/hero.png
[img-architecture]: https://raw.githubusercontent.com/demaconsulting/AgentKit/main/docs/images/architecture.png

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
[link-releases]: https://github.com/demaconsulting/AgentKit/releases
[link-samples]: https://github.com/demaconsulting/AgentKit/blob/main/samples/README.md
[link-sample-document]: https://github.com/demaconsulting/AgentKit/tree/main/samples/document-assistant
[link-sample-research]: https://github.com/demaconsulting/AgentKit/tree/main/samples/research-assistant
[link-sample-custom]: https://github.com/demaconsulting/AgentKit/tree/main/samples/custom-tools
[link-contributing]: https://github.com/demaconsulting/AgentKit/blob/main/CONTRIBUTING.md
[link-code-of-conduct]: https://github.com/demaconsulting/AgentKit/blob/main/CODE_OF_CONDUCT.md
[link-issues]: https://github.com/demaconsulting/AgentKit/issues
[link-security-policy]: https://github.com/demaconsulting/AgentKit/blob/main/SECURITY.md
[link-copilot-cli]: https://github.com/github/copilot-cli

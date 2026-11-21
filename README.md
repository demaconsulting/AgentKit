# AgentKit

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
![AgentKit banner: the AgentKit name with the tagline 'Every tool, guarded. Every chat, compacted.' and the C# call 'new ToolPackBuilder(policy).Add(new FilePack()).Build()'. A model's tool calls such as text_file_read on notes.md, the application's read-write and read-only path grants, tool limits such as a 64 KB read ceiling, and long chats flow on curved arrows into the AgentKit .NET library, and arrows lead out to what it produces: confined results where a permitted read returns and a path outside every grant is refused, a guarded tool list, a session compacted into a verbatim tail and three tiers of consolidated records, and agents on any provider such as an IChatClient or GitHub Copilot. A footnote reads 'Unsafe calls cannot be expressed.'][hero]

## Overview

AgentKit is a family of .NET libraries that give an AI agent hardened, provider-neutral tools that are
safe by construction: an unsafe operation is not refused at call time, it cannot be expressed. The
application decides which tools an agent receives and the controls they carry, and AgentKit makes each
tool behave predictably within them. It also gives the agent a session of its own that compacts its
context, so a long-running agent behaves the same way on every provider.

## Features

- 🛡️ **Safe by Construction**: Unsafe operations cannot be expressed, not merely refused at call time
- 🎛️ **Application-Set Controls**: Path grants, tool limits, and host capabilities bound every tool
- 🧰 **Ready-Made Tool Families**: Text file, file, Markdown, image, todo, memory, and agent delegation
- 🧩 **Custom Tools**: Build your own tools through the same guarded factory and publish them as packs
- 🔌 **Provider-Neutral**: Plain `AIFunction` tools for Microsoft Agent Framework, Copilot, and any `IChatClient`
- 🧠 **Self-Compacting Sessions**: Tiered context compaction keeps a long conversation alive on any provider
- 📏 **Honest Context Windows**: Sessions account against the window a provider really runs, Ollama included
- 🖼️ **Images That Arrive**: Tool-returned images reach the model even on providers that would drop them
- 🧪 **Testable Offline**: An in-memory provider session exercises the whole session lifecycle without a model
- 📚 **Agent-Readable API Reference**: Every package ships a gradually-disclosed Markdown API reference
- 🖥️ **Multi-Platform Support**: Builds and runs on Windows, Linux, and macOS, targeting .NET 8, 9, and 10
- 📋 **Continuous Compliance**: CI generates evidence using [Continuous Compliance][link-continuous-compliance]

## Installation

Install the core libraries and the adapter for your provider using the .NET CLI:

```bash
dotnet add package DemaConsulting.AgentKit.Core
dotnet add package DemaConsulting.AgentKit.Tools
dotnet add package DemaConsulting.AgentKit.Agents.ChatClient   # any IChatClient provider
```

For the GitHub Copilot runtime, add `DemaConsulting.AgentKit.Agents.Copilot` instead of the `IChatClient` adapter, and
reference `Microsoft.Copilot.SDK` 2.0.1 directly from the host application: the SDK provisions its runtime only into a
project that references it directly. On Ollama, also add `DemaConsulting.AgentKit.Agents.Ollama` so a session is told
the context window the instance is actually running.

## Usage

Decide what the agent may touch, decide which tools it receives, and hand both to a provider:

```csharp
using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

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

// A Microsoft Agent Framework agent over any IChatClient (OpenAI, Azure OpenAI, Ollama, ...).
var agent = ChatClientAgentFactory.Create(
    chatClient,
    tools,
    instructions: "You are a document assistant. Use the tools provided.");

var response = await agent.RunAsync("List the files in the workspace and summarize the largest one.");
Console.WriteLine(response.Text);
```

Every tool in that list observes the same policy: a request for a path outside `/workspace` is
refused with a reason the model can act on, never carried out. To keep a long conversation alive past
the model's context window, run the agent on a `CompactingAgentSession`, as the User Guide describes.

## Architecture

![AgentKit architecture: your application adds AgentKit.Tools packs, which are built through the AgentKit.Core tool safety contract that enforces its controls, and builds an agent or session through an AgentKit.Agents adapter, which feeds the Core session lifecycle: Agents.ChatClient builds on Microsoft Agent Framework and any IChatClient provider, Agents.Copilot provides its own AIAgent on the Microsoft Copilot SDK, and Agents.Ollama reads an Ollama server's window, none of which is part of AgentKit][img-architecture]

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

Each release publishes build notes, the User Guide, a code quality report, requirements,
justifications, and a trace matrix. Each NuGet package also ships its API reference in its `api/`
folder: an index, then a page per namespace, type, and member, with every example compiled against
the real API by the build.

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

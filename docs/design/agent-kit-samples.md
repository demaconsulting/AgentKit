# AgentKitSamples System Design

![AgentKit Samples Structure](AgentKitSamplesView.svg)

The AgentKitSamples system is the repository's set of runnable demonstration applications. Each
sample is a self-contained console application that exercises AgentKit end to end against a live
model, and each answers a different question a reader brings to the library: how to consume it, how
an agent works across turns, and how to extend it.

## Purpose

AgentKitSamples exists to make AgentKit legible by example. The samples are not a shipped library:
none is packed, published, or given an SBOM. They are delivered as source in the repository so that
a reader can read and run them. Because they are still locally-developed software with their own
test projects, they are modeled as a software system — but a system whose requirements are
*demonstration* requirements. A sample requirement states what the sample must prove to a reader
("the sample shall demonstrate X"), not a library capability ("the library shall do X"). Their
requirement style differs from the library systems precisely because their purpose is pedagogical:
what each sample must make observable.

The three samples are:

- **DocumentAssistant (Unit)** — the consumption path. It grants an agent a workspace to read and a
  separate session location to write, hands it the shipped tool packs, and prints every tool call so
  containment, asymmetric read/write grants, capability gating, the relative-versus-absolute path
  dialect, and the adapters' suppression of provider built-ins are all observable.
- **ResearchAssistant (Unit)** — the agent-infrastructure path. It composes the todo, memory, and
  agent families that let an agent plan, remember, and delegate across turns, over a read-only
  corpus and a separate writable notes location, and supplies its own offline embedding backend so a
  reader can run it with nothing installed.
- **CustomTools (Unit)** — the extension path. It shows an application author writing their own
  guarded tools and publishing them as packs that compose alongside a shipped pack.

## Architecture

The system is deliberately **flat**: three units sit directly under the system with no intervening
subsystem layer. Each unit is one sample application — a folder of several source files — rather than
a single class. Flatness follows the precedent stated for AgentKitCore in the design introduction: a
subsystem layer would add a requirements file, a design document, a verification document, and a
review set per subsystem without reducing the number of items anyone reviews. Grouping three
independent samples under a "samples" subsystem would carry no architectural information, because the
samples share no code and depend on each other in no way; each simply consumes the shipped AgentKit
packages as an ordinary application would.

The three samples share one recognizable shape — a tool set composed once, then a single
factory-selection switch choosing the provider runtime — so a reader who has read one recognizes the
others. That shared shape is a convention the samples follow, not a shared component; there is no
common library beneath them.

## External Interfaces

Each sample presents a command-line interface and a console chat loop to a human reader, and consumes
a model provider (GitHub Copilot or Ollama) over its respective transport. These interfaces exist to
be operated by a reader running the sample, not by another software item in this repository. The
command-line surface of the research-assistant sample is the most substantial and is documented in
*ResearchAssistant Unit Design*.

## Dependencies

The samples depend on the shipped AgentKit packages exactly as any consuming application would:

- **AgentKitCore** — supplies the guarded construction path, the path policy, the pack contract, and
  the pack builder; see *AgentKitCore System Design*.
- **AgentKitTools** — supplies the shipped tool families (text-file, file, Markdown, todo, memory,
  agent) the samples compose; see *AgentKitTools System Design*.
- **AgentKitAgentsChatClient** and **AgentKitAgentsCopilot** — supply the provider factories each
  sample selects between; see *AgentKitAgentsChatClient System Design* and *AgentKitAgentsCopilot
  System Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies the `AIFunction`/`IChatClient` currency the
  samples pass through.
- **Ollama** (via `OllamaSharp`) and **Microsoft.Agents.AI.GitHub.Copilot** — the two provider
  runtimes a sample selects between at run time.

The samples are the only place in the repository that composes all of these together, which is what
makes them end-to-end demonstrations rather than unit exercises.

## Risk Control Measures

N/A - the samples are demonstration applications, not a shipped medical or safety-related component,
and they introduce no risk control measure of their own. The safety properties they make observable
(containment, asymmetric grants, capability gating, child-agent isolation) are risk control measures
of the underlying AgentKit systems, documented in those systems' design documentation; the samples
demonstrate them rather than implement them.

## Data Flow

A run reads a command line, composes a policy over the granted locations, composes the shipped and
(for the custom-tools sample) author-written tool packs into a tool list, builds a provider agent
carrying that tool list and the run's instructions, and drives a console chat loop that prints each
tool call and result. The research-assistant sample additionally files findings into a memory store,
writes conclusions into a notes location, and, when asked, runs a final recall turn over the memory
store alone. No sample writes outside the locations its policy grants.

## Design Constraints

- **Platform**: the samples target `net10.0` only and are not a shipped multi-platform package.
  **This system deliberately has no `platform-requirements.yaml`.** Its absence is a design decision,
  not an oversight: platform requirements exist to record the operating systems a shipped, packaged
  system supports, and the samples are neither shipped nor packaged. A reader should not read the
  missing file as a gap.
- **Provider-neutrality within a sample**: each sample composes its tool set once, identically for
  every provider, and branches only to select a provider factory. This constraint is what lets a
  reader see that the safety model is a property of the composition, not of a particular provider.
- **No shared sample library**: the samples share a shape by convention but no code, so that each can
  be read in isolation as a complete worked example.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.

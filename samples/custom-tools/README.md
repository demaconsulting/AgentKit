# Custom Tools Sample

A console chat application that demonstrates how an **application author writes their own guarded
tools** with `GuardedToolFactory` and publishes them as packs — composed together with a shipped
pack, built into an agent, and exercised against a live model. Where the
[document-assistant sample](../document-assistant/) shows how to *consume* AgentKit's ready-made
tools, this sample shows how to *extend* AgentKit with tools of your own.

It runs unchanged against the GitHub Copilot runtime and against any tool-calling Ollama model, and
it prints every tool call and result so you can watch the custom tools behave exactly as a shipped
tool would.

## Two custom tools, two packs

The sample ships two author-written tools, each in its own pack. A pack declares a single family
prefix, and `ToolPackBuilder.Build` verifies that every tool the pack creates begins with that
prefix — which is why two tools from different families cannot share one pack.

### `docstats_wordcount` — a path-taking tool (the `docstats` pack)

Reports the word, line, and character counts of a text file. It is a worked example of an author
writing a tool that touches the file system, and it does so the same way a shipped tool does:

- **It is built through `GuardedToolFactory.Create`** — the only supported construction path — so it
  carries a validated name and a mandatory description, and its result is delivered to the runtime
  correctly.
- **It routes every path through `PathPolicy` for containment.** A path outside the granted
  workspace is refused; the tool never touches the file system on its own terms. A refusal is
  returned as `ToolResult.Denied` with an appropriate `DenialReason` (`PathNotPermitted` for a
  containment refusal, `TargetNotFound` for a missing file, `ResourceTooLarge` for a file over the
  policy's ceiling, `InvalidRequest` for a directory), never thrown.
- **It returns a structured result** via `ToolResult.Structured`, so the counts arrive as
  machine-readable fields — `path`, `words`, `lines`, and `characters` — rather than as prose a model
  would have to re-parse.
- **It reports file locations in the same path dialect the shipped tools use.** This is a specific
  goal of the sample: a third-party tool that reported paths in its own dialect would contradict the
  built-in tools in the same conversation. So the tool consumes the same public `PathPolicy` helpers
  the built-in tools do — `PathPolicy.IsDiscoveryRequest` to recognize a no-argument request,
  `PathPolicy.DiscoveryRoots` and `PathPolicy.WorkingDirectoryIsGranted` to describe the locations it
  can inspect and whether relative addressing is meaningful, and `PathPolicy.EmitRelative` to mirror
  the caller's dialect when a result path is reported.

### `clock_now` — a no-path tool (the `clock` pack)

Reports the current date and time, both local and in UTC, with the local time-zone identifier. It
takes **no path — indeed no arguments at all — and consults no `PathPolicy`**. It is the deliberate
contrast to the docstats tool: it demonstrates that `GuardedToolFactory` is the construction path for
*every* tool, not only path-based ones, and that a tool needing no policy simply does not accept or
consult one. Its pack accepts the policy the contract hands every pack and pointedly ignores it —
the honest expression of a tool that has nothing to ask a policy about.

## Composing custom packs alongside a shipped pack

The whole point is that an author-written pack composes exactly like a shipped one. All three packs
are added to the same `ToolPackBuilder`, on the same policy, before the provider is ever chosen:

```csharp
var policy = new PathPolicy(workspaceRoot, [PathRule.ReadWrite(workspaceRoot)]);

var builder = new ToolPackBuilder(policy)
    .Add(new TextFilePack())     // shipped
    .Add(new DocStatsToolPack()) // custom
    .Add(new ClockToolPack());   // custom

IList<AIFunction> tools = [.. builder.Build()];
```

`Build` verifies each pack's tool names carry its declared family prefix, so a malformed custom pack
fails here — at composition — rather than at a model's call.

Like the document-assistant sample, this one keeps **all provider knowledge inside a single
factory-selection switch**. The tool set is composed once, identically; only the final call that
builds the agent branches on provider, and everything after it — the session, the chat loop, the
streamed tool display — is provider-neutral.

## Running the sample

From the repository root. The workspace folder shipped with the sample already contains a
`sample.md` file.

Against GitHub Copilot (uses your logged-in Copilot CLI; no host or model flags needed):

```pwsh
dotnet run --project samples/custom-tools -- `
  --workspace samples/custom-tools/workspace `
  --provider copilot `
  --prompt "Count the words of sample.md, then tell me the current UTC time."
```

Against an Ollama server (any tool-calling model):

```pwsh
dotnet run --project samples/custom-tools -- `
  --workspace samples/custom-tools/workspace `
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.5:9b `
  --prompt "Count the words of sample.md, then tell me the current UTC time."
```

Omit `--prompt` to start an interactive chat instead. Type `exit` or `quit`, press Ctrl-C, or reach
end-of-input to leave.

## Options

| Option | Description |
| -------- | ------------- |
| `--workspace <path>` | Folder relative paths anchor to, granted read-write. **Required.** |
| `--provider copilot\|ollama` | Runtime to run the agent on. Default: `copilot`. |
| `--host <url>` | Ollama server URL. Default: `http://localhost:11434`. Ollama only. |
| `--model <name>` | Ollama model name. Default: `qwen3.5:9b`. Ollama only. |
| `--prompt "<text>"` | Run a single prompt and exit. Otherwise start an interactive chat. |
| `--help` | Show help and exit. |

## What to try

- **A structured count.** "Count the words of sample.md." The
  agent calls `docstats_wordcount` with `sample.md`; the result is a structured object naming the
  file's word, line, and character counts, with the file reported by its bare relative name because it
  lies inside the granted workspace.
- **A no-path tool.** "What time is it, in local and UTC?" The agent calls `clock_now` with no
  arguments and gets back a structured result — no path, no policy involved.
- **Containment.** "Count the words of ../outside.txt." The `docstats_wordcount` tool routes the
  path through `PathPolicy`, which refuses it, and returns `Denied (PathNotPermitted)`. The sibling
  file `outside.txt` exists precisely so this refusal is a real containment decision.
- **Discovery.** "What locations can the docstats tool inspect?" A no-argument `docstats_wordcount`
  call returns a structured discovery result listing the `inspectableLocations` and whether relative
  addressing applies — built from the same `PathPolicy` helpers the shipped tools use.
- **Composition with a shipped pack.** "Read sample.md, then count its words." The agent uses the
  shipped `text_file_read` and the custom `docstats_wordcount` together, proving the packs coexist.

## Requirements

- The .NET SDK matching the repository's `global.json`.
- For `--provider copilot`: a logged-in GitHub Copilot CLI on the machine.
- For `--provider ollama`: a reachable Ollama server hosting a model with tool-calling.

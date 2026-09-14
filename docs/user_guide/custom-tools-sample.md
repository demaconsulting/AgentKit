# Sample: Custom Tools

The repository ships a runnable sample under `samples/custom-tools`. Where the *document-assistant*
sample shows how to *consume* AgentKit's shipped tools, this
one shows how to *extend* AgentKit: how an application author writes their own guarded tools with
`GuardedToolFactory` and publishes them as packs, composed alongside a shipped pack and run against
either the GitHub Copilot runtime or an Ollama model. Refer to the sample's own README for the full
option reference and a catalogue of things to try.

## Two Custom Tools, Two Packs

The sample ships two author-written tools, each in its own pack. A pack declares a single family
prefix, and `ToolPackBuilder.Build` verifies that every tool the pack creates begins with that
prefix — which is why two tools from different families cannot share one pack.

The `docstats_wordcount` tool is a **path-taking** tool: it reports the word, line, and character
counts of a text file. It is written exactly as a shipped tool is — constructed through
`GuardedToolFactory`, routing every path through `PathPolicy` for containment, refusing with
`ToolResult.Denied` rather than throwing, and returning its findings through `ToolResult.Structured`
so the shape is machine-readable. Crucially, it reports file locations in the **same path dialect the
shipped tools use**, by consuming the same public `PathPolicy` helpers — `IsDiscoveryRequest`,
`DiscoveryRoots`, `WorkingDirectoryIsGranted`, and `EmitRelative`. That a third-party tool author can
reuse those helpers to stay consistent with the built-in tools is a deliberate demonstration of the
extensibility contract.

The `clock_now` tool is the **deliberate contrast**: it reports the current local and UTC time, takes
no path — no arguments at all — and consults no `PathPolicy`. It demonstrates that
`GuardedToolFactory` is the construction path for every tool, not only path-based ones, and that a
tool needing no policy simply does not consult one. Its pack accepts the policy the contract hands
every pack and pointedly ignores it.

## Composing Custom Packs Alongside a Shipped Pack

The point of the sample is that an author-written pack composes exactly like a shipped one. All three
packs are added to the same `ToolPackBuilder`, on the same policy, before the provider is chosen:

```csharp
var policy = new PathPolicy(workspaceRoot, [PathRule.ReadWrite(workspaceRoot)]);

var builder = new ToolPackBuilder(policy)
    .Add(new TextFilePack())     // shipped
    .Add(new DocStatsToolPack()) // custom
    .Add(new ClockToolPack());   // custom

IList<AIFunction> tools = [.. builder.Build()];
```

`Build` verifies each pack's tool names carry its declared family prefix, so a malformed custom pack
fails at composition rather than at a model's call. As in the document-assistant sample, all provider
knowledge stays inside a single factory-selection switch: the tool set is composed once, identically,
and only the final call that builds the agent branches on provider.

## Running the Sample

Run the sample from the repository root. Against the GitHub Copilot runtime:

```bash
dotnet run --project samples/custom-tools -- \
  --workspace samples/custom-tools/workspace \
  --provider copilot \
  --prompt "Count the words of sample.md, then tell me the current UTC time."
```

Against an Ollama server hosting a tool-calling model:

```bash
dotnet run --project samples/custom-tools -- \
  --workspace samples/custom-tools/workspace \
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.5:9b \
  --prompt "Count the words of sample.md, then tell me the current UTC time."
```

Only the `--provider` value changes what the sample does; the tools, the session, and the chat loop
are identical for both. Omitting `--prompt` starts an interactive chat that ends on `exit`, `quit`,
Ctrl-C, or end-of-input.

## Demonstrating Containment in a Custom Tool

Because the custom docstats tool routes its path through `PathPolicy` exactly as a shipped tool does,
containment applies to it identically. Point it at a path outside the workspace and watch it refuse:

```bash
dotnet run --project samples/custom-tools -- \
  --workspace samples/custom-tools/workspace \
  --provider copilot \
  --prompt "Count the words of ../outside.txt and show me exactly what the tool returns."
```

The `docstats_wordcount` tool returns a `Denied (PathNotPermitted)` result — a returned value, not a
crash — so the agent's turn continues and the model can act on the refusal. The sibling file
`outside.txt` exists precisely so this is a genuine containment decision rather than a staged one.

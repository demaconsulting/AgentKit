# Sample: Document Assistant

The repository ships a runnable sample under `samples/01-document-assistant` that puts the pieces
described earlier in this guide together into a working console application. It confines an agent to a
single workspace folder, hands it the text-file and image tool packs, and runs the same conversation
against either the GitHub Copilot runtime or an Ollama model. Refer to the sample's own README for the
full option reference and a catalogue of things to try.

## What the Sample Shows

The sample prints every tool call and every tool result as it happens, so the safety model is
something you watch rather than something you assume. In one short session you can observe each of the
guarantees this guide describes:

- **Containment.** Reading, writing, and listing are confined to the workspace folder. A path outside
  it is refused by the tool with guidance, not returned. The sample includes a sibling file outside
  the workspace so the refusal is a genuine containment decision rather than a staged one.
- **Capability gating.** The image tool appears only when the host declares the `Vision` capability.
  Running the sample with `--no-vision` removes `image_read` from the tools the model is offered — it
  is never presented, not merely refused.
- **Suppressed built-ins.** On the Copilot runtime the agent is offered only the tools the sample
  supplied; the runtime's own shell, fetch, and file-editing tools are suppressed by the adapter, as
  described under the Building an Agent material earlier in this guide.
- **Image delivery.** On an Ollama model reached through an `IChatClient`, a tool-returned image still
  reaches the model, because the adapter installs the image-promoting decorator automatically.

## Running Against Each Provider

Run the sample from the repository root. To run against the GitHub Copilot runtime, which uses the
logged-in Copilot CLI on the machine:

```bash
dotnet run --project samples/01-document-assistant -- \
  --workspace samples/01-document-assistant/workspace \
  --provider copilot \
  --prompt "List the files, read welcome.txt, then describe diagram.png"
```

To run against an Ollama server, naming the host and a tool-and-vision capable model:

```bash
dotnet run --project samples/01-document-assistant -- \
  --workspace samples/01-document-assistant/workspace \
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.5:9b \
  --prompt "List the files, read welcome.txt, then describe diagram.png"
```

Only the `--provider` value changes what the sample does; the tools, the session, and the chat loop
are identical for both. Omitting `--prompt` starts an interactive chat that retains conversation state
across turns and ends on `exit`, `quit`, Ctrl-C, or end-of-input.

## Demonstrating Capability Gating

The `--no-vision` flag omits both the image pack and the `Vision` host capability. Ask the agent to
list its tools with and without the flag to see the difference:

```bash
dotnet run --project samples/01-document-assistant -- \
  --workspace samples/01-document-assistant/workspace \
  --provider ollama --no-vision \
  --prompt "List every tool you have available by name."
```

With vision enabled the agent lists `text_file_read`, `text_file_write`, `text_file_list`, and
`image_read`; with `--no-vision` the image tool is absent, because the builder never asks a pack for
tools whose required capability the host has not declared.

## Demonstrating Containment

Point the agent at a path outside the workspace and watch the tool refuse it:

```bash
dotnet run --project samples/01-document-assistant -- \
  --workspace samples/01-document-assistant/workspace \
  --provider copilot \
  --prompt "Call text_file_read with the path ../outside-workspace.txt and show me exactly what it returns."
```

The `text_file_read` tool returns a `Denied (PathNotPermitted)` result whose message echoes the
requested path, states the location it was interpreted as, and names the permitted location — the
workspace folder — with its access level. The agent's turn continues normally — a refusal is a
value, not an error — so the model can act on the guidance rather than simply failing. Asking the
text tool to read the image instead produces a `Denied (UnsupportedMediaType)` result that redirects
the model to `image_read`.

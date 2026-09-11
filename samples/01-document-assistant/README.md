# Document Assistant Sample

A console chat application that confines an AI agent to a single workspace folder and hands it the
shipped AgentKit tool packs: reading text files, listing them, and — when vision is enabled — looking
at images. It is the first end-to-end demonstration of the AgentKit tools against live models, and it
runs unchanged against the GitHub Copilot runtime and against any Ollama model.

## What it demonstrates

This sample exists to make the AgentKit safety model *observable*. It prints every tool call and its
result as they happen, so you can watch the guarantees hold rather than take them on faith:

- **Containment by construction.** Every read, write, and listing is confined to the workspace folder
  you name with `--workspace`. A path outside it is not merely discouraged — the tool refuses it and
  returns guidance instead. You can point the agent at `../outside-workspace.txt` and watch the
  refusal.
- **Capability-gated tools.** The image tool is offered only when the host declares the `Vision`
  capability. Run with `--no-vision` and `image_read` is not refused at call time — it is never
  presented to the model at all.
- **Provider quirks absorbed by the adapters.** The Copilot runtime ships its own shell, fetch, and
  file-editing tools; the adapter suppresses them so the agent is offered only the tools you gave it.
  An `IChatClient` provider silently drops a tool-returned image at the wire; that adapter repairs it
  so the model actually sees the picture. Neither behavior is written in this sample — both live in
  the shipped packages.
- **One tool set, two providers.** The tools are composed once. The *only* place the sample branches
  on provider is the single factory call that builds the agent. Everything after — the session, the
  chat loop, the streamed tool display — is identical.

## Why the tools are safe

The safety does not come from asking the model to behave. It comes from what the model is able to
express:

- `new PathPolicy(workspace, [PathRule.ReadWrite(workspace)])` anchors relative paths at the
  workspace and grants that same folder read-write. The working directory (the relative anchor) and
  the grant (the permission) are separate ideas; here one folder plays both roles. A model asks for
  `notes.txt`, and that name resolves inside the workspace; an absolute or `..`-escaping path resolves
  to a location the policy refuses.
- Containment resolves symbolic links and junctions at every path component, so a path that merely
  looks contained cannot reach outside the folder you granted.
- A refusal is a returned value with useful guidance, not a crash: the agent's turn continues and the
  model is told how to phrase a permitted request.
- The image family is only ever created when `Vision` is declared, so a model without eyes is never
  offered a tool whose image result it could only describe from nothing.

Note that the text-file pack includes `text_file_write`, so the agent can also create or modify files
**within the workspace**. That is safe — writes are contained by the same policy — but it is a real
capability, not a read-only view, and the system instructions acknowledge it.

## Running the sample

From the repository root. The workspace folder shipped with the sample already contains two text files
and an image.

Against GitHub Copilot (uses your logged-in Copilot CLI; no host or model flags needed):

```pwsh
dotnet run --project samples/01-document-assistant -- `
  --workspace samples/01-document-assistant/workspace `
  --provider copilot `
  --prompt "List the files, read welcome.txt, then describe diagram.png"
```

Against an Ollama server (any tool-and-vision capable model):

```pwsh
dotnet run --project samples/01-document-assistant -- `
  --workspace samples/01-document-assistant/workspace `
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.5:9b `
  --prompt "List the files, read welcome.txt, then describe diagram.png"
```

Omit `--prompt` to start an interactive chat instead. The conversation keeps its thread state across
turns, so you can refer back to earlier answers. Type `exit` or `quit`, press Ctrl-C, or reach
end-of-input to leave.

## Options

| Option | Description |
| -------- | ------------- |
| `--workspace <path>` | Folder the agent is confined to. **Required.** |
| `--provider copilot\|ollama` | Runtime to run the agent on. Default: `copilot`. |
| `--host <url>` | Ollama server URL. Default: `http://localhost:11434`. Ollama only. |
| `--model <name>` | Ollama model name. Default: `qwen3.5:9b`. Ollama only. |
| `--no-vision` | Omit the image tool and the `Vision` capability entirely. |
| `--prompt "<text>"` | Run a single prompt and exit. Otherwise start an interactive chat. |
| `--help` | Show help and exit. |

## What to try

- **A plain relative read.** "Read welcome.txt and summarize it." The agent calls `text_file_read`
  with `welcome.txt`, resolved inside the workspace.
- **Listing.** "What files are here?" The agent calls `text_file_list` and reports `welcome.txt`,
  `notes.txt`, and `diagram.png`.
- **Vision.** "Describe diagram.png." `diagram.png` shows a red circle, a blue square, and a green
  triangle above the verification code `FALCON-4297`. See
  [Verifying vision honestly](#verifying-vision-honestly) below — reading that code back *verbatim*
  is the check that distinguishes real vision from a plausible guess, and it needs a capable model.
- **The denial.** "Read ../outside-workspace.txt." The `text_file_read` tool returns
  `Denied (PathNotPermitted)` with guidance to use a path inside the workspace. The sibling file
  `outside-workspace.txt` exists precisely so this refusal is a real containment decision.
- **The binary guard.** "Use text_file_read on diagram.png." The text tool returns
  `Denied (UnsupportedMediaType)` and redirects the model to `image_read`.
- **Suppressed built-ins.** "List your tools. Do you have a shell or web-fetch tool?" The agent
  reports only the workspace file tools and answers *no* — confirming the Copilot runtime's built-in
  tools are suppressed.
- **Capability gating.** Add `--no-vision` and ask the agent to list its tools: `image_read` is gone.

## Verifying vision honestly

`diagram.png` carries the printed code `FALCON-4297` so you can check the model's answer against
something objective. Asking a model to read it back word for word is the only way to tell genuine
vision from a confident, plausible-sounding description.

That check needs a model that can actually read small embedded text. The sample's default,
`qwen3.5:9b`, is fast and entirely adequate for reading text files, listing, the denial, and the
binary guard — but it is **not** reliable at OCR. It will consistently identify the three shapes and
their colors, which already proves the image genuinely reached it, while garbling the printed code
into a similar-looking but different string.

For the verbatim check, use a larger model:

```pwsh
dotnet run --project samples/01-document-assistant -- `
  --workspace samples/01-document-assistant/workspace `
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.8:27b `
  --prompt "Look at diagram.png and tell me the shapes, their colors, and every line of text printed on it exactly as written."
```

That returns the three shapes, their colors, and `FALCON-4297` exactly as printed.

The distinction is worth being precise about, because it marks the boundary of what AgentKit
guarantees. `image_read` either delivers the exact bytes of a permitted file to the model or refuses
— and when it delivers them, it has done its job completely. Whether the model can then *read* small
text in those bytes is a property of the model, not of the tool. AgentKit guarantees which files an
agent may touch and in what form it receives them; it cannot and does not guarantee the model's
perception. A weak OCR result is a reason to choose a stronger model, never a sign the tool failed.

## About the fixture image

`workspace/diagram.png` is a small (512×512) programmatically generated PNG: three high-contrast
primary-color shapes and large embedded text including the verification code `FALCON-4297`. It was
produced once with Python Pillow (canvas, three shapes via `ImageDraw`, and text via a bold TrueType
font) and committed as a fixture. The embedded, checkable code is deliberate: it lets a reader confirm
the vision path genuinely delivered the image to the model instead of accepting a confident guess.

## Requirements

- The .NET SDK matching the repository's `global.json`.
- For `--provider copilot`: a logged-in GitHub Copilot CLI on the machine.
- For `--provider ollama`: a reachable Ollama server hosting a model with tool-calling (and vision, if
  you want the image demo).

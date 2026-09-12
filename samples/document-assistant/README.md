# Document Assistant Sample

A console chat application that grants an AI agent exactly two locations — a **workspace** folder of
documents to read, and a separate **session** folder to write its artifacts into — and hands it the
shipped AgentKit tool packs: reading text files, listing them, writing text files, and, when vision is
enabled, looking at images. It is the first end-to-end demonstration of the AgentKit tools against live
models, and it runs unchanged against the GitHub Copilot runtime and against any Ollama model.

## Two locations, one anchor

Two locations is the *normal* shape of an application, not an exotic one. Essentially every real
application has a folder of the user's work plus somewhere of its own to put what the agent produces.
The sample is built that way so the behaviors that only appear with more than one location are visible
rather than theoretical.

AgentKit keeps two ideas strictly separate, and the sample shows both:

- **Working directory** — the one location a relative path resolves against. It is *not* a
  permission: anchoring grants nothing.
- **Access grant** — a permitted location carrying `ReadOnly` or `ReadWrite`. It is *not* an
  address: granting anchors nothing.

The sample composes them like this:

```csharp
var grants = workspaceReadOnly
    ? new[] { PathRule.ReadOnly(workspaceRoot),  PathRule.ReadWrite(sessionRoot) }
    : new[] { PathRule.ReadWrite(workspaceRoot), PathRule.ReadWrite(sessionRoot) };

var policy = new PathPolicy(workspaceRoot, grants);
```

The workspace is the working directory **and** a granted location — two separate decisions that happen
to name the same folder. The session folder is granted but is never an anchor. The application grants
the working directory whatever access it should have, exactly as it grants anything else; an
application whose working directory is granted nothing at all is equally valid and equally coherent.

**Where the session folder lives is the sample's choice, not an AgentKit convention.** AgentKit has no
opinion about session folders; an application supplies whatever locations it likes. This sample
defaults to a `document-assistant-session` folder beneath the system temporary directory — chosen
because it is unambiguously outside the workspace and keeps the repository clean — creates it if it is
absent, prints it at startup, and lets you override it with `--session <path>`.

### The dialect consequence

Because the workspace is the anchor **and** is granted, results inside it come back as bare relative
names (`welcome.txt`). Results in the session folder lie outside the anchor, so a relative name could
not truthfully name them and they come back as **absolute paths**. You see both dialects in one
session.

This is also the **transition hazard** worth knowing before you build your own application: an
application that starts with a single granted working directory gets the relative dialect throughout,
and the moment it adds a second location, output for that location switches to the absolute dialect.
Nothing else warns you. It is documented on `PathPolicy`'s constructor, and the sample exists partly to
let you watch it happen.

### Addressing the session folder correctly

**The sample's system instructions name both locations outright** — the workspace with its access
level, and the session folder as read-write — because the application resolved both paths before it
composed anything, so it is the authoritative source for them. The instructions are therefore built
per run rather than held as a constant string.

A no-argument `text_file_list` is still requested, and it is still a **discovery** request: it
reports every permitted location as an absolute header with the file names beneath them, and an empty
location under its header with a marker naming its access level. It is worth watching, because it is
what establishes the two path dialects. It is simply not the *only* way the agent learns where it may
write. Discovery reports even an empty session folder, so an agent could learn its path that way — but
naming both locations up front removes a round-trip and puts the session path in the agent's hands on
its very first turn, before it has listed anything. The sample therefore tells the truth up front
rather than relying on discovery alone.

Write with the **full absolute path** of the session folder. A relative name is always interpreted
against the workspace, so `session/summary.md` would target a `session` subfolder *inside the
workspace* — not the session location. A single bare segment matching a grant's last segment does
alias to that grant, but only after workspace interpretation finds nothing existing, so it is not a
form to rely on.

## What it demonstrates

This sample exists to make the AgentKit safety model *observable*. It prints every tool call and its
result as they happen, so you can watch the guarantees hold rather than take them on faith:

- **Containment by construction.** Every read, write, and listing is confined to the two granted
  locations. A path outside them is not merely discouraged — the tool refuses it and returns guidance
  instead. You can point the agent at `../outside-workspace.txt` and watch the refusal.
- **Asymmetric grants.** Run with `--read-only-workspace` and the user's documents become readable but
  unmodifiable while the session folder stays writable. A write into the workspace is refused, and the
  refusal *enumerates every permitted location with its access level*, so the agent learns where it may
  write and recovers by writing there instead of simply failing.
- **Cross-location work.** The agent reads from the workspace and writes an artifact into the session
  folder — the ordinary shape of real work, and the case that makes the two dialects visible.
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

- The policy shown above decides every path question. A model asks for `notes.txt`, and that name
  resolves against the workspace; an absolute or `..`-escaping path resolves to a location the policy
  refuses. A write is permitted only where a `ReadWrite` grant covers it, so a read-only workspace is
  genuinely unmodifiable no matter how the request is phrased.
- Containment resolves symbolic links and junctions at every path component, so a path that merely
  looks contained cannot reach outside the locations you granted.
- A refusal is a returned value with useful guidance, not a crash: the agent's turn continues, the
  refusal echoes what was asked, states how it was interpreted when a relative path was joined, and
  lists every permitted location with its access level.
- The image family is only ever created when `Vision` is declared, so a model without eyes is never
  offered a tool whose image result it could only describe from nothing.

Note that the text-file pack includes `text_file_write`, so the agent can create or modify files
wherever a `ReadWrite` grant permits. That is safe — writes are contained by the same policy — but it
is a real capability, not a read-only view, and the system instructions acknowledge it.

## Running the sample

From the repository root. The workspace folder shipped with the sample already contains two text files
and an image. The session folder is created for you.

Against GitHub Copilot (uses your logged-in Copilot CLI; no host or model flags needed):

```pwsh
dotnet run --project samples/document-assistant -- `
  --workspace samples/document-assistant/workspace `
  --provider copilot `
  --prompt "List the files, read welcome.txt, then describe diagram.png"
```

Against an Ollama server (any tool-and-vision capable model):

```pwsh
dotnet run --project samples/document-assistant -- `
  --workspace samples/document-assistant/workspace `
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.5:9b `
  --prompt "List the files, read welcome.txt, then describe diagram.png"
```

Omit `--prompt` to start an interactive chat instead. The conversation keeps its thread state across
turns, so you can refer back to earlier answers. Type `exit` or `quit`, press Ctrl-C, or reach
end-of-input to leave.

## Options

| Option | Description |
| -------- | ------------- |
| `--workspace <path>` | Folder relative paths anchor to, and which is granted. **Required.** |
| `--session <path>` | Folder for agent-written artifacts. Granted read-write; created if absent. |
| `--read-only-workspace` | Grant the workspace read-only; the session folder stays writable. |
| `--provider copilot\|ollama` | Runtime to run the agent on. Default: `copilot`. |
| `--host <url>` | Ollama server URL. Default: `http://localhost:11434`. Ollama only. |
| `--model <name>` | Ollama model name. Default: `qwen3.5:9b`. Ollama only. |
| `--no-vision` | Omit the image tool and the `Vision` capability entirely. |
| `--prompt "<text>"` | Run a single prompt and exit. Otherwise start an interactive chat. |
| `--help` | Show help and exit. |

The default session folder is `document-assistant-session` beneath the system temporary directory.

## What to try

- **Discovery.** "List every location you can reach and say which you can write to." The agent calls
  `text_file_list` with no argument and reports every permitted location as an absolute header, with
  the workspace files beneath the workspace header and an empty session folder shown under its header
  with an access-level marker. It also knows about both locations from the system instructions, which
  name them.
- **A plain relative read.** "Read welcome.txt and summarize it." The agent calls `text_file_read`
  with `welcome.txt`, resolved inside the workspace.
- **Cross-location work.** "Read welcome.txt, then save a summary into the session folder." Watch the
  agent write with the absolute session path it was given in its instructions, and get that absolute
  path back — the relative dialect it used for the workspace does not apply there.
- **Asymmetric grants.** Add `--read-only-workspace` and ask "Summarize welcome.txt into summary.md
  next to it." The `text_file_write` tool returns `Denied` and enumerates both locations with their
  access levels; the agent should then write into the session folder instead. This is the highest-value
  thing in the sample: the denial is what lets the agent recover correctly.
- **Vision.** "Describe diagram.png." `diagram.png` shows a red circle, a blue square, and a green
  triangle above the verification code `FALCON-4297`. See
  [Verifying vision honestly](#verifying-vision-honestly) below — reading that code back *verbatim*
  is the check that distinguishes real vision from a plausible guess, and it needs a capable model.
- **The denial.** "Read ../outside-workspace.txt." The `text_file_read` tool returns
  `Denied (PathNotPermitted)` with guidance and the permitted locations. The sibling file
  `outside-workspace.txt` exists precisely so this refusal is a real containment decision.
- **The binary guard.** "Use text_file_read on diagram.png." The text tool returns
  `Denied (UnsupportedMediaType)` and redirects the model to `image_read`.
- **Suppressed built-ins.** "List your tools. Do you have a shell or web-fetch tool?" The agent
  reports only the file tools and answers *no* — confirming the Copilot runtime's built-in
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
dotnet run --project samples/document-assistant -- `
  --workspace samples/document-assistant/workspace `
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

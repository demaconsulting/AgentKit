# Document Assistant Sample

A console chat application that grants an AI agent exactly two locations — a **workspace** folder of
documents to read, and a separate **session** folder to write its artifacts into — and hands it the
shipped AgentKit tool packs: searching, reading, creating, and editing text files; listing, copying,
moving, and deleting files of any type; outlining a Markdown file's headings; and, when vision is
enabled, looking at images and cutting a region out of one into a file it can reference; and drawing
diagrams — a flowchart of a document it has read, checked and saved as a picture. It is the
first end-to-end demonstration of the AgentKit tools against live
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

A no-argument `file_list` is still requested, and it is still a **discovery** request: it
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
result as they happen, so you can watch each behavior below rather than take it on faith:

- **Containment to the granted locations.** Every read, write, and listing is confined to the two granted
  locations. A path outside them is not merely discouraged — the tool refuses it and returns guidance
  instead. You can point the agent at `../outside-workspace.txt` and watch the refusal.
- **Asymmetric grants.** Run with `--read-only-workspace` and the user's documents become readable but
  unmodifiable while the session folder stays writable. A write into the workspace is refused, and the
  refusal *enumerates every permitted location with its access level*, so the agent learns where it may
  write and recovers by writing there instead of simply failing.
- **Cross-location work.** The agent reads from the workspace and writes an artifact into the session
  folder — the ordinary shape of real work, and the case that makes the two dialects visible.
- **Both grants in one call.** With a `destination`, `image_crop` reads the image under the workspace
  grant and writes the region it cut under the session folder's write grant. Under
  `--read-only-workspace` a destination in the workspace is refused while the same call into the
  session folder succeeds — one tool call, two independent decisions.
- **Capability-gated tools.** The image tools, from the `DemaConsulting.AgentKit.Tools.Visual` package, are offered only
  when the host declares the `Vision` capability. Run with `--no-vision` and none of `image_read`, `image_crop`,
  `image_auto_crop` or `image_view` are refused at call time — they are never presented to the model at all.
- **Diagrams that follow the host.** The diagram tools, from the same package, are offered either way:
  with vision the model is shown each drawing, and with `--no-vision` each drawing is saved and the
  model is told what it contains instead. The pack's own instructions paragraph is appended to the
  sample's instructions by the sample — the pack never adds it itself. `--no-diagrams` removes the
  diagram tools.
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

Note that the text-file pack includes `text_file_create` and `text_file_replace`, so the agent can
create new files and edit existing ones wherever a `ReadWrite` grant permits. That is safe — writes
are contained by the same policy — but it is a real capability, not a read-only view, and the system
instructions acknowledge it.

## Running the sample

From the repository root. The workspace folder shipped with the sample already contains three text
files — one of them `release-process.md`, the document the diagram demonstration draws — and an
image. The session folder is created for you.

Against GitHub Copilot (no host flag needed, and `--model` is optional; see [Requirements](#requirements)
for the Copilot CLI and signing in):

```pwsh
dotnet run --project samples/document-assistant -- `
  --workspace samples/document-assistant/workspace `
  --provider copilot `
  --prompt "List the files, read welcome.txt, then describe diagram.png"
```

Add `--model <name>` to choose which Copilot model backs the agent; omit it and the Copilot runtime
picks its own default:

```pwsh
dotnet run --project samples/document-assistant -- `
  --workspace samples/document-assistant/workspace `
  --provider copilot --model gpt-5.4-mini `
  --prompt "List the files, read welcome.txt, then describe diagram.png"
```

The same `--model` flag serves both providers, because "which model backs this agent" is the same
question on either runtime. It is worth exercising: model capability drives how reliably an agent
uses its tools and how accurately it reads an image — see *Verifying vision honestly* below.

Against an Ollama server (any tool-and-vision capable model):

```pwsh
dotnet run --project samples/document-assistant -- `
  --workspace samples/document-assistant/workspace `
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.5:9b `
  --prompt "List the files, read welcome.txt, then describe diagram.png"
```

### Choosing a model

The sample is a vision application, so it shows and accepts only models that can see the images
`image_read` returns. `--list-models` prints the models the provider offers that can run the sample,
grouped by the provider's category, with the reasoning efforts and context tiers each one accepts —
the list an application's model picker would show:

```pwsh
dotnet run --project samples/document-assistant -- --provider ollama --host http://your-ollama-host:11434 --list-models
```

`--reasoning-effort`, `--context-tier` (Copilot only) and `--context-window` (Ollama only) choose how
the model runs. The Copilot agent the sample builds is accounted against no window, so there is
nothing for `--context-window` to cap there, and it is a usage error with `--provider copilot`. The
choice is checked against the provider's catalog before the session starts, so a model that
cannot see, an effort the model does not offer, or a window larger than an Ollama model was trained
for is refused with every reason at once rather than failing part-way through a conversation:

```pwsh
dotnet run --project samples/document-assistant -- `
  --workspace samples/document-assistant/workspace `
  --provider ollama --host http://your-ollama-host:11434 --model gemma4:e4b `
  --reasoning-effort none --context-window 8192 `
  --prompt "Describe diagram.png"
```

Whether the image tools are offered follows from the model that was selected rather than from a
setting: the selected model's vision capability becomes the host's vision declaration. On Copilot
with no `--model`, nothing describes the runtime's own choice, so the sample declares vision itself —
the explicit declaration a catalog never takes away. `--no-vision` still removes the image tools.

Omit `--prompt` to start an interactive chat instead. The conversation keeps its thread state across
turns, so you can refer back to earlier answers. Type `exit` or `quit`, press Ctrl-C, or reach
end-of-input to leave.

If the provider stops responding in time — for example, Ollama does not answer within the sample's
client timeout, two minutes when listing models and ten during a chat — the sample prints the reason
to standard error and exits with code 1; Ctrl-C still ends the run cleanly with code 0.

## Options

| Option | Description |
| --- | --- |
| `--workspace <path>` | Folder relative paths anchor to, and which is granted. **Required.** |
| `--session <path>` | Folder for agent-written artifacts. Granted read-write; created if absent. |
| `--read-only-workspace` | Grant the workspace read-only; the session folder stays writable. |
| `--provider copilot\|ollama` | Runtime to run the agent on. Default: `copilot`. |
| `--host <url>` | Ollama server URL. Default: `http://localhost:11434`. Ollama only. |
| `--model <name>` | Model to back the agent. Default: the Copilot runtime's own choice, or `qwen3.5:9b` for Ollama. |
| `--no-vision` | Omit the image tools and `Vision`; stop requiring a vision model. Diagrams switch to text. |
| `--no-diagrams` | Omit the diagram tools (`diagram_help`, `diagram_render`) and their instructions paragraph. |
| `--list-models` | List the models that can run the sample, grouped by category, and exit. No `--workspace` needed. |
| `--reasoning-effort <id>` | Reasoning effort, from the model's list. Validated before the session starts. |
| `--context-tier <id>` | Context tier, from the model's list. Copilot only. |
| `--context-window <n>` | Tokens: the exact `num_ctx` (at most the trained length). Ollama only. |
| `--prompt "<text>"` | Run a single prompt and exit. Otherwise start an interactive chat. |
| `--help` | Show help and exit. |

The default session folder is `document-assistant-session` beneath the system temporary directory.

## What to try

- **Discovery.** "List every location you can reach and say which you can write to." The agent calls
  `file_list` with no argument and reports every permitted location as an absolute header, with
  the workspace files beneath the workspace header and an empty session folder shown under its header
  with an access-level marker. It also knows about both locations from the system instructions, which
  name them.
- **A plain relative read.** "Read welcome.txt and summarize it." The agent calls `text_file_read`
  with `welcome.txt`, resolved inside the workspace.
- **Cross-location work.** "Read welcome.txt, then save a summary into the session folder." Watch the
  agent write with the absolute session path it was given in its instructions, and get that absolute
  path back — the relative dialect it used for the workspace does not apply there.
- **Asymmetric grants.** Add `--read-only-workspace` and ask "Summarize welcome.txt into summary.md
  next to it." The `text_file_create` tool returns `Denied` and enumerates both locations with their
  access levels; the agent should then write into the session folder instead. This is the highest-value
  thing in the sample: the denial is what lets the agent recover correctly.
- **Vision.** "Describe diagram.png." `diagram.png` shows a red circle, a blue square, and a green
  triangle above the verification code `FALCON-4297`. See
  [Verifying vision honestly](#verifying-vision-honestly) below — reading that code back *verbatim*
  is the check that distinguishes real vision from a plausible guess, and it needs a capable model.
- **Figure preparation.** "Look at diagram.png, crop the region holding the printed code into the
  session folder as code.png, then write notes.md there referencing it." The agent reads the image,
  aims a region in the coordinate space `image_read` reported, and calls `image_crop` with the
  session folder's absolute path as the destination. What comes back is a *confirmation naming that
  path* rather than the picture — which is exactly what the agent needs, because the next thing it
  writes is a Markdown file pointing at that name. Whether it aims the region well is a property of
  the model, in the same way reading the code back is; see *Verifying vision honestly* below.
- **The write grant, on a crop.** Add `--read-only-workspace` and ask for the same crop written
  *beside* the image instead. `image_crop` reads the image and refuses the destination, enumerating
  the writable location — one call, two independent policy decisions.
- **The destination rules.** Ask for the crop as `code.jpg`, or at a name that already exists. The
  first is refused because a region is always encoded as PNG and a file named otherwise would be one
  every later reader is entitled to misread; the second because this tool writes a new file and does
  not replace one.
- **The denial.** "Read ../outside-workspace.txt." The `text_file_read` tool returns
  `Denied (PathNotPermitted)` with guidance and the permitted locations. The sibling file
  `outside-workspace.txt` exists precisely so this refusal is a real containment decision.
- **The binary guard.** "Use text_file_read on diagram.png." The text tool returns
  `Denied (UnsupportedMediaType)` and redirects the model to `image_read`.
- **Suppressed built-ins.** "List your tools. Do you have a shell or web-fetch tool?" The agent
  reports only the file tools and answers *no* — confirming the Copilot runtime's built-in
  tools are suppressed.
- **Capability gating.** Add `--no-vision` and ask the agent to list its tools: `image_read`,
  `image_crop`, `image_auto_crop` and `image_view` are gone, while `diagram_help` and
  `diagram_render` remain in text preview. Add `--no-diagrams` and those two are gone too.
- **Diagramming a document.** "Diagram the release process described in release-process.md." See
  [Diagramming a document](#diagramming-a-document) below.

## Diagramming a document

An agent that writes documentation often needs a figure it can only describe in words. The diagram
tools let it draw one from source it writes, look at the result, fix it and save it:

```pwsh
dotnet run --project samples/document-assistant -- `
  --workspace samples/document-assistant/workspace `
  --provider ollama --host http://your-ollama-host:11434 --model gemma4:26b `
  --prompt "Diagram the release process described in release-process.md."
```

The instructions the sample builds carry two parts. The first is `DiagramPack.Instructions`, the
paragraph the pack offers for its preview mode, which the sample appends itself — the pack never
adds it anywhere. The second is the sample's own description of the workflow, and the printed tool
calls follow it:

1. The agent reads `release-process.md` (`markdown_outline` shows its sections).
2. It may read a `diagram_help` topic for the diagram type, for example the Mermaid flowchart card.
3. It writes the Mermaid source into the session folder as `release-process.mmd`, so the source
   can be edited later.
4. It calls `diagram_render` with the source and `save_as` set to the session folder's absolute path
   ending in `release-process.png`, looks at the picture that comes back, and fixes the source if
   the drawing is wrong. A source that cannot be drawn comes back with the line, the column and a
   help topic to read, rather than a picture.
5. It links the saved picture from the Markdown it writes.

Expected outputs in the session folder: `release-process.mmd` and `release-process.png`. Saving
again under the same name replaces the earlier picture, so the agent can save on every attempt.
Both files go to the session folder because it is the only location writable in both workspace
modes.

With `--no-vision` the same scenario runs in text preview: `diagram_render` requires `save_as`, and
instead of the picture the model receives its size, the labels and features it contains and its
dominant colors — enough to check that every step of the release process appears. Whether the
flowchart is a good one is a property of the model; the tools guarantee only where it may be
written and that what was saved is what was drawn. Open `release-process.png` to judge it.

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

The same applies on Copilot: `--model` selects the model there too, and Copilot models differ from
one another in OCR accuracy just as Ollama models do.

## About the fixture image

`workspace/diagram.png` is a small (512×512) programmatically generated PNG: three high-contrast
primary-color shapes and large embedded text including the verification code `FALCON-4297`. It was
produced once with Python Pillow (canvas, three shapes via `ImageDraw`, and text via a bold TrueType
font) and committed as a fixture. The embedded, checkable code is deliberate: it lets a reader confirm
the vision path genuinely delivered the image to the model instead of accepting a confident guess.
It is also the only fixture the figure-preparation demonstration needs: the region holding
`FALCON-4297` is legible on its own once cut out, so the crop's destination file is itself checkable
in exactly the same way.

## Requirements

- The .NET SDK matching the repository's `global.json`.
- For `--provider copilot`:
  - The GitHub Copilot CLI, version 1.0.113 or later, installed by `winget install GitHub.Copilot`,
    `brew install --cask copilot-cli`, `npm install -g @github/copilot` or the install script.
    AgentKit finds it on `PATH` or where those installers put it, and runs the version built into the
    installed executable, so check it with `copilot --no-auto-update --version` (`copilot --version`
    can report a newer version that `copilot update` downloaded, which AgentKit does not run; only
    upgrading the installation changes it). Nothing is downloaded at build time and the sample ships no
    runtime.
  - A GitHub user with Copilot access signed in with the GitHub CLI (`gh auth login`, with `gh` on `PATH`), or
    a token in `GH_TOKEN`. Signing in to the Copilot CLI itself is **not** enough: AgentKit keeps the
    runtime's credentials in its own data directory and does not use the CLI's sign-in.
  - Nothing about the shell: AgentKit starts the runtime with the shell's `COPILOT_`/`GITHUB_COPILOT_`
    variables withheld apart from an allow-listed credential, network and telemetry-off set, so the sample
    runs from a Copilot CLI shell too. The runtime keeps its state in
    `AgentKit/document-assistant/copilot-runtime` under the user's local application data.
  - To run without an installed CLI, see *Bundling the Runtime Instead* in the user guide: pass
    `runtimeSource: CopilotRuntimeSource.Bundled` in `AgentComposition.cs` and reference
    `Microsoft.Copilot.SDK` in the sample's project, which then downloads the runtime at build time.
- For `--provider ollama`: a reachable Ollama server hosting a model with tool-calling (and vision, if
  you want the image demo).

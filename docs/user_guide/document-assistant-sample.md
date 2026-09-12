# Sample: Document Assistant

The repository ships a runnable sample under `samples/01-document-assistant` that puts the pieces
described earlier in this guide together into a working console application. It grants an agent two
locations — a workspace folder of documents and a separate session folder for the artifacts the agent
produces — hands it the text-file and image tool packs, and runs the same conversation against either
the GitHub Copilot runtime or an Ollama model. Refer to the sample's own README for the full option
reference and a catalogue of things to try.

## Two Locations, One Anchor

Two locations is the ordinary shape of an application, not an exceptional one: there is nearly always
a folder of the user's work and somewhere of the application's own to put results. The sample is built
that way because several behaviors only exist once there is more than one location.

It keeps the two ideas this guide describes strictly apart. The workspace is passed as the policy's
**working directory**, which makes it the single anchor a relative path is resolved against and
nothing else; it is then **granted** separately, at read-write by default or read-only with
`--read-only-workspace`. The session folder is granted read-write and is never an anchor. An
application grants its working directory whatever access it should have, exactly as it grants any
other location — and an application whose working directory is granted nothing at all is equally
valid.

Where the session folder lives is the **sample's** choice. AgentKit has no session-folder convention
and never creates one; an application supplies whatever locations it likes. The sample defaults to a
`document-assistant-session` folder beneath the system temporary directory, creates it if it is
absent, prints it at startup, and accepts `--session <path>` to override it.

One consequence is visible in every run. Because the workspace is both the anchor and granted, results
inside it are reported as bare relative names; results in the session folder lie outside the anchor, so
they are reported as absolute paths. That is the transition hazard described earlier in this guide,
happening live: an application that adds a second location moves from the relative dialect to the
absolute one for that location, and nothing else announces it.

A no-argument `text_file_list` is a discovery request — it reports every permitted location as an
absolute header, with any files beneath it, and an empty location shown under its header with a
marker naming its access level — and the sample's system instructions still direct the model to list
before it writes, because that listing is what establishes the two dialects. The instructions also
**name both locations outright**, with the workspace's access level and the session folder's absolute
path, and are built per run rather than held as a constant for that reason. That is deliberate rather
than redundant: an application knows its own locations, and stating them up front is the robust design
— it removes a discovery round-trip and puts the session folder's absolute path in the agent's hands
on its very first turn, before it has listed anything. Discovery would report the empty session folder
too, so the two are complementary; naming it outright is simply the simpler path.

## What the Sample Shows

The sample prints every tool call and every tool result as it happens, so the safety model is
something you watch rather than something you assume. In one short session you can observe each of the
guarantees this guide describes:

- **Containment.** Reading, writing, and listing are confined to the two granted locations. A path
  outside them is refused by the tool with guidance, not returned. The sample includes a sibling file
  outside the workspace so the refusal is a genuine containment decision rather than a staged one.
- **Asymmetric grants.** With `--read-only-workspace` the documents are readable but unmodifiable
  while the session folder stays writable, and the refusal of a workspace write enumerates the
  writable location so the agent can recover.
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
requested path, states the location it was interpreted as, and names every permitted location — the
workspace and the session folder — with its access level. The agent's turn continues normally — a
refusal is a value, not an error — so the model can act on the guidance rather than simply failing.
Asking the text tool to read the image instead produces a `Denied (UnsupportedMediaType)` result that
redirects the model to `image_read`.

## Demonstrating Asymmetric Grants

The `--read-only-workspace` flag changes nothing except the workspace grant's access level. The
workspace is still the working directory, so relative paths still resolve against it and results
inside it are still reported as relative names; what changes is that no write there is permitted.

```bash
dotnet run --project samples/01-document-assistant -- \
  --workspace samples/01-document-assistant/workspace \
  --read-only-workspace \
  --provider copilot \
  --prompt "Summarize welcome.txt into summary.md next to it, and show me exactly what each tool returns."
```

The read succeeds. The write into the workspace is refused, and the refusal enumerates both permitted
locations with their access levels — the workspace as read-only and the session folder as read-write.
That enumeration is the point: the agent is told where it *may* write, so it recovers by writing there
rather than retrying the same refused call. This is the difference between a denial that ends a turn
and a denial that redirects one.

## Demonstrating Cross-Location Work

Reading from one location and writing to another is the ordinary shape of real work, and it is where
the two path dialects become visible in a single exchange:

```bash
dotnet run --project samples/01-document-assistant -- \
  --workspace samples/01-document-assistant/workspace \
  --provider copilot \
  --prompt "List every location you can reach, read welcome.txt, then save a summary into the session folder."
```

The no-argument listing reports every permitted location as an absolute header, with any files
beneath it and an empty location shown under its header with an access-level marker. The read of
`welcome.txt` comes back as a relative name, because the result lies inside the granted working
directory. The write comes back as an absolute path, because the session folder lies outside the
anchor and no relative name could truthfully identify it. Both answers are correct; the change of
dialect is the tool telling the truth about where the file actually is. The agent addresses the
session folder by the absolute path its instructions named, which works on a first run even when the
session folder is still empty — discovery reports it either way, but naming it outright saves the
round-trip.

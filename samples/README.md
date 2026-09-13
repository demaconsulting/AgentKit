# AgentKit Samples

Runnable applications that demonstrate AgentKit end to end against live models. Each sample is a
self-contained console application; none is a shipped library, so none is packed, published, or
given an SBOM — they exist to be read and run.

There are two, and they answer two different questions. Read the **document-assistant** sample to
learn how to *consume* AgentKit: how an application composes the shipped tool packs onto a policy
and hands the result to a provider. Read the **custom-tools** sample to learn how to *extend*
AgentKit: how an application author writes their own guarded tools and publishes them as packs
alongside the shipped ones.

Both samples share the same provider-selection design — a tool set composed once, then a single
factory-selection switch choosing the runtime — so a reader who has seen one recognizes the shape
of the other immediately.

## [document-assistant](document-assistant/)

**The consumption path.** A console chat application that grants an agent exactly two locations — a
workspace folder to read and a separate session folder to write artifacts into — and hands it the
shipped text-file, file, Markdown, and image tool packs. It makes the AgentKit safety model *observable*: it prints
every tool call and result, so containment, asymmetric read/write grants, capability gating, the
relative-versus-absolute path dialect, and the adapters' suppression of provider built-ins are all
visible as they happen. It runs unchanged against the GitHub Copilot runtime and any Ollama model.

Read this first if you are attaching AgentKit's ready-made tools to an agent and want to see how a
policy, its grants, and the pack builder fit together.

## [custom-tools](custom-tools/)

**The extension path.** A console chat application that demonstrates how an application author
writes their *own* guarded tools with `GuardedToolFactory` and publishes them as packs, composed
together with a shipped pack. It ships two author-written packs:

- **`docstats`** — a `docstats_wordcount` tool that reports the word, line, and character counts of
  a text file. It is a *path-taking* tool: it goes through `PathPolicy` for containment exactly as
  a shipped tool does, reports the file's location in the same path dialect the shipped tools use
  (via the public `PathPolicy` helpers), and returns its findings as a structured, machine-readable
  result. Its `docstats` family prefix is deliberately one the shipped library does not publish — the
  library now owns the `markdown` prefix — so the custom pack never collides with a built-in family.
- **`clock`** — a `clock_now` tool that reports the current local and UTC time. It takes *no path at
  all*, demonstrating that `GuardedToolFactory` is the construction path for **every** tool, not
  only path-based ones, and that a tool needing no policy simply does not consult one.

Read this if you are writing tools of your own and want a worked, compiling example of the
authoring contract — the naming convention, the pack's family prefix and required capabilities, the
structured-result and refusal shapes, and the path-dialect helpers a third-party tool reuses.

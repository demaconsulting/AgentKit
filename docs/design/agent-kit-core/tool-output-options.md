## ToolOutputOptions

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolOutputOptions` class carries the preview size and the retention ceilings a composition
observes when it retains large tool outputs.

### Purpose

Retention of large tool outputs needs two kinds of number: how much of a retained output the model
is shown in place of it, and how much a composition may hold in memory. `ToolOutputOptions` gathers
them in one immutable value a host passes to `ToolPackBuilder.WithToolOutputSpill`,
`ToolOutputSpill.Apply` or `AgentPack`'s `childToolOutput` parameter.

The threshold above which an output is retained is deliberately **not** here. It is
`ToolLimits.MaxResultCharacters`, the ceiling that has always bounded a tool result, so one budget
keeps one meaning; see *ToolLimits Unit Design*.

The class is immutable after construction and is safe for concurrent use.

### Data Model

| Member | Type | Default | Description |
| --- | --- | --- | --- |
| `PreviewCharacters` | `int` | 4,000 | Characters of the output shown, head and tail together. |
| `MaxOutputCharacters` | `int` | 4,194,304 | Ceiling on one retained output. |
| `MaxRetainedCharacters` | `int` | 16,777,216 | Ceiling on everything one composition retains. |
| `MaxRetainedOutputs` | `int` | 256 | Ceiling on how many outputs one composition retains. |
| `Default` | `ToolOutputOptions` | — | The shared instance carrying every default. |

The defaults are published as constants (`DefaultPreviewCharacters` and so on) and reason from the
model's context budget and a host's memory:

- A 4,000-character preview is about a thousand tokens — an eighth of the default result ceiling.
- One output of 4 Mi characters is well over a hundred default pages; anything larger is not a
  result a model can usefully page and is refused.
- 16 Mi characters is about 32 MiB of UTF-16 text: four maximum-size outputs, and far more of the
  outputs a working session actually produces.
- 256 outputs bounds the bookkeeping independently of size.

**Invariants:** `PreviewCharacters >= 0`; `MaxOutputCharacters >= 1`; `MaxRetainedOutputs >= 1`;
`MaxRetainedCharacters >= MaxOutputCharacters`.

### Key Methods

#### ToolOutputOptions(previewCharacters, maxOutputCharacters, maxRetainedCharacters, maxRetainedOutputs)

Every parameter is optional and defaults to its published constant, so a host replaces one value
without restating the others. Validation runs before any assignment.

**Preconditions:** the invariants above. A zero preview is permitted: the model is then shown only
the reference and the line count, which is a legitimate choice for a host that wants every large
output read deliberately.

**Throws:** `ArgumentOutOfRangeException` when any invariant is violated.

### Error Handling

| Condition | Handling |
| --- | --- |
| Negative preview | `ArgumentOutOfRangeException` propagates |
| Non-positive per-output or count ceiling | `ArgumentOutOfRangeException` propagates |
| Total ceiling below the per-output ceiling | `ArgumentOutOfRangeException` propagates |

Each is a mistake in the host's configuration code rather than a condition a model can provoke,
so it is surfaced rather than clamped.

### Dependencies

None.

### Callers

- **ToolOutputStore** — reads the ceilings; see *ToolOutputStore Unit Design*.
- **ToolOutputSpill** — reads the preview size and passes the options to the store; see
  *ToolOutputSpill Unit Design*.
- **ToolPackBuilder** — carries the options from `WithToolOutputSpill` to `Build`.
- **AgentPack** (AgentKitTools) — carries the options for delegated agents.

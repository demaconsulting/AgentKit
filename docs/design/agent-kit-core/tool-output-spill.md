## ToolOutputSpill

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolOutputSpill` class composes retention of large tool outputs into a tool list: an output
too large to return is retained, the model is handed a bounded preview and a reference in its
place, and two confined reader tools are appended to page through and search it.

### Purpose

Without retention, a tool whose output exceeds `ToolLimits.MaxResultCharacters` must refuse — a
truncated result would hide what the model never knew to ask for — and the work that produced the
output is lost. A delegated agent's whole report, a listing of a large tree, or a broad search
becomes a refusal the model can only answer by guessing how to narrow the request, and a custom
tool's oversized result reaches the provider unbounded. A provider-side remedy that diverts a
large output to a temporary file is unusable here, because the confined tools cannot read outside
the locations the application granted.

`ToolOutputSpill` retains the whole output in a per-composition in-memory store and lets the model
read exactly the part it needs. Because the behavior lives inside each `AIFunction`, it is
identical on every provider: ChatClient, Ollama and Copilot all invoke the very instances the
composition produced.

**It is opt-in.** A composition that never calls `Apply` — directly or through
`ToolPackBuilder.WithToolOutputSpill` — behaves byte-for-byte as before.

The class is static and stateless; the tools it returns are safe for concurrent invocation.

### Data Model

`ToolOutputSpill` holds no state. `ReservedPrefix` (`tool_output`) is the internal name prefix
reserved for the readers; it is deliberately not a public family prefix, because the readers are
infrastructure the composition adds rather than a pack an application attaches.

The internal `SpillAttachment` is one composition's retention. It holds:

| Member | Type | Description |
| --- | --- | --- |
| `_store` | `ToolOutputStore` | The composition's store. |
| `_limits` | `ToolLimits` | The ceilings in force; `MaxResultCharacters` is the threshold. |
| `HeadQuarters` | `const int` | 3 — the share of the preview, in quarters, given to the head. |
| `LineNumberDelimiter` | `const string` | `"\| "`, matching `text_file_read`. |

### Key Methods

#### Apply(IEnumerable&lt;AIFunction&gt; tools, ToolLimits limits, ToolOutputOptions? options)

1. Materializes the list and rejects a null entry, a duplicate name, or a name carrying the
   reserved prefix.
2. Allocates one `ToolOutputStore` with the options (or `ToolOutputOptions.Default`) and one
   `SpillAttachment` over it and the limits.
3. Wraps each tool: a `GuardedFunction` is re-wrapped over the same inner function through
   `WithSpill`; any other `AIFunction` is wrapped in a new `GuardedFunction` carrying the
   attachment. The supplied instances are not modified.
4. Appends `ToolOutputReadTool` and `ToolOutputSearchTool` over the same store and limits. The
   readers are **not** wrapped: their own results are bounded and refused when over the ceiling,
   so a page can never become another reference.

**Postconditions:** the result holds the supplied tools in order, then `tool_output_read`, then
`tool_output_search`; every supplied tool keeps its metadata.

#### SpillAttachment.Deliver(string toolName, object? result)

Called by the wrapper with every result:

- An `OversizedResult` marker (from `ToolResult.Oversized`) is always retained; if it cannot be
  retained the tool's own refusal is returned.
- A purely textual result — a `string`, a `JsonElement`, a `TextContent`, or a non-empty sequence
  made only of `TextContent` — is retained when its delivered length exceeds the result ceiling,
  measured as the provider would have received it (raw JSON text for a `JsonElement`). A JSON
  object or array is indented only once it is known to be retained.
- Anything else — `null`, `DataContent`, a caption beside an image, any other shape — passes
  through unchanged. Binary content is already bounded by `MaxBinaryBytes`.

#### Preview composition

A retained output is answered with a successful string result:

```text
[Output output-3 from text_file_search: 184233 characters in 3012 lines, larger than the
32000-character result limit. Showing lines 1-58 and 2990-3012. The full output is retained:
read it with tool_output_read (id 'output-3', startLine, lineCount) or find text in it with
tool_output_search.]
   1| ...
[... lines 59-2989 omitted ...]
3012| ...
```

The body budget is `min(PreviewCharacters, MaxResultCharacters − widestHeader − omissionMarker)`,
computed against a header naming the widest possible ranges so the real header never overruns it.
Whole numbered lines are taken from the head up to three quarters of the budget, then from the tail
with what remains. When long lines were wrapped the header says so. When no line fits — a very
small ceiling or a zero preview — only the header is delivered, because the reference is the
result. Naming the readers is the designed recourse of a successful result, not a prescription
attached to a refusal.

An output whose normalized size exceeds `MaxOutputCharacters` is not retained: the result is the
marker's own refusal, or `Denied (ResourceTooLarge): The output of '<tool>' is N characters, which
exceeds the M-character retention limit.` It is never truncated.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `tools` or `limits` | `ArgumentNullException` propagates |
| Null tool, duplicate name, reserved name | `ArgumentException` propagates |
| Output over the retention ceiling | A refusal is returned to the model |
| Binary or non-textual result | Passed through unchanged |

### Dependencies

- **GuardedToolFactory** — the `GuardedFunction` wrapper that calls the attachment; see
  *GuardedToolFactory Unit Design*.
- **ToolResult** — the oversized marker and the refusals; see *ToolResult Unit Design*.
- **ToolLimits** — the result ceiling; see *ToolLimits Unit Design*.
- **ToolOutputOptions**, **ToolOutputStore**, **ToolOutputReadTool**, **ToolOutputSearchTool** —
  see their unit designs.
- **Microsoft.Extensions.AI.Abstractions** — `AIFunction`, `TextContent`; **System.Text.Json** —
  `JsonElement`.

### Callers

- **ToolPackBuilder** — `Build` applies it when `WithToolOutputSpill` was called.
- **AgentPack** (AgentKitTools) — applies it to a delegated agent's filtered tool list.
- An application — calls `Apply` directly on a hand-assembled tool list.

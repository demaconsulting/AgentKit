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

`MinimumResultCharacters` (public, 1,000) is the smallest `ToolLimits.MaxResultCharacters` a
retaining composition accepts. Its derivation is the worst-case fixed overhead of a preview: with
the tool name bounded at `ToolName.MaxLength` (64), a reference numbered at `long.MaxValue`
(`output-` and 19 digits), every count at `int.MaxValue` (10 digits) and the wrapped-lines note,
the header is at most 485 characters, the omission marker 45 and its line break 1 — 531 in all.
The header naming a tool's declaration instead of a size comparison is shorter than the comparison
at its widest, so it does not raise the worst case.
The internal `WorstCaseOverheadCharacters()` composes exactly that worst case from the same code
that composes a real preview, taking the maximum over both header forms, and a unit test asserts it
does not exceed the minimum, so lengthening the header's wording cannot silently break the
guarantee. 1,000 leaves at least 469 characters for
preview lines in the worst case, and covers every fixed result the readers return (an empty window,
`No matches.`) and every refusal the composition writes itself.

The internal `SpillAttachment` is one composition's retention. It holds:

| Member | Type | Description |
| --- | --- | --- |
| `_store` | `ToolOutputStore` | The composition's store. |
| `_limits` | `ToolLimits` | The ceilings in force; `MaxResultCharacters` is the threshold. |
| `HeadQuarters` | `const int` | 3 — the share of the preview, in quarters, given to the head. |
| `LineNumberDelimiter` | `const string` | `"\| "`, matching `text_file_read`. |

### Key Methods

#### Apply(IEnumerable&lt;AIFunction&gt; tools, ToolLimits limits, ToolOutputOptions? options)

1. Rejects a `limits` whose `MaxResultCharacters` is below `MinimumResultCharacters`, then
   materializes the list and rejects a null entry, a name longer than `ToolName.MaxLength`, a name
   carrying the reserved prefix, or a duplicate name. Nothing is allocated for a rejected call.
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

#### SpillAttachment.Deliver(string toolName, JsonSerializerOptions? serializerOptions, object? result)

Called by the wrapper with every result and the tool's serializer options:

- An `OversizedResult` marker (from `ToolResult.Oversized`) is always retained; if it cannot be
  retained the tool's own refusal is returned. When its text fits the result ceiling — the tool
  judged it against a tighter ceiling of its own — the preview header states that the tool reported
  it as too large to return, rather than a size comparison the output would fail.
- A sequence of `AIContent` that is not already an `IReadOnlyList<AIContent>` is materialized once,
  and when it is not retained the materialized list — the same parts in the same order — is what is
  delivered. A tool may return a single-pass or lazily produced sequence; measuring it and then
  letting the provider's adapter enumerate it again would deliver it empty or altered. A list or an
  array is delivered as the very instance the tool returned.
- A result that is not `null`, a `string`, a `JsonElement`, an `AIContent` or a sequence of
  `AIContent` — a record, an anonymous object, a collection or dictionary, a number, a date, an
  enumeration value, a `JsonNode`, a byte array — is serialized to a `JsonElement` with the tool's
  own `JsonSerializerOptions` (`AIJsonUtilities.DefaultOptions` when none is supplied) through
  `GuardedToolFactory.SerializeStructured`, the helper the guarded factory's result guard uses. A
  factory-built tool's guard has already done this, so only a function the factory did not build
  reaches it, and such a function's structured result is then measured, retained and delivered
  exactly as a factory-built tool's is. Within the ceiling the `JsonElement` is what is delivered,
  not the original object: it is the form the Microsoft.Extensions.AI function factory itself
  returns for structured data, its JSON text is the text measured, and every adapter sends a
  `JsonElement` as its JSON text — whereas the original object would be serialized by each adapter
  with options of its own (the OpenAI adapter with `AIJsonUtilities.DefaultOptions`, OllamaSharp
  with its client's, the Copilot SDK with the function's) and could arrive larger than measured.
  With the default options the text sent is identical to what the adapter would have produced from
  the object. A byte array is not binary content: every adapter sends it as a base64 JSON string
  within the tool's text result, so it is measured, and retained, as that string. A value the
  serializer cannot handle — a stream, a delegate, an object graph with a cycle — throws, and the
  exception (`NotSupportedException`, `InvalidOperationException` or `JsonException`, as the
  serializer raises it) fails the invocation exactly as the guarded factory's would; it is neither
  passed through unmeasured nor replaced by a refusal the composition composes.
- A purely textual result — a `string`, a `JsonElement` (including one a structured result was
  serialized to), a `TextContent`, or a non-empty list
  made only of `TextContent` — is retained when its size exceeds the result ceiling. A result is
  measured by its own representation, not by any envelope a provider adds around it, which differs
  from provider to provider: a string by its characters, any `JsonElement` by its JSON text
  (`GetRawText()`), text content by its text (a list's parts joined by `\n`). A JSON *string* is
  therefore measured with its quotes and escapes — the form the Microsoft.Extensions.AI chat
  adapters send — which never under-counts an adapter that sends the decoded value instead.
- What is retained is the form a model can page and search: a JSON string is retained as its
  decoded value (`GetString()`), and a JSON object or array is indented, only once it is known to
  be retained. Retaining escaped or compact JSON would collapse a multi-line output into one line
  that `tool_output_read` could not page by line and `tool_output_search` could not match literally.
- Anything else — `null`, an undefined `JsonElement` (which carries no value), `DataContent` or any
  other content that is not text, or a sequence containing such content, such as a caption beside an
  image — passes through as it arrived, except that a sequence of content that is not already a list
  is the list it was enumerated into, the same parts in the same order. Content other than text is
  never retained: each adapter renders it differently, so it has no single text form to measure, and
  binary content must reach the provider as the attachment it is. The library's own tools bound
  binary content by `MaxBinaryBytes`; a custom tool's content is delivered as the tool returned it.

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

The body budget is
`min(PreviewCharacters, MaxResultCharacters − widestHeader − omissionMarker − 1)`, computed against
a header naming the widest possible ranges (`1-T and T-T`) so the real header never overruns it.
Whole numbered lines are taken from the head up to three quarters of the budget, then from the tail
with what remains. The header states the lines shown: both ranges, the head alone, or the tail
alone when the first line is too long for the head's share. When long lines were wrapped the header
says so. When no line fits — a zero preview, or lines longer than the budget — only the header is
delivered, because the reference is the result. Naming the readers is the designed recourse of a
successful result, not a prescription attached to a refusal.

**The preview always fits the ceiling.** The only header longer than the widest is the no-lines
form, by at most two characters for an output of fewer than ten lines, and it comes with no body
and no omission marker, whose reserved room covers it. Because `Apply` rejects any ceiling below
`MinimumResultCharacters`, which is at least the worst-case overhead, the budget is never clamped
below zero, so every preview — header, body and marker — is within `MaxResultCharacters`.

**What a retaining composition guarantees.** Every successful result it produces fits within
`MaxResultCharacters`: previews (including header-only previews), reader pages, empty windows,
search listings and `No matches.`. A result that already fit is returned as it arrived; a structured
result as the JSON document it was serialized to; and a sequence of content that is not already a
list as the list it was enumerated into, the same parts in the same order. Content other than text
is not measured and is not covered by this guarantee. Every refusal the composition writes itself —
the retention-limit refusal and
the reader refusals, whose malformed-identifier echo `ToolOutputStore` bounds — is far shorter
than the minimum. A refusal a tool writes itself (the message given to `ToolResult.Oversized`, or
any `ToolResult.Denied`) is the tool's own text, exactly as without retention.

An output whose normalized size exceeds `MaxOutputCharacters` is not retained: the result is the
marker's own refusal, or `Denied (ResourceTooLarge): The output of '<tool>' is N characters, which
exceeds the M-character retention limit.` It is never truncated. `N` is the size the output would
be retained at — line endings normalized, long lines wrapped, structured data indented — which is
the figure `MaxOutputCharacters` is measured on; it comes from `ToolOutputStore.Measure`, which
shares the store's normalization scan without copying the output. The size the tool returned can be
smaller (compact JSON, or one long line that wrapping lengthens) and would contradict the limit it
is said to exceed. The preview header, by contrast, reports the size the tool returned, the figure
the result ceiling judged.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `tools` or `limits` | `ArgumentNullException` propagates |
| `MaxResultCharacters` below `MinimumResultCharacters` | `ArgumentException` (`limits`) propagates |
| Null tool, name over 64 characters, reserved name, duplicate name | `ArgumentException` propagates |
| Output over the retention ceiling | A refusal stating the retained size is returned to the model |
| Content other than text, or a sequence containing it | Passed through; a single-pass sequence as its enumerated list |
| Structured result from a hand-built function | Serialized with the function's serializer options, then measured |
| Unserializable structured result | The serializer's exception fails the invocation, as from the guarded factory |
| Single-pass sequence of content | Enumerated once; the parts enumerated are delivered |
| Undefined `JsonElement` | Passed through unchanged; never measured |

### Dependencies

- **GuardedToolFactory** — the `GuardedFunction` wrapper that calls the attachment, and
  `SerializeStructured`, which serializes a structured result; see *GuardedToolFactory Unit Design*.
- **ToolResult** — the oversized marker and the refusals; see *ToolResult Unit Design*.
- **ToolLimits** — the result ceiling; see *ToolLimits Unit Design*.
- **ToolOutputOptions**, **ToolOutputStore**, **ToolOutputReadTool**, **ToolOutputSearchTool** —
  see their unit designs.
- **Microsoft.Extensions.AI.Abstractions** — `AIFunction`, `AIContent`, `TextContent`,
  `AIJsonUtilities`; **System.Text.Json** — `JsonElement`, `JsonSerializerOptions`.

### Callers

- **ToolPackBuilder** — `Build` applies it when `WithToolOutputSpill` was called.
- **AgentPack** (AgentKitTools) — applies it to a delegated agent's filtered tool list.
- An application — calls `Apply` directly on a hand-assembled tool list.

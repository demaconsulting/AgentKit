## ToolOutputReadTool

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolOutputReadTool` class builds the `tool_output_read` tool, which reads a range of lines
from a large tool output the composition retained instead of returning whole.

### Purpose

A retained output is only useful if the model can read it. `tool_output_read` pages through one by
line number, in exactly the shape `text_file_read` pages through a file, so a model already knows
how to read a page and how to ask for the next one.

**It cannot reach the file system.** Its parameters are an output identifier and a line range;
there is no path parameter, so no request it accepts can name a file. It reads only the store
`ToolOutputSpill.Apply` allocated for its composition. It is appended by that call and is never
attached directly by an application.

The class is static; a constructed tool captures only the lock-guarded store and the immutable
limits and is safe for concurrent use.

### Data Model

| Member | Type | Description |
| --- | --- | --- |
| `ToolName` | `const string` | `tool_output_read`. |
| `ToolDescription` | `const string` | The description the model reads. |
| `LineNumberDelimiter` | `const string` | `"\| "`, matching `text_file_read`. |

Parameters: `id` (string, required), `startLine` (1-based, optional, default 1), `lineCount`
(optional). Every parameter is nullable so an omitted argument reaches the body as a request the
tool answers rather than a framework error.

### Key Methods

#### Create(ToolOutputStore store, ToolLimits limits)

Builds the tool through `GuardedToolFactory.Create` with a delegate declared to return `object`.

#### Read

1. Refuses `startLine < 1` or `lineCount < 0` as `InvalidRequest`.
2. Looks the identifier up; a failure returns `ToolOutputStore.Refusal`.
3. A window starting past the end, or a zero count, returns the honest empty header
   `output-3 lines S-(S-1) of N`.
4. With an explicit count, renders that range and refuses it as `ResourceTooLarge` — naming the
   range, its size and the ceiling, and stating "Request fewer lines." — when the page exceeds
   `MaxResultCharacters`. A truncated page would hide lines the model believes it read.
5. With no count, takes as many whole lines from the start as fit the ceiling, budgeting against
   the widest possible header. No range end was requested, so this is not a truncation; the header
   states exactly where the page stopped. When not even one line fits, the result is a
   `ResourceTooLarge` refusal naming the ceiling.

**Page shape:** a header `output-3 lines A-B of N` followed by each line as its number, padded to
the width of `N`, then `| `, then the content.

### Error Handling

| Condition | Handling |
| --- | --- |
| Invalid range | `InvalidRequest` refusal returned |
| Missing or malformed identifier | `InvalidRequest` refusal returned |
| Unknown or evicted identifier | `TargetNotFound` refusal returned |
| Page over the ceiling | `ResourceTooLarge` refusal returned |
| Null store or limits at construction | `ArgumentNullException` propagates |

### Dependencies

- **ToolOutputStore** — lookups and refusals; see *ToolOutputStore Unit Design*.
- **GuardedToolFactory**, **ToolResult**, **ToolLimits** — see their unit designs.

### Callers

**ToolOutputSpill** — creates the tool in `Apply`. The model invokes it through the runtime.

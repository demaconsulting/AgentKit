## ToolResult

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolResult` class constructs the results a guarded tool returns to the model, and defines
the `DenialReason` enumeration naming why a tool refused an operation.

### Purpose

`ToolResult` exists so that every tool in every pack returns one of a small, known set of shapes,
rather than each tool inventing its own. A model consuming a tool's output has no way to ask what
shape it is in, so the shapes have to be fixed by the library.

**Every constructor returns `object`, and that is a consequence rather than a shortcut.** A tool
returns a union — a refusal, or text, or content — and `object` is the only type that expresses
it. No strongly-typed signature captures that union: a bespoke result type would be a plain
object and would be serialized just the same. That union is also precisely the declared return
type the underlying function factory would serialize into JSON, which is why a tool built from
these results must be built through `GuardedToolFactory`; see _GuardedToolFactory Unit Design_
for why that matters and what goes wrong when it is bypassed.

**A refusal is a return value, not an exception.** An exception raised while a model is calling a
tool ends the agent's turn and strands it with no way forward. A returned refusal lets the model
read the reason and choose a permitted alternative. Only programming errors — a missing text, an
invalid media type, an undefined reason — are exceptions. This is the same dividing line
_PathPolicy Unit Design_ draws.

The class is static and stateless and is therefore safe for concurrent use.

### Data Model

`ToolResult` holds no state. Its only data are the fixed fragments from which a refusal is
composed:

| Member           | Type           | Description                                                     |
|------------------|----------------|-----------------------------------------------------------------|
| `DenialPrefix`   | `const string` | The fixed word introducing a refusal.                           |
| `RedirectPrefix` | `const string` | The fixed opening of the sentence naming a better tool.         |
| `RedirectSuffix` | `const string` | The fixed close of the sentence naming a better tool.           |

`DenialReason` names the situations a tool may refuse for:

| Member                      | Value | The situation it names                                      |
|-----------------------------|-------|-------------------------------------------------------------|
| `PathNotPermitted`          | 1     | The requested path lies outside the permitted location.     |
| `ResourceTooLarge`          | 2     | The resource exceeds a ceiling the host configured.         |
| `UnsupportedMediaType`      | 3     | The media type is not one this tool can handle.             |
| `TargetNotFound`            | 4     | The requested target does not exist.                        |
| `HostCapabilityUnavailable` | 5     | The host does not provide the required capability.          |
| `InvalidRequest`            | 6     | The request is malformed or self-contradictory.             |

**There is deliberately no zero member.** `default(DenialReason)` is therefore not a valid reason
and `Denied` rejects it. A refusal with an unspecified reason is a bug, and the absence of a zero
member makes that bug unrepresentable rather than merely discouraged. Adding a member later is
source-compatible; removing one is not.

### Key Methods

#### Text(string text)

Returns the supplied string itself.

**Preconditions:** `text` is non-null. An empty string is permitted — an empty file is a
legitimate read result, and reporting it as a refusal would be a lie.

**Postconditions:** the returned value is the identical string. Text is not wrapped in a content
object, because a plain string is what every runtime already knows how to present to a model.

**Throws:** `ArgumentNullException` when `text` is null.

#### Binary(ReadOnlyMemory&lt;byte&gt; data, string mediaType, string? caption)

Returns binary content carrying its media type, preceded by a caption when one is given.

**Preconditions:** `mediaType` is non-null and non-empty.

**Postconditions:** with no caption, a single `DataContent`. With a caption, a
`List<AIContent>` of exactly two parts — a `TextContent` holding the caption at index 0, and the
`DataContent` at index 1. **The order is the contract**: the model reads the parts in sequence
and must be told what the attachment is before it reaches it.

**Throws:** `ArgumentNullException` / `ArgumentException` when `mediaType` is missing or empty.

#### Image(ReadOnlyMemory&lt;byte&gt; data, string mediaType, string? caption)

Delegates to `Binary` once the media type has been confirmed to denote an image, by an
ordinal case-insensitive `image/` prefix test.

The extra check is not ceremony. A caption attached to bytes that are not an image would tell the
model it is looking at a picture when it is not, and the model has no way to discover otherwise;
the mistake is therefore refused at construction rather than being handed to a provider.

**Throws:** `ArgumentException` when the media type does not denote an image, in addition to the
conditions `Binary` reports.

#### Denied(DenialReason reason, string message, string? redirectToolName)

Returns the refusal text a model receives.

**Preconditions:** `reason` is a defined `DenialReason` member; `message` is non-null and
non-empty; `redirectToolName`, when supplied, is a valid tool name.

**Composition**, exactly:

- without a redirect: `Denied ({reason}): {message}`
- with a redirect: `Denied ({reason}): {message} Use the '{redirectToolName}' tool instead.`

The redirect is validated through `ToolName.Validate`, so a refusal cannot direct the model at a
name no tool could legally carry.

**Throws:** `ArgumentOutOfRangeException` for an undefined reason; `ArgumentNullException` /
`ArgumentException` for a missing or empty message; `ArgumentException` for an invalid redirect
name.

### Error Handling

| Condition                              | Handling                                      |
|----------------------------------------|-----------------------------------------------|
| Null `text`                            | `ArgumentNullException` propagates            |
| Null or empty `mediaType`              | `ArgumentNullException` / `ArgumentException` |
| `mediaType` not denoting an image      | `ArgumentException` propagates                |
| Undefined `DenialReason`               | `ArgumentOutOfRangeException` propagates      |
| Null or empty refusal `message`        | `ArgumentNullException` / `ArgumentException` |
| Invalid `redirectToolName`             | `ArgumentException` propagates                |
| A refused operation                    | Not an error; a refusal is returned           |

The dividing line is the same one `PathPolicy` draws: **programming errors propagate; runtime
policy outcomes are returned.**

### Design Constraints

**A refusal adds no host detail of its own.** The composed text consists of a fixed prefix, the
name of the `DenialReason` member, the caller's `message`, and — when a redirect is given — a
fixed sentence naming it. Nothing about the host is added here. The refusal text is handed to a
model and the resulting transcript leaves the process, so the only route by which host layout can
reach it is the caller's `message`. That discipline is enforced at the call sites: `PathPolicy`'s
four denial messages are compile-time constants with no interpolation, and any pack composing a
refusal is expected to do the same.

A defensive check rejecting directory separators in `message` was considered and **rejected**:
legitimate messages contain `/` — for example, "unsupported media type image/svg+xml" — so the
check would refuse correct refusals while still not preventing a determined caller from
interpolating a path.

**Refusals are returned, never thrown.** See _Purpose_. A future change that converts a refusal
into an exception would strand every agent that encounters it.

**Every constructor returns `object`.** This must not be "tightened" into strongly-typed
overloads to make the guarded factory look unnecessary; see _GuardedToolFactory Unit Design_.

### Dependencies

- **ToolName** — validates a redirect tool name; see _ToolName Unit Design_.
- **Microsoft.Extensions.AI.Abstractions** — supplies `AIContent`, `TextContent` and
  `DataContent`, the content types a provider recognizes.

### Callers

`ToolResult` is a public API entry point: a tool author calls it to build the value a tool
returns. Within this system nothing else calls it; the tool packs that consume it are introduced
in later increments.

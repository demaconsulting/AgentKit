## GuardedToolFactory

![AgentKit Core Structure](AgentKitCoreView.svg)

The `GuardedToolFactory` class is the only supported way to construct a tool in this library.

### Purpose

`GuardedToolFactory` exists to make two safety properties impossible to omit: a tool's result reaches
the runtime in the form the tool produced it, and a tool's name obeys the naming convention.
Both are properties a tool author can forget, and the first fails silently when it is forgotten.
Making construction the single supported path means these properties are widened deliberately or
not at all.

The class is static and stateless and is therefore safe for concurrent use.

### Result Delivery — Why the Guard Exists and Must Not Be Removed

**Result handling by the underlying factory depends on the tool delegate's *declared* return
type, not on the type of the value it actually returns.** A delegate declared to return `object`
or `Task<object>` has its result serialized to JSON and handed to the runtime as a `JsonElement`:
a returned `DataContent` becomes a `data:` URI inside a JSON object, a returned list of content
becomes a JSON array, and a returned string becomes a JSON string. A delegate declared to return
`DataContent` — or any other concrete content type — is passed through unchanged, with or without
this guard.

A guarded tool is necessarily declared to return `object` or `Task<object>`, because it returns a
union: a refusal, or text, or content, depending on what happened. No strongly-typed signature
expresses that union and survives — a bespoke result type is a plain object and is serialized
just the same. The natural, correct way to write a tool is therefore exactly the case the factory
would serialize. The guard is not an edge case; it is the default path.

**Do not remove this guard after observing that a strongly-typed return works.** That observation
is true and irrelevant: it demonstrates the passthrough case, not the case this library is in.
The same caution applies to the tests. A test whose delegate is declared `Task<DataContent>`
passes with the guard deleted and proves nothing; the requirement-linked scenarios declare
`Task<object>` deliberately and must stay that way. See *GuardedToolFactory Unit Verification
Design*.

**The failure mode without the guard is severe and silent.** The provider never receives an image
attachment, and the model — having been told a tool returned an image — reports that it can see
the image and then fabricates a description of it. There is no error to notice.

**The guard also changes the refusal path**, in a way worth stating so it is not mistaken for a
defect: a refusal string reaches the runtime as a raw `string` rather than as a `JsonElement`
wrapping a JSON string. Both are consumable by the runtime, so the change is benign — but it is
observable on **every** tool result, not only on binary ones, and a reader who expected the
wrapped form must not read it as a regression.

Verified against `Microsoft.Extensions.AI.Abstractions` 10.9.0, the version this library pins:

| Declared return type   | Value returned    | Without the guard                     | With the guard    |
|------------------------|-------------------|---------------------------------------|-------------------|
| `Task<object>`         | `DataContent`     | `JsonElement` (a `data:` URI in JSON) | `DataContent`     |
| `Task<object>`         | `List<AIContent>` | `JsonElement` (a JSON array)          | `List<AIContent>` |
| `Task<object>`         | `string`          | `JsonElement` (a JSON string)         | `string`          |
| `object` (synchronous) | `DataContent`     | `JsonElement`                         | `DataContent`     |
| `Task<DataContent>`    | `DataContent`     | `DataContent`                         | `DataContent`     |

The last row is the trap. It is characterized by a deliberately unlinked test that lives beside
the guard it justifies; see *GuardedToolFactory Unit Verification Design*.

### Data Model

`GuardedToolFactory` holds no state. Its only data is the options object it constructs per call,
which is local to `Create`.

### Key Methods

#### Create(Delegate method, string name, string description, JsonSerializerOptions? serializerOptions)

Creates a tool that delivers its result to the runtime in the form the tool returned it.

**Preconditions:** `method` is non-null; `name` satisfies the naming convention; `description` is
non-null and non-empty. `serializerOptions` may be null.

**Algorithm:**

1. Reject a missing delegate, then a missing or empty description.
2. Validate `name` through `ToolName.Validate`, so a malformed or colliding name is refused
   before anything is constructed.
3. Construct the factory options internally, setting the name, the description, the supplied
   serializer options and the result-delivery guard.
4. Delegate to the underlying `AIFunctionFactory.Create(Delegate, AIFunctionFactoryOptions)`
   overload and return the tool.

**Postconditions:** the returned tool carries the validated name and the supplied description,
and its result is delivered unchanged whatever its declared return type.

**The wrapped overload is specifically `Create(Delegate, AIFunctionFactoryOptions)`.** It is the
only overload that accepts a result-marshaling hook. The
`(Delegate, string?, string?, JsonSerializerOptions?)` overload cannot carry the guard and must
never be used by this library or by a pack.

**The options object is never accepted from a caller.** It is a mutable type with a settable
result-marshaling hook, so accepting one would mean either mutating the caller's instance or
silently discarding their hook — either way the guard would become negotiable, which is exactly
what this unit exists to prevent.

**`name` and `description` are required positional parameters, not options.** With no name the
underlying factory derives a compiler-generated identifier from the delegate — a value observed
in practice to be of the form `_Main_b_0_10` — which is meaningless to a model, unstable across
recompilation, and would fail the family-prefix convention. An absent description defaults to the
empty string rather than failing, leaving the model guessing what the tool does. Both are the
model's only means of choosing the tool, so both are mandatory.

**`serializerOptions` is the one escape hatch, and deliberately the only one.** It feeds the
options object's serializer setting, which governs **parameter** schema generation and cannot
weaken result delivery. `null` means "the factory default".

**`Delegate` is the parameter type**, matching the overload being wrapped. Natural lambdas —
synchronous, asynchronous, with or without parameters — convert implicitly, so callers write no
casts and JSON schema generation for the parameters is unaffected.

**Throws:** `ArgumentNullException` for a missing delegate or description; `ArgumentException`
for an empty description or a name violating the convention.

### Error Handling

| Condition                                  | Handling                                      |
|--------------------------------------------|-----------------------------------------------|
| Null `method`                              | `ArgumentNullException` propagates            |
| Null or empty `description`                | `ArgumentNullException` / `ArgumentException` |
| `name` violating the naming convention     | `ArgumentException` propagates                |
| Null `serializerOptions`                   | Not an error; the factory default is used     |

Nothing is handled locally. Every condition above is a construction-time programming error
discovered by the developer writing the tool, so each is surfaced immediately.

### Design Constraints

**The result-delivery guard must be applied unconditionally and must not be made configurable.**
A guard a tool author can omit is a guard that will be omitted, and its absence produces no error
to notice.

**The guard lambda is `static`**, so no closure is allocated per tool.

**A future maintainer must not "simplify" a tool's declared return type to demonstrate that the
guard is unnecessary.** See *Result Delivery* above; that demonstration is valid for the case it
tests and irrelevant to the case this library is in.

### Dependencies

- **ToolName** — validates the tool name before construction; see *ToolName Unit Design*.
- **ToolResult** — the results a guarded tool returns; see *ToolResult Unit Design*. The
  dependency is conceptual rather than compile-time: the factory does not reference the result
  constructors, but the guard exists precisely because of the shapes they produce.
- **Microsoft.Extensions.AI.Abstractions** — supplies `AIFunction`, `AIFunctionFactory` and
  `AIFunctionFactoryOptions`.

### Callers

`GuardedToolFactory` is a public API entry point: a pack author calls it to build every tool.
Within this system nothing else calls it; the tool packs that consume it are introduced in later
increments.

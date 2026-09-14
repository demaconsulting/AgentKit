## ToolLimits

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolLimits` class carries the ceilings a tool observes when reading, returning and
attaching content.

### Purpose

`ToolLimits` exists so that the budget a tool operates within is a decision the host makes once,
rather than a decision each tool makes for itself. It is attached to a `PathPolicy` and travels
with it, so every tool a host governs observes the same ceilings.

**The values reason from the model's context budget, not from what is convenient in bytes.** A
tool's real constraint is not the file system or the network; it is that everything a tool
returns is spent out of the same finite context window the conversation, the system prompt and
the model's own reasoning must share. Choosing a round number of bytes and hoping is how an agent
ends up forgetting its instructions halfway through a task — a failure that presents as the model
becoming vague rather than as an error anyone can see.

**`MaxReadBytes` = 64 KiB.** Common tokenizers encode ordinary English prose and source code at
roughly four bytes per token, so 64 KiB is on the order of sixteen thousand tokens. That is
generous for a source file, a configuration file or a chapter of a document, while still
consuming a minority of a 128k context window and leaving room for the conversation that has to
follow. A larger ceiling would not read materially more useful documents; it would only make it
easier to destroy a session with one call.

**`MaxResultCharacters` = 32,000.** What a tool *reads* and what it *returns* are deliberately
different budgets, and the return budget is the tighter one. A tool commonly reads a whole file
and returns a region of it, or reads a directory and returns a summary. Thirty-two thousand
characters is on the order of eight thousand tokens handed back to the model, which bounds any
single tool result to a small fraction of a turn and keeps a multi-step task affordable. Setting
this equal to `MaxReadBytes` would let one result crowd out every subsequent step.

**`MaxBinaryBytes` = 8 MiB.** Images are not charged against the context window by the same route
as text, so a character ceiling is the wrong control for them and a byte ceiling is the right one.
Eight MiB sits at or below the per-attachment ceilings the major providers publish, so
content this library accepts is content a provider will accept. A tool that hands a provider an
attachment the provider rejects produces an opaque failure at the far end of the call, which is
exactly what this ceiling exists to prevent.

**`MaxAgentDepth` = 2.** This bounds how deep a chain of delegated agents may run: the root agent
an application starts is at depth zero, so at 2 that agent may start a child and that child may
start one more, and the grandchild's own attempt to delegate is refused. It sits here rather than
as a constant on whichever tool happens to delegate because it is the same kind of thing as the
ceilings above — a budget the host sets once and every tool observes. Delegation spends the
host''s money and time in a way the root agent''s own context window never reveals, and an agent
that can delegate can delegate to something that delegates, so an unbounded chain is a runaway
neither the model nor the user can see. Two levels is enough for the pattern delegation exists to
serve — a coordinator, a worker, and a specialist the worker consults — while keeping the worst
case a host can be billed for finite and small.

**These values are provisional.** They are public API defaults and they appear in requirement
text, so they are to be confirmed by the repository owner before the first tagged release.
Nothing is published yet, so they remain freely changeable until then.

An instance is immutable after construction and is safe for concurrent use.

### Data Model

| Member                          | Type         | Description                                                       |
|---------------------------------|--------------|-------------------------------------------------------------------|
| `MaxReadBytes`                  | `int`        | Ceiling on the bytes a tool may read from one source.             |
| `MaxResultCharacters`           | `int`        | Ceiling on the characters a tool result may return to the model.  |
| `MaxBinaryBytes`                | `int`        | Ceiling on the bytes of binary content a tool may return.         |
| `MaxAgentDepth`                 | `int`        | Ceiling on how deep a chain of delegated agents may run.          |
| `Default`                       | `ToolLimits` | Shared instance a host receives when it configures nothing.       |
| `DefaultMaxReadBytes`           | `const int`  | The published default for `MaxReadBytes`, 65,536.                 |
| `DefaultMaxResultCharacters`    | `const int`  | The published default for `MaxResultCharacters`, 32,000.          |
| `DefaultMaxBinaryBytes`         | `const int`  | The published default for `MaxBinaryBytes`, 8,388,608.            |
| `DefaultMaxAgentDepth`          | `const int`  | The published default for `MaxAgentDepth`, 2.                     |

The four constants exist so that this document, the requirement text and the tests can all name
one source of truth rather than repeating literals.

Invariants:

- Every ceiling is zero or greater for the lifetime of the instance.
- `Default` is a single shared instance, so a policy that was given no ceilings can be
  distinguished from one that was given an equal copy.

### Key Methods

#### ToolLimits(int maxReadBytes, int maxResultCharacters, int maxBinaryBytes, int maxAgentDepth)

The only constructor. Every parameter is optional and defaults to the corresponding published
constant, which is what delivers per-ceiling customization without a builder: a host writes
`new ToolLimits(maxBinaryBytes: 1024)` and keeps the other three defaults. `maxAgentDepth` is last
because it was the most recently added ceiling.

**Preconditions:** every supplied ceiling is zero or greater.

**Postconditions:** the instance exposes exactly the supplied ceilings; no clamping or rounding
is applied.

**Zero is permitted.** A zero ceiling is the expressible way to disable an operation entirely and
is a meaningful host configuration, not a mistake. Rejecting it alongside a negative value would
remove the only way to say "this agent may not delegate".

**Throws:** `ArgumentOutOfRangeException` when any ceiling is negative.

#### Default

A static property holding a single shared instance equivalent to `new ToolLimits()`. It is a
shared instance rather than a factory method because the type is immutable, so sharing carries no
risk, and because reference identity lets a caller establish that no host configuration was
applied.

### Error Handling

| Condition                   | Handling                                    |
|-----------------------------|---------------------------------------------|
| A negative ceiling supplied | `ArgumentOutOfRangeException` propagates    |
| A zero ceiling supplied     | Not an error; accepted and exposed as zero  |

Nothing is handled locally. A negative ceiling is a programming error in the host's configuration
code — not a runtime condition a model can provoke — so it is surfaced immediately rather than
clamped into something plausible.

### Design Constraints

**Ceilings are carried, not enforced, by this unit.** `ToolLimits` states the budget; the tools
that consume it are responsible for observing it. Separating the statement from the enforcement
means a host configures one object rather than one setting per tool, and it keeps this unit free
of any dependency on what a tool actually does.

**The defaults are provisional pending the repository owner's confirmation** before the first
tagged release. They are recorded here, in the type's own documentation and in the requirement
text so that the decision is made on the reasoning above rather than on taste.

### Dependencies

N/A - `ToolLimits` depends only on the .NET base class library.

### Callers

`ToolLimits` is a public API entry point: a host constructs one — or takes `Default` — and hands
it to a `PathPolicy`. Within this system it is held by `PathPolicy`; see *PathPolicy Unit Design*.

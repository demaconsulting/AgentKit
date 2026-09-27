## ToolLimits

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolLimits` class carries the ceilings a tool observes when reading, returning, decoding and
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

**Every ceiling is a default, and every one is overridable.** A host that wants different budgets
supplies them through the optional parameters of the `ToolLimits` constructor and hands the result
to the three-argument `PathPolicy` constructor; a host that configures nothing receives `Default`.

**`MaxImagePixels` = 16,777,216 (4096 × 4096).** This bounds how many pixels a tool may decode out
of one image, counted from the dimensions that image declares. It is a count of pixels and not of
bytes, deliberately, and the reasoning is worth stating in full because it is the kind of thing a
later reader would "simplify" back into a defect.

A byte ceiling on the *file* does not bound what decoding that file costs. Image formats compress,
so a file well inside `MaxBinaryBytes` can declare a very large number of pixels: an image of
8192 × 8192 is 67,108,864 pixels, and a decoded pixel buffer is **four bytes per pixel**, so that
image costs 268,435,456 bytes — a quarter of a gigabyte of transient allocation — reachable from a
file of a few hundred kilobytes. Any per-axis bound a decoder enforces is likewise insufficient on
its own, because it bounds each dimension and says nothing about their product. It is the product
that must be bounded.

**The cost is four bytes per pixel regardless of what the file's header says about its channels.**
A header reports the encoding the *file* uses — a palette-indexed image declares one sample per
pixel, a grayscale image one, a truecolor image three — but the decoded buffer is 32-bit RGBA
whatever the source was, because the palette or the gray level is resolved into RGBA during
decoding. An estimate computed from a file's declared channel count would therefore under-count a
palette-indexed image by a factor of four, and that is precisely the image a hostile caller would
choose: palette-indexed content compresses extremely well, so it reaches the largest declared
dimensions from the smallest file. The estimate must use a fixed four bytes per pixel, and in
practice the implementation compares the **pixel product** against this ceiling and never
materializes a byte figure at all — the four-bytes-per-pixel arithmetic is what justifies the
ceiling's value, not an intermediate a tool computes.

At 16,777,216 the transient decode is bounded at roughly 64 MiB of pixel buffer plus a comparable
scanline buffer, which any host absorbs, while still admitting the images an agent realistically
looks at: a 4K screenshot is 8.3 megapixels, a 300-dpi US-Letter page is 8.4. The first realistic
thing the ceiling excludes is a 24-megapixel camera original, and an agent asked to work with one
should be told a ceiling it can request within rather than cost the host the decode. It sits here
rather than as a constant on whichever tool decodes, for the same reason `MaxAgentDepth` does: it
is host resource spend a model can provoke, a host on a constrained container will want it lower
and a host doing high-resolution document work will want it higher, and a refusal that names a
configured ceiling tells the model something about the host rather than about an implementation
detail.

An instance is immutable after construction and is safe for concurrent use.

**`MaxDeleteEntries` = 1,000.** This bounds how many entries one recursive removal may take,
counting the named directory itself, and it is the one ceiling here that bounds *damage* rather
than spend. It sits beside the others for the same reason they do: a host configures all of its
budgets in one place, and a refusal names a number the host chose rather than an implementation
detail.

**What it buys must be stated honestly, because it is easy to oversell.** This is **not**
protection against a determined agent. An agent that means to destroy a tree can remove it a file
at a time with `file_delete`, and this ceiling will not stop it. What the ceiling buys is that a
*mistake* — a wrong path, a model confusion, an off-by-one in a constructed path — is survivable
and observable rather than total in one call. Any reading of this document that takes the ceiling
for a security boundary against a hostile agent is a misreading, and the tool's own documentation
says the same.

The value was chosen against trees measured in a real repository rather than picked for
roundness. One thousand is three orders of magnitude above a hand-made scratch directory; well
above one project's whole build output, measured at a few hundred entries; and between one half
and one fortieth of the trees an agent must never remove by mistake — a source tree at several
thousand entries, a test tree at tens of thousands, an installed package directory at tens of
thousands more. A mistaken "remove the workspace" is refused; ordinary housekeeping succeeds.

Counting the named directory itself is what makes an empty directory cost one entry, and therefore
what makes a ceiling of zero forbid recursive removal entirely — the expressible way for a host to
attach the family and withhold the capability, exactly as a zero delegation depth does for the
agent family.

**What enforcing this ceiling costs.** The refusal names the exact entry count rather than "more
than the limit", because a real figure is what tells a model whether subdividing the request would
help. An exact figure requires the whole tree to be walked, even the tree that is about to be
refused. That walk is metadata-only and covers a tree the approved case was going to enumerate
anyway, so the cost is a traversal rather than a read. It is *not* the cost of holding the tree:
the walk stops retaining paths once the running count passes the ceiling and only keeps counting,
so the request too large to approve is also the request that accumulates nothing. The traversal is
iterative over an explicit stack rather than recursive, so a deep tree reaches this ceiling rather
than exhausting the thread's stack first — a stack overflow cannot be caught and would take the
host process with it, which is the one outcome a damage ceiling must not have.

### Data Model

| Member                          | Type         | Description                                                       |
|---------------------------------|--------------|-------------------------------------------------------------------|
| `MaxReadBytes`                  | `int`        | Ceiling on the bytes a tool may read from one source.             |
| `MaxResultCharacters`           | `int`        | Ceiling on the characters a tool result may return to the model.  |
| `MaxBinaryBytes`                | `int`        | Ceiling on the bytes of binary content a tool may return.         |
| `MaxAgentDepth`                 | `int`        | Ceiling on how deep a chain of delegated agents may run.          |
| `MaxImagePixels`                | `int`        | Ceiling on the pixels a tool may decode out of one image.         |
| `MaxDeleteEntries`              | `int`        | Ceiling on the entries one recursive removal may take.            |
| `Default`                       | `ToolLimits` | Shared instance a host receives when it configures nothing.       |
| `DefaultMaxReadBytes`           | `const int`  | The published default for `MaxReadBytes`, 65,536.                 |
| `DefaultMaxResultCharacters`    | `const int`  | The published default for `MaxResultCharacters`, 32,000.          |
| `DefaultMaxBinaryBytes`         | `const int`  | The published default for `MaxBinaryBytes`, 8,388,608.            |
| `DefaultMaxAgentDepth`          | `const int`  | The published default for `MaxAgentDepth`, 2.                     |
| `DefaultMaxImagePixels`         | `const int`  | The published default for `MaxImagePixels`, 16,777,216.           |
| `DefaultMaxDeleteEntries`       | `const int`  | The published default for `MaxDeleteEntries`, 1,000.              |

The six constants exist so that this document, the requirement text and the tests can all name
one source of truth rather than repeating literals.

Invariants:

- Every ceiling is zero or greater for the lifetime of the instance.
- `Default` is a single shared instance, so a policy that was given no ceilings can be
  distinguished from one that was given an equal copy.

### Key Methods

#### ToolLimits(maxReadBytes, maxResultCharacters, maxBinaryBytes, maxAgentDepth, maxImagePixels, maxDeleteEntries)

The only constructor, taking six `int` ceilings. Every parameter is optional and defaults to the
corresponding published
constant, which is what delivers per-ceiling customization without a builder: a host writes
`new ToolLimits(maxBinaryBytes: 1024)` and keeps the other five defaults. `maxDeleteEntries` is
last because it is the most recently added ceiling, and appending it leaves every existing named
and positional caller unaffected — the same discipline `maxImagePixels` followed before it.

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

### Dependencies

N/A - `ToolLimits` depends only on the .NET base class library.

### Callers

`ToolLimits` is a public API entry point: a host constructs one — or takes `Default` — and hands
it to a `PathPolicy`. Within this system it is held by `PathPolicy`; see *PathPolicy Unit Design*.

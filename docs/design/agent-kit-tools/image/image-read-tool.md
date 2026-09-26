### ImageReadTool

![AgentKit Tools Image Structure](ImageView.svg)

The `ImageReadTool` class publishes the `image_read` tool.

#### Purpose

To return the visual content of one file the access policy permits the agent to read — an image or
a PDF — as a caption followed by the content itself, stating the image's pixel dimensions in that
caption wherever it can establish them, and to refuse, in a way the agent can act on,
every request it cannot honor: a path outside the permitted location, a directory, a type the
family cannot read, a file that does not exist, or a file
too large to return.

The unit does no containment reasoning of its own and decides no media type of its own. It asks the
policy, asks the media-type map, and shapes the answer into something a model can use — and, for a
success, delivers that content through the guarded path so it reaches the provider intact.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                      |
| ----------------- | -------- | -------------------------------------------------------------- |
| `ToolName`        | `string` | `image_read`; public constant; carries the family prefix       |
| `ToolDescription` | `string` | Non-empty; the basis on which a model chooses this tool        |
| Caption prefix    | `string` | Constant; names the media type, never the word "image" alone   |
| Denial messages   | `string` | Tool-composed constants; policy denials come from `PathPolicy` |

One ceiling from `PathPolicy.Limits` bounds the operation: `MaxBinaryBytes`, the greatest size of
content that may be returned. It is inclusive — a file exactly at the ceiling is returned.

#### The Caption Carries the Image's Pixel Dimensions

The caption states the media type and, where the size can be established, the image's declared
pixel dimensions: `File content of media type image/png, 1920x1080 pixels.` Where it cannot, the
caption is the media-type-only form: `File content of media type image/gif.`

**Why the size belongs here at all.** A model can see a picture but cannot measure one. Without
the size stated alongside the content it has no coordinate space in which to name a region of that
image, so a request for a region would be a guess — and a guess is either refused, costing a turn,
or, in a library that clamped instead of refusing, silently answered for a region other than the
one asked about. Stating the size is what lets a region request be aimed, which is why it is part
of reading rather than a capability of its own.

**This unit now parses a header; it still decodes nothing.** The size is read from the bytes
already in hand — 33 bytes for a PNG, a bounded scan of leading marker segments for a JPEG — via
the shared header-probe helper described in *Image Subsystem Design*. No pixel buffer is allocated
anywhere on this path, and the file is not reopened, so there is no window in which the bytes
captioned and the bytes returned could differ.

**A header this unit cannot read costs the caption its dimensions and nothing else.** This unit's
contract has never been to validate content: it hands a permitted file's bytes to the provider,
whose own decoder is the authority on them, and the probe exists only to enrich the caption.
Refusing a file because an *optional enrichment* failed would narrow a tool whose whole promise is
to hand the model what the file holds, and would buy nothing — the bytes were already inside the
binary ceiling and were already going to be returned. The rule is live rather than defensive.
Two input classes reach it routinely: `gif`, `webp` and `pdf` are in this unit's admitted set and
have no header probe at all, so they are the common case rather than the edge case; and a
malformed or truncated header may belong to a file a provider still renders. **The caption never
states a size it did not read.**

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and
`ImagePack` is the only place the `image` family prefix is claimed.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with.

**Algorithm:** validates `policy`, then returns
`GuardedToolFactory.Create(delegate, ToolName, ToolDescription)`. The delegate is declared
`Task<object>` deliberately — that is the shape the underlying function factory would otherwise
serialize into JSON, and for content that would flatten the image into a `data:` URI the provider
never receives; see *GuardedToolFactory Unit Design*. The policy is captured by the delegate's
closure.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? path = null, CancellationToken cancellationToken = default)`

**Algorithm**, in this order, because the order is itself the contract:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`, naming the form a
   request should take. **The parameter carries a default**, which is load-bearing rather than
   cosmetic: a parameter with no default is required by the function factory, and an omitted
   argument then fails inside the factory before this step is reached, leaving the model an
   opaque framework error rather than a refusal it can act on
2. `policy.TryResolveRead(path, …)` — a refusal is returned as `PathNotPermitted` carrying the
   policy's own message unchanged. **A relative path is interpreted against the workspace here**,
   using the same policy the text file family uses, so a name a text file listing reported is
   directly usable. This step normalizes the
   path, so a request outside
   the permitted location is refused here without this unit reasoning about containment
3. An existing directory is refused as `InvalidRequest`, stating the fact and prescribing nothing
4. `ImageMediaTypes.TryResolveMediaType` — an unsupported type is refused through
   `ImageMediaTypes.DenyUnsupportedType`, which names a sibling reader only where doing so states
   what the file is. The type is judged
   before the file system is consulted for size or content, so a file is never read only to be
   discarded
5. A non-existent file is refused as `TargetNotFound`, stating the fact and prescribing nothing
6. A file larger than `MaxBinaryBytes` is refused as `ResourceTooLarge`, naming the ceiling. The
   file is opened once and its size read from that open handle, before any content is read, so
   nothing is loaded merely to discover a file was oversized and no file can grow between the size
   being judged and the bytes being taken. Exactly the number of bytes the ceiling admitted is
   then read; a file that shrinks under the read ends it early, which surfaces as an
   `EndOfStreamException` and is refused as an unreadable file
7. Otherwise the bytes are read and returned with a caption naming the media type and, where the
   header yields it, the image's pixel dimensions: an `image/*` type through `ToolResult.Image`,
   and `application/pdf` through `ToolResult.Binary`. The header probe runs on the bytes already
   read, after the ceiling check, and its failure costs the caption its dimensions and nothing
   else

The policy decision precedes every observation of the file system, so a refused path never discloses
whether it exists.

##### Image versus binary dispatch

The result constructor for an image refuses a media type that does not begin `image/`, so a PDF must
take the binary path or the read would throw. The dispatch is made on the `image/` prefix rather
than on the specific type, so a type added to `ImageMediaTypes` later is routed correctly without
this unit changing — and it is the same prefix the image constructor itself guards on. Both paths
produce the identical caption-plus-content shape.

#### Error Handling

Everything a model controls produces a returned refusal, never an exception: an exception raised
during a tool call ends the agent's turn and strands it with no way forward. The only exception the
unit raises is `ArgumentNullException` for a null policy at construction, which is a programming
error in the composing application — the same dividing line `PathPolicy` and `ToolName` draw.

File system failures are caught by an explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal. That classification already covers the read ending early because the file
shrank beneath it: `EndOfStreamException` derives from `IOException`, so a file truncated between
the size being judged and the bytes being taken is refused as an unreadable file rather than
propagating. The classification is enumerated rather than catching everything so that a
genuine defect still surfaces during development instead of being reported to a model as an
unreadable file. Cancellation is not classified, so a canceled read propagates as the runtime
expects.

**An oversized file is refused, never truncated.** A partial image is corruption the model cannot
detect; it would then reason confidently about a picture it never wholly saw. Refusing with the
ceiling named lets it narrow the request instead.

Disclosure depends on which unit composes the refusal. A `PathNotPermitted` refusal carries the
`PathPolicy` message unchanged: it states what was requested, how a relative request was
interpreted, and the permitted locations with their access levels, so a confined model learns where
it may read instead of guessing. Refusals this unit composes itself — including unsupported media
types, directory requests, missing or unreadable files, and the oversized-image ceiling — are
constants or interpolate only a media type, a sibling tool name, or an integer ceiling. **Each
refusal states a fact and stops** — it does not prescribe a course of action, because a denial that
suggested one was measured driving a model into a destructive workaround the user had explicitly
forbidden. The directory and missing-file refusals therefore say only what is so. Naming a sibling
tool survives that rule in exactly one place — the unsupported-type refusal `ImageMediaTypes`
composes — because there the naming *is* the statement of what the file is, on the same basis as
`TextFileReadTool`'s binary-content refusal naming `image_read`.

#### Dependencies

`PathPolicy` and `ToolLimits` for the decision and the ceiling, `ImageMediaTypes` for the type
resolution and the unsupported-type refusal, `ImageProbe` for the header read that yields the
caption's pixel dimensions, `ToolResult` for every result it returns, and
`GuardedToolFactory` for construction. From the Base Class Library: `File`, `FileStream` and
`Directory`. `AIFunction` and the content types, from `Microsoft.Extensions.AI.Abstractions`, are
the form the constructed tool and its result take.

#### Callers

`ImagePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.

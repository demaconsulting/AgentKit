### ImageCropTool

![AgentKit Tools Image Structure](ImageView.svg)

The `ImageCropTool` class publishes the `image_crop` tool.

#### Purpose

To return a rectangular region of one image the access policy permits the agent to read — named in
pixels from the image's top-left corner — as a caption followed by the region itself, and to
refuse, in a way the agent can act on, every request it cannot honor.

**The capability exists because a model that can see an image still cannot examine part of one
closely.** A detail occupying a hundredth of a large picture is, at the resolution a provider
receives, effectively invisible. Handing the model the region it named, at full resolution, is what
turns "I think that says something" into reading it.

**It ships with dimension reporting on the read tool because the two are one capability.** A region
request the model cannot aim is a region request it will aim wrongly. The coordinate space this
unit consumes is the one `ImageReadTool` states in its caption; see *ImageReadTool Unit Design*.

**Nothing is written.** The region is returned inline as image content, so this unit adds no write
decision, no output path and no filesystem surface of any kind. It is a read capability whose
result happens to be derived rather than copied, which is what lets it compose with the host's
existing image-promoting chat client unchanged.

The unit does no containment reasoning of its own and decides no media type of its own. It asks the
policy, asks the media-type map, and shapes the answer into something a model can use.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                      |
| ----------------- | -------- | -------------------------------------------------------------- |
| `ToolName`        | `string` | `image_crop`; public constant; carries the family prefix       |
| `ToolDescription` | `string` | Non-empty; names the coordinate space a region is stated in    |
| Croppable set     | `string` | Held by `ImageMediaTypes`; interpolated into its refusals      |
| Denial messages   | `string` | Tool-composed; interpolate only integers and the media type    |

One internal value type, `Region`, carries the four region values as one unit once each is known to
be present and individually sane. It exists so the triage steps and the messages cannot disagree
about which number is which, and so no method takes four same-typed parameters a caller could
transpose.

Two ceilings from `PathPolicy.Limits` bound the operation:

- `MaxBinaryBytes` bounds both the source file, judged from its directory entry before it is
  opened, and the returned region, judged after it is encoded. The second is not redundant: a
  region of a compressed source, returned losslessly, can genuinely exceed a ceiling the source
  file sat well inside.
- `MaxImagePixels` bounds how many pixels may be decoded out of one image, judged from what the
  image's header declares. See the decode budget below.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and
`ImagePack` is the only place the `image` family prefix is claimed.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with.

**Algorithm:** validates `policy`, then returns
`GuardedToolFactory.Create(delegate, ToolName, ToolDescription)`. The delegate is declared
`Task<object>` deliberately — that is the shape the underlying function factory would otherwise
serialize into JSON, which for image content means the provider never receives the region; see
*GuardedToolFactory Unit Design*. The policy is captured by the delegate's closure.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? path, int? x, int? y, int? width, int? height, CancellationToken)`

**Every parameter carries a default.** A parameter with no default is required by the function
factory, and an omitted argument then fails inside the factory before this unit is reached, leaving
the model an opaque framework error rather than a refusal it can act on. This unit takes five
parameters, so it has five ways to reach that failure, and every one of them is closed the same way.

**Algorithm**, in this order, because the order is itself the contract — each step refuses before
the next allocates anything:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`, naming the form a request
   should take, in wording identical to the read tool's so the family answers the same mistake the
   same way
2. An omitted region value is refused as `InvalidRequest`, naming the four values to supply
3. A negative origin is refused as `InvalidRequest`, **stating that the origin is measured in
   pixels from the top-left corner** — the one thing about the coordinate space a model cannot
   observe from the picture itself, and the most likely reason it produced a negative value
4. A non-positive extent is refused as `InvalidRequest`. An empty region is a self-contradictory
   request rather than a resource problem, which is why the reason is the malformed-request one
   and not the too-large one. Steps 3 and 4 need no file, so they run before the file system is
   consulted at all
5. `policy.TryResolveRead(path, …)` — a refusal is returned as `PathNotPermitted` carrying the
   policy's own message unchanged. A relative path is interpreted against the workspace here,
   using the same policy the read tool and the text file family use
6. An existing directory is refused as `InvalidRequest`, stating the fact and prescribing nothing
7. `ImageMediaTypes.TryResolveCroppableMediaType` — a type no region can be taken from is refused
   through `ImageMediaTypes.DenyNonCroppableType`. The type is judged before the file system is
   consulted for size or content, matching the read tool's documented order
8. A non-existent file is refused as `TargetNotFound`
9. A file larger than `MaxBinaryBytes` is refused as `ResourceTooLarge`, naming the ceiling. Size
   is judged from the directory entry before the file is opened
10. The header is read. A header that will not read is refused as `UnsupportedMediaType` stating
    that the file is not readable as the resolved type — **and stating no size, because none was
    read**
11. A file the header reader reports as one the decoder will not decode is refused as
    `UnsupportedMediaType`, stating that the file is well formed and naming the declared size
12. The decode budget is checked against the declared dimensions
13. The region is checked against the declared dimensions
14. The image is decoded and the region copied out of it — the first step that allocates pixel data
15. The region is encoded, and a result larger than `MaxBinaryBytes` is refused as
    `ResourceTooLarge`

The policy decision precedes every observation of the file system, so a refused path never discloses
whether it exists.

#### The Decode Budget

**A ceiling on a file's size does not bound what decoding that file costs.** Image formats compress,
so a file well inside `MaxBinaryBytes` can declare far more pixels than its size suggests, and a
decoded pixel buffer is four bytes per pixel. An image of 8192 × 8192 is 67,108,864 pixels and
268,435,456 bytes — a quarter of a gigabyte of transient allocation — reachable from a file of a
few hundred kilobytes.

**Two bounds are checked, and neither implies the other.** The decoder publishes the largest extent
it will allocate on one axis; that value is read from the decoder rather than restated here, so the
two cannot drift apart. `MaxImagePixels` bounds the *product*. A very wide, very short image passes
the product bound and fails the axis bound; a moderately proportioned image can pass the axis bound
on both sides and still declare four times more pixels than the host sanctioned. Both bounds are
named in the refusal, because a model needs both to form a request that would be accepted.

**The estimate uses a fixed four bytes per pixel, never the channel count the file declares.** This
is worth stating explicitly, because it is exactly the kind of thing a later reader would
"simplify" back into a defect. A header reports the encoding the *file* uses: a palette-indexed
image declares **one** channel per pixel, a grayscale image one, a truecolor image three. The
decoded buffer is 32-bit RGBA whatever the source was, because the palette or the gray level is
resolved into RGBA during decoding. An estimate of the form `width × height × channels` would
therefore **under-count a palette-indexed image by a factor of four** — and that is precisely the
image a hostile caller would choose, since palette-indexed content compresses extremely well and so
reaches the largest declared dimensions from the smallest file. In practice the implementation
compares the **pixel product** against `MaxImagePixels` and never materializes a byte figure at
all; the four-bytes-per-pixel arithmetic is what justifies the ceiling's value, not an intermediate
this unit computes. A dedicated regression test pins this, using a palette-indexed header that
differs from the truecolor one only in its declared color type.

**The decision is made from the header alone.** No pixel data is allocated in order to refuse an
image for being too large to allocate. The test that proves this uses a fixture carrying no pixel
data at all: a tool that decoded before triaging would produce the undecodable refusal instead, and
the two refusals are distinguishable.

#### Region Validation

**A region that does not lie wholly inside the image is refused, never clamped.** Clamping would
return a different region from the one asked about while reporting success, and the model has no way
to detect the substitution: it would then describe, confidently, a part of the picture it never
received. That is the same "confidently describe what you did not see" failure the whole family
exists to prevent, and it is why the answer is a refusal rather than a best effort.

**The refusal names the image's real dimensions.** That is what turns it from a dead end into the
fact the model was missing: it can restate the region correctly on the next turn rather than
guessing again.

**This unit validates the region itself rather than catching the decoder's complaint.** The decoder
does validate, but its argument-level report does not map onto the mistake the model made — an
origin sitting exactly at the image's width is reported against the *width* rather than against the
origin. Validating here is what lets the refusal describe what the model actually got wrong. Both
comparisons use `long` arithmetic, because an origin plus an extent can overflow.

#### Output Format

**The region is returned as PNG whatever the source was.** The encoding is lossless, so the model
receives the source's pixels rather than a re-compressed approximation of them — which matters most
precisely here, in the region the model asked to examine closely, where an artifact is
indistinguishable from the thing being examined. PNG is also accepted by every vision provider, so
the choice costs nothing in reach.

**The result is constructed through `ToolResult.Image` and never through `ToolResult.Structured`.**
The latter is serialized to JSON by design, which would destroy the content the guarded factory
exists to deliver intact. The shape is the same two-part caption-plus-content list the read tool
returns, so the guarantee holds by construction and the host's image-promoting chat client needs no
change.

**The caption restates the region and the source's dimensions**, so a follow-up region can be aimed
without reading the whole image again: `Cropped region 120,80 400x300 of a 1920x1080 image/png
image, returned as image/png.`

**Every pixel buffer this unit creates is released.** The decoded image is disposed as soon as the
region has been copied out of it, because the region copy is independent of its source and nothing
the region needs outlives the surface it came from; the region itself is disposed once it has been
encoded, which is why the encode precedes the disposal rather than following it. The library
documents that disposal releases nothing in the current release — the buffer is a plain managed
array — so honoring the contract now is what keeps this unit correct when a future release backs
that buffer with a pooled array, rather than a change that would then have to be found.

**The encoder sits outside the decode's `catch`, deliberately.** Widening that clause to span the
encode would convert a defect on a buffer this unit constructed into a "pixel data could not be
decoded" refusal, which is a false statement about the file and hides the defect.

#### Error Handling

Everything a model controls produces a returned refusal, never an exception: an exception raised
during a tool call ends the agent's turn and strands it with no way forward. The only exception the
unit raises is `ArgumentNullException` for a null policy at construction, which is a programming
error in the composing application.

File system failures are caught by an explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal. Decode failures are caught by a second explicit classification and
reported as `UnsupportedMediaType`. Both are enumerated rather than catching everything, so a
genuine defect still surfaces during development. **A failure inside the encoder, on a pixel buffer
this unit constructed, is a defect rather than anything a model can provoke, and is allowed to
propagate** — the same dividing line the read tool draws.

**The decoding library's own exception text never reaches a model.** It is developer-facing and may
echo values read out of the file, neither of which belongs in a transcript that leaves the process.
Every message this unit composes is its own.

**Three undecodable conditions are distinguished, and the distinction is the size.** A file whose
header will not read is refused with no size stated, because none was read. A file the header
reader reports as one the decoder will not decode is refused stating that it is well formed and
naming the size, because describing an intact file as damaged would be false. A file whose header
read and whose body did not is refused naming the size and stating that the pixel data could not
be decoded. The middle condition is established from the header's own feasibility report rather
than from any format knowledge this subsystem holds, so it generalizes to any feature the decoder
declines; see *Image Subsystem Design*.

Disclosure depends on which unit composes the refusal. A `PathNotPermitted` refusal carries the
`PathPolicy` message unchanged, disclosing the request, its interpretation and the permitted
locations. Every refusal this unit composes itself interpolates only integers and the resolved
media type — no absolute path, no permitted location, no directory separator. **Each refusal states
a fact and stops**, and none of them names a sibling tool: the well-formed-but-undecodable refusal
hands over the declared size itself rather than sending the model elsewhere for it, and the
non-croppable-type refusals state what the format is and which formats a region can be taken from.

#### Dependencies

`PathPolicy` and `ToolLimits` for the read decision and both ceilings, `ImageMediaTypes` for the
croppable-type resolution and its refusals, `ImageProbe` for the header read that yields the
declared size and whether the decoder expects to decode it, `ToolResult` for every result it
returns, and
`GuardedToolFactory` for construction. From the Base Class Library: `File`, `FileInfo`, `Directory`
and `MemoryStream`. From the `CanvasNet` OTS item: the decoders, the pixel buffer's rectangular
sub-region copy, the published per-axis bound and the encoder; see *CanvasNet Design*. **No type
from that library appears in this unit's signatures**, so the dependency stays an implementation
detail of the family. `AIFunction` and the content types, from
`Microsoft.Extensions.AI.Abstractions`, are the form the constructed tool and its result take.

#### Callers

`ImagePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.

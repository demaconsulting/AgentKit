### ImageCropTool

![AgentKit Tools Image Structure](ImageView.svg)

The `ImageCropTool` class publishes the `image_crop` tool.

#### Purpose

To return a rectangular region of one image the access policy permits the agent to read — named in
pixels from the image's top-left corner — either as a caption followed by the region itself or as a
new PNG file written where a read-write grant permits it, and to
refuse, in a way the agent can act on, every request it cannot honor.

**The capability exists because a model that can see an image still cannot examine part of one
closely.** A detail occupying a hundredth of a large picture is, at the resolution a provider
receives, effectively invisible. Handing the model the region it named, at full resolution, is what
turns "I think that says something" into reading it.

**It ships with dimension reporting on the read tool because the two are one capability.** A region
request the model cannot aim is a region request it will aim wrongly. The coordinate space this
unit consumes is the one `ImageReadTool` states in its caption; see *ImageReadTool Unit Design*.

**A region is either returned or written, and the request says which.** With no destination named
the region comes back inline as image content to examine, adding no write decision to a request
that did not ask for one — the behavior this unit has always had, unchanged. With a destination
named the region is written there as a new PNG file and the result is a text confirmation naming
what was written. The second outcome exists because a region that can only be looked at cannot
become a figure: an agent preparing a document needs the region it identified to exist as a file it
can point at, and returning the bytes inline leaves it with nothing to reference.

**The destination is a named part of the request rather than a second tool, and that is a reuse
decision rather than a mode switch.** A hypothetical `image_crop_to_file` would have to restate
every refusal this unit composes — the coordinate convention, the out-of-bounds refusal naming real
dimensions, the decode budget with both bounds, the three undecodable conditions, the
non-croppable-type map — and the moment two copies exist they drift. One tool, one taxonomy, one
extra decision. This is not the rejected coordinate-`units` shape: three spellings of one argument
give a model three ways to express one intent and no way to tell which the tool understood, whereas
here the model chooses between two *outcomes* and says which one it wants. The two are
distinguishable in the result's own shape — a content list against a string.

**The destination is resolved through `PathPolicy.TryResolveWrite`, independently of the read
decision that admitted the source.** This is `TextFileCreateTool`'s reasoning applied here: *a path
an agent may read is refused for creation unless a read-write grant permits it too, which is what
keeps a read-wide, write-narrow configuration meaningful*. Deriving the write from the read would
silently convert every readable location into a writable one and make the asymmetry an operator
configured an illusion. One `image_crop` call therefore exercises **both** of the grants an
application configured, rather than one of them twice — which is precisely the configuration the
`document-assistant` sample runs in, and something the library previously claimed in prose with no
single call demonstrating it.

The unit does no containment reasoning of its own and decides no media type of its own. It asks the
policy, asks the media-type map, and shapes the answer into something a model can use.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                      |
| ----------------- | -------- | -------------------------------------------------------------- |
| `ToolName`        | `string` | `image_crop`; public constant; carries the family prefix       |
| `ToolDescription` | `string` | Non-empty; names the coordinate space and both outcomes        |
| Croppable set     | `string` | Held by `ImageMediaTypes`; interpolated into its refusals      |
| Denial messages   | `string` | Tool-composed; interpolate only integers and the media type    |

Five of those denial messages concern the destination. **Four of the five now live in the shared
`ImageDestination` helper**, unchanged in wording, because both region tools ask the same
destination questions in the same order and a second copy would be a second place a later correction
has to reach. The fifth stays here, because it names *what* could not be written and only the tool
that produced it knows that:

| Constant                              | Reason           | Why that reason                                       |
| ------------------------------------- | ---------------- | ----------------------------------------------------- |
| `ImageDestination.MustBePng`          | `InvalidRequest` | No file exists yet whose type could be unsupported    |
| `ImageDestination.IsDirectory`        | `InvalidRequest` | Matches `TextFileCreateTool.PathIsDirectory`          |
| `ImageDestination.Exists`             | `InvalidRequest` | Matches `TextFileCreateTool.FileExists`               |
| `ImageDestination.ParentMissing`      | `TargetNotFound` | Matches `TextFileCreateTool.ParentMissing`            |
| `DestinationUnwritable` (this unit)   | `InvalidRequest` | Matches `ImageAdmission.FileUnreadable`               |

The `Destination` record that carries a permitted destination together with the form a confirmation
reports it in likewise belongs to `ImageDestination` rather than to this unit, for the same reason:
it is the shape of the shared helper's answer.

A sixth destination refusal — the destination not being writable at all — is composed by nothing in
this unit. It is `PathPolicy.TryResolveWrite`'s own message, returned unchanged under
`PathNotPermitted`, exactly as the read refusal is.

Two internal value types carry values as a unit rather than as loose parameters. `Region` carries
the four region values once each is known to be present and individually sane. `Destination`
carries the permitted real path together with the form the confirmation reports it in, computed
once at resolution. Both exist so that no method takes several same-typed parameters a caller could
transpose, and so the steps and the messages cannot disagree about which value is which.

Two ceilings from `PathPolicy.Limits` bound the operation:

- `MaxBinaryBytes` bounds the source file, judged from the handle the file was opened on and before
  any of its content is read, and
  the **inline** returned region, judged after it is encoded. The second is not redundant: a region
  of a compressed source, returned losslessly, can genuinely exceed a ceiling the source file sat
  well inside. It does **not** bound a region written to a file; see *The Binary Ceiling and the
  Written File* below.
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

##### The tool delegate: `(string? path, int? x, int? y, int? width, int? height, string? destination, CancellationToken)`

**Every parameter carries a default.** A parameter with no default is required by the function
factory, and an omitted argument then fails inside the factory before this unit is reached, leaving
the model an opaque framework error rather than a refusal it can act on. This unit takes six
parameters, so it has six ways to reach that failure, and every one of them is closed the same way.
`destination` additionally carries a default because omitting it is a *legitimate request* rather
than a mistake: it selects the inline outcome.

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
5. A `destination` that does not resolve, through the family's own extension map, to PNG is refused
   as `InvalidRequest`. **This step sits with the request-shape checks and not with the
   file-system ones** because it is a judgment about the request: a destination named for another
   format is asking for PNG bytes under a name that claims otherwise, which needs no file to
   diagnose
6. `policy.TryResolveRead(path, …)` — a refusal is returned as `PathNotPermitted` carrying the
   policy's own message unchanged. A relative path is interpreted against the workspace here,
   using the same policy the read tool and the text file family use
7. An existing directory is refused as `InvalidRequest`, stating the fact and prescribing nothing
8. `ImageMediaTypes.TryResolveCroppableMediaType` — a type no region can be taken from is refused
   through `ImageMediaTypes.DenyNonCroppableType`. The type is judged before the file system is
   consulted for size or content, matching the read tool's documented order
9. A non-existent file is refused as `TargetNotFound`
10. When a destination was named: `policy.TryResolveWrite` refuses as `PathNotPermitted` with the
    policy's own message; an existing directory refuses as `InvalidRequest`; an existing file
    refuses as `InvalidRequest`; an absent parent directory refuses as `TargetNotFound`. See
    *The Destination* below for why this step sits here
11. A file larger than `MaxBinaryBytes` is refused as `ResourceTooLarge`, naming the ceiling. The
    file is opened once and its size read from that open handle, before any content is read, so
    nothing is loaded to discover a file was oversized and no file can grow between the size being
    judged and the bytes being taken. Exactly the number of bytes the ceiling admitted is then
    read; a file that shrinks under the read ends it early, which surfaces as an
    `EndOfStreamException` and is refused as an unreadable file
12. The header is read. A header that will not read is refused as `UnsupportedMediaType` stating
    that the file is not readable as the resolved type — **and stating no size, because none was
    read**
13. A file the header reader reports as one the decoder will not decode is refused as
    `UnsupportedMediaType`, stating that the file is well formed and naming the declared size
14. The decode budget is checked against the declared dimensions
15. The region is checked against the declared dimensions
16. The image is decoded and the region copied out of it — the first step that allocates pixel data
17. The region is encoded. **Inline**: a result larger than `MaxBinaryBytes` is refused as
    `ResourceTooLarge`, and otherwise the region is returned as image content. **Written**: the
    file is created and a text confirmation returned

The policy decision precedes every observation of the file system, so a refused path never discloses
whether it exists.

#### The Destination

**Step 10 sits after the source's own file-system checks, and before the first byte is read.**
After, because the source is the subject of the request: when both the source and the destination
are wrong, the refusal should be about the thing the model asked to look at, and nothing expensive
has happened either way — step 11 is the first byte read. Before that read, because a mistyped
destination must not cost a file read and a decode. The governing principle is the same one the
rest of the algorithm follows: each step refuses before the next allocates anything.

**The write decision is taken alone.** A location a read-only grant permits is not thereby one the
agent may produce files in, and the policy's refusal is returned unchanged because it enumerates
every permitted location with its access level — which is what lets a confined agent recover to one
it may actually use rather than guess.

**The tool is published under every policy, and its description never varies.** Both are one
decision. The text-file and file families withhold their write-performing tools when the policy
permits no writing anywhere — see *Policy-derived publication* in the system design — but this tool
is not one of those: its primary mode returns the region inline and writes nothing, so it succeeds
under a read-only policy and withholding it would remove a working capability. What remains is
whether the *description* should stop advertising the destination when it cannot be used, and it
must not, for a reason that is structural rather than stylistic. `GuardedToolFactory.Create` sets
the tool's own description through a value a pack could compute per composition, but the
`destination` **parameter's** description is a `[Description]` attribute argument on the delegate
parameter, and an attribute argument is a compile-time constant. A description that stopped
mentioning the destination would therefore ship inside the same declaration as a parameter
description still offering it — one payload contradicting itself, which serves truth strictly worse
than one honest description does. The cost of the fixed description is one refusal, once, and that
refusal enumerates the locations the agent could write to instead.

**The destination must be named as a `.png` file.** A region is always encoded as PNG, so a
destination named `.jpg` would hold PNG bytes under a name that says otherwise: a file every later
reader, human or program, would be entitled to misread, and one this unit would have produced
knowingly. The check reuses `ImageMediaTypes.TryResolveMediaType` rather than adding a member to
that unit, so the family keeps one answer to "what is this file" and a capitalized extension is
accepted for the same reason every other extension is matched case-insensitively. **A blank or
whitespace-only destination has no extension and lands in this same refusal.** That is deliberate,
not an omission: the answer is truthful and actionable, and it means a destination the model did
not really intend is never silently read as "no destination".

**An existing file is never replaced, and two mechanisms are involved.** The `File.Exists`
pre-check is what **teaches** — without it the model receives only "could not be written" and has
nothing to correct — and `FileMode.CreateNew` is what **enforces**, so a file appearing between the
check and the write is refused rather than silently destroyed. Neither is redundant. This is a
deliberate strengthening over `TextFileCreateTool`, which uses `File.WriteAllTextAsync` after its
existence check and so leaves that window open; the observable behavior is identical, and the
single line is the one to change if exact sibling parity is ever preferred.

**A destination equal to the source is caught by that same refusal**, because step 9 has already
proven the source exists. An image can therefore never be consumed by its own region, and no
special-case comparison is needed to guarantee it.

**No directory is created.** A missing parent is refused rather than materialized, because silently
creating a tree is a side effect the operator never asked for and, on a mistyped path, would
scatter directories the agent then believes are real. That is the same reading `TextFileCreateTool`
takes.

#### The Binary Ceiling and the Written File

**`MaxBinaryBytes` governs the inline return only. A written file receives no byte ceiling of its
own, and that is a decision rather than an omission.**

The ceiling exists to bound what this family hands *a provider*. `ToolLimits`' own remarks say it
plainly: images are not charged against the context window by the same route as text, so a byte
ceiling is the right control for them, and that ceiling sits at or below the per-attachment
ceilings the major providers publish. **A file on disk is handed to no provider and occupies no
context window.** Applying a provider-attachment ceiling to it would refuse a legitimate figure for
a reason that does not describe it.

**The write is not unbounded; it is bounded by two controls that already exist.** The decode is
capped by `MaxImagePixels` and by the decoder's own published per-axis bound, both judged from the
header before a pixel is allocated. The output is bounded by the requested region, which must lie
wholly inside those dimensions. The worst case a model can provoke is therefore one RGBA buffer of
at most `MaxImagePixels` pixels and a PNG encoding of it — the *same* worst case the inline path
already allows through decode. A host that wants it smaller lowers `MaxImagePixels`, which is the
control that actually governs it.

**Disk consumption is an operator concern the path grant already answers.** The operator chose which
directory is writable, and a tool that could write there could always fill it; `text_file_create`
carries no byte ceiling either. Adding one here would be a control in the wrong place, sized by a
number chosen for provider attachments.

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

**The result is constructed through `ToolResult.Image` or `ToolResult.Text`, and never through
`ToolResult.Structured`.** The last is serialized to JSON by design, which would destroy the content
the guarded factory exists to deliver intact. The inline shape is the same two-part
caption-plus-content list the read tool returns, so the guarantee holds by construction and the
host's image-promoting chat client needs no change.

**The inline caption restates the region and the source's dimensions**, so a follow-up region can be
aimed without reading the whole image again: `Cropped region 120,80 400x300 of a 1920x1080
image/png image, returned as image/png.`

**The written confirmation names the destination, the region and the source's dimensions**:
`Wrote the cropped region 120,80 400x300 of a 1920x1080 image/png image to
/home/u/session/panel.png.` The destination is there so the model can reference the file it just
produced; the region and the dimensions are there for the same reason the inline caption carries
them. **It carries no byte count**: unlike a line count on a created text file, a byte count is not
something a model can act on, and stating one would imply a ceiling that deliberately does not
exist.

**The destination is reported in the policy's own dialect**, through
`policy.EmitRelative(realDestination, destination)` — relative to the working directory when the
anchor is granted and the result lies within it, absolute otherwise. This is the idiom
`TextFileReadTool`, `MarkdownOutlineTool` and `FileListTool` already use, and it is what makes the
`document-assistant` demonstration truthful: a workspace destination comes back relative, while a
session folder outside the anchor comes back absolute, because an absolute path is the only name
that would actually reach it. *`TextFileReadTool` additionally passes its result through
`TextLines.ToForwardSlash`; the image family has no dependency on `TextLines` today and gains
nothing from one, so the confirmation reports the host's own separators. Recorded as a deliberate
difference rather than an oversight.*

**Every pixel buffer this unit creates is released.** The decoded image is disposed as soon as the
region has been copied out of it, because the region copy is independent of its source and nothing
the region needs outlives the surface it came from; the region itself is disposed once it has been
encoded, which is why the encode precedes the disposal rather than following it. The write, when
one was asked for, happens inside that same block, which is harmless because the encoded bytes are
already detached from the surface by then. The library
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
`InvalidRequest` refusal. The same classification covers the write: every way a `FileStream` create
can fail for a reason the model provoked is one of those four, and the defects that must stay loud
are excluded from all of them. It also already covers the source read ending early because the file
shrank beneath it: `EndOfStreamException` derives from `IOException`, so a file truncated between
the size being judged and the bytes being taken is refused as an unreadable file rather than
propagating. Decode failures are caught by a second explicit classification and
reported as `UnsupportedMediaType`. Both are enumerated rather than catching everything, so a
genuine defect still surfaces during development. **A failure inside the encoder, on a pixel buffer
this unit constructed, is a defect rather than anything a model can provoke, and is allowed to
propagate** — the same dividing line the read tool draws.

**Cancellation is not classified, so a canceled call propagates as the runtime expects.** A
cancellation is not an access failure, and reporting it as one would tell the model a destination
was unwritable when the caller simply stopped waiting. It has one consequence worth stating: the
`FileStream` is disposed with whatever bytes reached it, so a cancellation arriving mid-write
leaves a partial `.png` at the destination. A retry meets that file as an existing one and is
refused, which is the correct answer — the partial file is the operator's to remove, and deleting
it here would mean a canceled tool call performing a file deletion the agent never asked for.
`text_file_create` carries the same exposure for the same reason.

**Neither the decoding library's own exception text nor the operating system's ever reaches a
model.** Both are developer-facing and may echo values read out of the file or details of the
host's layout, neither of which belongs in a transcript that leaves the process. Every message this
unit composes is its own. `DestinationUnwritable` deliberately offers no remedy, because there is
nothing true to say: the unit does not know why the operating system refused, and a guess would be
worse than silence. That is the same line `FileUnreadable` draws.

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
locations — and this now happens for a refused *destination* as well as for a refused source, which
is what makes a read-wide, write-narrow configuration recoverable from. Every refusal this unit
composes itself interpolates only integers and the resolved media type — no absolute path, no
permitted location, no directory separator. **Each refusal states
a fact and stops**, and none of them names a sibling tool: the well-formed-but-undecodable refusal
hands over the declared size itself rather than sending the model elsewhere for it, and the
non-croppable-type refusals state what the format is and which formats a region can be taken from.

**That disclosure rule is scoped to refusals, not to results.** The written confirmation names the
destination, in the policy's own dialect, exactly as `text_file_read`'s header names the file it
read. This is not an exception being carved out: a name the model is handed is a name it can hand
forward, and withholding it would leave the model unable to reference the file it just produced —
which is the entire purpose of writing one. The requirement states the rule the same way.

#### Dependencies

`PathPolicy` and `ToolLimits` for the read decision, the write decision, the output dialect
(`PathPolicy.EmitRelative` and `PathPolicy.WorkingDirectory`) and both ceilings, `ImageMediaTypes`
for the croppable-type resolution, `ImageAdmission` for the read within the binary ceiling, the
header triage — including the declared size and whether the decoder expects to decode it, which it
obtains from `ImageProbe` — the decode budget and the decode itself, `ImageDestination` for the
destination's extension check, its write decision, its remaining refusals and its
`FileMode.CreateNew` write, `ToolResult` for every result it
returns, and
`GuardedToolFactory` for construction. From the Base Class Library: `File`, `Directory` and
`MemoryStream`. From the `CanvasNet` OTS item: the pixel buffer's
rectangular
sub-region copy and the encoder; see *CanvasNet Design*. **No type
from that library appears in this unit's signatures**, so the dependency stays an implementation
detail of the family. `AIFunction` and the content types, from
`Microsoft.Extensions.AI.Abstractions`, are the form the constructed tool and its result take.

`ImageAdmission` and `ImageDestination` are shared helpers of this subsystem rather than units, on
the precedent `ImageProbe` sets; see *Image Subsystem Design*. This unit keeps its own control flow,
its own check order and every one of its own messages — only the bodies of the steps it shares with
`ImageAutoCropTool` live in the helpers.

#### Callers

`ImagePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.

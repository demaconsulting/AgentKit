## Image

![AgentKit Tools Image Structure](ImageView.svg)

The Image subsystem is the image tool family: the pack an application attaches to give a
vision-capable agent policy-governed reading of images and PDF documents, reporting an image's
pixel dimensions wherever it can establish them.

### Overview

The subsystem's responsibility is to turn two file operations — reading visual content, and taking
a rectangular region of an image — into tools an agent can be handed safely, and to hand the model
the content itself rather than a serialized copy of it. It owns no containment logic of its own —
every decision about whether a path may be read is made by the `PathPolicy` the composing
application supplies — and its own
design is therefore about the things a tool must get right *around* that decision: constructing
tools so an unguarded one cannot exist, delivering binary content through the guarded path so it is
not flattened into JSON, returning refusals instead of throwing, keeping host layout out of
everything that reaches a model, and honoring the byte ceiling the policy carries instead of
returning a truncated image.

The boundary is narrow and deliberate. The subsystem reads the visual content a vision host can
render: the raster image types `png`, `jpg`/`jpeg`, `gif` and `webp`, and the paginated document
type `pdf`. It does not list, convert or interpret content, and the one thing it writes is a
cropped region of an image it was permitted to read — a single new PNG file, at a destination the
policy's write decision permits and nowhere else. It does not read vector formats: an `.svg` is
text a text tool reads, and an `.svgz` is that content compressed, which no tool in this family
reads. Each of those would be a separate decision an operator should be able to grant or withhold
separately, and none of them is needed for the "let the agent look at this file"
loop this family exists to support.

**A second, narrower boundary governs region extraction**, and the two are held in one place so the
family never gives two answers to "what is this file". Taking a region means *decoding*, which the
subsystem can do only for the still raster types `png`, `jpg` and `jpeg`. That set is the
intersection of what the family reads and what it can decode, chosen over the alternative of
"whatever the decoder handles": widening to formats the read tool refuses would create an
accidental conversion path — a model could launder an unreadable format into a viewable one by
taking a region covering the whole image — which nobody designed and no requirement covers. Adding
formats later is then a single coherent widening across both tools, reviewable as one decision.

The subsystem contains five units, plus three shared helpers that are not units:

| Unit                | Responsibility                                                                |
|---------------------|-------------------------------------------------------------------------------|
| `ImageMediaTypes`   | Maps an extension to the media type the family reads, and refuses the rest    |
| `ImageReadTool`     | Publishes `image_read`: returns one permitted file's content with a caption   |
| `ImageCropTool`     | Publishes `image_crop`: returns a pixel region of one permitted image         |
| `ImageAutoCropTool` | Publishes `image_auto_crop`: returns the content region of a permitted image  |
| `ImagePack`         | Publishes the tools as one family under the `image` prefix, gated on Vision   |

`ImageProbe`, `ImageAdmission` and `ImageDestination` are shared helpers rather than units: each is
an internal static function of its arguments, with no state, no policy, no result and nothing a
requirement would promise that the tools consuming them do not already promise observably.
`ImageProbe` answers what a file's header declares; `ImageAdmission` turns a permitted file into
pixels within the host's ceilings; `ImageDestination` decides whether a named destination may
receive a new PNG and writes one there. They follow the same treatment `TextLines` and
`MemoryEmbedding` receive in their subsystems — covered by this subsystem's review-set, documented
here rather than in a file of their own.

The two extracted helpers exist because their logic is security-relevant and must have exactly one
implementation. Both region tools answer the same five destination questions in the same order, and
both must read within the same binary ceiling and refuse the same oversized declaration before any
pixel buffer exists. A second copy of either sequence would be a second place a later correction has
to reach, and the one it failed to reach would be the one an operator was relying on.

### Interfaces

The subsystem exposes five public types — `ImagePack`, the unit of attachment, `ImageMediaTypes`,
the media-type map a caller may consult, and the three tool classes, which publish the name
constants their tools carry. Each tool's factory is `internal`, so a tool cannot be obtained
except through the pack that claims its family prefix — the pack is the unit of attachment, and an
application that could construct a single tool directly could also construct one outside the
family whose prefix protects it from collision.

| Interface                   | Direction | Format                       | Constraints                           |
|-----------------------------|-----------|------------------------------|---------------------------------------|
| `ImagePack`                 | Outbound  | AgentKitCore `IToolPack`     | Prefix `image`; requires Vision       |
| `ImageReadTool.ToolName`    | Outbound  | `string` constant            | The name the read tool carries        |
| `ImageCropTool.ToolName`    | Outbound  | `string` constant            | The name the crop tool carries        |
| `ImageAutoCropTool.ToolName`| Outbound  | `string` constant            | The name the auto-crop tool carries   |
| `ImageMediaTypes`           | Outbound  | Media-type maps and refusals | Extension-driven; states content kind |
| `PathPolicy`                | Inbound   | AgentKitCore policy object   | Supplied at construction              |
| File system                 | Inbound   | Base Class Library file APIs | Reached only where policy permits     |

The subsystem consumes `PathPolicy`, `ToolLimits`, `ToolResult`, `GuardedToolFactory`, `IToolPack`
and `HostCapabilities` from AgentKitCore, `AIFunction` and the content types (`AIContent`,
`DataContent`, `TextContent`) from `Microsoft.Extensions.AI.Abstractions` reached through Core, and
`TextFileReadTool.ToolName` from the sibling TextFile subsystem — read as a constant, for the one
classification an unsupported `.svg` earns. It additionally consumes the `CanvasNet` raster
imaging library directly, which is the one OTS runtime dependency this package takes; see
*CanvasNet Design*. It exposes nothing of its own that another package would depend on.

**No type from the imaging library crosses the subsystem's boundary.** `ImageProbe` takes bytes
and reports integers and booleans; nothing in the subsystem's public surface mentions the library,
so the generated API reference the package ships never names it and an application is never made
to depend on its types to use the family. `ImageAdmission` hands a decoded pixel buffer back to the
tool that asked for it, but only across an internal boundary, and the calling tool holds it under
its own `using` so no helper decides how long a buffer lives.

### Design

**The shared header probe.** `ImageProbe` answers what an image file declares about itself, from
its header alone. It reports the declared pixel dimensions for the two formats this subsystem can
read headers of, and whether the decoder expects a full decode to succeed.

*Nothing it does allocates a pixel buffer.* A PNG header is 33 bytes; a JPEG header is found by a
bounded scan of leading marker segments. That is what lets a caller consult the helper *before*
deciding whether decoding the file is affordable at all.

*Every probe is offered, never required.* The helper reports failure rather than throwing, because
the tools want different things from a failure: for the read tool a failed probe costs the caption
its dimensions and nothing else. Neither the tools nor the helper ever surfaces the library's own
exception text, which is developer-facing and may echo values read out of the file.

*Decode feasibility is read from the header rather than inferred from a failed decode.* The
library itself reports, from the header, whether it expects to decode the file — so the subsystem
holds no format knowledge of its own: no byte offset, no feature name, nothing that would have to
be revisited when a decoder gains or loses a capability. A refusal can therefore tell a model that
its file is well formed and merely of a kind this tool does not decode, rather than leaving it to
guess whether the file is damaged, and the rule generalizes to any feature the decoder declines
rather than the one variant this subsystem happened to know how to detect. The cost is that the
refusal names no feature, because the report names none — the only feature token the library
offers is reachable solely by attempting the decode this refusal exists to avoid. The distinction
being protected is *damaged versus not damaged*, and that is stated directly; the alternative was
a refusal that said only "could not be decoded" about a file that is not damaged at all.

**The read caption carries the image's pixel dimensions.** A model can see a picture but cannot
measure one, so without the size stated alongside the content it has no coordinate space in which
to name a region of that image. `ImageReadTool` therefore states the declared size in its caption
wherever the header probe establishes it, and states none it did not read. **The probe is an
enrichment, never a condition**: this family's read contract has never been to validate content —
it hands a permitted file's bytes to the provider, whose own decoder is the authority on them — so
a header this library cannot read costs the caption its dimensions and nothing else. Refusing on a
failed enrichment would narrow the tool for no safety gain, since the bytes were already inside
the binary ceiling and were already going to be returned. The rule is live rather than defensive:
`gif`, `webp` and `pdf` are in the read tool's admitted set and have no probe at all, which makes
them the common case rather than the edge case, and a malformed or truncated header may still
belong to a file a provider renders.

**A region is returned inline or written where the policy permits, refused rather than clamped, and
bounded before it is decoded.** `ImageCropTool` returns the region a caller names in pixels as image
content, carrying the source's pixels unaltered, when no destination is named; when one is named it
writes that region as a new PNG file and confirms in text instead.

*The destination is a second, independent policy decision.* It is resolved through
`PathPolicy.TryResolveWrite`, never derived from the read that admitted the source, because a path
an agent may read is not thereby one it may create files in — which is what keeps a read-wide,
write-narrow configuration meaningful rather than decorative, and what makes one `image_crop` call
exercise both of the grants an application configured. The destination must be named as a `.png`
file, because a region is always encoded as PNG and a name claiming otherwise would be a file every
later reader is entitled to misread; an existing file is never replaced; and no directory is ever
created. **`MaxBinaryBytes` does not apply to a written file**: that ceiling bounds what this family
hands a provider, and a file on disk is handed to none — what bounds a written region is the decode
budget below and the extent of the region asked for. See *ImageCropTool Unit Design*, which states
the decision in full.

*A region that does not lie wholly inside the image is refused, never reduced.* Reducing it would
answer a different question from the one asked while reporting success, and the model cannot detect
the substitution: it would then describe, confidently, a part of the picture it never received —
the same failure the whole family exists to prevent. The refusal names the image's real dimensions,
which is what turns it into the fact the model was missing.

*The decode budget is decided from the header, before any pixel data is allocated.* A compressed
image well inside the binary ceiling can declare far more pixels than its size suggests, and a
decoded buffer costs four bytes per pixel whatever the file's own encoding was — so 8192 × 8192 is
67,108,864 pixels and 268,435,456 bytes from a file of a few hundred kilobytes. Two bounds are
checked and neither implies the other: the decoder's published per-axis bound, read from the
decoder so the two cannot drift, and `MaxImagePixels` on the policy's limits, which bounds the
product. **The estimate uses a fixed four bytes per pixel and never the channel count a file
declares**, because a header reports the file's own encoding — a palette-indexed image declares one
channel per pixel — while the decoded buffer is always RGBA; an estimate from the declared channels
would under-count exactly the format a hostile caller would choose by a factor of four. See
*ImageCropTool Unit Design*, which states the arithmetic in full.

*The region comes back as PNG whatever the source was*, because the encoding alters no pixel — and
in the region a model asked to examine closely, a compression artifact is indistinguishable from
the thing being examined.

**An image is also trimmable to its own content, which is the region a model cannot name.**
`ImageAutoCropTool` publishes `image_auto_crop`, which takes no region at all: it finds the
rectangle the image's content occupies, expands it by a padding the caller may state, and returns
or writes that. The need is the complement of `image_crop`'s: a model can see that a screenshot or
a slide export is mostly margin, but it cannot measure where the margin stops, so any rectangle it
named would be a guess.

*The background is sampled from the image's own border, never assumed.* The value used is the modal
exact color of the one-pixel perimeter ring, visited in a fixed order with ties resolved to the
earliest position. An assumed white would return the picture unchanged — while reporting success —
on a dark-themed screenshot, a colored slide and a transparent export alike, all of which are
ordinary inputs. The **mode** is used rather than a corner sample or a mean because both of those
fail on the equally ordinary case of content running to an edge: a corner may itself be content,
and a mean over a partly-content border lands on a color present nowhere in the image, so nothing
matches it.

*A small fixed tolerance is what makes the rule survive real images.* A pixel counts as background
when no channel — alpha included — differs from the sampled background by more than eight of 255.
Matching exactly would classify an anti-aliased ring or a lossy encoder's edge ringing as content
and return nearly the whole picture. The tolerance is **not** a parameter: a caller that cannot see
the image cannot choose one better than the default, and a knob a model must guess at is exactly the
unpredictability that got automatic trimming rejected for this library the first time. Fixing it
makes the region a pure function of the file and the padding, on every host and every run. The
measure is a per-channel maximum rather than a Euclidean distance so that it stays integer
arithmetic — floating point is not guaranteed bit-identical across runtimes — and because for a
given tolerance it is the stricter of the two, so a marginal pixel is classified as content. That
direction matters: misclassification can only ever *enlarge* the region, never lose content. See
*ImageAutoCropTool Unit Design*, which states the algorithm and both decisions in full.

*Padding clamps at an edge rather than refusing.* Content flush to an edge is ordinary rather than a
fault, and a padding larger than every margin yields the whole image, which is truthful. This is not
the substitution `image_crop` refuses: there the caller named a rectangle and would have received a
different one, whereas here the caller named none, so there is nothing to substitute for.

*An image that is entirely background is refused, and nothing is written.* There is no honest region
to return; returning the whole picture would answer a different question while reporting success.
The destination, when one was named, has already passed every governance check by then, so the
guarantee is positional — the write is the last step and is never reached.

**Construction.** `ImagePack.CreateTools` receives the composition's policy and calls each tool's
internal `Create(PathPolicy)`. Each factory validates the policy, then builds the tool through
`GuardedToolFactory.Create`, capturing the policy in the tool's delegate. There is no other
construction path, no setter and no default policy, so a tool that is unguarded, or governed by a
policy other than the composition's, is unrepresentable rather than merely discouraged. Each delegate
is declared to return `Task<object>` because a tool returns a union — a refusal, or content — and
that declared shape is exactly the case the guard exists to protect.

**The guard is load-bearing here, not incidental.** A successful read returns a caption followed by
binary content — a `List<AIContent>`. The underlying function factory decides whether to serialize
a result by the delegate's *declared* return type, and an `object`-declared delegate would have its
content flattened into a `JsonElement` before a provider ever saw it. Serialized, the provider
receives no image; the model, told a tool returned one, reports that it can see the image and
fabricates a description of content it never received, with no error to notice. The guarded
factory's selective result marshalling is what keeps the content intact, which is why this
family in particular cannot be constructed any other way. Delivering the content intact to the
runtime is necessary but not always sufficient: a provider that accepts images on messages and not
in tool responses discards it at the wire, which is the separate problem
*ImagePromotingChatClient Unit Design* addresses at the host's chat client rather than here.

**A path a model supplies is a workspace-relative path.** The family shares the access policy the
text file family uses, so a name a text file listing reported is directly usable here. The tool
interprets no path itself: it passes the model's text to the policy, which holds the workspace a
relative name is measured against. An absolute path remains expressible and remains subject to the
same containment decision.

**One read decision per source, one independent write decision per destination.** Both tools
consult `TryResolveRead` for the file they are asked to look at, and `ImageCropTool` additionally
consults `TryResolveWrite` for a destination it is asked to write. The two are taken separately
and neither is derived from the other, so a path an agent may read is refused for writing unless a
write grant permits that as well — which is what keeps a read-wide, write-narrow configuration
meaningful. Nothing in the subsystem combines the two decisions or re-implements either. Each is
made on the path's normalized location, so a request outside the permitted location is refused
without the tool having to reason about containment, and each is made before anything is learned
about the file, so a refused path never discloses whether it exists.

**The type is decided from the extension.** `ImageMediaTypes` maps an extension to the media type
the family reads it as, because the media type is what a provider is told the content is and a
caller that named a `.png` is asking for it to be delivered as one. A type the family cannot read
is refused through the same unit, which owns whether naming a sibling reader states what the file is
rather than prescribing a way around the refusal: an `.svg` genuinely *is* text, so its refusal says
so and names `text_file_read`, on the same basis as `text_file_read`'s own binary-content refusal
naming `image_read`; an `.svgz` is stated as compressed vector content with nothing further, and any
other extension is stated as unsupported with nothing further.

**A denial states a fact and stops.** Refusals this subsystem composes — an absent path, a directory,
a missing file, a ceiling overrun — say what is so and prescribe no course of action, because a
denial that suggested one was measured driving a model into a destructive workaround the user had
explicitly forbidden.

**Image and PDF take different result paths, by necessity.** The result constructor for an image
refuses a media type that does not begin `image/`, so a PDF — `application/pdf` — cannot go through
it. The read tool dispatches on the resolved media type: an `image/*` type through the image result
path, `application/pdf` through the binary result path. Both produce the identical
caption-plus-content shape, so the marshalling guarantee is unaffected; the split exists only
because the image constructor's guard would otherwise throw.

**Refusals are results.** Every condition a model can provoke — an absent path, a path outside the
permitted location, a directory, an unsupported type, a missing file, an oversized file — produces a
`ToolResult.Denied` naming its reason. Nothing is thrown at a model, because an exception raised
during a tool call ends the agent's turn and strands it. A refusal the tool composes itself — an
absent path, a directory, an unsupported type, a ceiling overrun — is fixed text carrying at most an
integer naming a ceiling and the resolved media type in a caption. A refusal the policy composes — a
path outside a permitted location — deliberately discloses the request, its interpretation and the
permitted locations, so a confined model learns where it may work.

**The ceiling refuses, it does not truncate.** `MaxBinaryBytes` bounds what the tool may return;
the tool opens the file once, judges its size from that open handle before reading any content, and
refuses an overrun with the ceiling named,
never returning a truncated image, because a partial image is corruption the model cannot detect and
will reason past. Reading exactly the count the ceiling admitted, from the handle the size was read
from, is what binds the ceiling to the bytes actually taken rather than to a size that might already
have changed.

**The family is gated on Vision.** `ImagePack` declares `HostCapabilities.Vision`, so a host that
has not declared it receives none of the family's tools — and receives none because the composition
never asks the pack for them, not because it offers them and refuses later. A model that cannot see
an image is therefore never offered a tool that returns one it could only fabricate a description
of.

**The tools are published together because they are one capability.** The read tool states the
coordinate space and the crop tool consumes it; a family publishing only one of them would offer a
model either a region request it cannot aim, or a size it has nothing to use. The auto-crop tool
completes the set from the other side: it answers the region question a model cannot state in that
coordinate space at all, because it can see that a picture is mostly margin without being able to
measure where the margin stops. That is why the pack creates all three in the one place the family
prefix is claimed, rather than leaving the pairing to each application's composition code.

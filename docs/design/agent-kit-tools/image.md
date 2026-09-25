## Image

![AgentKit Tools Image Structure](ImageView.svg)

The Image subsystem is the image tool family: the pack an application attaches to give a
vision-capable agent policy-governed reading of images and PDF documents, reporting an image's
pixel dimensions wherever it can establish them.

### Overview

The subsystem's responsibility is to turn two file operations — reading visual content, and taking
a rectangular region of an image — into tools an agent can be handed safely, and to hand the model
the content itself rather than a serialized copy of it. It owns no containment logic of its own —
every decision about whether a
path may be read is made by the `PathPolicy` the composing application supplies — and its own
design is therefore about the things a tool must get right *around* that decision: constructing
tools so an unguarded one cannot exist, delivering binary content through the guarded path so it is
not flattened into JSON, returning refusals instead of throwing, keeping host layout out of
everything that reaches a model, and honoring the byte ceiling the policy carries instead of
returning a truncated image.

The boundary is narrow and deliberate. The subsystem reads the visual content a vision host can
render: the raster image types `png`, `jpg`/`jpeg`, `gif` and `webp`, and the paginated document
type `pdf`. It does not write, list, convert or interpret content, and it does not read vector
formats: an `.svg` is text a text tool reads, and an `.svgz` is that content compressed, which no
tool in this family reads. Each of those would be a separate decision an operator should be able to
grant or withhold separately, and none of them is needed for the "let the agent look at this file"
loop this family exists to support.

**A second, narrower boundary governs region extraction**, and the two are held in one place so the
family never gives two answers to "what is this file". Taking a region means *decoding*, which the
subsystem can do only for the still raster types `png`, `jpg` and `jpeg`. That set is the
intersection of what the family reads and what it can decode, chosen over the alternative of
"whatever the decoder handles": widening to formats the read tool refuses would create an
accidental conversion path — a model could launder an unreadable format into a viewable one by
taking a region covering the whole image — which nobody designed and no requirement covers. Adding
formats later is then a single coherent widening across both tools, reviewable as one decision.

The subsystem contains four units, plus one shared helper that is not a unit:

| Unit              | Responsibility                                                              |
|-------------------|-----------------------------------------------------------------------------|
| `ImageMediaTypes` | Maps an extension to the media type the family reads, and refuses the rest  |
| `ImageReadTool`   | Publishes `image_read`: returns one permitted file's content with a caption |
| `ImageCropTool`   | Publishes `image_crop`: returns a pixel region of one permitted image       |
| `ImagePack`       | Publishes the tools as one family under the `image` prefix, gated on Vision |

`ImageProbe` is a shared helper rather than a unit: it is an internal static function of bytes
with no state, no policy, no result and nothing a requirement would promise that the tools
consuming it do not already promise observably. It follows the same treatment `TextLines` and
`MemoryEmbedding` receive in their subsystems — covered by this subsystem's review-set, documented
here rather than in a file of its own.

### Interfaces

The subsystem exposes three public types — `ImagePack`, the unit of attachment, and
`ImageMediaTypes`, the media-type map a caller may consult — plus the name constants the two tool
units publish. Each tool's factory is `internal`, so a tool cannot be obtained except through the
pack
that claims its family prefix — the pack is the unit of attachment, and an application that could
construct a single tool directly could also construct one outside the family whose prefix protects
it from collision.

| Interface               | Direction | Format                       | Constraints                           |
|-------------------------|-----------|------------------------------|---------------------------------------|
| `ImagePack`             | Outbound  | AgentKitCore `IToolPack`     | Prefix `image`; requires Vision       |
| `ImageReadTool.ToolName`| Outbound  | `string` constant            | The name the read tool carries        |
| `ImageCropTool.ToolName`| Outbound  | `string` constant            | The name the crop tool carries        |
| `ImageMediaTypes`       | Outbound  | Media-type maps and refusals | Extension-driven; states content kind |
| `PathPolicy`            | Inbound   | AgentKitCore policy object   | Supplied at construction              |
| File system             | Inbound   | Base Class Library file APIs | Reached only where policy permits     |

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
to depend on its types to use the family.

### Design

**The shared header probe.** `ImageProbe` answers what an image file declares about itself, from
its header alone. It reports the declared pixel dimensions for the two formats this subsystem can
read headers of, and — for a PNG — whether the file declares interlaced storage.

*Nothing it does allocates a pixel buffer.* A PNG header is 33 bytes; a JPEG header is found by a
bounded scan of leading marker segments. That is what lets a caller consult the helper *before*
deciding whether decoding the file is affordable at all.

*Every probe is offered, never required.* The helper reports failure rather than throwing, because
the tools want different things from a failure: for the read tool a failed probe costs the caption
its dimensions and nothing else. Neither the tools nor the helper ever surfaces the library's own
exception text, which is developer-facing and may echo values read out of the file.

*Interlacing is read from the header rather than inferred from a failed decode.* The decoder
reports an interlaced file and a corrupt file with the same exception type, so the two are
indistinguishable after the fact, and matching on an exception's message would be both fragile and
a route for developer-facing text to reach a model. One named byte offset — the last byte of the
PNG header payload — lets a refusal tell a model the true, specific reason rather than leaving it
to guess whether its file is damaged. The cost is a single constant of format knowledge in a
subsystem that otherwise decides an image's type from its extension; the alternative was a refusal
that said only "could not be decoded" about a file that is not damaged at all.

**The read caption carries the image's pixel dimensions.** A model can see a picture but cannot
measure one, so without the size stated alongside the content it has no coordinate space in which
to name a region of that image. `ImageReadTool` therefore states the declared size in its caption
wherever the header probe establishes it, and states none it did not read. **The probe is an
enrichment, never a condition**: this family's read contract has never been to validate content —
it hands a permitted file's bytes to the provider, whose own decoder is the authority on them — so
a header this library cannot read costs the caption its dimensions and nothing else. Refusing on a
failed enrichment would narrow the tool for no safety gain, since the bytes were already inside
the binary ceiling and were already going to be returned. The rule is live rather than defensive:
`gif`, `webp` and `pdf` are in the read tool's admitted set and have no probe at all.

**A region is returned inline, refused rather than clamped, and bounded before it is decoded.**
`ImageCropTool` returns the region a caller names in pixels as image content, carrying the source's
pixels unaltered — nothing is written, so the capability adds no write decision and no new location
an agent can reach.

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

**One decision per read.** Both tools consult `TryResolveRead`, and nothing in the subsystem
consults the write decision, combines the two, or re-implements either. The decision is made on the
path's normalized location, so a request outside the permitted location is refused without the
tool having to reason about containment, and it is made before anything is learned about the file,
so a refused path never discloses whether it exists.

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
the tool judges the file's size before opening it and refuses an overrun with the ceiling named,
never returning a truncated image, because a partial image is corruption the model cannot detect and
will reason past.

**The family is gated on Vision.** `ImagePack` declares `HostCapabilities.Vision`, so a host that
has not declared it receives none of the family's tools — and receives none because the composition
never asks the pack for them, not because it offers them and refuses later. A model that cannot see
an image is therefore never offered a tool that returns one it could only fabricate a description
of.

**The two tools are published together because they are one capability.** The read tool states the
coordinate space and the crop tool consumes it. A family publishing only one of them would offer a
model either a region request it cannot aim, or a size it has nothing to use — which is why the
pack creates both in the one place the family prefix is claimed, rather than leaving the pairing to
each application's composition code.

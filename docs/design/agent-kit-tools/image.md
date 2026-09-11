## Image

![AgentKit Tools Image Structure](ImageView.svg)

The Image subsystem is the image tool family: the pack an application attaches to give a
vision-capable agent policy-governed reading of images and PDF documents.

### Overview

The subsystem's responsibility is to turn one file operation — reading visual content — into a
tool an agent can be handed safely, and to hand the model the content itself rather than a
serialized copy of it. It owns no containment logic of its own — every decision about whether a
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

The subsystem contains three units:

| Unit              | Responsibility                                                              |
|-------------------|-----------------------------------------------------------------------------|
| `ImageMediaTypes` | Maps an extension to the media type the family reads, and refuses the rest  |
| `ImageReadTool`   | Publishes `image_read`: returns one permitted file's content with a caption |
| `ImagePack`       | Publishes the tool as one family under the `image` prefix, gated on Vision  |

### Interfaces

The subsystem exposes two public types — `ImagePack`, the unit of attachment, and
`ImageMediaTypes`, the media-type map a caller may consult — plus the name constant the tool unit
publishes. The tool's factory is `internal`, so a tool cannot be obtained except through the pack
that claims its family prefix — the pack is the unit of attachment, and an application that could
construct a single tool directly could also construct one outside the family whose prefix protects
it from collision.

| Interface               | Direction | Format                       | Constraints                           |
|-------------------------|-----------|------------------------------|---------------------------------------|
| `ImagePack`             | Outbound  | AgentKitCore `IToolPack`     | Prefix `image`; requires Vision       |
| `ImageReadTool.ToolName`| Outbound  | `string` constant            | The name the tool is published under  |
| `ImageMediaTypes`       | Outbound  | Media-type map and refusals  | Extension-driven; refusals redirect   |
| `PathPolicy`            | Inbound   | AgentKitCore policy object   | Supplied at construction              |
| File system             | Inbound   | Base Class Library file APIs | Reached only where policy permits     |

The subsystem consumes `PathPolicy`, `ToolLimits`, `ToolResult`, `GuardedToolFactory`, `IToolPack`
and `HostCapabilities` from AgentKitCore, `AIFunction` and the content types (`AIContent`,
`DataContent`, `TextContent`) from `Microsoft.Extensions.AI.Abstractions` reached through Core, and
`TextFileReadTool.ToolName` from the sibling TextFile subsystem — read as a constant, for the one
redirect an unsupported `.svg` earns. It exposes nothing of its own that another package would
depend on.

### Design

**Construction.** `ImagePack.CreateTools` receives the composition's policy and calls the tool's
internal `Create(PathPolicy)`. That factory validates the policy, then builds the tool through
`GuardedToolFactory.Create`, capturing the policy in the tool's delegate. There is no other
construction path, no setter and no default policy, so a tool that is unguarded, or governed by a
policy other than the composition's, is unrepresentable rather than merely discouraged. The delegate
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

**One decision per read.** The read tool consults `TryResolveRead`, and nothing in the subsystem
consults the write rule, combines the two, or re-implements either. The decision is made on the
path's real location, so a link that reaches outside the permitted location is refused without the
tool having to know links exist, and it is made before anything is learned about the file, so a
refused path never discloses whether it exists.

**The type is decided from the extension.** `ImageMediaTypes` maps an extension to the media type
the family reads it as, because the media type is what a provider is told the content is and a
caller that named a `.png` is asking for it to be delivered as one. A type the family cannot read
is refused through the same unit, which owns whether a redirect is honest to give: an `.svg` is
refused with a redirect to `text_file_read`, an `.svgz` is refused without one because no tool reads
it, and any other extension is refused without one because there is no better tool to name.

**Image and PDF take different result paths, by necessity.** The result constructor for an image
refuses a media type that does not begin `image/`, so a PDF — `application/pdf` — cannot go through
it. The read tool dispatches on the resolved media type: an `image/*` type through the image result
path, `application/pdf` through the binary result path. Both produce the identical
caption-plus-content shape, so the marshalling guarantee is unaffected; the split exists only
because the image constructor's guard would otherwise throw.

**Refusals are results.** Every condition a model can provoke — an absent path, a path outside the
permitted location, a directory, an unsupported type, a missing file, an oversized file — produces a
`ToolResult.Denied` naming its reason. Nothing is thrown at a model, because an exception raised
during a tool call ends the agent's turn and strands it. Every refusal message is composed from
compile-time constants, with the only interpolated values being an integer naming a ceiling and the
resolved media type in a caption, so no path, permitted location or directory separator ever reaches
the transcript.

**The ceiling refuses, it does not truncate.** `MaxBinaryBytes` bounds what the tool may return;
the tool judges the file's size before opening it and refuses an overrun with the ceiling named,
never returning a truncated image, because a partial image is corruption the model cannot detect and
will reason past.

**The family is gated on Vision.** `ImagePack` declares `HostCapabilities.Vision`, so a host that
has not declared it receives none of the family's tools — and receives none because the composition
never asks the pack for them, not because it offers them and refuses later. A model that cannot see
an image is therefore never offered a tool that returns one it could only fabricate a description
of.

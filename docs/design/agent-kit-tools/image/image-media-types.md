### ImageMediaTypes

![AgentKit Tools Image Structure](ImageView.svg)

The `ImageMediaTypes` class maps a file's extension to the media type the image family reads, and
composes the refusal for a file whose type it cannot read.

#### Purpose

To be the one place that decides what the image family can read, what it can take a region of, and
how it refuses what it cannot. The media type is what a provider is told a file's content is, so
deciding it from the extension —
rather than by sniffing bytes — keeps the decision predictable and keeps the refusal for an
unsupported type in a single place the tools delegate to.

**Both questions live here so the family never gives two answers to "what is this file".** The set
a region can be extracted from is narrower than the set that can be read, because extracting a
region means decoding: `png`, `jpg` and `jpeg` rather than those plus `gif`, `webp` and `pdf`.
Holding the two maps apart in separate units would make it possible for one to widen without the
other, and confining the croppable set to the intersection of what the family reads and what it
decodes is what stops a region request becoming an accidental format conversion for content the
read tool itself refuses.

The unit does no file-system work and reads no content. It answers questions about a path's
extension: what media type, if any, the family reads it as, whether a region can be extracted from
it, and — when the answer is none — what refusal a model should be handed.

#### Data Model

The class is static and holds no state.

| Member                | Type     | Invariant                                                       |
|-----------------------|----------|-----------------------------------------------------------------|
| `Png`                 | `string` | `image/png`; public constant                                    |
| `Jpeg`                | `string` | `image/jpeg`; public constant; carried by `.jpg` and `.jpeg`    |
| `Gif`                 | `string` | `image/gif`; public constant                                    |
| `Webp`                | `string` | `image/webp`; public constant                                   |
| `Pdf`                 | `string` | `application/pdf`; public constant; not an `image/` media type  |
| Croppable set         | `string` | Compile-time constant; interpolated into each croppable refusal |
| Denial messages       | `string` | Compile-time constants; contain no host location                |

#### Key Methods

##### TryResolveMediaType(string path, out string? mediaType)

Resolves the media type the family reads a file's extension as.

**Algorithm:** takes the path's extension, lowered with the invariant culture so the decision is
identical on every host, and maps it: `.png` to `image/png`; `.jpg` and `.jpeg` to `image/jpeg`;
`.gif` to `image/gif`; `.webp` to `image/webp`; `.pdf` to `application/pdf`. Any other extension
resolves to nothing.

**Postconditions:** returns `true` with the media type when the extension is supported, and `false`
with a null media type otherwise. The out parameter is annotated so a caller may use the resolved
type without a null check on the `true` branch.

##### DenyUnsupportedType(string path)

Composes the refusal for a file whose extension the family cannot read.

**Preconditions:** called only once `TryResolveMediaType` has reported the type unsupported, so the
refusal is always an `UnsupportedMediaType`.

**Algorithm:** chooses on the extension. An `.svg` genuinely *is* text and vector content, so the
refusal states that and names `TextFileReadTool.ToolName` — the reader for that kind of content. That
naming survives the library's rule that **a denial states a fact and never prescribes another tool**,
on exactly the basis the established precedent in `TextFileReadTool` does: its binary-content refusal
names `image_read` because what is being stated is what the file *is*, not a way around the refusal.
The two are deliberately kept symmetric rather than one being stripped and the other left. An
`.svgz` is that same content gzip-compressed, which no tool reads as such, so its refusal states only
that. Any other extension is stated as unsupported, with nothing further.

**Postconditions:** returns a `ToolResult.Denied` result naming the reason and, where the extension
identifies the kind of content, the reader for that kind.

##### TryResolveCroppableMediaType(string path, out string? mediaType)

Resolves the media type of a file a region can be extracted from.

**Algorithm:** takes the path's extension, lowered with the invariant culture, and maps `.png` to
`image/png` and `.jpg`/`.jpeg` to `image/jpeg`. Every other extension — including those the family
reads — resolves to nothing.

**Postconditions:** returns `true` with the media type when a region can be taken from the
extension, and `false` with a null media type otherwise. The out parameter is annotated so a caller
may use the resolved type without a null check on the `true` branch.

##### DenyNonCroppableType(string path)

Composes the refusal for a file whose extension no region can be extracted from.

**Preconditions:** called only once `TryResolveCroppableMediaType` has reported the type
uncroppable, so the refusal is always an `UnsupportedMediaType`.

**Algorithm:** chooses on the extension. A `.gif` is stated to be an animated raster image with no
single frame to take a region of. A `.webp` is stated to be a raster image this family does not
decode — its own wording, because its reason is its own rather than the animated one. A `.pdf` is
stated to be a paginated document whose region could only be taken by choosing a page and a
resolution to rasterize it at, which no tool here does; it earns its own refusal because it is the
one admitted type that *looks* croppable and is not, and without the reason stated a model would
reasonably retry with different coordinates. Each of the three additionally names the set of types
a region *can* be taken from, so the model is told what it may ask for and not only what it may
not. Any extension the family cannot read at all is delegated to `DenyUnsupportedType`, so the
family answers "what is this file" in a single voice.

**None of the refusals composed here names a sibling tool.** The one place a media-type refusal may
name one is where the naming *is* the statement of what the file is — an `.svg` is text, so the
text reader is its reader. A `.gif` genuinely *is* an image, so naming the image reader would not
classify it: it would offer a route to the content the refusal withheld, handing the model the
whole image after it asked to examine one part of it closely. A model that then described that part
confidently would be describing what it never examined, which is precisely the failure this family
exists to prevent.

**Postconditions:** returns a `ToolResult.Denied` result naming the reason and, for a readable
type, the set a region can be taken from.

#### Error Handling

The unit raises `ArgumentNullException` for a null path, which is a programming error in the calling
tool rather than anything a model can provoke; by the time the read tool calls this unit it has
already refused an absent path as a malformed request. No refusal message contains a path, a
permitted location or a directory separator — the messages are compile-time constants.

#### Dependencies

`ToolResult` for the refusals it composes, `DenialReason` for the reason each carries, and
`TextFileReadTool.ToolName` for the one classification an `.svg` earns — read as a published constant
so the named tool cannot drift from the name the sibling family publishes. From the Base Class
Library:
`Path` for the extension.

#### Callers

`ImageReadTool` calls `TryResolveMediaType` to decide a permitted file's type and `DenyUnsupportedType`
to refuse one it cannot read. `ImageCropTool` calls `TryResolveCroppableMediaType` and
`DenyNonCroppableType` for the same two questions about a region. Nothing else in this package calls
the unit, though all four methods are public so a composing application may consult the same maps.

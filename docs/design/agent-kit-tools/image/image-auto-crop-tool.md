### ImageAutoCropTool

#### Purpose

`ImageAutoCropTool` publishes the `image_auto_crop` tool: it trims one image the access policy
permits the agent to read down to the region that image's own content occupies, expands that region
by a padding margin, and either returns the result inline as image content or writes it as a new
PNG file at a destination a read-write grant permits.

**The unit exists because the region a model wants is usually the region it cannot name.** A
screenshot, a slide export or a rendered chart arrives surrounded by margin that carries no
information and costs the same resolution budget the content does. A model can see that a picture is
mostly empty; it cannot measure where the emptiness stops, because measuring is exactly what looking
does not give it. *ImageCropTool Unit Design* answers "give me this rectangle"; this unit answers
"give me the part that matters", which is the question a model can actually ask.

The unit's single responsibility is that decision. It owns no containment logic — `PathPolicy` makes
every read and write decision — no type map, no header probe, no decode budget and no destination
taxonomy. What is its own is the trimming: sampling a background, classifying pixels against it,
finding the content box, padding it and clamping it.

#### Data Model

| Member | Type | Invariant |
| --- | --- | --- |
| `ToolName` | `public const string` | `"image_auto_crop"`; carries the `image` family prefix |
| `ToolDescription` | `private const string` | Fixed; does not vary with the policy |
| `BackgroundTolerance` | `private const int` | `8`; not a parameter, see *The tolerance* below |
| `DefaultPadding` | `private const int` | `8`; applied when the caller states no padding |
| `MaxPadding` | `private const int` | `256`; the largest padding accepted, inclusive |
| Refusal texts | `private const string` | Fixed; name no host path |
| `Region` | `private sealed record` | Four integers; extent positive; `Describe()` gives `x,y WxH` |

`Region` states itself in the same form *ImageCropTool Unit Design*'s own region record does, so a
model reads one shape whichever tool produced it. It is a private value type in each unit rather
than a shared one, because it is a value each unit's messages state rather than a contract between
them.

The destination value the unit carries between its resolution step and its write step is
`ImageDestination.Destination`, which belongs to the shared destination helper rather than to this
unit — see *Dependencies*.

#### Key Methods

**`Create(PathPolicy policy)`** — internal factory; the only construction path. Validates the policy
(`ArgumentNullException` on null, because a missing policy is the composing application's error
rather than anything a model supplied), declares the delegate with every parameter defaulted, and
builds the tool through `GuardedToolFactory.Create`. The delegate is declared `Task<object>` because
the tool returns a union — a content list, a string confirmation, or a string refusal — and the
guarded factory's selective result passthrough is what keeps a content list from being serialized
into JSON before a provider sees it.

**`AutoCropAsync(policy, path, padding, destination, cancellationToken)`** — the request pipeline.
**The order of the checks is the contract**, and it is *ImageCropTool*'s order with the region's
shape replaced by the padding's, so a model that has learned one tool has learned both:

1. `path` null, empty or whitespace → `InvalidRequest`.
2. `padding < 0` → `InvalidRequest`; `padding > MaxPadding` → `InvalidRequest` naming the bound.
   A null padding is **not** a refusal: it means "apply the default", which is why the parameter is
   `int?` rather than defaulted to `8` directly — a null and a zero must stay distinguishable, and a
   negative value must still be told.
3. `destination` named but not a `.png` → `InvalidRequest`. Judged before any policy decision,
   because a destination that cannot truthfully hold PNG bytes is a contradiction in the request.
4. `policy.TryResolveRead` → `PathNotPermitted` carrying the policy's own disclosure.
5. `Directory.Exists` → `InvalidRequest`.
6. `ImageMediaTypes.TryResolveCroppableMediaType` → the family's own refusal for a type no region
   can be taken from. The set is `png`, `jpg`, `jpeg`; `gif`, `webp`, `pdf`, `svg`, `svgz` and any
   unknown extension are refused.
7. `File.Exists` → `TargetNotFound`.
8. `ImageDestination.TryResolve`, when a destination was named → the four shared destination
   refusals. **After** the source's checks, because the source is the subject of the request;
   **before** the first byte is read, because a mistyped destination must not cost a decode. The
   source having been proven to exist by then is what makes a destination equal to the source fall
   into the already-exists refusal.

**`TrimPermittedFileAsync(...)`** — admission, then trimming. `ImageAdmission.ReadWithinCeilingAsync`
reads within the binary ceiling; `ImageAdmission.TryTriage` refuses an unreadable header, a
well-formed file the decoder will not decode, and a declared size beyond the host's decode budget —
all before a pixel buffer exists; `ImageAdmission.TryDecode` produces the surface. The surface is
held under a `using`, so this unit disposes it at exactly the point the region has been copied out
of it.

**`SampleBackground(Surface)`** — the background is the **modal exact RGBA value of the image's
one-pixel perimeter ring**, visited in one fixed order: the top row left to right, then the bottom
row left to right, then the left column and then the right column, each excluding corners already
visited. Each pixel is packed into a `uint` and counted; a value replaces the incumbent only on a
**strictly greater** count, so when two values end level the winner is the one that **reached**
that count first in the scan order — not necessarily the one that appeared first. A border visited
as `A,B,B,A,A,B,B,A` ends four to four with `A` seen first, and `B` wins, because `B`'s fourth
pixel precedes `A`'s. Determinism is what the tie-break exists for and this rule has it; which of
two equally frequent border colors is chosen is arbitrary either way, so the rule is stated rather
than changed.

*Never assumed.* Nothing in the computation mentions white. *Not a corner*: one pixel is one sample,
and a compression artifact or a single-pixel rule at that coordinate picks a background matching
nothing in the image — a corner is also the pixel most likely to be atypical. *Not a mean*: a mean
over a border containing even a few content pixels — the ordinary case of content running to an edge
— drifts to a color present nowhere in the image, so nothing matches it and the trim degenerates to
the whole picture. *Modal*: the mode is always a color the image genuinely contains and is unmoved
by a minority of content pixels on the border.

Degenerate sizes are total rather than special-cased. A one-pixel-high or one-pixel-wide image is
entirely its own border; a one-by-one image makes its single pixel the background, so that image is
entirely background and is refused. Perimeter size is `2W + 2H − 4` for `W,H ≥ 2`, bounded above by
`2 × 8192 + 2 × 8192 − 4 = 32,764` pixels, which bounds the counting dictionary.

**`IsBackground(Rgba32 pixel, Rgba32 background)`** — the classification. A pixel is background when
**no channel, alpha included, differs from the background by more than `BackgroundTolerance`**.
Three reasons for the per-channel maximum rather than a Euclidean distance:

1. **Determinism.** A Euclidean radius needs a square root or a squared comparison; floating point
   is not guaranteed bit-identical across runtimes and architectures, and this library's thesis is
   that output is predictable from inputs. Integer Chebyshev is exactly reproducible everywhere.
2. **Plain meaning.** It states in one sentence what a reviewer and a model both reason in: no
   channel differs by more than N of 255. A Euclidean radius has no such reading.
3. **Safe direction.** For a given N the per-channel test is the stricter of the two on
   multi-channel drift, so it classifies a marginal pixel as *content*. **Misclassifying background
   as content only ever enlarges the region; misclassifying content as background would lose
   content.** The measure errs toward the harmless failure by construction.

**Alpha participates as a fourth channel under the same rule.** A fully transparent margin around a
rendered figure is the most common background in an export with transparency, and its RGB is
frequently arbitrary — encoders vary, and nothing requires it to be zero. If alpha were ignored, a
transparent margin whose RGB differed from the sampled background would read as content and defeat
the trim entirely. Including it also makes an opaque pixel over a transparent background differ by
the full range in one channel, so it is correctly content.

**A stated consequence, not hidden:** two pixels that are both fully transparent but carry different
RGB do not match under this rule. That is deliberate and is not special-cased — the effect is to
classify such a pixel as content, which enlarges the region and never loses anything, and keeping
the rule to one sentence is worth more than the marginal tightening.

**The tolerance is fixed at 8 of 255 (about 3.1%) and is not a parameter.** A caller cannot see the
image, so to choose a better value it would have to already know the noise characteristics of the
margin — which is the thing it called this tool in order not to have to know. A knob a model must
guess at produces output that varies with the guess, and that unpredictability is why automatic
trimming was rejected for this library the first time; fixing it makes the region a pure function of
the file and the padding. The value itself: JPEG chroma ringing beside a hard edge on a flat field
sits within roughly two to six counts of the flat value at ordinary quality settings, so 8 absorbs
it with margin; the outermost ring of an anti-aliased glyph or rule — the ring that decides whether a
halo survives — is blended only a few percent toward the ink; and genuine content clears 8 in at
least one channel in every realistic case, since the faintest useful gridline in a rendered chart is
at least 20 from white. The failure modes are asymmetric and both bounded: too small leaves a halo
and returns nearly the whole image, which is visible and harmless, while too large would eat faint
content, which is genuinely lossy. 8 sits an order of magnitude below any plausible faint-content
threshold.

**`TryFindContent(...)`** — scans every row through `Surface.GetRowSpan(int)`, tracking the minimum
and maximum content column and row and a `found` flag. **Every pixel is visited and there is no
early exit**, because an early exit would make the result depend on the traversal order. The scan
allocates nothing and costs one comparison per channel per pixel. It reports `false` for an image in
which no pixel is content.

**Padding and clamping** — with the content box found:

```text
left   = Max(0,      minX − padding)
top    = Max(0,      minY − padding)
right  = Min(W − 1,  maxX + padding)
bottom = Min(H − 1,  maxY + padding)
```

**Clamping is never an error.** Content flush to an edge is ordinary rather than a fault, and a
padding larger than every margin yields the whole image, which is a truthful and predictable answer.
This is not the substitution *ImageCropTool* refuses: there the caller named a rectangle and would
have received a different one, whereas here the caller named no rectangle at all, so there is nothing
to substitute for. The arithmetic cannot overflow, because the padding is at most 256 and the
coordinates are bounded by the decoder's per-axis maximum of 8192.

**`EncodeRegionAsync(...)`** — encodes the region as PNG with an alpha channel, then either returns
it inline or writes it. The inline branch applies `MaxBinaryBytes` to the encoded result: a region of
a compressed source, re-encoded losslessly, can genuinely exceed a ceiling the source sat well
inside, and this tool chooses the region itself so the caller cannot have narrowed it in advance. A
region written to a file is **not** measured against that ceiling, which bounds what the family
hands a provider; a file on disk is handed to none.

**Determinism.** Every step is integer arithmetic over the decoded buffer, in a fixed traversal
order, with a fixed tie-break and no image-derived threshold other than the exact mode. Given the
same file bytes and the same padding, the region is identical on every host, every target framework
and every run.

#### Error Handling

Every condition a model can provoke is a **returned refusal**, never an exception: an exception
raised during a tool call ends the agent's turn and strands it.

| Condition | Reason | Composed by |
| --- | --- | --- |
| Absent or blank path | `InvalidRequest` | This unit |
| Negative padding, padding beyond the bound | `InvalidRequest` | This unit |
| Destination not named `.png` | `InvalidRequest` | `ImageDestination` |
| Path outside a permitted location, or a destination outside a grant | `PathNotPermitted` | `PathPolicy`, unchanged |
| Path is a directory | `InvalidRequest` | This unit |
| Type no region can be taken from | `UnsupportedMediaType` | `ImageMediaTypes` |
| File not found; destination's parent missing | `TargetNotFound` | This unit / `ImageDestination` |
| Destination is a directory or already exists | `InvalidRequest` | `ImageDestination` |
| File beyond the binary ceiling; unreadable file | `ResourceTooLarge` / `InvalidRequest` | `ImageAdmission` |
| Header unreadable, decoder will not decode, body will not decode | `UnsupportedMediaType` | `ImageAdmission` |
| Declared size beyond the decode budget | `ResourceTooLarge` | `ImageAdmission` |
| **Image entirely background** | `InvalidRequest` | This unit |
| Inline result beyond the binary ceiling | `ResourceTooLarge` | This unit |
| Permitted destination the file system refuses | `InvalidRequest` | This unit, on `ImageDestination`'s report |

**The entirely-background refusal is this unit's own, and nothing is written when it fires.** The
destination has already passed every one of its governance checks by then, so the guarantee is
positional: the write is the last step and is simply never reached. There is no honest content region
for such an image — returning the whole picture would answer a different question while reporting
success, and returning an empty region is not a region at all.

**What is propagated rather than handled.** A null policy at construction. A failure inside the
*encoder*, on a pixel buffer this unit constructed, which is a defect rather than anything a model
can provoke — which is why the encoder sits outside the decode's own `catch` inside `ImageAdmission`.
Neither the decoding library's exception text nor the operating system's ever reaches a model: both
are developer-facing and may echo values read out of the file or details of the host's layout.

**Refusal hygiene.** Every refusal this unit composes itself interpolates only integers and the
resolved media type — no absolute path, no permitted location, no directory separator. The rule
governs refusals, not results: the confirmation for a written region names the destination in the
dialect `PathPolicy.EmitRelative` decides, because a name the model is handed is a name it can hand
forward.

#### Dependencies

- **`ImageAdmission`** (shared helper, same subsystem) — reading within the binary ceiling, the
  header triage, the decode budget and the decode; also the family's number and size formatting and
  its one classification of a file-system access failure.
- **`ImageDestination`** (shared helper, same subsystem) — the whole destination taxonomy: the
  `.png` extension check, the write decision, the directory / already-exists / missing-parent checks,
  the reported dialect and the `FileMode.CreateNew` write.
- **`ImageMediaTypes`** (unit, same subsystem) — the croppable type set and its refusals.
- **`ImagePack`** — the only caller; see *Callers*.
- **AgentKitCore** — `PathPolicy`, `ToolLimits`, `ToolResult`, `DenialReason`, `GuardedToolFactory`.
- **CanvasNet** — `Surface` (including `GetRowSpan`, `Crop` and `MaxDimension`), `Rgba32`,
  `PngCodec`, `PngColorType`. See *CanvasNet Design*.
- **`Microsoft.Extensions.AI.Abstractions`** — `AIFunction`, reached through Core.

The two shared helpers are **not units**, on the precedent `ImageProbe` sets; see *Image Subsystem
Design*, which states why and where they are reviewed.

#### Callers

`ImagePack.CreateTools` is the only caller. `Create` is `internal`, so the tool cannot be obtained
except through the pack that claims the `image` family prefix — a pack is the unit of attachment, and
an application able to construct a single tool directly could construct one outside the family whose
prefix protects it from collision. The pack publishes this tool **unconditionally**, under every
access policy, because its primary mode writes nothing; see *ImagePack Unit Design*.

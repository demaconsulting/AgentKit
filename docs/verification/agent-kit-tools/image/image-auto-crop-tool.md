### ImageAutoCropTool Unit Verification Design

This document describes the unit-level verification strategy for the `ImageAutoCropTool` class.

#### Verification Approach

Nothing is mocked or stubbed. The unit's whole purpose is to behave correctly against a real access
policy, a real file system and real image content, and a substituted decoder would verify only the
substitute. Each scenario constructs a real `PathPolicy` over a temporary directory tree it creates,
writes an image into it, builds the tool through its own internal factory, and invokes it the way a
runtime does — through `InvokeAsync` with a named argument dictionary — so that the delivery of the
image result through the guarded factory is exercised rather than bypassed. Both optional arguments
are added to the invocation only when the scenario supplies one, so an omitted padding and an
omitted destination reach the unit as genuinely absent arguments rather than as explicit nulls.

**Every image a scenario uses is built by the test.** No binary fixture is committed, so each
image's background, its content block and its noise are stated in code beside the scenario that
depends on it — which is what makes the region a correct trim must produce known by arithmetic
before the library is asked for it. The well-formed images are produced through the same decoder the
unit itself uses; the hostile and variant images are hand-built byte arrays, because no encoder will
produce a header that declares eight thousand pixels on a side and then supplies no pixel data at
all, and that file is precisely what proves a decode budget is judged from the header rather than
after the fact.

**No fixture in this file is white on white, and that is the central methodological decision.** The
single most likely wrong implementation of this unit compares pixels against white rather than
against a background it sampled, and that implementation passes every scenario written on a white
background. Every background here is therefore dark, saturated, transparent or dithered, and each
of the three background scenarios asserts the *exact* returned region rather than merely that
something smaller came back — so a unit that returned the whole picture fails visibly instead of
returning a weaker answer nobody notices.

**The trim tolerance is verified from both sides, by two fixtures that disagree at different
tolerances.** The anti-aliased fixture surrounds its content block with a one-pixel ring differing
from the background by four counts per channel: inside a tolerance of eight, outside a tolerance of
zero. A unit tolerating nothing returns a region one pixel larger on every side, which is a
different size and therefore a different assertion. The dithered fixture varies the background by up
to three counts per channel across the whole image, border included: a unit tolerating nothing
classifies almost every background pixel as content, the region becomes the whole image, and the
trim is defeated entirely — which is the specification's own stated failure, reproduced rather than
described. Between them the two make the tolerance's value load-bearing rather than merely present.

**The padding is verified at every boundary the clamp can reach**: content flush to the left and top
edges, content flush to the right and bottom edges, a padding larger than every margin, a padding of
zero, and an omitted padding. The first two matter separately because a unit clamping only at zero
passes the first and fails the second by asking the decoder for a region extending past the image.
The last two matter separately because "omitted" and "zero" must produce different regions, and the
standard fixture's margins exceed the default so the default is unclamped and therefore exactly
assertable.

**The JPEG scenario asserts a robust pair rather than an exact rectangle.** The encoder is
deterministic, so an exact rectangle would pass today; it would also make the scenario fail on an
encoder revision that changed nothing about the capability being demonstrated. The assertions are
that the region is strictly smaller than the source on both axes and no smaller than the content
block it must wholly contain, which is what "trimmed despite compression artifacts" actually claims.

**The scenarios that turn on the grant distinction run under an asymmetric policy**, built by the
file's own `AsymmetricPolicy` helper: one location granted read-only and a separate location granted
read-write. A policy granting read-write over a single root cannot distinguish a write decision from
a read decision, so only a scenario built on this helper can catch a unit that derived the
destination from the read decision. The remaining destination scenarios use the symmetric
`RootedPolicy` helper deliberately, because their answers do not depend on which grant admitted the
destination and granting the two separately would add a variable to a scenario that is not about
grants.

**A written file is read back and decoded, never merely counted.** "A PNG was written" is asserted
against the decoded pixels of the file on disk, so a scenario cannot pass on bytes that happen to
exist under the right name.

**The shared admission and destination helpers are exercised entirely through this unit's own
scenarios**, as they are through `ImageCropTool`'s: they publish nothing of their own, so their
correctness is exactly the correctness of the refusals and ceilings the scenarios below assert.
Substituting the read decision for the write decision inside the shared destination helper fails the
grant scenarios here *and* the corresponding scenarios of `ImageCropTool`, which is what proves the
sharing is real rather than coincidental.

#### Test Environment

The standard test runner, with no external service, no network access and no configuration beyond
the repository's own. Each scenario creates and disposes its own temporary directory tree, so no
scenario observes another's files and the suite is order-independent. One scenario provokes a
file-system refusal with a file name longer than a single path component may be — 255 characters on
NTFS, ext4 and APFS alike — which is the component limit rather than the total path length, so the
scenario does not depend on a host's long-path configuration. The test project multi-targets
`net8.0`, `net9.0` and `net10.0`, so every scenario below runs three times.

#### Acceptance Criteria

A unit test run passes when all scenarios below pass without error or exception beyond those
explicitly asserted.

Each of the following constitutes a failure: a trimmed region arriving as a `JsonElement`; a
background taken from an assumption rather than from the image's own border, which shows as the
whole picture being returned for a dark, colored or transparent margin; a background defeated by a
minority of content pixels on the border; an anti-aliased ring retained as a halo; a dithered
background classified as content; a JPEG source whose ringing defeats the trim; a padding that is
refused rather than clamped at an edge, or that produces a negative origin or a region extending
past the image; an omitted padding that applies none; a padding of zero read as omitted; an
entirely-background image answered with the whole picture rather than refused; a file written for an
entirely-background image; any pixel of a returned or written region differing from the source's; a
negative padding clamped rather than refused; a padding beyond the bound accepted, or refused
without naming the bound.

The destination scenarios add these failures: a destination accepted under a read-only grant; a file
left behind after a refused destination; an existing file replaced; a source consumed by its own
trimming; a non-PNG destination accepted; a capitalized extension refused; a confirmation that omits
the destination, the region or the source's dimensions; image content returned alongside a
confirmation; a directory materialized on the way to a destination whose parent did not exist; a
destination the file system refuses reported with the operating system's own wording or not refused
at all; and a destination refusal this unit composes that names a host path.

The admission scenarios add these failures: a file beyond the binary ceiling read before being
refused; an inline result beyond that ceiling returned rather than refused; an oversized declared
image decoded before being refused; a decode budget that accepts a palette-indexed image it would
refuse in truecolor form; a decode budget enforced against the published default rather than against
the ceiling the host configured; a well-formed file the decoder will not decode described as
damaged; a refusal that states dimensions that were never read, or omits dimensions that were; a
refusal carrying the decoding library's own wording; a non-trimmable type refused with a sibling
tool named where none is honest; an exception or framework error raised at a malformed or omitted
request; or a policy refusal that omits the permitted location.

This document lists **64** scenarios, each naming exactly one test method entry, for a total of
**64** test method entries. Two of those methods are parameterized — the non-PNG destination over
five destinations and the blank path over two — so a single-framework run executes **69** test
cases, and the project's three target frameworks bring one full run to **207** executions.

#### Test Scenarios

##### AgentKitTools-Image-AutoCropTool-ToolName: The Name Constant Is Family-Qualified

**Test**: `ImageAutoCropTool_ToolName_Constant_IsTheFamilyQualifiedName`

Asserts the constant is `image_auto_crop` and begins with the pack's declared family prefix.

##### AgentKitTools-Image-AutoCropTool-ToolName: The Constructed Tool Carries the Name and a Description

**Test**: `ImageAutoCropTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

Asserts the constructed tool's name is the published constant and its description is non-empty, and
that the description states both that the caller names no region and that the background comes from
the image's border — the two facts that distinguish this tool from its sibling and tell a model it
is usable on a non-white export.

##### AgentKitTools-Image-AutoCropTool-GuardedConstruction: A Null Policy Throws

**Test**: `ImageAutoCropTool_Create_NullPolicy_ThrowsArgumentNullException`

Asserts the factory raises `ArgumentNullException` rather than producing a tool governed by nothing.

##### AgentKitTools-Image-AutoCropTool-GuardedConstruction: The Result Is a Content List, Not a JsonElement

**Test**: `ImageAutoCropTool_AutoCrop_Result_IsContentListNotJsonElement`

Asserts the inline result is a `List<AIContent>` and is not a `JsonElement`. Without the guarded
factory's result passthrough the region would be serialized before a provider saw it, and the model
would describe content it never received.

##### AgentKitTools-Image-AutoCropTool-TrimsToContent: A Permitted PNG Yields Trimmed Image Content

**Test**: `ImageAutoCropTool_AutoCrop_PermittedPng_ReturnsTrimmedImageContent`

Asserts the result is a caption followed by PNG data content whose decoded size is smaller than the
source on both axes, with no region named by the caller.

##### AgentKitTools-Image-AutoCropTool-TrimsToContent: The Region Is the Content Box Plus the Padding

**Test**: `ImageAutoCropTool_AutoCrop_TrimmedRegion_IsTheContentBoxPlusPadding`

The arithmetic scenario. The content block sits at 12,9 and is 10x8, so the content box runs to
21,16; a padding of 8 expands it to 4,1 through 29,24, which is 26x24 with no edge clamped. Asserts
both the caption's stated region and the decoded size, so an off-by-one or a mis-sampled background
fails here.

##### AgentKitTools-Image-AutoCropTool-TrimsToContent: The Caption States the Region, the Padding and the Source Size

**Test**: `ImageAutoCropTool_AutoCrop_Caption_StatesTheRegionThePaddingAndTheSourceDimensions`

Asserts all three, with a padding distinguishable from the default. The model named no region, so
the caption is its only way to learn where in the picture it is now looking, whether the trim
achieved anything, and which input to change for a different answer.

##### AgentKitTools-Image-AutoCropTool-TrimsToContent: A Bare File Name Resolves Against the Workspace

**Test**: `ImageAutoCropTool_AutoCrop_BareFileName_ResolvesAgainstTheWorkspace`

Asserts a path given as a file name alone — the form a model writes — yields the region rather than
a refusal.

##### AgentKitTools-Image-AutoCropTool-SamplesBackgroundFromTheBorder: A Dark Background Is Trimmed From the Border

**Test**: `ImageAutoCropTool_AutoCrop_DarkBackground_IsTrimmedFromTheBorderNotFromWhite`

Near-white content on a near-black margin, trimmed with no padding so only the classification
decides the size. Asserts the decoded region is exactly the content block. A unit comparing against
white returns the whole image and fails.

##### AgentKitTools-Image-AutoCropTool-SamplesBackgroundFromTheBorder: A Colored Background Is Trimmed From the Border

**Test**: `ImageAutoCropTool_AutoCrop_ColoredBackground_IsTrimmedFromTheBorderNotFromWhite`

The same proof in the direction a dark background cannot reach: a violet margin is far from white
and far from black, so neither assumption survives it.

##### AgentKitTools-Image-AutoCropTool-SamplesBackgroundFromTheBorder: A Transparent Background Is Trimmed With Alpha Participating

**Test**: `ImageAutoCropTool_AutoCrop_TransparentBackground_IsTrimmedWithAlphaParticipating`

An opaque content block on a fully transparent margin whose color channels are zero — the shape a
transparent export commonly carries. Asserts the exact region and that the content's alpha survived
the round trip.

##### AgentKitTools-Image-AutoCropTool-SamplesBackgroundFromTheBorder: A Border Carrying Content Still Yields the Background

**Test**: `ImageAutoCropTool_AutoCrop_BorderWithAMinorityOfContentPixels_StillSamplesTheBackground`

Content occupying the top-left corner, so seventeen border pixels — including the corner itself —
are content. A corner sample would take the content as its background and classify the whole margin
as content; a mean would land between the two colors and match nothing. Asserts the content block,
which only a modal sample produces.

##### AgentKitTools-Image-AutoCropTool-ToleratesNearBackground: A Dithered Background Is Still Trimmed

**Test**: `ImageAutoCropTool_AutoCrop_DitheredBackground_IsStillTrimmedToTheContent`

A background dithered by up to three counts per channel across the whole image, border included.
Asserts the content block. A unit tolerating nothing returns the whole image — the tolerance's own
stated failure, reproduced.

##### AgentKitTools-Image-AutoCropTool-ToleratesNearBackground: An Anti-Aliased Edge Leaves No Halo

**Test**: `ImageAutoCropTool_AutoCrop_AntiAliasedContentEdge_DoesNotRetainTheHalo`

A one-pixel ring differing from the background by four counts — inside the tolerance, outside zero.
Asserts the 10x8 block rather than the 12x10 block a unit tolerating nothing produces. The two
answers are different sizes, so the assertion cannot be satisfied by both.

##### AgentKitTools-Image-AutoCropTool-ToleratesNearBackground: A JPEG Source Is Trimmed Despite Its Artifacts

**Test**: `ImageAutoCropTool_AutoCrop_JpegSource_IsTrimmedDespiteCompressionArtifacts`

A lossy round trip whose content block rings against a flat field. Asserts the robust pair: strictly
smaller than the source on both axes, and no smaller than the content block it must contain. Also
asserts the result's media type is PNG rather than the source's.

##### AgentKitTools-Image-AutoCropTool-PadsAndClampsAtEdges: Padding Clamps at the Left and Top Edges

**Test**: `ImageAutoCropTool_AutoCrop_ContentFlushToTheLeftAndTopEdges_ClampsThePadding`

Content in the top-left corner with a padding those margins cannot supply. Asserts the region starts
at 0,0 and is 18x16 — asymmetric padding, which is the honest answer and the one a unit that refused,
or that produced a negative origin, cannot give.

##### AgentKitTools-Image-AutoCropTool-PadsAndClampsAtEdges: Padding Clamps at the Right and Bottom Edges

**Test**: `ImageAutoCropTool_AutoCrop_ContentFlushToTheRightAndBottomEdges_ClampsThePadding`

The mirror, on the axes the first scenario cannot reach. A unit clamping only at zero passes the
first and fails this one by asking the decoder for a region extending past the image.

##### AgentKitTools-Image-AutoCropTool-PadsAndClampsAtEdges: A Padding Larger Than Every Margin Yields the Whole Image

**Test**: `ImageAutoCropTool_AutoCrop_PaddingLargerThanEveryMargin_ReturnsTheWholeImage`

Asserts the whole image, clamped on every side, for the largest padding the tool accepts — which
also pins that the stated bound is inclusive rather than refused.

##### AgentKitTools-Image-AutoCropTool-PadsAndClampsAtEdges: A Padding of Zero Yields the Tight Content Box

**Test**: `ImageAutoCropTool_AutoCrop_ZeroPadding_ReturnsTheTightContentBox`

Asserts the content box with nothing added, distinguishing zero from the default.

##### AgentKitTools-Image-AutoCropTool-PadsAndClampsAtEdges: An Omitted Padding Applies the Default

**Test**: `ImageAutoCropTool_AutoCrop_OmittedPadding_AppliesTheDefault`

Asserts the region and the caption's stated padding for an invocation carrying no padding argument
at all. The fixture's margins exceed the default, so the default is unclamped and exactly
assertable, and "omitted" is proven different from both "zero" and "the whole image".

##### AgentKitTools-Image-AutoCropTool-RefusesAnEntirelyBackgroundImage: A Uniform Image Is Refused With the Reason Named

**Test**: `ImageAutoCropTool_AutoCrop_UniformImage_IsRefusedNamingTheReason`

An image whose every pixel is the same non-white color. Asserts an `InvalidRequest` refusal naming
the image's dimensions and stating that there is no content region to trim to.

##### AgentKitTools-Image-AutoCropTool-RefusesAnEntirelyBackgroundImage: A Uniform Image With a Destination Writes Nothing

**Test**: `ImageAutoCropTool_AutoCrop_UniformImageWithADestination_WritesNothing`

The destination named here would have been permitted and has already passed every governance check,
so this is what proves the write is genuinely the last step rather than one that happens to be
skipped. Asserts the refusal and the absence of the file.

##### AgentKitTools-Image-AutoCropTool-PixelsPreservedExactly: The Returned Region Carries the Source's Pixels

**Test**: `ImageAutoCropTool_AutoCrop_ReturnedRegion_CarriesTheSourcePixelsExactly`

A content block whose every pixel is a distinct function of its absolute coordinate, on a
transparent margin. Compares every channel of every pixel of the returned region against the pixel
it came from. An encoder that discarded alpha, or an origin off by one, fails here.

##### AgentKitTools-Image-AutoCropTool-RefusesMalformedPadding: A Negative Padding Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_NegativePadding_ReturnsDenial`

Asserts an `InvalidRequest` refusal stating what the padding means. A negative padding would shrink
the content box and lose content, so it is refused rather than clamped to zero.

##### AgentKitTools-Image-AutoCropTool-RefusesMalformedPadding: A Padding Beyond the Bound Is Refused Naming the Bound

**Test**: `ImageAutoCropTool_AutoCrop_PaddingBeyondTheBound_ReturnsDenialNamingTheBound`

One pixel past the bound. Asserts the refusal names the bound, which is what turns it into the next
correct request.

##### AgentKitTools-Image-AutoCropTool-WritesRegionToDestination: A Permitted Destination Receives the PNG

**Test**: `ImageAutoCropTool_AutoCrop_PermittedDestination_WritesThePngAndConfirmsInText`

Asserts a text confirmation, the file's existence, and the decoded size of what was written.

##### AgentKitTools-Image-AutoCropTool-PixelsPreservedExactly: The Written File Carries the Source's Pixels

**Test**: `ImageAutoCropTool_AutoCrop_WrittenFile_CarriesTheSourcePixelsExactly`

The written counterpart of the inline fidelity scenario. A figure that is not the region it claims
to be is a silent wrong answer, and one that would survive every size assertion.

##### AgentKitTools-Image-AutoCropTool-WritesRegionToDestination: A Written Region Is Not Also Returned

**Test**: `ImageAutoCropTool_AutoCrop_WithDestination_ReturnsNoImageContent`

Asserts the result is a string and is not a content list, so nothing image-shaped can be hiding in
it. This is what keeps a document-preparation loop affordable.

##### AgentKitTools-Image-AutoCropTool-WritesRegionToDestination: The Confirmation Names Three Facts

**Test**: `ImageAutoCropTool_AutoCrop_Confirmation_NamesTheDestinationRegionAndSourceDimensions`

Asserts all three, and that the workspace root appears nowhere — a destination inside the anchor
comes back relative, which is what makes the path dialect a real choice rather than a coincidence.

##### AgentKitTools-Image-AutoCropTool-WritesRegionToDestination: A Destination Outside the Anchor Is Confirmed Absolutely

**Test**: `ImageAutoCropTool_AutoCrop_DestinationOutsideTheWorkingDirectory_IsConfirmedAbsolutely`

The configuration a real application runs in: a workspace to read and a separate session folder to
write into. A relative name would name a location the model cannot reach from the anchor.

##### AgentKitTools-Image-AutoCropTool-WritesRegionToDestination: A JPEG Source Written to a Destination Yields PNG Bytes

**Test**: `ImageAutoCropTool_AutoCrop_JpegSourceToDestination_WritesPngBytes`

The written file is decoded as a PNG, so a file carrying JPEG bytes under a `.png` name fails here.

##### AgentKitTools-Image-AutoCropTool-WritesRegionToDestination: An Omitted Destination Still Returns the Region Inline

**Test**: `ImageAutoCropTool_AutoCrop_OmittedDestination_StillReturnsTheRegionInline`

Asserts the inline content and that nothing was created beside the source, so the inline outcome is
proven to have no side effect — the mode that keeps this tool fully useful under a policy permitting
no writing anywhere.

##### AgentKitTools-Image-AutoCropTool-DestinationRequiresWriteGrant: A Destination Under a Read-Only Grant Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_DestinationUnderAReadOnlyGrant_IsRefusedDisclosingTheWritableLocation`

Runs under the asymmetric policy. The source is admitted by the read decision and the destination
refused by the write decision in the same call. Asserts the policy's refusal names the writable
location with its access level. Deriving the write from the read would make this call succeed.

##### AgentKitTools-Image-AutoCropTool-DestinationRequiresWriteGrant: A Destination Outside Every Grant Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_DestinationOutsideEveryGrant_IsRefused`

The same refusal stated the other way: no grant covers the location at all.

##### AgentKitTools-Image-AutoCropTool-DestinationRequiresWriteGrant: A Refused Destination Leaves No File

**Test**: `ImageAutoCropTool_AutoCrop_RefusedDestination_WritesNothing`

What makes the grant a boundary rather than advice. A refusal that nonetheless wrote the file would
be the worst of both answers.

##### AgentKitTools-Image-AutoCropTool-RefusesToReplaceDestination: An Existing Destination Is Refused and Unchanged

**Test**: `ImageAutoCropTool_AutoCrop_ExistingDestination_IsRefusedWithoutReplacingIt`

Asserts the refusal, that it says the tool writes a new file, and that the prior file's bytes are
exactly as they were.

##### AgentKitTools-Image-AutoCropTool-RefusesToReplaceDestination: A Destination Equal to the Source Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_DestinationEqualToTheSource_IsRefusedWithoutReplacingIt`

The guarantee that an image can never be consumed by its own trimming. Asserts the refusal and the
source's own bytes.

##### AgentKitTools-Image-AutoCropTool-RequiresPngDestination: A Non-PNG Destination Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_NonPngDestination_IsRefusedNamingTheExpectedExtension`

Parameterized over a `.jpg` name, a `.gif` name, an extensionless name, an empty string and
whitespace. The last two land here by design: a destination the model did not really intend is never
silently read as "no destination". Asserts the refusal names the extension to use instead.

##### AgentKitTools-Image-AutoCropTool-RequiresPngDestination: A Capitalized Extension Is Accepted

**Test**: `ImageAutoCropTool_AutoCrop_UppercaseDestinationExtension_IsAccepted`

Asserts the confirmation and the decoded size of the written file, so acceptance is proven by the
file rather than by the absence of a refusal.

##### AgentKitTools-Image-AutoCropTool-RefusesUnusableDestination: A Destination That Is a Directory Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_DestinationIsADirectory_IsRefused`

The directory is named `figure.png`, because the extension rule is judged first and a directory with
any other name would not reach this check.

##### AgentKitTools-Image-AutoCropTool-RefusesUnusableDestination: A Missing Parent Is Refused, Not Created

**Test**: `ImageAutoCropTool_AutoCrop_DestinationParentMissing_IsRefused`

Asserts the refusal and that no directory was materialized on the way.

##### AgentKitTools-Image-AutoCropTool-RefusesUnusableDestination: An Unwritable Destination Is Refused as a Plain Fact

**Test**: `ImageAutoCropTool_AutoCrop_UnwritableDestination_IsRefusedWithoutDisclosingTheFailure`

Provoked by a file name exceeding the single-component limit, so the failure comes from the host
rather than from anything the unit checked. Asserts the unit's own wording, that no host path is
disclosed, and that nothing was left behind.

##### AgentKitTools-Image-AutoCropTool-DenialDisclosure: No Destination Refusal Names a Host Path

**Test**: `ImageAutoCropTool_AutoCrop_DestinationDenials_NameNoHostPath`

Provokes each destination refusal the unit composes in turn and asserts that none of them carries
the workspace, an absolute path or any directory separator.

##### AgentKitTools-Image-AutoCropTool-ObservesBinaryCeiling: A File Beyond the Ceiling Is Refused Before Parsing

**Test**: `ImageAutoCropTool_AutoCrop_FileLargerThanTheBinaryCeiling_ReturnsDenialNamingTheCeiling`

The fixture is a real image, so a unit that parsed before checking would succeed rather than fail.
The refusal is asserted by its whole text and the result's own refusal by its absence, because both
name the same ceiling.

##### AgentKitTools-Image-AutoCropTool-ObservesBinaryCeiling: An Encoded Region Beyond the Ceiling Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_EncodedRegionBeyondTheBinaryCeiling_ReturnsDenialNamingTheCeiling`

A ceiling chosen to admit the source file and refuse its lossless re-encoding, so only the second
check can be what fires.

##### AgentKitTools-Image-AutoCropTool-RefusesOversizedDecode: A Header Beyond the Pixel Budget Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_HeaderDeclaringMorePixelsThanTheDecodeBudget_IsRefusedNamingTheBudget`

Both axes inside the per-axis bound, only the product outside the budget. The fixture carries no
pixel data, so a unit that decoded before triaging produces the undecodable refusal instead — which
is how "decided from the declared dimensions alone" is asserted rather than assumed. Asserts both
bounds are named and the undecodable wording is absent.

##### AgentKitTools-Image-AutoCropTool-RefusesOversizedDecode: An Oversized Axis Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_HeaderDeclaringAnOversizedAxis_IsRefusedNamingTheBounds`

Ninety thousand pixels in total, far inside the budget, with one axis beyond the decoder's per-axis
bound. Proves both bounds are checked rather than only the product.

##### AgentKitTools-Image-AutoCropTool-RefusesOversizedDecode: A Palette-Indexed Declaration Is Refused Alike

**Test**: `ImageAutoCropTool_AutoCrop_PalettizedPngNearThePixelCeiling_IsRefusedNamingTheBudget`

The regression guard for the most likely wrong simplification: a budget computed from the declared
channel count under-counts a palette-indexed file fourfold. The fixture differs from the truecolor
one only in its declared color type, so nothing but the estimate can change the answer.

##### AgentKitTools-Image-AutoCropTool-RefusesOversizedDecode: A Host-Lowered Ceiling Is the One Enforced

**Test**: `ImageAutoCropTool_AutoCrop_HeaderExceedingAHostLoweredPixelCeiling_IsRefusedNamingThatCeiling`

A declaration far above the configured ceiling and far below the published default, so the refusal
can only come from the configured value. Asserts the configured number is named and the default is
not.

##### AgentKitTools-Image-AutoCropTool-RefusesUndecodableContent: A Lying Extension Is Refused Without Dimensions

**Test**: `ImageAutoCropTool_AutoCrop_FileWhoseExtensionLiesAboutItsContent_ReturnsDenialWithoutDimensions`

Nothing was read, so nothing is claimed. Asserts no size is stated and no host path appears.

##### AgentKitTools-Image-AutoCropTool-RefusesUndecodableContent: A Truncated Body Is Refused Naming the Declared Size

**Test**: `ImageAutoCropTool_AutoCrop_TruncatedPngBody_ReturnsDenialNamingTheDeclaredDimensions`

The size was learned, so it is stated, which tells the model the file is damaged rather than of the
wrong kind.

##### AgentKitTools-Image-AutoCropTool-RefusesUndecodableContent: An Interlaced PNG Is Refused Naming the Declared Size

**Test**: `ImageAutoCropTool_AutoCrop_Adam7InterlacedPng_ReturnsDenialNamingTheDeclaredDimensions`

The one case where a file is entirely sound and still has to be refused: the decoder reports from
the header that it will not decode it.

##### AgentKitTools-Image-AutoCropTool-RefusesUndecodableContent: An Empty File Is Refused Without Throwing

**Test**: `ImageAutoCropTool_AutoCrop_ZeroByteFile_ReturnsDenialWithoutThrowing`

Asserts a returned refusal, because an exception would end the agent's turn.

##### AgentKitTools-Image-AutoCropTool-RefusesNonCroppableType: A GIF Is Refused Naming the Trimmable Types

**Test**: `ImageAutoCropTool_AutoCrop_GifFile_ReturnsDenialNamingTheCroppableTypes`

Asserts the types that can be trimmed are named and that no sibling tool is: a GIF genuinely is an
image, so naming the read tool would offer a route to the whole picture after the model asked for
one part of it.

##### AgentKitTools-Image-AutoCropTool-RefusesNonCroppableType: A WebP File Is Refused Naming the Trimmable Types

**Test**: `ImageAutoCropTool_AutoCrop_WebpFile_ReturnsDenialNamingTheCroppableTypes`

A type the family reads but does not decode.

##### AgentKitTools-Image-AutoCropTool-RefusesNonCroppableType: A PDF Is Refused Stating Rasterization Is Required

**Test**: `ImageAutoCropTool_AutoCrop_PdfFile_ReturnsDenialStatingRasterizationIsRequired`

Asserts the refusal states what would have to happen first.

##### AgentKitTools-Image-AutoCropTool-RefusesNonCroppableType: An SVG Is Refused With a Redirect to the Text Reader

**Test**: `ImageAutoCropTool_AutoCrop_SvgFile_ReturnsDenialRedirectingToTextFileRead`

An SVG genuinely is text, so naming the text reader classifies the file rather than offering a way
around the refusal.

##### AgentKitTools-Image-AutoCropTool-RefusesNonCroppableType: An Unknown Extension Is Refused With No Redirect

**Test**: `ImageAutoCropTool_AutoCrop_UnknownExtension_ReturnsDenialWithoutRedirect`

Asserts neither sibling reader is named, because neither would help.

##### AgentKitTools-Image-AutoCropTool-DenyOutsideRoot: A Path Outside the Read Root Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_PathOutsideTheReadRoot_ReturnsDenial`

The primary threat the library exists to control. The decision is made before anything is learned
about the file, so a refused path never reveals whether it exists.

##### AgentKitTools-Image-AutoCropTool-DenialDisclosure: A Policy Refusal Discloses the Permitted Location

**Test**: `ImageAutoCropTool_AutoCrop_DeniedPath_DenialDisclosesPermittedLocation`

The one refusal that deliberately names host paths, so a confined model learns where it may look
instead of guessing.

##### AgentKitTools-Image-AutoCropTool-DirectoryRefused: A Directory Is Refused

**Test**: `ImageAutoCropTool_AutoCrop_DirectoryPath_ReturnsDenial`

Asserts the fact is stated and no remedy is prescribed.

##### AgentKitTools-Image-AutoCropTool-MissingFileDenied: A Missing File Is Refused as Not Found

**Test**: `ImageAutoCropTool_AutoCrop_MissingFile_ReturnsDenial`

A trimmable extension naming no file is refused as not found rather than as an untrimmable type,
which pins the order in which the type and the existence are judged.

##### AgentKitTools-Image-AutoCropTool-MalformedRequestDenied: An Omitted Path Is Refused Without Throwing

**Test**: `ImageAutoCropTool_AutoCrop_MissingPathArgument_ReturnsDenialWithoutThrowing`

Invoked with no arguments at all. A parameter with no default would fail inside the function factory
before the unit was reached, leaving the model an opaque framework error rather than a refusal
naming what to supply.

##### AgentKitTools-Image-AutoCropTool-MalformedRequestDenied: A Blank Path Is Refused Without Throwing

**Test**: `ImageAutoCropTool_AutoCrop_BlankPath_ReturnsDenialWithoutThrowing`

Parameterized over an empty string and whitespace, under an unrestricted policy so only the request
is at fault.

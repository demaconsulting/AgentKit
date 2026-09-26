### ImageCropTool Unit Verification Design

This document describes the unit-level verification strategy for the `ImageCropTool` class.

#### Verification Approach

Nothing is mocked or stubbed. The unit's whole purpose is to behave correctly against a real access
policy, a real file system and real image content, and a substituted decoder would verify only the
substitute. Each scenario constructs a real `PathPolicy` over a temporary directory tree it
creates, writes an image into it, builds the tool through its own internal factory, and invokes it
the way a runtime does — through `InvokeAsync` with a named argument dictionary — so that the
delivery of the image result through the guarded factory is exercised rather than bypassed.

**Every image a scenario uses is built by the test.** No binary fixture is committed, so each
image's exact shape — its dimensions, its color type, its declared size, and whether its pixel
data is even present — is stated in code beside the scenario that depends on it. The well-formed
images are produced through the same decoder the unit itself uses, so a scenario asserting on
pixels is asserting against content the library can genuinely produce; the hostile and variant
images are hand-built byte arrays, because no encoder will produce a header that declares eight
thousand pixels on a side and then supplies no pixel data — and that file is precisely what proves
a decode budget is judged from the header rather than after the fact.

**The hostile fixtures are genuinely well formed as far as they go.** A synthetic header carries a
correct checksum, because a reader validates it and would otherwise reject the file as corrupt
rather than as oversized — in which case the oversized scenarios would pass for entirely the wrong
reason. The oversized scenarios therefore additionally assert the refusal names the **pixel
budget** rather than the generic undecodable message, so a mis-built fixture fails the test instead
of passing it.

Unit tests reside in `Image/ImageCropToolTests.cs`, reusing the shared temporary-directory fixture
from `TextFile/TempDirectoryFixture.cs` and the shared fixture builder from
`Image/ImageTestImages.cs`, within the `DemaConsulting.AgentKit.Tools.Tests` project.

**The scenarios that turn on the grant distinction run under an asymmetric policy**, built by the
file's own `AsymmetricPolicy` helper: one location granted read-only and a separate location
granted read-write. A policy granting read-write over a single root cannot distinguish a write
decision from a read decision, so only a scenario built on this helper can catch a unit that
derived the destination from the read decision. Two scenarios exist for exactly that — a
destination under a read-only grant refused while the writable location is disclosed, and a refused
destination that leaves no file behind — and substituting the read decision for the write decision
in the unit fails precisely those two here, together with the subsystem's own read-only-grant
scenario, and nothing else. A third scenario uses the helper for a different reason: a destination
outside the working directory must still be permitted before it can be confirmed by its absolute
path, and that needs a second granted location rather than a second access level. The asymmetric
shape is also the one a real application configures: a workspace to read and a separate session
folder to produce into.

**The remaining destination scenarios use the symmetric `RootedPolicy` helper deliberately.** They
verify what a permitted destination produces — the written file and its decoded size, the pixel
fidelity of what was written, the PNG bytes a JPEG source yields, the confirmation's three facts,
the absence of image content, the acceptance of a capitalized extension, and the inline outcome an
omitted destination still returns — and the refusals the destination itself provokes: a non-PNG
name, an occupied name, a name equal to the source, a directory, a missing parent, a name the file
system will not accept, and the rule that none of those texts names a host path. None of those
answers depends on which grant admitted the destination, so granting the two separately would add a
variable to a scenario that is not about grants. The one destination refusal the policy itself
composes is stated both ways: outside every grant under `RootedPolicy`, and inside a read-only
grant under `AsymmetricPolicy`.

**A written file is read back and decoded, never merely counted.** "A PNG was written" is asserted
against the decoded pixels of the file on disk, so a scenario cannot pass on bytes that happen to
exist; the pixel-fidelity scenario compares every channel of every pixel, alpha included, against
the source the region was taken from.

**The omitted-destination scenarios are textually unchanged.** Every scenario written before the
parameter existed calls the file's `InvokeAsync` helper without a destination, and the helper adds
the argument to the invocation only when one was supplied — so an omitted destination reaches the
unit as a genuinely absent argument, and the unchanged scenarios are themselves the demonstration
that the inline outcome is exactly the behavior that shipped before.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: Each scenario creates and deletes its own temporary tree containing a permitted
  root and a sibling directory outside it, writing image files directly
- **Isolation**: Each test constructs its own policy, tool and tree; no state is shared

#### Acceptance Criteria

A unit test run passes when all scenarios below pass without error or exception beyond those
explicitly asserted. A region arriving as a `JsonElement`, any pixel of a returned region differing
from the source's, an out-of-bounds region answered with content rather than a refusal, a refusal
that omits the image's real dimensions where they were read, a refusal that states dimensions that
were never read, an oversized declared image that is decoded before being refused, a decode budget
that accepts a palette-indexed image it would refuse in truecolor form, a well-formed file the
decoder will not decode described as damaged, a refusal carrying the decoding library's own
wording, a non-croppable
type refused with a sibling tool named, an exception or framework error raised at a malformed or
omitted request, or a policy refusal that omits the request, permitted location or access level
each constitute a failure.

The destination scenarios add these failures: a destination accepted under a read-only grant; a
file left behind after a refused destination; an existing file replaced; a non-PNG destination
accepted; a written region whose pixels differ from the source's; a confirmation that omits the
destination, the region or the source's dimensions; image content returned alongside a
confirmation; a directory materialized on the way to a destination whose parent did not exist; a
destination the file system refuses reported with the operating system's own wording or not refused
at all; a destination refusal this unit composes that names a host path; and an inline crop altered
in any observable way by the parameter's existence.

#### Test Scenarios

##### AgentKitTools-Image-CropTool-ToolName: The Name Constant Is Family-Qualified

**Test**: `ImageCropTool_ToolName_Constant_IsTheFamilyQualifiedName`

Asserts the constant is `image_crop` and begins with the pack's declared family prefix.

##### AgentKitTools-Image-CropTool-ToolName: The Constructed Tool Carries the Name and a Description

**Test**: `ImageCropTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

Normal operation: the tool a model is offered carries the published name and a non-empty
description. Additionally asserts the description names the read tool, because the description is
what tells the model where the coordinate space comes from — and that naming is what makes the two
tools compose.

##### AgentKitTools-Image-CropTool-GuardedConstruction: A Null Policy Throws

**Test**: `ImageCropTool_Create_NullPolicy_ThrowsArgumentNullException`

Error path at construction: a tool governed by no policy cannot be built. An exception rather than
a denial, because it is a programming error in the composing application.

##### AgentKitTools-Image-CropTool-GuardedConstruction: The Result Is a Content List, Not a JsonElement

**Test**: `ImageCropTool_Crop_Result_IsContentListNotJsonElement`

Asserts both the positive and the negative type. Without the guard the region would arrive
serialized, the provider would never receive it, and nothing would report an error.

##### AgentKitTools-Image-CropTool-CropPermittedRegion: A Permitted PNG Yields Its Region

**Test**: `ImageCropTool_Crop_PermittedPng_ReturnsCroppedImageContent`

Normal operation: asserts a caption followed by data content carrying `image/png`, and that the
returned image's decoded dimensions are exactly the region requested.

##### AgentKitTools-Image-CropTool-CropPermittedRegion: A Permitted JPEG Yields PNG Content

**Test**: `ImageCropTool_Crop_PermittedJpeg_ReturnsPngContent`

Normal operation on a second source format, and the proof that the family's output format is its
own rather than the source's.

##### AgentKitTools-Image-CropTool-CropPermittedRegion: A Palette-Indexed PNG Yields Its Region

**Test**: `ImageCropTool_Crop_PalettizedPng_ReturnsCroppedImageContent`

Variant input: a file storing one index per pixel is cropped successfully, and the returned
region's first pixel is asserted to be the palette's color rather than a raw index — so a decoder
regression in palette resolution is a test failure rather than a field report.

##### AgentKitTools-Image-CropTool-CropPermittedRegion: A Bare File Name Resolves Against the Workspace

**Test**: `ImageCropTool_Crop_BareFileName_ResolvesAgainstTheWorkspace`

Normal operation for the request a model actually makes: a name stated with no location at all.

##### AgentKitTools-Image-CropTool-CropPermittedRegion: The Whole Image Is an Acceptable Region

**Test**: `ImageCropTool_Crop_WholeImageRegion_IsAccepted`

Boundary condition: a region exactly equal to the image is accepted, pinning the bound as
inclusive so no caller has to guess.

##### AgentKitTools-Image-CropTool-CropPermittedRegion: A Single Pixel at the Far Corner Is Acceptable

**Test**: `ImageCropTool_Crop_SinglePixelRegion_IsAccepted`

Boundary condition at the other end of the same bound: the minimum valid extent, at the last
addressable pixel.

##### AgentKitTools-Image-CropTool-PixelsPreservedExactly: The Region Carries the Source Pixels

**Test**: `ImageCropTool_Crop_ReturnedRegion_CarriesTheSourcePixelsExactly`

The demonstrable form of "the pixels were preserved exactly". The source is built with a distinct
value in every channel of every pixel, including alpha; the region is taken from an origin that is
not the image's own; and every pixel of the result is compared against the pixel it was supposed to
come from. An encoder that quietly discarded alpha, or an offset that was off by one, fails here.

##### AgentKitTools-Image-CropTool-RefusesOutOfBounds: A Region Past the Right Edge Is Refused

**Test**: `ImageCropTool_Crop_RegionExtendingPastTheRightEdge_IsRefusedNamingTheDimensions`

Error path: a region one pixel too wide is refused, and the refusal is asserted to contain the
image's real dimensions — which is what lets the model correct itself rather than guess again.

##### AgentKitTools-Image-CropTool-RefusesOutOfBounds: A Region Past the Bottom Edge Is Refused

**Test**: `ImageCropTool_Crop_RegionExtendingPastTheBottomEdge_IsRefusedNamingTheDimensions`

Asserts the second axis is checked too, not only the first.

##### AgentKitTools-Image-CropTool-RefusesOutOfBounds: An Origin at the Image Width Is Refused

**Test**: `ImageCropTool_Crop_OriginAtTheImageWidth_IsRefusedNamingTheDimensions`

The off-by-one, and the case in which the decoder's own argument-level complaint would name the
wrong parameter. Validating the region in the unit rather than re-interpreting the decoder's
exception is what lets the refusal describe the mistake the model actually made.

##### AgentKitTools-Image-CropTool-RefusesOutOfBounds: An Origin at the Image Height Is Refused

**Test**: `ImageCropTool_Crop_OriginAtTheImageHeight_IsRefusedNamingTheDimensions`

The same boundary on the other axis.

##### AgentKitTools-Image-CropTool-RefusesOutOfBounds: A Region Wholly Outside the Image Is Refused

**Test**: `ImageCropTool_Crop_RegionEntirelyOutsideTheImage_IsRefusedNamingTheDimensions`

Error path for a region sharing no pixel with the image, which is still told where the image
actually is.

##### AgentKitTools-Image-CropTool-RefusesOutOfBounds: An Out-of-Bounds Region Is Not Clamped

**Test**: `ImageCropTool_Crop_OutOfBoundsRegion_IsNotClamped`

The explicit anti-clamping proof, asserted as a type rather than as a message: the result is a
refusal and specifically **not** content. Clamping would answer a different question while
reporting success, and the model could not detect the substitution.

##### AgentKitTools-Image-CropTool-RefusesMalformedRegion: A Negative Origin Is Refused

**Test**: `ImageCropTool_Crop_NegativeOrigin_ReturnsDenial`

Run as a theory over each axis. Asserts the refusal states that the origin is measured from the
top-left corner — the one thing about the coordinate space a model cannot observe from the picture.

##### AgentKitTools-Image-CropTool-RefusesMalformedRegion: A Non-Positive Extent Is Refused

**Test**: `ImageCropTool_Crop_NonPositiveExtent_ReturnsDenial`

Run as a theory over a zero width, a zero height and a negative width. Asserts the malformed-request
reason rather than the too-large one, because an empty region is a contradiction in the request and
not a resource problem.

##### AgentKitTools-Image-CropTool-MalformedRequestDenied: An Omitted Region Is Refused

**Test**: `ImageCropTool_Crop_MissingRegionArguments_ReturnsDenialWithoutThrowing`

The scenario that pins every region parameter as optional. A parameter with no default fails inside
the function factory before the tool body is reached, and the model then receives an opaque
framework error rather than a refusal naming the four values to supply.

##### AgentKitTools-Image-CropTool-MalformedRequestDenied: An Omitted Path Is Refused

**Test**: `ImageCropTool_Crop_MissingPathArgument_ReturnsDenialWithoutThrowing`

The same property for the path parameter.

##### AgentKitTools-Image-CropTool-MalformedRequestDenied: A Blank Path Is Refused

**Test**: `ImageCropTool_Crop_BlankPath_ReturnsDenialWithoutThrowing`

Run as a theory over an empty path and a whitespace-only one, under an unrestricted policy so only
the request itself can be at fault.

##### AgentKitTools-Image-CropTool-RefusesOversizedDecode: A Header Declaring Too Many Pixels Is Refused

**Test**: `ImageCropTool_Crop_HeaderDeclaringMorePixelsThanTheDecodeBudget_IsRefusedNamingTheBudget`

**The case a per-axis bound alone cannot catch.** The fixture declares 8000 by 8000 — both axes
inside the largest extent the decoder accepts, their product far outside the host's budget. The
fixture carries **no pixel data at all**, so a tool that decoded before triaging would produce the
undecodable refusal instead; the scenario asserts the oversized refusal and explicitly asserts the
undecodable wording is absent, which is how "decided from the declared dimensions alone" is
observed rather than assumed. Both bounds are asserted to appear.

##### AgentKitTools-Image-CropTool-RefusesOversizedDecode: A Header Declaring an Oversized Axis Is Refused

**Test**: `ImageCropTool_Crop_HeaderDeclaringAnOversizedAxis_IsRefusedNamingTheBounds`

The mirror of the scenario above: 9000 by 10 is ninety thousand pixels, far inside the budget, but
one axis exceeds what the decoder will allocate. Proves both bounds are checked rather than only
the product.

##### AgentKitTools-Image-CropTool-RefusesOversizedDecode: A Palette-Indexed Header Near the Ceiling Is Refused

**Test**: `ImageCropTool_Crop_PalettizedPngNearThePixelCeiling_IsRefusedNamingTheBudget`

**The regression guard for the single most likely wrong simplification in this unit.** A header
reader reports a palette-indexed file as carrying one channel per pixel, because that is what the
file stores — while decoding it produces four bytes per pixel, because every index is resolved into
RGBA. A budget computed as width × height × channels would therefore under-count this file by a
factor of four and **accept** it. Palette-indexed content also compresses best, so it is exactly
what a hostile caller would reach for. The fixture differs from the truecolor one above only in its
declared color type, so nothing but the estimate can be what changes the answer.

##### AgentKitTools-Image-CropTool-RefusesUndecodableContent: A Truncated Body Is Refused With Its Size

**Test**: `ImageCropTool_Crop_TruncatedPngBody_ReturnsDenialNamingTheDeclaredDimensions`

A file whose header reads cleanly and whose pixel data stops part-way. Asserts the refusal states
the declared size, which is what tells the model the file is damaged rather than of the wrong kind.

##### AgentKitTools-Image-CropTool-RefusesUndecodableContent: An Extension That Lies Is Refused Without a Size

**Test**: `ImageCropTool_Crop_FileWhoseExtensionLiesAboutItsContent_ReturnsDenialWithoutDimensions`

The counterpart: JPEG bytes in a file named `.png`. Asserts by pattern that **no** dimensions appear
— nothing was read, so nothing is claimed — and asserts that no text from the decoding library
reaches the model, since its messages are developer-facing and may echo values read out of the file.

##### AgentKitTools-Image-CropTool-RefusesUndecodableContent: A Well-Formed File the Decoder Declines Is Refused

**Test**: `ImageCropTool_Crop_Adam7InterlacedPng_IsRefusedNamingTheDimensions`

Adam7 interlacing is today the one thing a well-formed, specification-conforming file can declare
that this unit will not decode, so the file is **not damaged** and must not be described as though
it were. Asserts the refusal states the file is well formed but uses a feature the tool does not
decode, states the declared size, and is specifically **not** the undecodable refusal — the
distinction is drawn from the header reader's feasibility report rather than from an exception, so
the unit needed no knowledge of the PNG format to draw it. Asserts it names **no sibling tool**:
handing over the size directly serves the model better than sending it to another tool for the
same fact, and naming a tool here would be a route to the content the refusal withheld rather than
a statement of what the file is.

##### AgentKitTools-Image-CropTool-RefusesUndecodableContent: An Empty File Is Refused

**Test**: `ImageCropTool_Crop_ZeroByteFile_ReturnsDenialWithoutThrowing`

Boundary condition: a file with no content at all produces a returned refusal rather than an
exception from the decoder.

##### AgentKitTools-Image-CropTool-DenyOutsideRoot: A Path Outside the Read Grant Is Refused

**Test**: `ImageCropTool_Crop_PathOutsideTheReadRoot_ReturnsDenial`

Security control: a file in a sibling directory no read-capable grant permits is refused as
`PathNotPermitted`, before anything is learned about the file.

##### AgentKitTools-Image-CropTool-DenialDisclosure: A Refusal Discloses the Permitted Location

**Test**: `ImageCropTool_Crop_DeniedPath_DenialDisclosesPermittedLocation`

Disclosure behavior: the refused request is echoed and the permitted location is named with its
access level, so a confined model learns where it may look instead.

##### AgentKitTools-Image-CropTool-DirectoryRefused: A Directory Is Refused

**Test**: `ImageCropTool_Crop_DirectoryPath_ReturnsDenial`

Error path: a directory has no image content to take a region of, so it is refused with a plain
statement of that fact.

##### AgentKitTools-Image-CropTool-MissingFileDenied: A Missing File Is Refused

**Test**: `ImageCropTool_Crop_MissingFile_ReturnsDenial`

Error path: a croppable-type file that does not exist is refused as `TargetNotFound`, proving the
type is judged before existence.

##### AgentKitTools-Image-CropTool-ObservesBinaryCeiling: An Oversized Source File Is Refused

**Test**: `ImageCropTool_Crop_FileLargerThanTheBinaryCeiling_ReturnsDenialNamingTheCeiling`

Boundary condition: the fixture is a real, decodable image, so a tool that parsed before checking
the ceiling would succeed rather than fail. Asserting the ceiling's refusal is therefore evidence
the size was judged first.

##### AgentKitTools-Image-CropTool-ObservesBinaryCeiling: An Oversized Returned Region Is Refused

**Test**: `ImageCropTool_Crop_EncodedRegionBeyondTheBinaryCeiling_ReturnsDenialNamingTheCeiling`

A real case rather than a theoretical one: a region of a compressed source, returned without
altering a pixel, can exceed a ceiling the source file sat well inside. The ceiling is chosen to
admit the source and refuse the result, so only the second check can be what fires.

##### AgentKitTools-Image-CropTool-RefusesNonCroppableType: A gif File Is Refused

**Test**: `ImageCropTool_Crop_GifFile_ReturnsDenialNamingTheCroppableTypes`

Error path: the refusal states that the format may hold more than one frame and that this tool
cannot tell how many, and names the types a region can be taken from, so the model is told what it
may ask for.

##### AgentKitTools-Image-CropTool-RefusesNonCroppableType: The gif Refusal Names No Sibling Tool

**Test**: `ImageCropTool_Crop_GifDenial_NamesNoSiblingTool`

The explicit no-redirect assertion, stated as its own scenario because it is a rule about the
refusal rather than about the format. A `.gif` genuinely is an image, so naming the read tool would
not classify it — it would offer a route to the content the refusal withheld, handing the model a
whole image after it asked to examine one part closely.

##### AgentKitTools-Image-CropTool-RefusesNonCroppableType: A WebP File Is Refused in Its Own Words

**Test**: `ImageCropTool_Crop_WebpFile_ReturnsDenialNamingTheCroppableTypes`

Its own reason rather than the `.gif`'s: WebP is a format the family hands to a provider
without decoding, not one whose frame count it cannot establish.

##### AgentKitTools-Image-CropTool-RefusesNonCroppableType: A PDF Names Rasterization

**Test**: `ImageCropTool_Crop_PdfFile_ReturnsDenialStatingRasterizationIsRequired`

A PDF is the one admitted type that *looks* croppable and is not, so without the reason stated a
model would reasonably retry with different coordinates.

##### AgentKitTools-Image-CropTool-RefusesNonCroppableType: An svg Is Refused Exactly as the Read Tool Refuses It

**Test**: `ImageCropTool_Crop_SvgFile_ReturnsDenialRedirectingToTextFileRead`

The regression guard for the family giving **one** answer to "what is this file". An `.svg`
genuinely is text, so naming the text reader is a classification rather than a way around the
refusal — and this unit delegates to the same map rather than inventing its own answer.

##### AgentKitTools-Image-CropTool-RefusesNonCroppableType: An Unknown Extension Is Refused Without a Redirect

**Test**: `ImageCropTool_Crop_UnknownExtension_ReturnsDenialWithoutRedirect`

Error path: an extension the family cannot read at all is refused with no tool invented to send the
model to.

##### AgentKitTools-Image-CropTool-WritesRegionToDestination: A Permitted Destination Receives the Region

**Test**: `ImageCropTool_Crop_PermittedDestination_WritesThePngAndConfirmsInText`

Normal operation for the second outcome: a text confirmation comes back, and a real PNG of the
requested size exists on disk. The file is read back and decoded, so the assertion is about pixels
rather than about bytes that merely exist.

##### AgentKitTools-Image-CropTool-WritesRegionToDestination: The Written File Carries the Source Pixels Exactly

**Test**: `ImageCropTool_Crop_WrittenFile_CarriesTheSourcePixelsExactly`

The written counterpart of the inline pixel-fidelity scenario. A figure that is not the region it
claims to be is a silent wrong answer that would survive every size assertion, so every channel of
every pixel, alpha included, is compared against the source position it came from.

##### AgentKitTools-Image-CropTool-WritesRegionToDestination: A Written Region Returns No Image Content

**Test**: `ImageCropTool_Crop_WithDestination_ReturnsNoImageContent`

What keeps a document-preparation loop affordable: an agent cropping six figures would otherwise
carry six full-resolution images it no longer needs through every subsequent turn. The result is
asserted to be a string and explicitly not a content list, so nothing image-shaped can be hiding
in it.

##### AgentKitTools-Image-CropTool-WritesRegionToDestination: The Confirmation Names All Three Facts

**Test**: `ImageCropTool_Crop_Confirmation_NamesTheDestinationRegionAndSourceDimensions`

Each of the three is load-bearing and each is asserted separately: the destination so the model can
reference the file it just produced, and the region and the source's dimensions so a second,
adjacent figure can be aimed without reading the image again. A fourth assertion pins the relative
half of the path dialect: the destination lies inside the anchor, so the confirmation must name it
relatively and the workspace root must appear nowhere in the text. That assertion is what makes the
dialect a decision the suite can detect — without it, reporting the absolute path in every case
would satisfy both this scenario and the absolute-branch scenario below.

##### AgentKitTools-Image-CropTool-WritesRegionToDestination: A Destination Outside the Anchor Is Confirmed Absolutely

**Test**: `ImageCropTool_Crop_DestinationOutsideTheWorkingDirectory_IsConfirmedAbsolutely`

The dialect rule, in the configuration the sample runs in. A relative name would name a location
the model cannot reach from the anchor, so the absolute path is the only truthful answer — and the
model needs a truthful one, because it goes on to reference the file by that name.

##### AgentKitTools-Image-CropTool-WritesRegionToDestination: A JPEG Source Is Written as PNG

**Test**: `ImageCropTool_Crop_JpegSourceToDestination_WritesPngBytes`

The output format is the family's own rather than the source's, whichever outcome was asked for.
The written file's signature bytes are asserted directly, which is also what makes the `.png`
destination rule honest rather than decorative.

##### AgentKitTools-Image-CropTool-WritesRegionToDestination: An Omitted Destination Still Returns the Region Inline

**Test**: `ImageCropTool_Crop_OmittedDestination_StillReturnsTheRegionInline`

The explicit statement of the compatibility guarantee: image content comes back, and the permitted
location afterwards holds exactly the file it held before — so the parameter's existence changes
nothing for a caller that does not use it.

##### AgentKitTools-Image-CropTool-DestinationRequiresWriteGrant: A Read-Only Destination Is Refused

**Test**: `ImageCropTool_Crop_DestinationUnderAReadOnlyGrant_IsRefusedDisclosingTheWritableLocation`

**The increment's central guarantee.** The source is admitted by the read decision and the
destination beside it refused by the write decision, in one call. The refusal is asserted to name
the writable location with its access level, because that disclosure is what lets a confined agent
recover to a location it may actually use. A unit that resolved the destination through the read
decision would return a success here.

##### AgentKitTools-Image-CropTool-DestinationRequiresWriteGrant: A Destination Outside Every Grant Is Refused

**Test**: `ImageCropTool_Crop_DestinationOutsideEveryGrant_IsRefused`

Containment applied to the destination as it is to the source: a location no grant covers is
refused by the policy's own decision rather than by any judgment this unit makes.

##### AgentKitTools-Image-CropTool-DestinationRequiresWriteGrant: A Refused Destination Leaves Nothing Behind

**Test**: `ImageCropTool_Crop_RefusedDestination_WritesNothing`

What makes the grant a boundary rather than advice. A refusal that nonetheless wrote the file would
be the worst of both answers: the model told it failed, and the operator's confinement broken
anyway.

##### AgentKitTools-Image-CropTool-RefusesToReplaceDestination: An Existing Destination Is Refused and Left Unchanged

**Test**: `ImageCropTool_Crop_ExistingDestination_IsRefusedWithoutReplacingIt`

A crop that clobbered a figure someone already placed would report success while the document went
on referencing a name that now points at a different picture. Both halves are asserted: the refusal
states that this tool does not replace an existing file, and the occupying file is compared
byte-for-byte afterwards.

##### AgentKitTools-Image-CropTool-RefusesToReplaceDestination: An Image Cannot Be Consumed by Its Own Region

**Test**: `ImageCropTool_Crop_DestinationEqualToTheSource_IsRefusedWithoutReplacingIt`

The source was proven to exist one step earlier, so the existing-file refusal covers this case and
no separate guard is needed. The source's bytes are compared afterwards, because the failure this
prevents is destroying the very image the model asked to examine.

##### AgentKitTools-Image-CropTool-RequiresPngDestination: A Non-PNG Destination Is Refused Naming the Extension

**Test**: `ImageCropTool_Crop_NonPngDestination_IsRefusedNamingTheExpectedExtension`

Six cases in one theory: `.jpg`, `.jpeg`, `.webp`, `.txt`, a name with no extension, and a
whitespace-only destination. The blank case is included deliberately rather than by accident — it
has no extension, so it lands here with a truthful and actionable answer instead of being silently
read as "no destination". Each case additionally asserts that no file was produced.

##### AgentKitTools-Image-CropTool-RequiresPngDestination: A Capitalized Extension Is Accepted

**Test**: `ImageCropTool_Crop_UppercaseDestinationExtension_IsAccepted`

The family already matches every extension it reads case-insensitively, because a capitalized
extension names the same content; the destination follows that same rule rather than inventing a
stricter one. The written file is decoded, so acceptance is demonstrated rather than assumed.

##### AgentKitTools-Image-CropTool-RefusesUnusableDestination: An Unusable Destination Is Refused and No Directory Created

**Test**: `ImageCropTool_Crop_UnusableDestination_IsRefused`

Two cases in one theory: a destination that names an existing directory, and one whose parent
directory does not exist. Both must be named `.png` to reach these checks at all, because the
extension rule is judged first — which is why the directory case uses a directory literally named
`panel.png`. Both cases assert that no directory was materialized, because silently creating a tree
on a mistyped path would scatter directories the agent then believes are real.

##### AgentKitTools-Image-CropTool-RefusesUnusableDestination: An Unwritable Destination Is Refused

**Test**: `ImageCropTool_Crop_UnwritableDestination_IsRefusedWithoutDisclosingTheFailure`

The destination is permitted by the policy, is named `.png`, does not exist, and has an existing
parent directory, so every check this unit makes passes and the write itself is what fails: the
name is longer than a single path component may be, which no file system in use permits — 255
characters on NTFS, ext4 and APFS alike. It is the component limit rather than the total path
length that is exceeded, so the scenario is deterministic on Windows, Linux and macOS and does not
depend on a host's long-path configuration. The refusal is asserted to state that the region could
not be written and nothing further — no operating system wording, no host path — and the permitted
location is asserted to hold no new file.

##### AgentKitTools-Image-CropTool-DenialDisclosure: Destination Refusals Name No Host Path

**Test**: `ImageCropTool_Crop_DestinationDenials_NameNoHostPath`

Four of the five refusals this unit composes for a destination — wrong extension, directory,
existing file, missing parent — are provoked in one scenario and each is asserted to contain no
directory separator of either form and no part of the workspace path. The refusal text reaches a
model and the resulting transcript leaves the process, so nothing about the host's layout may be
composed into one. The success confirmation is governed by the opposite rule and is covered by the
dialect scenarios above. The fifth, the unwritable-destination refusal, is a fixed string with
nothing interpolated into it and is asserted to name no host path by the scenario above.

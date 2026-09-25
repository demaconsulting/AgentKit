## CanvasNet

### Purpose

`DemaConsulting.CanvasNet` is used because the image family must read what an image declares about
itself and must produce a region of one, and neither is expressible without a raster imaging
library. Reporting an image's pixel dimensions is what gives a model a coordinate space to name a
region in; extracting and re-encoding that region is what returns it. Both are format work, and
format work written by hand inside a safety library would be the largest and least reviewable part
of it.

It was chosen over the established alternatives on the property that matters most here: **the
library is fully safe managed code with no native binaries at all.** AgentKit's whole thesis is
that an unsafe operation should be impossible to express rather than refused at call time, and a
tool family whose decoder parses hostile input inside native memory would undermine that at the
foundation. SkiaSharp and Magick.NET both ship native binaries, which additionally bind the package
to a runtime-identifier matrix AgentKit does not otherwise have. ImageSharp's license is not
compatible with this library's terms. StbImageSharp is a translation of a C library and carries no
encoder, so it could read an image but not return a region of one. CanvasNet is MIT licensed,
targets exactly the same frameworks AgentKit does, and brings a single further dependency —
`System.Numerics.Tensors` — with no native component.

It is one of the two runtime NuGet dependencies the AgentKit packages carry outside the
provider-specific adapters, and the only one outside Core.

### Features Used

- Header inspection for PNG and JPEG — reports the dimensions a file declares, and the channel
  count and alpha flag of the encoding the *file* uses, reading only a signature and a header
  chunk (PNG) or a bounded scan of leading marker segments (JPEG). Deliberately reports a
  file's declared dimensions without enforcing what it will decode, which is what makes it usable
  for deciding whether a decode is affordable
- PNG decoding — every color type the specification defines, at every bit depth that
  specification permits for that color type, including palette-indexed and grayscale files, with
  palette indices resolved through the file's own palette
- JPEG decoding — baseline and progressive frames, one-component and three-component, with the
  common chroma-subsampling factors
- The pixel buffer type and its rectangular sub-region copy — a 32-bit RGBA buffer, and a
  region operation that produces an independent copy validating its own bounds
- The published bound on how large a single axis of a pixel buffer may be — read rather than
  restated, so the family's own triage cannot drift from what the decoder will actually accept
- PNG encoding — writes a pixel buffer back out losslessly, so a region returned to a model
  carries the source's pixels rather than a re-compressed approximation of them

Adam7-interlaced PNG is the one well-formed input the decoder refuses; the image family detects it
from the header and refuses it in its own words rather than surfacing the library's.

Nothing in the library's vector, drawing, text or font surface is used, and no type from the
library appears in any AgentKit signature.

### Integration Pattern

`DemaConsulting.CanvasNet` is consumed by `AgentKitTools` as a **direct** `PackageReference`,
deliberately **without** `PrivateAssets`: it is a runtime dependency, so an application that
attaches the image family needs it present at run time and must receive it transitively. It is
reached from exactly two places in the subsystem — the shared header-probe helper and the crop
tool — so the surface that would have to change if the dependency were ever replaced is two files.

There is no initialization, no configuration object and no disposal step. The pixel buffer type is
**not** disposable, so no `using` governs one; buffers are ordinary managed objects the collector
reclaims. Every entry point the family uses is static, and every one is called on bytes already
read and already inside the policy's binary ceiling, so the library is never given a path, never
opens a file and never touches the file system.

**Release precondition.** The dependency is currently published only as a prerelease. A non-
prerelease release of `AgentKitTools` therefore requires a non-prerelease release of this
dependency first, because a stable package must not carry a prerelease transitive dependency. This
is recorded here rather than discovered at release time. The risk it carries is small: the library
is produced by the same organization under the same template and quality gates, is MIT licensed,
ships no native binaries and contains no unsafe code.

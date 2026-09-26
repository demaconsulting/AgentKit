## CanvasNet Verification

This document provides the verification evidence for the `CanvasNet` OTS software item.

### Required Functionality

`CanvasNet` supplies the raster imaging capability the image family is built on: reading the pixel
dimensions an image file declares in its header without decoding any pixel data — including for a
file whose declared dimensions exceed what it will decode — reporting, from the header alone,
whether a full decode is expected to succeed, decoding PNG and JPEG files into an
addressable pixel buffer including a PNG whose pixels are stored as palette indices, copying a
rectangular sub-region of such a buffer, and
encoding a buffer back to a form that reproduces every pixel exactly when decoded again.

### Verification Approach

Like the other runtime library dependencies, and unlike the build-and-verify pipeline tools, this
OTS item has no self-validation command. Per `software-items.md`, an OTS library is verified
through integration tests proving the required functionality works, so the evidence is AgentKit's
own tests: each exercises the library through the tool that consumes it, invoked the way a runtime
invokes it, against files the test itself constructs so that the expected answer is known
independently of the library.

The scenarios are chosen so that each observes something only the required capability could
produce. A caption stating an image's real size is evidence the header was read. A refusal naming
a decode budget, issued for a file that declares its size and then carries no pixel data at all,
is evidence the header was read *without* decoding — a library that decoded first would have
failed differently. These tests run on every platform with no platform filter, so a single-OS run
satisfies them.

### Test Scenarios

#### ImageReadTool_Read_SupportedPng_CaptionStatesThePixelDimensions

**Scenario**: A PNG of a known size is read through the image read tool.

**Expected**: The caption states that size, which the tool obtained from the file's header rather
than from the test.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-HeaderProbe`.

#### ImageReadTool_Read_SupportedJpeg_CaptionStatesThePixelDimensions

**Scenario**: A JPEG of a known size is read through the image read tool.

**Expected**: The caption states that size, obtained by a bounded scan of the file's leading
marker segments.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-HeaderProbe`.

#### ImageCropTool_Crop_HeaderDeclaringMorePixelsThanTheDecodeBudget_IsRefusedNamingTheBudget

**Scenario**: A file declaring eight thousand pixels on each side, and carrying no pixel data at
all, is offered to the image crop tool.

**Expected**: A refusal naming the host's decode budget and the declared size, and specifically not
the undecodable refusal — so the header was read, and reported, without any attempt to decode.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-HeaderProbe`.

#### ImageCropTool_Crop_PalettizedPng_ReturnsCroppedImageContent

**Scenario**: A region is taken from a PNG whose pixels are stored as palette indices.

**Expected**: The region decodes successfully and its first pixel is the palette's color rather
than a raw index, proving the indices were resolved through the file's own palette.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-Decode`.

#### ImageCropTool_Crop_PermittedJpeg_ReturnsPngContent

**Scenario**: A region is taken from a JPEG.

**Expected**: The region decodes successfully, with the requested dimensions, proving the second
format decodes into the same addressable buffer.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-Decode`.

#### ImageCropTool_Crop_ReturnedRegion_CarriesTheSourcePixelsExactly

**Scenario**: A region whose origin is not the image's own is taken from a source built with a
distinct value in every channel of every pixel, and the returned region is decoded again.

**Expected**: Every channel of every pixel of the result equals the source pixel it came from,
including the alpha channel, proving both that the sub-region copy addresses the right pixels and
that the encoding alters none of them.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-Crop`, `AgentKit-OTS-CanvasNet-Encode`.

#### ImageCropTool_Crop_Adam7InterlacedPng_IsRefusedNamingTheDimensions

**Scenario**: A complete, well-formed PNG declaring Adam7 interlacing — today the one well-formed
input this library does not decode — is offered to the image crop tool.

**Expected**: A refusal stating the file is well formed but uses a feature the tool does not
decode, naming the size the header declared, and specifically **not** the undecodable refusal —
so the feasibility was reported from the header, before any decode was attempted, and the
consuming tool needed no knowledge of the PNG format to say so.

**Requirement coverage**: `AgentKit-OTS-CanvasNet-DecodeFeasibility`.

### Requirements Coverage

- **`AgentKit-OTS-CanvasNet-HeaderProbe`**:
  ImageReadTool_Read_SupportedPng_CaptionStatesThePixelDimensions,
  ImageReadTool_Read_SupportedJpeg_CaptionStatesThePixelDimensions,
  ImageCropTool_Crop_HeaderDeclaringMorePixelsThanTheDecodeBudget_IsRefusedNamingTheBudget
- **`AgentKit-OTS-CanvasNet-Decode`**:
  ImageCropTool_Crop_PalettizedPng_ReturnsCroppedImageContent,
  ImageCropTool_Crop_PermittedJpeg_ReturnsPngContent
- **`AgentKit-OTS-CanvasNet-Crop`**:
  ImageCropTool_Crop_ReturnedRegion_CarriesTheSourcePixelsExactly
- **`AgentKit-OTS-CanvasNet-Encode`**:
  ImageCropTool_Crop_ReturnedRegion_CarriesTheSourcePixelsExactly
- **`AgentKit-OTS-CanvasNet-DecodeFeasibility`**:
  ImageCropTool_Crop_Adam7InterlacedPng_IsRefusedNamingTheDimensions

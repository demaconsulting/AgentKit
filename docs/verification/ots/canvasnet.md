## CanvasNet Verification

This document provides the verification evidence for the `CanvasNet` OTS software item.

### Required Functionality

`CanvasNet` supplies the raster imaging capability the image family is built on: reading the pixel
dimensions an image file declares in its header without decoding any pixel data, decoding PNG and
JPEG files into an addressable pixel buffer, copying a rectangular sub-region of such a buffer, and
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

### Requirements Coverage

- **`AgentKit-OTS-CanvasNet-HeaderProbe`**:
  ImageReadTool_Read_SupportedPng_CaptionStatesThePixelDimensions,
  ImageReadTool_Read_SupportedJpeg_CaptionStatesThePixelDimensions

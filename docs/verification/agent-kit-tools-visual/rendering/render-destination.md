### RenderDestination Unit Verification Design

This document describes the unit-level verification strategy for the `RenderDestination` class.

#### Verification Approach

Each refusal is asserted with its reason and its fixed text, and each write against the bytes on
disk and the contents of the folder, because a destination check that looked right but wrote
somewhere else — or left a temporary file behind — is exactly what these scenarios exist to catch.
A real temporary directory tree is used throughout.

Unit tests reside in `Rendering/RenderDestinationTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: A temporary tree per test; the link scenario adds a real link (an NTFS junction on
  Windows, a symbolic link elsewhere) and fails rather than skips when it cannot be created
- **Isolation**: Each test creates its own tree and policy

#### Acceptance Criteria

A unit test run passes when all seven scenarios below pass without error or exception beyond those
explicitly asserted. A non-PNG name accepted, a write the policy refuses accepted, a destination
beneath a link that leaves the grant accepted or written to, a directory or a
missing parent accepted, a directory created, an existing picture not replaced, or a temporary file
left behind each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Rendering-RenderDestination-MustBePng: A Non-PNG Name Is Refused

**Test**: `RenderDestination_TryResolve_NotPng_IsRefusedAsInvalidRequest`

`figure.jpg` is refused as `InvalidRequest` with the fixed text.

##### AgentKitToolsVisual-Rendering-RenderDestination-WriteDecision: A Read-Only Location Is Refused by the Write Decision

**Test**: `RenderDestination_TryResolve_ReadOnlyLocation_IsRefusedByTheWriteDecision`

Under a read-only grant, `figure.png` is refused as `PathNotPermitted` with the policy's own text,
which marks the location read-only.

##### AgentKitToolsVisual-Rendering-RenderDestination-LinkConfined: A Linked Folder Leaving the Grant Is Refused

**Test**: `RenderDestination_TryResolve_ParentLinkLeavingTheGrant_IsRefused`

A link inside the writable grant points at an empty folder outside it. `jn/figure.png` is refused
as `PathNotPermitted`, no destination is returned, and the outside folder is still empty.

##### AgentKitToolsVisual-Rendering-RenderDestination-NotADirectory: A Directory Is Refused

**Test**: `RenderDestination_TryResolve_Directory_IsRefused`

A directory named `pictures.png` is refused with the directory refusal.

##### AgentKitToolsVisual-Rendering-RenderDestination-ParentMustExist: A Missing Parent Is Refused and Not Created

**Test**: `RenderDestination_TryResolve_MissingParent_IsRefusedWithoutCreatingIt`

`missing/figure.png` is refused as `TargetNotFound`, and no `missing` directory exists afterwards.

##### AgentKitToolsVisual-Rendering-RenderDestination-ReplacesAtomically: An Existing Picture Is Replaced

**Test**: `RenderDestination_TryWriteAsync_ExistingFile_IsReplaced`

An earlier file at `figure.png` is replaced by the new bytes, the destination is reported as
`figure.png`, and the folder holds that one file and no temporary file.

##### AgentKitToolsVisual-Rendering-RenderDestination-NoTemporaryLeftOnFailure: A Refused Write Leaves Nothing Behind

**Test**: `RenderDestination_TryWriteAsync_FileSystemRefuses_LeavesNoTemporaryFile`

After resolution, the target is replaced by a non-empty directory, so the move is refused; the write
reports failure and the folder holds no file.

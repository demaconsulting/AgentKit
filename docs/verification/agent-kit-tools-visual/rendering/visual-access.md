### VisualAccess Unit Verification Design

This document describes the unit-level verification strategy for the `VisualAccess` class.

#### Verification Approach

The mapping is verified **behaviorally**: each scenario draws a real document whose reference points
somewhere specific, through the real rendering library, under the policy the mapping produced, and
asserts whether the referenced file was used. A policy that merely looked right would pass an
inspection of its parts and fail here; the one structural assertion — the parts' names — is made in
addition to, not instead of, the behavioral ones. Every escape route the rendering spike exercised is
a scenario: a parent-folder step, an absolute path, a link, a `file:` URI and a deny pattern, for
embedded pictures and for included and data files.

Unit tests reside in `Rendering/VisualAccessTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project, using `TempDirectoryFixture.cs`,
`DirectoryLink.cs`, `Rendering/CountingListener.cs` and `Rendering/RenderSamples.cs`.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: The CanvasNet Viewer, in process
- **File system**: A temporary tree per test; the link scenario creates a directory junction with
  `mklink /J` on Windows (which needs no privilege) and a symbolic link elsewhere, and fails rather
  than skips when it cannot, so a missing control is never reported as covered
- **Network**: The network scenario starts an HTTP listener on a free loopback port
- **Isolation**: Each test creates its own tree, policy and renderer

#### Acceptance Criteria

A unit test run passes when all twelve scenarios below pass without error or exception beyond those
explicitly asserted. A picture inside the grant that is not drawn, any escaping reference that is
drawn, a deny pattern ignored, an unrestricted grant that bypasses the read decision, a mapping
lacking one of its parts, a refusal naming a host path, a request reaching the listener, or a base
folder chosen for an unreadable working directory each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Rendering-VisualAccess-InsideGrantDrawn: A Picture Inside the Grant Is Drawn

**Test**: `VisualAccess_ToFileAccess_PictureInsideTheGrant_IsDrawn`

Normal operation: an SVG embedding `ok.png` beside it renders, with no note about the picture.

##### AgentKitToolsVisual-Rendering-VisualAccess-EscapeRefused: A Parent-Folder Escape Is Refused

**Test**: `VisualAccess_ToFileAccess_ParentFolderEscape_IsRefused`

`../outside/secret.png` is refused and the render fails as `file-access-denied`.

##### AgentKitToolsVisual-Rendering-VisualAccess-EscapeRefused: An Absolute Path Outside Is Refused

**Test**: `VisualAccess_ToFileAccess_AbsolutePathOutside_IsRefused`

The full path of a picture outside the grant is refused.

##### AgentKitToolsVisual-Rendering-VisualAccess-LinkRefused: A Link Escape Is Refused

**Test**: `VisualAccess_ToFileAccess_LinkEscape_IsRefused`

`jn/secret.png`, reached through a junction or symbolic link inside the grant pointing outside it, is
refused although its written path lies inside the grant.

##### AgentKitToolsVisual-Rendering-VisualAccess-DenyPatternEnforced: A Deny Pattern Inside the Grant Is Enforced

**Test**: `VisualAccess_ToFileAccess_DenyPatternInsideTheGrant_IsRefused`

A picture matching the grant's `*.secret.png` pattern is refused, by AgentKit's own decision rather
than by the grant's root.

##### AgentKitToolsVisual-Rendering-VisualAccess-FileUriJudged: A File URI Is Judged Like a Path

**Test**: `VisualAccess_ToFileAccess_FileUri_IsJudgedLikeAPath`

A `file:` URI to a picture outside the grant is refused, and one to a picture inside it is drawn.

##### AgentKitToolsVisual-Rendering-VisualAccess-EveryReferenceKind: Included and Data Files Outside Are Refused

**Test**: `VisualAccess_ToFileAccess_IncludedFileOutside_IsRefused`

Run as a theory over a PlantUML include, an OpenSCAD include and a Vega-Lite data file outside the
grant; each fails as `file-access-denied`.

##### AgentKitToolsVisual-Rendering-VisualAccess-UnrestrictedGrantUsesPolicy: An Unrestricted Grant Still Uses the Policy

**Test**: `VisualAccess_ToFileAccess_UnrestrictedGrant_UsesThePolicyDecision`

The mapping of an unrestricted grant omits `roots` but keeps `no-symlinks` and `agentkit-read`; a
permitted picture is drawn and one matching the grant's deny pattern is refused.

##### AgentKitToolsVisual-Rendering-VisualAccess-NeverAllowAll: A Rooted Policy Combines All Three Parts

**Test**: `VisualAccess_ToFileAccess_RootedPolicy_CombinesAllThreeParts`

The mapped policy is named exactly `roots+no-symlinks+agentkit-read`, in that order.

##### AgentKitToolsVisual-Rendering-VisualAccess-RefusalDisclosesNoHostPath: The Refusal Names No Host Path

**Test**: `VisualAccess_ToFileAccess_Refusal_DisclosesNoHostPath`

A deny-pattern refusal, shaped as a tool shapes it, contains the fixed reason, no part of the
temporary directory's absolute path and no naming of the file-access policy.

##### AgentKitToolsVisual-Rendering-VisualAccess-NetworkNeverFetched: A Network Reference Is Never Fetched

**Test**: `VisualAccess_ToFileAccess_NetworkReference_IsNeverFetched`

An SVG embedding a picture from a local listener renders, and the listener receives no request.

##### AgentKitToolsVisual-Rendering-VisualAccess-InlineBaseFolder: The Working Directory Is the Base Only When Readable

**Test**: `VisualAccess_InlineBaseFolder_WorkingDirectoryReadability_DecidesTheFolder`

A policy granting its working directory yields that directory; one granting only a subfolder yields
none, so relative references fail closed.

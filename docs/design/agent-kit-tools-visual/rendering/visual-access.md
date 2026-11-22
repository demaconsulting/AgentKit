### VisualAccess

![AgentKit Tools Visual Rendering Structure](RenderingView.svg)

The `VisualAccess` class maps an AgentKit access policy onto the file-access policy the rendering
library applies to every file a drawn document references, and chooses the folder inline source's
references resolve against.

#### Purpose

A document names other files — an SVG's pictures, a diagram's includes, a chart's data — and the
rendering library opens them itself. AgentKit's read decision would never see them unless it were
handed to the library as a policy the library consults. The library's default policy reads any
local file through `..`, an absolute path, a `file:` URI or a link, which is exactly the escape a
confined agent must not be given.

#### Data Model

The class is static and stateless; each policy it creates is immutable and thread-safe.

| Member | Type | Value |
| --- | --- | --- |
| `DelegateName` | `string` | `agentkit-read`, the delegate part's name in diagnostics |
| `OutsideReason` | `string` | "the file is outside the locations the agent may read" |
| `OutsideHint` | `string` | "reference a file inside a location you may read" |

#### Key Methods

##### ToFileAccess(PathPolicy policy)

Creates the combined file-access policy.

**Algorithm:** collects up to three parts, every one of which must allow a file, in this order:

1. `FileAccessPolicy.UnderRoots(roots)` over the root of every grant, when the policy has grants and
   every grant has a root. Both the path as written and the real path of the opened handle must lie
   inside a root. Omitted when any grant is unrestricted, because such a grant has no root to
   confine to.
2. `FileAccessPolicy.NoSymlinks`: a link or junction below the folder the reference starts from is
   refused, whatever its target.
3. `FileAccessPolicy.FromDelegate("agentkit-read", …)`, which asks
   `PathPolicy.TryResolveRead(request.RequestedPath)` and allows only when the policy permits the
   file and resolves it to that same full path; otherwise it denies with the fixed reason and hint.

The three are joined with `FileAccessPolicy.Combine`. The library's `AllowAll` is never used.

**Postconditions:** a policy named `roots+no-symlinks+agentkit-read` for a rooted policy, or
`no-symlinks+agentkit-read` when a grant is unrestricted.

**Why the policy is created per render.** `UnderRoots` captures the real location of each root when
it is created and never looks it up again, and a root that does not exist yet allows no file. A
grant root created after the composition was built — a session folder — would otherwise deny every
reference for the life of the composition. Creating the policy for each render costs one real-path
lookup per root, which is negligible beside a render.

**Why the delegate asks about the path as written.** The access policy resolves a path with
`Path.GetFullPath` and does not follow links, so the full written path is the dialect it decides in.
Asking about the real path instead would refuse every file whose real path differs from its written
path by a link the host placed above a grant — on macOS the temporary folder is reached through
one — while adding no protection: a real path outside every grant is refused by `UnderRoots`, and a
link below the starting folder by `NoSymlinks`. What the delegate adds is AgentKit's own rules,
above all deny patterns, applied to every referenced file exactly as to a path a tool is given.

**Why the delegate's refusal is fixed text.** The access policy's own denial names the permitted
locations and the full path of the file, an absolute host path the model never supplied. The library
prefixes the reason with the reference as the document wrote it, which the model did supply, so the
fixed reason is all that needs to be added.

##### InlineBaseFolder(PathPolicy policy)

Chooses the folder an inline source's relative references resolve against: the working directory
when `TryResolveRead` permits it, and otherwise none. A render given no folder resolves no relative
reference, so an unreadable working directory makes every relative reference fail closed rather
than resolve somewhere unintended. A file's references resolve against the file's own folder
instead; the calling tool supplies that.

#### Error Handling

`ArgumentNullException` for a null policy, which is a programming error in the calling tool.
Nothing else can fail. A delegate that threw would be treated by the library as a denial; this one
does not throw, because `TryResolveRead` returns its decision rather than throwing.

**Platform limitation.** A restrictive policy is never consulted without the real path of the opened
file, and where the platform cannot report it — Linux without `/proc` — the library refuses every
reference. That fails closed and is accepted.

#### Dependencies

`PathPolicy` from AgentKitCore for the grants, their roots and the read decision.
`FileAccessPolicy`, `FileAccessDecision` and `FileAccessRequest` from the CanvasNet core.

#### Callers

`ImageViewTool` maps the policy for each view. The diagram family will call `InlineBaseFolder` for
inline source. `VisualRenderer.WarmUpAsync` builds its own refuse-everything policy and does not call
this unit.

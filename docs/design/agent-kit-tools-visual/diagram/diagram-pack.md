### DiagramPack

![AgentKit Tools Visual Diagram Structure](DiagramView.svg)

The `DiagramPack` class publishes the diagram family as one pack under the `diagram` prefix, in one
of the two preview modes of the public `DiagramPreview` enum.

#### Purpose

A pack is the unit of attachment, so it is the only public way to obtain the family's tools. The
pack also decides the two things that must hold for both tools alike: which languages are offered
(from its options) and how the model is shown a drawing (from its preview mode).

#### Data Model

The class is sealed. It holds its preview mode, its options, its instruction text, the help catalog
filtered to its languages and one `VisualRenderer`; all are immutable or safe for concurrent use, so
the class is safe for concurrent use. The contract expects a pack to be stateless; the render gate
inside the renderer is the deliberate exception, shared by every composition the instance is added
to so that the concurrency bound holds per pack instance.

| Member | Type | Value |
| --- | --- | --- |
| `FamilyPrefix` (constant) | `string` | `diagram` |
| `Preview` | `DiagramPreview` | `Image` (default) or `Text` |
| `Options` | `DiagramPackOptions` | `DiagramPackOptions.Default` when none is given |
| `Instructions` | `string` | About eighty tokens; follows the preview mode |
| `RequiredCapabilities` | `HostCapabilities` | `Vision` in image preview, `None` in text preview |

`DiagramPreview.Image` returns the picture to look at with `save_as` optional;
`DiagramPreview.Text` requires `save_as` and returns a description of the saved picture.

#### Key Methods

##### DiagramPack(DiagramPreview preview = Image, DiagramPackOptions? options = null)

Refuses an undefined preview mode with `ArgumentOutOfRangeException`, takes the default options for
`null`, and refuses `MinPreviewSide` above `MaxRenderSide` with `ArgumentException` — the one
relation the options cannot check themselves because an initializer sets the two values one at a
time. It then creates the renderer, filters the help catalog with
`DiagramHelpTool.CatalogFor(Options.Formats)` and composes the instructions.

##### For(HostCapabilities host, DiagramPackOptions? options = null)

Returns a pack in image preview when `host` includes `Vision`, and in text preview otherwise. The
preview mode is not an option precisely so that this factory never overrides a value the caller
set.

##### CreateTools(PathPolicy policy)

Refuses a null policy with `ArgumentNullException`. Returns `diagram_help`, followed by
`diagram_render` unless the pack is in text preview and `policy.AnyLocationIsWritable` is false:
text preview requires `save_as`, so under a policy that permits no writing the render tool's only
outcome would be a refusal. Both tools capture the policy and the filtered catalog; the render tool
also captures the pack's renderer.

##### Instructions

Composed once at construction from the preview mode and the offered languages. Image preview: read
the help topic for the diagram type, draw with the complete source, look at the picture, fix until
it shows what was meant, use `save_as` to keep it. Text preview: the same, with `save_as` required
and a description returned instead of the picture. The pack never adds the text to any instructions
or tool description.

##### WarmUpAsync(CancellationToken cancellationToken = default)

Delegates to `VisualRenderer.WarmUpAsync(Options.Formats, …)`, drawing one small sample of each
offered language through the pack's gate and timeout. Optional and never called by the pack.

#### Error Handling

`ArgumentOutOfRangeException` and `ArgumentException` from the constructor and `For`;
`ArgumentNullException` from `CreateTools`; `OperationCanceledException` from `WarmUpAsync` when its
token is canceled.

#### Dependencies

`IToolPack`, `HostCapabilities`, `PathPolicy` and `ToolPackBuilder` from AgentKitCore;
`DiagramPackOptions`, `DiagramHelpTool` and `DiagramRenderTool` in this subsystem; `VisualRenderer`
from the Rendering subsystem.

#### Callers

An application constructs the pack, or obtains it from `For`, and adds it to a `ToolPackBuilder`;
the builder calls `CreateTools` when the host provides `RequiredCapabilities`.

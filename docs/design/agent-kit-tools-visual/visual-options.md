## VisualOptions

![AgentKit Tools Visual Structure](AgentKitToolsVisualView.svg)

The `VisualOptions` record carries the rendering bounds a host gives a visual tool pack, in addition
to the limits the access policy already carries.

### Purpose

A drawing a model asks for is authored by the model, so its cost is chosen by the model: a few
lines of a diagram or a 3D model can ask for gigabytes of pixels or geometry, or minutes of work.
The access policy's `ToolLimits` already bound the pixels a render may decode, the bytes a tool may
return and the bytes of inline text it may read. What they do not bound is time, picture size,
three-dimensional geometry and concurrency, and those are what this record carries. Repeating a
`ToolLimits` value here would give a host two settings for one limit, so none is repeated.

### Data Model

The record is sealed and immutable; it is safe to share between packs and threads.

| Member | Type | Default | Range |
| --- | --- | --- | --- |
| `Default` (static) | `VisualOptions` | every default below | — |
| `RenderTimeout` | `TimeSpan` | 30 seconds | greater than zero, at most 10 minutes |
| `MaxRenderSide` | `int` | 2048 | 16 to 8192 |
| `MinPreviewSide` | `int` | 768 | 1 to 8192, and not above `MaxRenderSide` |
| `ScadMaxTriangles` | `long` | 500,000 | at least 1 |
| `MaxConcurrentRenders` | `int` | 1 | at least 1 |

- **RenderTimeout** bounds one render, including the time it waits for a place in the pack's render
  gate. The upper bound keeps one render from holding the gate indefinitely.
- **MaxRenderSide** bounds the longer side of a picture: a larger natural size is drawn again at
  this side, and a larger requested width is reduced to it. 8192 is the largest side the rendering
  library can produce.
- **MinPreviewSide** is the longer side a small vector picture is drawn again at when no width was
  requested, because small labels are read poorly by vision models. A raster image is never
  enlarged.
- **ScadMaxTriangles** bounds the geometry of an OpenSCAD model. The triangle count drives the
  memory a model needs — a model of a few lines was measured needing over half a gigabyte under a
  generous limit — so the default is a quarter of the library's own default of two million.
- **MaxConcurrentRenders** bounds how many renders one pack instance runs at once.

### Key Methods

#### init accessors

Each property's `init` accessor refuses a value outside its range with an
`ArgumentOutOfRangeException` naming the property, before storing it. **No value is clamped or
ignored**: a host that believed a bound was in force when it was not would have no way to notice,
so every accepted option is applied as stated and every other value is refused at the line that set
it.

The one relation between two values — `MinPreviewSide` not above `MaxRenderSide` — cannot be checked
by either accessor, because an object initializer sets them one at a time and either may be set
first. The pack that receives the options is the first place both are known, so it checks the
relation at construction; see *ImagePack Unit Design*.

### Error Handling

`ArgumentOutOfRangeException` from an `init` accessor, for a value outside its range. Nothing else
can fail: the record holds values and has no behavior beyond validation.

### Dependencies

None beyond the Base Class Library.

### Callers

A host constructs one and passes it to `ImagePack(VisualOptions?)`. The pack's `VisualRenderer`
reads every value; see *VisualRenderer Unit Design*.

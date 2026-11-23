### RenderDestination

![AgentKit Tools Visual Rendering Structure](RenderingView.svg)

The `RenderDestination` class decides whether a drawn picture may be saved where a caller named, and
saves it there, replacing an earlier picture of the same name in one step.

#### Purpose

A model that draws a diagram for a document saves it where the document can reference it, looks at
it, fixes the source and saves again under the same name. The destination rules every writing tool
follows must hold for that save too, and the loop must not be stopped by its own previous result.

#### Data Model

The class is static and stateless. It carries three fixed refusals — `MustBePng`, `IsDirectory` and
`ParentMissing` — and the `Destination` record: the real path the write decision permitted and the
form a confirmation reports it in.

#### Key Methods

##### TryResolve(PathPolicy policy, string destination, out Destination? resolved, out object? denial)

**Algorithm,** in this order:

1. The name must end in `.png` (case-insensitive), because the bytes are always PNG; otherwise
   `InvalidRequest`. This contradiction is in the request, so it is refused before any policy
   decision.
2. `PathPolicy.TryResolveWrite` must permit it; otherwise `PathNotPermitted` with the policy's own
   refusal unchanged, which names the writable locations. The write decision is taken alone, never
   derived from a read. The decision is lexical: a symbolic link or junction inside a grant can lead
   the destination outside the grants, the same as for `text_file_write`; see *AgentKitToolsVisual
   System Design*, *Known Limitations*.
3. It must not be a directory; otherwise `InvalidRequest`.
4. Its parent directory must already exist; otherwise `TargetNotFound`. No directory is ever
   created: on a mistyped path that would scatter directories the agent then believes are real.

**Postconditions:** on success, the destination and its reported form — relative to the working
directory when `PathPolicy.EmitRelative` says so, otherwise absolute — computed once so the write
and the confirmation cannot disagree.

**Why an existing file is replaced, unlike the region tools.** The region tools refuse an existing
destination, because a figure silently replaced is a document now pointing at a different picture.
A rendering is different: the draw, look, fix loop saves to the same name each time, and refusing the
second save would stop the loop or litter the workspace with a new name per attempt. The same write
grant that lets `text_file_write` replace a file's content governs it.

##### TryWriteAsync(byte[] bytes, Destination destination, CancellationToken cancellationToken)

**Algorithm:** creates a new temporary file `.<name>.<guid>.tmp` beside the target with
`FileMode.CreateNew`, writes and flushes the bytes, then moves it over the target with
`File.Move(…, overwrite: true)`, so a reader sees the old picture or the new one and never a partial
one. A temporary file left by any failure, including cancellation, is removed in a `finally`.

**Postconditions:** `true` when the picture was written; `false` when the file system refused it
(`IOException`, `UnauthorizedAccessException`, `NotSupportedException`). No exception's own message
is surfaced: the caller states that the picture could not be saved, because this helper does not
know why and a guess would be worse than silence.

#### Error Handling

`ArgumentNullException` for a null argument. `OperationCanceledException` for cancellation, after
the temporary file is removed. Every other failure is a refusal or `false`.

#### Dependencies

`PathPolicy`, `ToolResult` and `DenialReason` from AgentKitCore. From the Base Class Library:
`FileStream`, `File`, `Directory` and `Path`.

#### Callers

`DiagramRenderTool` saves its pictures through this unit, resolving the destination before it draws. No tool of the
image family calls it today; the region tools keep `ImageDestination`, whose rule is never to replace.

# Outside the workspace

This Markdown file lives beside the sample's workspace folder, not inside it. It exists so a
request such as "list the sections of ../outside.md" is a genuine containment decision: the
`markdown_sections` tool routes the path through `PathPolicy`, which refuses it because it lies
outside the one granted location, and the tool returns `Denied (PathNotPermitted)` rather than
reading a file it was never granted.

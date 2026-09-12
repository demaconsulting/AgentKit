## ApiMark

### Purpose

DemaConsulting.ApiMark is used to generate the API reference documentation for the four shipped
packages from their XML doc comments. It was chosen because its output is organized for *gradual
disclosure* — an index page, then a page per namespace, per type, and per member — which matches
the stated audience for this reference: a coding agent assembling an application against AgentKit,
which reads a small targeted page far more effectively than one large document. Generating the
reference from the doc comments rather than maintaining it by hand is what keeps it from drifting
away from the code it describes.

### Features Used

- MSBuild integration (`DemaConsulting.ApiMark.MSBuild`), so the reference is produced by the
  ordinary build rather than by a separate command a developer must remember to run
- Output redirection (`ApiMarkOutputDir`) into each project's `generated/api` folder
- Package inclusion (`ApiMarkPackDocs`), which places the generated `api/` tree inside the produced
  NuGet package, so a consumer receives the reference with the library
- Rendering of `<example>`/`<code>` blocks as fenced code blocks, and of `<remarks>` prose, so the
  worked examples and behavioral caveats reach the generated output rather than only the signatures

### Integration Pattern

ApiMark is referenced per shipped project as a build-time-only `PackageReference` with
`PrivateAssets="All"`, so it never becomes a transitive dependency of a consuming project and is
never linked into or shipped as runtime code. There is no `Directory.Build.props` in this
repository — every project is standalone — so a per-project reference is the established idiom, and
it is also what gives the exclusion the increment requires for free: the sample and test projects
simply do not carry the reference, so they produce no API documentation.

Each shipped project sets two properties:

- **`ApiMarkOutputDir`** — `$(MSBuildProjectDirectory)\generated\api`. Places the output inside
  `generated/`, which is already excluded by `.gitignore`, `.markdownlint-cli2.yaml`, and
  `.cspell.yaml`, and is covered by the standing rule that agents never read, lint, or modify
  generated content. No new ignore rule is needed anywhere.
- **`ApiMarkPackDocs`** — `true`. Ships the generated `api/` tree inside the `.nupkg`, which is the
  delivery mechanism for the consuming-agent audience. Verified to work with
  `dotnet pack --no-build`, which is how the CI workflow packs.

The targets run once per build — for the first target framework only — which is correct for these
multi-targeted projects, whose public surface is identical across `net8.0`, `net9.0`, and `net10.0`.
Generation depends on `GenerateDocumentationFile`, which all four shipped projects already set.

The ApiMark **CLI** tool is deliberately *not* added to `.config/dotnet-tools.json`. Its
`--validate` self-test covers only version and help display; it does not self-test generation, so
adding it would capture a tool version in `.versionmark.yaml` without producing any evidence that
generation works. The evidence used instead is FileAssert asserting the real generated output —
see `docs/verification/ots/apimark.md`.

### Documentation Enforcement — Deliberately Not Enabled

ApiMark can fail a build on undocumented public API (`ApiMarkEnforceDocs`). It is **not** enabled,
for one decisive reason: the feature does not exist in the stable release this repository pins
(0.4.10). It appears only in a prerelease, and the repository's convention is to pin stable
versions of every tool.

The documentation debt it would report was measured against the four real assemblies and is
recorded here so the decision is reversible without re-investigation: **2 undocumented items out of
102 checked** — Core 0 of 72, Tools 2 of 26, Copilot 0 of 2, ChatClient 0 of 2. Both are implicit
default constructors, on `TextFilePack` and `ImagePack`. Enabling enforcement would therefore
require moving to ApiMark 0.5.0 or later once it is stable, setting
`ApiMarkEnforceDocs=Public` and `ApiMarkEnforceDocsSeverity=Error`, and declaring explicit
documented constructors with no parameters on those two types.

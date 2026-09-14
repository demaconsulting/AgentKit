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
- Documentation-coverage enforcement (`ApiMarkEnforceDocs`, `ApiMarkEnforceDocsSeverity`), which
  fails the build on any public API member that lacks an XML doc `<summary>`
- Rendering of `<example>`/`<code>` blocks as fenced code blocks, and of `<remarks>` prose, so the
  worked examples and behavioral caveats reach the generated output rather than only the signatures

### Integration Pattern

ApiMark is referenced per shipped project as a build-time-only `PackageReference` with
`PrivateAssets="All"`, so it never becomes a transitive dependency of a consuming project and is
never linked into or shipped as runtime code. There is no `Directory.Build.props` in this
repository — every project is standalone — so a per-project reference is the established idiom, and
it is also what gives the exclusion the increment requires for free: the sample and test projects
simply do not carry the reference, so they produce no API documentation.

Each shipped project sets four properties:

- **`ApiMarkOutputDir`** — `$(MSBuildProjectDirectory)\generated\api`. Places the output inside
  `generated/`, which is already excluded by `.gitignore`, `.markdownlint-cli2.yaml`, and
  `.cspell.yaml`, and is covered by the standing rule that agents never read, lint, or modify
  generated content. No new ignore rule is needed anywhere.
- **`ApiMarkPackDocs`** — `true`. Ships the generated `api/` tree inside the `.nupkg`, which is the
  delivery mechanism for the consuming-agent audience. Verified to work with
  `dotnet pack --no-build`, which is how the CI workflow packs.
- **`ApiMarkEnforceDocs`** — `Public`. Fails the build on any public API member without an XML doc
  `<summary>`. The tier matches `ApiMarkVisibility` (also `Public`), so enforcement covers exactly
  the surface that is generated and shipped, and cannot pass on a surface the reference omits.
- **`ApiMarkEnforceDocsSeverity`** — `Error`. See below.

The targets run once per build — for the first target framework only — which is correct for these
multi-targeted projects, whose public surface is identical across `net8.0`, `net9.0`, and `net10.0`.
Generation depends on `GenerateDocumentationFile`, which all four shipped projects already set.

### Documentation Enforcement

ApiMark fails the build on undocumented public API, and this repository **enables** that. The four
shipped projects set `ApiMarkEnforceDocs=Public` and `ApiMarkEnforceDocsSeverity=Error`. The
property names and their accepted values were read from the MSBuild targets inside the referenced
package (`build/DemaConsulting.ApiMark.MSBuild.targets`, which forwards both properties to
`ApiMarkTask`) and from the tool's own help, which documents the tiers `Public`,
`PublicAndProtected`, and `All`, and the severities `Warning` and `Error`.

`Error` rather than `Warning` is deliberate. The documentation debt is closed — coverage is **0
undocumented of 216 checked** across the four assemblies (Core 0 of 75, Tools 0 of 137, Copilot 0
of 2, ChatClient 0 of 2) — so nothing has to be tolerated, and a warning in a build nobody reads
would be indistinguishable from no enforcement at all.

The debt this closed was the 2 items previously recorded here: the implicit default constructors on
`TextFilePack` and `ImagePack`. Both types now declare an explicit, documented constructor taking
no parameters. The C# compiler cannot catch this class of gap — `CS1591` does not fire on an
implicit constructor — which is precisely why a separate coverage check earns its place: it detects
public surface the compiler's own documentation warning is blind to.

Enforcement is confirmed to be *active* rather than merely configured. Before the constructors were
documented, the Tools build failed with `2 undocumented API item(s) found`, naming
`TextFilePack.TextFilePack()` and `ImagePack.ImagePack()`; afterwards it reports 0. For the three
projects that had no debt to fail on, building with the tier temporarily raised to `All` produces a
per-project undocumented count and a failed build, which demonstrates the same machinery is wired
up and reachable in each of them rather than silently inert.

The blind spot above is recorded from observation, not inference. With `ImagePack`'s explicit
constructor removed so that the constructor reverted to being implicit, the Tools build failed with
`Error: 1 undocumented API item(s) found (--enforce-docs-severity Error).` and
`ApiMark.Tool exited with code 1`, and restoring the constructor returned it to 0. The failing
build also names the member it objected to — `[Undocumented] Method:
DemaConsulting.AgentKit.Tools.Image.ImagePack.ImagePack()` — so a failure identifies its own cause
without a re-run at higher verbosity. That transition is the load-bearing evidence: it is a change
from
green to red and back, with the count tracking the change exactly, rather than a tool noticing a
condition that was already present. No `CS1591` and no analyzer diagnostic appeared at any point
during it, because an implicit constructor is invisible to both — so ApiMark was the only check
that objected. An earlier attempt to demonstrate the same property by removing the documentation
from an *explicit* constructor proves nothing about this tool, because the compiler raises `CS1591`
first and fails the build before the coverage check is reached.

This pins a stable release. Enforcement was originally adopted on a prerelease as a deliberate
exception to the convention of pinning stable versions of every tool, because documentation-
coverage enforcement did not exist in the then-current stable release and shipping an API reference
with unenforced gaps was judged the worse outcome. That exception is now closed: enforcement is
available in a stable release of the same line, so the convention holds again with no prerelease
carve-out. The pinned version lives in the four project files, which are the only place it is
recorded: `.versionmark.yaml` captures versions from `dotnet tool list`, and the ApiMark **CLI** is
deliberately absent from `.config/dotnet-tools.json`, so ApiMark has no VersionMark entry to
update. Version provenance for a given build therefore comes from those project files and from the
build's own record, never from this document.

Because the move to the stable release skipped intermediate prereleases, it was verified as a real
upgrade rather than a label change. The verification does not rest on a green build: ApiMark
prints its own version and the path it loaded its task assembly from, and both were read back out
of the build log and matched against the package the restore actually resolved. That check matters
here because a build that skips restore can run a cached older package while the project file
claims a newer one. Every one of the generated pages came out byte-identical to the output the
previous pin produced from the same sources, and the FileAssert assertions over that output pass
unchanged.

The MSBuild integration did change in one respect. The current release defaults
`ApiMarkReferencePaths` to the
resolved `@(ReferencePath)` items when the property is not set explicitly, so a doc comment
inherited from a type in a referenced assembly resolves without configuration; the opt-out is
`ApiMarkDisableReferencePathsHarvest=true`. This repository sets neither property, so the
harvesting is active, and it is harmless and welcome here: the identical output confirms no page
depended on an inherited doc comment that previously failed to resolve, and any future use of
documentation inheritance across the four packages will now resolve rather than render empty. The
tiers and
severities this repository relies on are unchanged — the tool's help still documents
`--enforce-docs` accepting `Public`, `PublicAndProtected`, and `All`, and
`--enforce-docs-severity` accepting `Warning` and `Error`. (`.versionmark.yaml`'s version patterns
accept prerelease suffixes, so neither the earlier prerelease pin nor this stable one violates any
convention there.)

The ApiMark CLI tool remains deliberately *not* added to `.config/dotnet-tools.json`. Its
`--validate` self-test covers only version and help display; it does not self-test generation, so
adding it would capture a tool version in `.versionmark.yaml` without producing any evidence that
generation works. The evidence used instead is FileAssert asserting the real generated output —
see `docs/verification/ots/apimark.md`.

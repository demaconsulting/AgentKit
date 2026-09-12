## ReviewMark

### Purpose

DemaConsulting.ReviewMark is used to manage file-review status enforcement. It was chosen because
it turns the informal practice of "review before merge" into an enforceable, structured process:
review-sets, review evidence, and review currency are all tracked and machine-checkable rather
than relying on manual sign-off.

### Features Used

- Review-plan generation (`--plan`) from `.reviewmark.yaml` review-set definitions
- Review-report generation (`--report`) documenting review coverage and currency against the
  evidence store
- Review-plan coverage reporting: the plan's coverage section states whether every file requiring
  review belongs to a review-set and lists by path any file that belongs to none
- Evidence-index scanning (`--index`) of PDF review evidence into an `index.json` catalogue
- Enforcement mode (`--enforce`), which exits non-zero when files in a review-set are not current
- Elaboration mode (`--elaborate`), which prints a review-set's ID, fingerprint, and file list for
  agent consumption
- Lint mode (`--lint`), which validates `.reviewmark.yaml` for structural and semantic issues
- Working-directory override (`--dir`)

### Integration Pattern

ReviewMark is invoked as a `dotnet tool` from several places: `lint.ps1` runs
`dotnet reviewmark --lint` as an early-detection configuration check, and
`.github/workflows/build.yaml` runs ReviewMark to generate
`docs/code_review_plan/generated/plan.md` and `docs/code_review_report/generated/report.md`, and
to enforce review currency against the evidence store published on the repository's `reviews`
branch. It is a stateless CLI invocation — each run reads `.reviewmark.yaml` and the evidence
source, produces its output or exit code, and exits; there is no initialization, configuration
object, or disposal step.

`lint.ps1` additionally consumes the review plan for its coverage verdict, because
`reviewmark --lint` validates the configuration but does not detect a file that requires review
yet belongs to no review-set, and `reviewmark --plan` exits zero whether or not gaps exist — the
exit code carries no coverage signal. The plan is therefore written to a scratch file outside the
repository and inspected as text. That inspection deliberately asserts the success sentence
("All files requiring review are covered by a review-set") rather than matching the failure text:
if a future ReviewMark release changes the wording or structure of the coverage section, asserting
success fails closed and the gate reports a problem, whereas matching the failure text would fail
open and pass silently while verifying nothing. This repository has had three separate incidents
of a verification passing while not actually running, so fail-closed is a requirement here, not a
stylistic preference. The same reasoning applies to the FileAssert assertion on the generated plan
in CI.

## ReviewMark Verification

This document provides the verification evidence for the ReviewMark OTS software item. Requirements
for this OTS item are defined in the ReviewMark OTS Software Requirements document.

### Required Functionality

DemaConsulting.ReviewMark reads the `.reviewmark.yaml` configuration and the review evidence store
to produce a review plan and review report documenting file review coverage and currency. It runs in
the same CI pipeline that produces the TRX test results, so a successful pipeline run is evidence
that ReviewMark executed without error.

### Verification Approach

ReviewMark is verified by two complementary layers of evidence. First, the CI pipeline runs
`reviewmark --validate --results artifacts/reviewmark-self-validation.trx`, which exercises
ReviewMark's built-in self-validation suite against test review configurations and records
results for ReqStream.

Second, the pipeline invokes ReviewMark to generate
`docs/code_review_plan/generated/plan.md` and `docs/code_review_report/generated/report.md`.
Pandoc converts each to HTML; if either file were absent or malformed, Pandoc would fail.
WeasyPrint renders both to PDF and FileAssert asserts their content
(`WeasyPrint_ReviewPlanPdf`, `WeasyPrint_ReviewReportPdf`). A CI build failure at any step is
evidence that ReviewMark did not produce the required review documents.

Third, the review-plan coverage verdict is asserted directly. After ReviewMark generates
`docs/code_review_plan/generated/plan.md`, FileAssert's `code-review` group asserts that the plan
contains a coverage section reporting that every file requiring review is covered by a review-set
(`ReviewMark_PlanCoverageVerdict`). This demonstrates that ReviewMark emits an explicit,
machine-checkable coverage verdict for this repository's real review configuration — the verdict
the `lint.ps1` coverage gate depends on. It demonstrates the covered case only: because the
repository's review-sets are complete in CI, the assertion does not exercise ReviewMark listing
the paths of uncovered files. That listing behavior was confirmed manually, by introducing a file
requiring review into a directory belonging to no review-set and observing the plan name that file
in its coverage section; that confirmation is a manual observation and is not captured as a
recorded test result. Because the assertion targets the success sentence rather than the failure text, a
genuine coverage gap or an unrecognized plan format fails this test rather than passing silently.

### Test Scenarios

#### ReviewMark_ReviewPlanGeneration

**Scenario**: ReviewMark self-validation uses `--definition` and `--plan` to generate a review plan
from a test configuration.

**Expected**: Exits 0 and produces a non-empty review plan markdown file.

**Requirement coverage**: `AgentKit-OTS-ReviewMark`.

#### ReviewMark_ReviewReportGeneration

**Scenario**: ReviewMark self-validation uses `--definition` and `--report` to generate a review
report from a test configuration and evidence store.

**Expected**: Exits 0 and produces a non-empty review report.

**Requirement coverage**: `AgentKit-OTS-ReviewMark`.

#### ReviewMark_IndexScan

**Scenario**: ReviewMark self-validation uses `--index` to scan PDF evidence files and write an
`index.json` catalogue.

**Expected**: Exits 0 and produces a correctly structured `index.json`.

**Requirement coverage**: `AgentKit-OTS-ReviewMark-IndexScan`.

#### ReviewMark_WorkingDirectoryOverride

**Scenario**: ReviewMark self-validation uses `--dir` to override the working directory for file
operations.

**Expected**: Exits 0 and resolves paths relative to the specified directory.

**Requirement coverage**: `AgentKit-OTS-ReviewMark-DirectoryOverride`.

#### ReviewMark_Enforce

**Scenario**: ReviewMark self-validation uses `--enforce` against a configuration with review
issues.

**Expected**: Exits with a non-zero exit code when review issues are present.

**Requirement coverage**: `AgentKit-OTS-ReviewMark-Enforce`.

#### ReviewMark_Elaborate

**Scenario**: ReviewMark self-validation uses `--elaborate` to print a Markdown elaboration of a
named review set.

**Expected**: Exits 0 and prints the review-set ID, fingerprint, and file list.

**Requirement coverage**: `AgentKit-OTS-ReviewMark-Elaborate`.

#### ReviewMark_Lint

**Scenario**: ReviewMark self-validation uses `--lint` to validate a definition file and report
issues.

**Expected**: Exits non-zero and outputs at least one reported issue identifying a structural or
semantic error in the test definition that contains known errors.

**Requirement coverage**: `AgentKit-OTS-ReviewMark-Lint`.

#### ReviewMark_PlanCoverageVerdict

**Scenario**: FileAssert inspects the review plan ReviewMark generated from this repository's
`.reviewmark.yaml` during the CI documentation build, checking for the coverage section and its
verdict.

**Expected**: The plan contains a coverage section stating that all files requiring review are
covered by a review-set. This demonstrates the covered case only; it does not demonstrate
ReviewMark listing the paths of uncovered files, which is confirmed manually rather than by this
test.

**Requirement coverage**: `AgentKit-OTS-ReviewMark-PlanCoverage`.

### Requirements Coverage

- **`AgentKit-OTS-ReviewMark`**: ReviewMark_ReviewPlanGeneration, ReviewMark_ReviewReportGeneration
- **`AgentKit-OTS-ReviewMark-IndexScan`**: ReviewMark_IndexScan
- **`AgentKit-OTS-ReviewMark-Enforce`**: ReviewMark_Enforce
- **`AgentKit-OTS-ReviewMark-Elaborate`**: ReviewMark_Elaborate
- **`AgentKit-OTS-ReviewMark-Lint`**: ReviewMark_Lint
- **`AgentKit-OTS-ReviewMark-DirectoryOverride`**: ReviewMark_WorkingDirectoryOverride
- **`AgentKit-OTS-ReviewMark-PlanCoverage`**: ReviewMark_PlanCoverageVerdict

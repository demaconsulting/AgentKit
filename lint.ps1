# lint.ps1
#
# PURPOSE:
#   Runs all lint checks and reports failures. Exits 1 on error.
#   Used by CI/CD as the merge gate and by the lint-fix agent
#   during pre-PR cleanup.
#
#   To auto-fix formatting issues, run fix.ps1 instead.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to find the designated locations
#   for adding project-specific lint checks.
#
# MODIFICATION POLICY:
#   Only modify this file to add project-specific operations at the designated
#   [PROJECT-SPECIFIC] extension points, or to update tool versions as needed.

# ==============================================================================
# HELPER FUNCTIONS
# ==============================================================================

function Get-VenvActivateScript {
    if (Test-Path ".venv/Scripts/Activate.ps1") { return ".venv/Scripts/Activate.ps1" }  # Windows
    if (Test-Path ".venv/bin/Activate.ps1") { return ".venv/bin/Activate.ps1" }          # Linux/macOS
    return $null
}

function Initialize-PythonVenv {
    if (-not (Test-Path ".venv")) {
        python -m venv .venv
        if ($LASTEXITCODE -ne 0) { return $false }
    }

    $activateScript = Get-VenvActivateScript
    if (-not $activateScript) { return $false }
    & $activateScript
    if (-not (Get-Command deactivate -ErrorAction SilentlyContinue)) { return $false }

    $installSucceeded = $false
    try {
        pip install -r pip-requirements.txt --quiet --disable-pip-version-check
        $installSucceeded = $LASTEXITCODE -eq 0
        return $installSucceeded
    }
    finally {
        if (-not $installSucceeded -and (Get-Command deactivate -ErrorAction SilentlyContinue)) {
            deactivate 2>$null
        }
    }
}

# ==============================================================================
# LINT CHECKS
# Runs all lint checks. Exits 1 if any check fails.
# ==============================================================================

$lintError = $false

# --- PYTHON SECTION ---
# Sets up a virtual environment and runs yamllint.
Write-Host "Linting: YAML..."
$skipPython = -not (Initialize-PythonVenv)
if ($skipPython) { $lintError = $true }

if (-not $skipPython) {
    yamllint .
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
    deactivate
}

# [PROJECT-SPECIFIC] Add additional Python-based lint checks here.
# Example:
#   if (-not $skipPython) {
#       flake8 src/
#       if ($LASTEXITCODE -ne 0) { $lintError = $true }
#   }

# --- NPM SECTION ---
# Installs npm dependencies and runs cspell and markdownlint-cli2.
Write-Host "Linting: spelling and markdown..."
$skipNpm = $false
$env:PUPPETEER_SKIP_DOWNLOAD = "true"
npm install --silent
if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipNpm = $true }

if (-not $skipNpm) {
    npx cspell --no-progress --no-color --quiet "**/*.{md,yaml,yml,json,cs,cpp,hpp,h,txt}"
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    npx markdownlint-cli2 "**/*.md"
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
}

# [PROJECT-SPECIFIC] Add additional npm-based lint checks here.
# Example (ESLint for TypeScript):
#   if (-not $skipNpm) {
#       npx eslint "src/**/*.ts"
#       if ($LASTEXITCODE -ne 0) { $lintError = $true }
#   }

# --- DOTNET LINTING SECTION ---
# Runs compliance tools: reqstream, versionmark, reviewmark, sysml2tools.
Write-Host "Linting: compliance tools..."
$skipDotnetTools = $false
dotnet tool restore > $null
if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipDotnetTools = $true }

if (-not $skipDotnetTools) {
    dotnet reqstream --lint --requirements requirements.yaml
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    dotnet versionmark --lint
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    dotnet reviewmark --lint
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    # 'reviewmark --lint' validates the configuration but does NOT detect files that require
    # review yet belong to no review-set. Only the review plan reports those, and it exits 0
    # whether or not gaps exist, so the plan must be written to a scratch file and inspected.
    # The success sentence is asserted rather than the failure text matched, so an unrecognized
    # plan format fails closed instead of passing silently. Capability requirement:
    # AgentKit-OTS-ReviewMark-PlanCoverage.
    $reviewPlan = Join-Path ([System.IO.Path]::GetTempPath()) "reviewmark-coverage-$PID.md"
    dotnet reviewmark --plan $reviewPlan > $null
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
    if (-not (Test-Path $reviewPlan)) {
        Write-Host "reviewmark: review plan was not produced; coverage could not be checked."
        $lintError = $true
    }
    elseif (-not (Select-String -Path $reviewPlan -Pattern 'All files requiring review are covered by a review-set' -Quiet)) {
        Write-Host "reviewmark: review-set coverage gap"
        Select-String -Path $reviewPlan -Pattern 'not covered by any review-set' -Context 0, 20 |
            ForEach-Object { $_.Line.Trim(); $_.Context.PostContext | Where-Object { $_ -match '^\s*-\s' } }
        $lintError = $true
    }
    Remove-Item $reviewPlan -ErrorAction SilentlyContinue

    if (Test-Path docs/sysml2) {
        dotnet sysml2tools lint 'docs/sysml2/**/*.sysml'
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    }
}

# [PROJECT-SPECIFIC] Add additional dotnet tool lint checks here.
# Example:
#   if (-not $skipDotnetTools) {
#       dotnet custom-tool --lint
#       if ($LASTEXITCODE -ne 0) { $lintError = $true }
#   }

# [PROJECT-SPECIFIC] Document-set completeness.
#
# Pandoc builds each document from the ordered 'input-files' list in its definition.yaml and
# reports nothing about a chapter the list omits: the omitted chapter is simply absent from the
# output. ReviewMark, ReqStream and 'sysml2tools lint' all read the chapter files directly and
# so pass while the shipped document is missing them. That is how the Todo, Memory and Agent
# families - three of seven, including every memory tool - plus ImagePromotingChatClient and
# TextFileCopyLinesTool were dropped from the Design and Verification documents and stayed
# dropped across two releases.
#
# Two directions are checked, because each catches an omission the other cannot:
#   (1) disk vs. definition - every non-generated chapter file under a document set is listed,
#       and every non-generated entry listed actually exists. Catches a chapter that was
#       written but never wired into the document, and an entry left behind after a rename.
#   (2) model vs. definition - every 'designRef' and 'verificationRef' artifact location
#       recorded in the SysML2 model names a file that exists AND is listed in that document's
#       definition.yaml. Catches a modelled unit whose chapter was never written at all, which
#       direction (1) cannot see.
# Generated chapters are exempt from the existence check because they are produced by earlier
# CI steps and are absent from a clean working tree.
Write-Host "Linting: document-set completeness..."

$definitionEntries = @{}   # 'docs/<set>' -> string[] of listed .md paths

foreach ($definition in Get-ChildItem docs -Recurse -Filter definition.yaml -ErrorAction SilentlyContinue) {
    $setRoot = $definition.Directory
    $setKey = "docs/$($setRoot.Name)"

    $listed = [regex]::Matches(
        (Get-Content $definition.FullName -Raw),
        '(?m)^\s*-\s+(\S+\.md)\s*$') | ForEach-Object { $_.Groups[1].Value }
    $definitionEntries[$setKey] = @($listed)

    $onDisk = Get-ChildItem $setRoot.FullName -Recurse -Filter *.md -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/]generated[\\/]' } |
        ForEach-Object { "$setKey/" + ($_.FullName.Substring($setRoot.FullName.Length + 1) -replace '\\', '/') }

    foreach ($chapter in $onDisk) {
        if ($listed -notcontains $chapter) {
            Write-Host "document-set: $chapter exists but is not listed in $setKey/definition.yaml"
            $lintError = $true
        }
    }

    foreach ($entry in $listed) {
        if ($entry -match '(^|/)generated/') { continue }
        if (-not (Test-Path $entry)) {
            Write-Host "document-set: $setKey/definition.yaml lists $entry, which does not exist"
            $lintError = $true
        }
    }
}

if (Test-Path docs/sysml2/model) {
    $modelRefs = Select-String -Path (Get-ChildItem docs/sysml2/model -Recurse -Filter *.sysml).FullName `
        -Pattern 'comment\s+(design|verification)Ref\s+/\*\s*(?:Design|Verification):\s*(\S+\.md)\s*\*/' -AllMatches |
        ForEach-Object {
            $source = $_.Path
            $_.Matches | ForEach-Object { [pscustomobject]@{ Source = $source; Path = $_.Groups[2].Value } }
        }

    foreach ($ref in $modelRefs) {
        $setKey = ($ref.Path -split '/')[0..1] -join '/'
        if (-not (Test-Path $ref.Path)) {
            Write-Host "document-set: the model references $($ref.Path), which does not exist"
            $lintError = $true
        }
        elseif ($definitionEntries.ContainsKey($setKey) -and $definitionEntries[$setKey] -notcontains $ref.Path) {
            Write-Host "document-set: the model references $($ref.Path), which $setKey/definition.yaml does not list"
            $lintError = $true
        }
    }
}

# [PROJECT-SPECIFIC] Tool names named in user-facing prose must exist.
#
# The shipped tool set and its family prefixes are read from the source constants rather than
# restated here, so this check cannot itself go stale. A backtick-quoted, lower-case token
# carrying a shipped family prefix is a claim that such a tool exists; if it is not in the
# shipped set, either the prose is stale or the tool was renamed.
#
# Scope is deliberately README.md and the user guide. Extending it to the design and
# verification documents was measured and rejected: those documents legitimately name tools
# that were considered and not built (for example 'memory_merge' in the memory subsystem
# design), so the check would report a correct document as an error.
#
# LIMIT, stated plainly: this does NOT catch prose that paraphrases the tool set in English.
# The defect that motivated it - the README describing the text-file family as "read, write,
# list" long after the surface became search/read/create/replace/cut/copy/paste - used no tool
# name at all and would still pass. No mechanical check was found for that; it remains a
# reviewer's responsibility.
Write-Host "Linting: tool names in user-facing prose..."

$toolSources = Get-ChildItem src -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]' }

if ($toolSources) {
    $shippedTools = Select-String -Path $toolSources.FullName -Pattern 'ToolName\s*=\s*"([a-z0-9_]+)"' -CaseSensitive |
        ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique
    $familyPrefixes = Select-String -Path $toolSources.FullName -Pattern 'FamilyPrefix\s*=\s*"([a-z0-9_]+)"' -CaseSensitive |
        ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique

    if ($shippedTools.Count -eq 0 -or $familyPrefixes.Count -eq 0) {
        # Fail closed: an empty ground truth would make every document pass vacuously.
        Write-Host "tool-names: no shipped tool names or family prefixes were found in src/"
        $lintError = $true
    }
    else {
        $prosePattern = '`(' + (($familyPrefixes | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')_[a-z0-9_]+`'
        $proseFiles = @('README.md') +
            (Get-ChildItem docs/user_guide -Recurse -Filter *.md -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '[\\/]generated[\\/]' } | ForEach-Object { $_.FullName })

        Select-String -Path ($proseFiles | Where-Object { Test-Path $_ }) -Pattern $prosePattern -CaseSensitive -AllMatches |
            ForEach-Object {
                $source = $_.Path
                $line = $_.LineNumber
                $_.Matches | ForEach-Object {
                    $token = $_.Value.Trim('`')
                    if ($shippedTools -notcontains $token) {
                        Write-Host "tool-names: ${source}:${line} names '$token', which the library does not ship"
                        $script:lintError = $true
                    }
                }
            }
    }
}

# --- DOTNET FORMATTING SECTION ---
# Verifies C# code formatting matches .editorconfig rules.
Write-Host "Linting: dotnet format..."
$skipDotnetFormat = $false
dotnet restore > $null
if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipDotnetFormat = $true }

if (-not $skipDotnetFormat) {
    dotnet format --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
}

# [PROJECT-SPECIFIC] Add additional format verification checks here.
# Example (clang-format check for C/C++):
#   Get-ChildItem -Recurse -Include "*.cpp","*.hpp","*.h" | ForEach-Object {
#       $result = clang-format --dry-run --Werror $_.FullName 2>&1
#       if ($LASTEXITCODE -ne 0) { Write-Output $result; $lintError = $true }
#   }

exit ($lintError ? 1 : 0)

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

# [PROJECT-SPECIFIC] Tool names named in user-facing prose must exist, and none may be missing.
#
# The shipped tool set and its family prefixes are read from the source constants rather than
# restated here, so this check cannot itself go stale. It runs in three directions:
#
#   1. Prose to source: a backtick-quoted, lower-case token carrying a shipped family prefix is a
#      claim that such a tool exists; if it is not in the shipped set, either the prose is stale
#      or the tool was renamed.
#   2. Source to prose: every shipped tool name must be named somewhere in the user guide, whose
#      'Available Tools' table is the one place that enumerates them. This catches a family that
#      shipped and was never written up - the todo, memory and agent families, nine tools, were
#      absent from all user-facing documentation while every other gate passed.
#   3. Counts: a spelled-out or numeric count preceding "tool families" must equal the number of
#      family-prefix constants. The documents said "four" long after the seventh family shipped.
#
# Scope is deliberately README.md and the user guide. Extending it to the design and
# verification documents was measured and rejected: those documents legitimately name tools
# that were considered and not built (for example 'memory_merge' in the memory subsystem
# design), so the check would report a correct document as an error.
#
# LIMIT, stated plainly: this does NOT catch prose that paraphrases what a tool set *does* in
# English. The defect that motivated direction 1 - the README describing the text-file family as
# "read, write, list" long after the surface became search/read/create/replace/cut/copy/paste -
# used no tool name and no family count, and would still pass. No mechanical check was found for
# that; it remains a reviewer's responsibility.
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

        # Reverse direction: every shipped tool must be named in the user guide.
        #
        # The check above catches prose naming a tool that was renamed or removed. It cannot see a
        # tool that shipped and was never written up - which is the defect that put the whole todo,
        # memory and agent surface, nine tools, outside the documentation while every gate passed.
        # The user guide's 'Available Tools' table is the one place that names every shipped tool,
        # so requiring each shipped name to appear there makes that table complete by construction
        # and removes the reader-facing tool list from the set of hand-maintained facts.
        $guideFiles = Get-ChildItem docs/user_guide -Recurse -Filter *.md -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '[\\/]generated[\\/]' } | ForEach-Object { $_.FullName }

        if ($guideFiles) {
            $guideText = (Get-Content -Path $guideFiles -Raw) -join "`n"
            foreach ($tool in $shippedTools) {
                if ($guideText -notmatch ('`' + [regex]::Escape($tool) + '`')) {
                    Write-Host "tool-names: docs/user_guide names no '$tool', which the library ships"
                    $lintError = $true
                }
            }
        }

        # Family counts written in prose must equal the number of shipped families.
        #
        # A count is the one fact about the tool surface that cannot be checked by looking for a
        # token, and it is the fact that went stale: the documents said "four" for months after the
        # seventh family shipped. Any spelled-out or numeric count immediately preceding
        # "tool families" in user-facing prose is compared against the family-prefix constants.
        $numberWords = @{
            'one' = 1; 'two' = 2; 'three' = 3; 'four' = 4; 'five' = 5; 'six' = 6;
            'seven' = 7; 'eight' = 8; 'nine' = 9; 'ten' = 10; 'eleven' = 11; 'twelve' = 12
        }
        $countPattern = '\b([A-Za-z]+|\d+)\s+(?:ready-made\s+)?(?:guarded\s+)?tool families\b'

        Select-String -Path ($proseFiles | Where-Object { Test-Path $_ }) -Pattern $countPattern -AllMatches |
            ForEach-Object {
                $source = $_.Path
                $line = $_.LineNumber
                $_.Matches | ForEach-Object {
                    $word = $_.Groups[1].Value
                    $claimed = $null
                    if ($word -match '^\d+$') { $claimed = [int]$word }
                    elseif ($numberWords.ContainsKey($word.ToLowerInvariant())) { $claimed = $numberWords[$word.ToLowerInvariant()] }

                    if ($null -ne $claimed -and $claimed -ne $familyPrefixes.Count) {
                        Write-Host "tool-names: ${source}:${line} claims $claimed tool families; the library ships $($familyPrefixes.Count)"
                        $script:lintError = $true
                    }
                }
            }
    }
}

# [PROJECT-SPECIFIC] Review-set hierarchy conformance.
#
# 'reviewmark --lint' validates that .reviewmark.yaml parses and 'reviewmark --plan' reports
# whether every file is covered by some review-set. Neither says anything about WHICH set a
# file belongs to, so a review-set may be scoped wrongly and still pass both: the
# AgentKitCore-AllRequirements set carried 'docs/reqstream/**/*.yaml' and fingerprinted 81
# files - every other system's requirements - instead of the 11 under its own system folder,
# and every gate was green the whole time.
#
# The systems are read from the top-level SysML2 model files rather than restated here, so a
# system added, renamed or removed is picked up automatically and this check cannot go stale.
# For each modelled system the four prescribed sets must exist with the prescribed titles and
# the prescribed scope, per the 'Review-Set Organization' section of reviewmark-usage.md.
#
# Path assertions are deliberately asymmetric, because the two failure modes differ:
#   - '-AllRequirements' is asserted to be EXACTLY the system's requirements glob. That set
#     exists to keep the requirements review bounded, so any extra entry is the defect.
#   - '-Architecture', '-Design' and '-Verification' are asserted to CONTAIN the prescribed
#     documents. Those sets legitimately carry extras - integration tests, shared test
#     helpers, the OTS overview - so requiring equality would report correct configuration
#     as an error.
# Context is asserted as containment for the same reason.
#
# LIMIT, stated plainly: this checks the system tier only. Subsystem and unit sets are not
# checked, because their legitimate contents are not derivable - subsystem sets deliberately
# carry shared helper sources that are not units (TextLines.cs, MemoryEmbedding.cs and
# others), and no rule distinguishes those from a unit source wrongly pulled up a level.
# Whether a subsystem set is scoped correctly remains a reviewer's responsibility.
Write-Host "Linting: review-set hierarchy..."

$reviewConfigPath = '.reviewmark.yaml'
if ((Test-Path $reviewConfigPath) -and (Test-Path docs/sysml2/model)) {
    $reviewSets = @()
    $currentSet = $null
    $currentList = $null

    foreach ($line in Get-Content $reviewConfigPath) {
        if ($line -match '^\s*#') { continue }
        if ($line -match '^  - id:\s*(\S+)\s*$') {
            if ($currentSet) { $reviewSets += $currentSet }
            $currentSet = [pscustomobject]@{ Id = $Matches[1]; Title = ''; Paths = @(); Context = @() }
            $currentList = $null
            continue
        }
        if (-not $currentSet) { continue }
        if ($line -match '^    title:\s*(.+?)\s*$') { $currentSet.Title = $Matches[1]; $currentList = $null; continue }
        if ($line -match '^    paths:\s*$') { $currentList = 'paths'; continue }
        if ($line -match '^    context:\s*$') { $currentList = 'context'; continue }
        if ($currentList -and $line -match '^      - "?([^"#]+?)"?\s*(#.*)?$') {
            if ($currentList -eq 'paths') { $currentSet.Paths += $Matches[1] } else { $currentSet.Context += $Matches[1] }
        }
    }
    if ($currentSet) { $reviewSets += $currentSet }

    $modelledSystems = Get-ChildItem docs/sysml2/model -File -Filter *.sysml -ErrorAction SilentlyContinue |
        ForEach-Object { $_.BaseName }

    if ($reviewSets.Count -eq 0 -or $modelledSystems.Count -eq 0) {
        # Fail closed: an empty review-set list or an empty system list would pass vacuously.
        Write-Host "review-sets: no review-sets or no modelled systems were found"
        $lintError = $true
    }
    else {
        function Test-ReviewSetMembers {
            param($Set, $Kind, [string[]]$Members, [string[]]$Required)
            foreach ($required in $Required) {
                if ($Members -notcontains $required) {
                    Write-Host "review-sets: $($Set.Id) $Kind omits '$required'"
                    $script:lintError = $true
                }
            }
        }

        $systemPrefixes = @()

        foreach ($kebab in $modelledSystems) {
            $pascal = ($kebab -split '-' | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) }) -join ''
            $systemPrefixes += $pascal

            $expected = @(
                @{ Suffix = 'Architecture'
                    Title = "Review that $pascal Architecture Satisfies Requirements"
                    Paths = @("docs/reqstream/$kebab.yaml", 'docs/design/introduction.md', "docs/design/$kebab.md",
                        'docs/verification/introduction.md', "docs/verification/$kebab.md")
                    Context = @('README.md', 'docs/user_guide/**/*.md')
                    Exact = $false
                }
                @{ Suffix = 'Design'
                    Title = "Review that $pascal Design is Consistent and Complete"
                    Paths = @('docs/design/introduction.md', "docs/design/$kebab.md", "docs/design/$kebab/**/*.md")
                    Context = @("docs/reqstream/$kebab.yaml")
                    Exact = $false
                }
                @{ Suffix = 'Verification'
                    Title = "Review that $pascal Verification is Consistent and Complete"
                    Paths = @('docs/verification/introduction.md', "docs/verification/$kebab.md",
                        "docs/verification/$kebab/**/*.md")
                    Context = @("docs/reqstream/$kebab.yaml")
                    Exact = $false
                }
                @{ Suffix = 'AllRequirements'
                    Title = "Review that All $pascal Requirements are Complete"
                    Paths = @("docs/reqstream/$kebab/**/*.yaml")
                    Context = @("docs/design/$kebab.md", "docs/reqstream/$kebab.yaml")
                    Exact = $true
                }
            )

            foreach ($spec in $expected) {
                $id = "$pascal-$($spec.Suffix)"
                $matching = @($reviewSets | Where-Object { $_.Id -eq $id })
                if ($matching.Count -ne 1) {
                    Write-Host "review-sets: the model defines system '$kebab' but .reviewmark.yaml has $($matching.Count) '$id' review-sets; expected exactly 1"
                    $lintError = $true
                    continue
                }

                $set = $matching[0]
                if ($set.Title -ne $spec.Title) {
                    Write-Host "review-sets: $id is titled '$($set.Title)'; the standard prescribes '$($spec.Title)'"
                    $lintError = $true
                }

                Test-ReviewSetMembers -Set $set -Kind 'paths' -Members $set.Paths -Required $spec.Paths
                Test-ReviewSetMembers -Set $set -Kind 'context' -Members $set.Context -Required $spec.Context

                if ($spec.Exact) {
                    foreach ($path in $set.Paths) {
                        if ($spec.Paths -notcontains $path) {
                            Write-Host "review-sets: $id includes '$path', which widens it beyond the system's own requirements"
                            $lintError = $true
                        }
                    }
                }
            }
        }

        # Repository-level sets: exactly one of each, and 'Purpose' scoped to user-facing prose only.
        foreach ($id in @('Purpose', 'Decomposition')) {
            $matching = @($reviewSets | Where-Object { $_.Id -eq $id })
            if ($matching.Count -ne 1) {
                Write-Host "review-sets: .reviewmark.yaml has $($matching.Count) '$id' review-sets; the standard allows exactly 1 per repository"
                $lintError = $true
            }
        }

        $purpose = @($reviewSets | Where-Object { $_.Id -eq 'Purpose' })
        if ($purpose.Count -eq 1) {
            $purposePaths = @('README.md', 'docs/user_guide/**/*.md')
            if (@(Compare-Object $purpose[0].Paths $purposePaths).Count -ne 0) {
                Write-Host "review-sets: Purpose is scoped to [$($purpose[0].Paths -join ', ')]; the standard prescribes README and the user guide only"
                $lintError = $true
            }
        }

        $decomposition = @($reviewSets | Where-Object { $_.Id -eq 'Decomposition' })
        if ($decomposition.Count -eq 1) {
            Test-ReviewSetMembers -Set $decomposition[0] -Kind 'paths' -Members $decomposition[0].Paths `
                -Required @('requirements.yaml', 'docs/design/introduction.md', 'docs/sysml2/**/*.sysml')
            Test-ReviewSetMembers -Set $decomposition[0] -Kind 'context' -Members $decomposition[0].Context `
                -Required @('README.md', 'docs/user_guide/**/*.md')
        }

        # A prefixed review-set whose prefix is not a modelled system, OTS or Shared item is
        # left over from a rename: it covers files nothing else claims, under a name that no
        # longer corresponds to anything in the tree.
        $knownPrefixes = $systemPrefixes + @('OTS', 'Shared')
        foreach ($set in $reviewSets) {
            if ($set.Id -notmatch '-') { continue }
            $prefix = ($set.Id -split '-')[0]
            if ($knownPrefixes -notcontains $prefix) {
                Write-Host "review-sets: $($set.Id) is prefixed '$prefix', which is not a modelled system, OTS or Shared item"
                $lintError = $true
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

<#
.SYNOPSIS
    Draws the README architecture diagram, docs/images/architecture.png, from docs/images/architecture.svg, or
    checks that it is current.

.DESCRIPTION
    architecture.svg is the README's package overview, kept beside the architecture.png it draws (matching base
    names). The PNG is drawn at twice the drawing's size over a transparent background, so the white card it
    draws reads well on light and dark pages. The diagram is drawn with "dotnet canvasnet" from the tool
    manifest; this script follows docs/images/render-hero.ps1.

    Without -Check it draws the diagram over docs/images/architecture.png. Commit the changed PNG with the change
    to the SVG that made it differ.

    With -Check it draws the diagram into a temporary folder and compares it with the committed PNG using
    "canvasnet compare ... --threshold rmse=1 --threshold diff-ratio=0.001", changes nothing in the repository,
    and exits 1 when the committed PNG is missing, has another size, or differs by more than the tolerance.

    Run it from anywhere; paths are taken from the repository the script is in.

.PARAMETER Check
    Compare a fresh drawing with the committed architecture.png instead of replacing it.
#>
[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$source = 'docs/images/architecture.svg'
$committed = 'docs/images/architecture.png'
$renderArgs = @('--strict', '--scale', '2', '--background', 'transparent')

Push-Location $repo
try {
    dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed with exit code $LASTEXITCODE"
    }

    if (-not $Check) {
        dotnet canvasnet render $source @renderArgs -o $committed
        if ($LASTEXITCODE -ne 0) {
            throw "canvasnet render failed with exit code $LASTEXITCODE"
        }
        exit 0
    }

    if (-not (Test-Path -LiteralPath $committed)) {
        Write-Host "FAIL  ${committed}: the committed image is missing (run docs/images/render-architecture.ps1)"
        exit 1
    }

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('render-architecture-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $fresh = Join-Path $work 'architecture.png'
    $failed = $false
    try {
        dotnet canvasnet render $source @renderArgs -o $fresh | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "canvasnet render failed with exit code $LASTEXITCODE"
        }

        dotnet canvasnet compare $committed $fresh --threshold 'rmse=1' --threshold 'diff-ratio=0.001' | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "ok    ${committed}: the committed architecture image is current."
        }
        else {
            $failed = $true
            Write-Host "FAIL  ${committed}: it differs from a fresh drawing of $source"
            Write-Host '      If the change is intended, run: pwsh docs/images/render-architecture.ps1 and commit the image.'
            Write-Host "      The fresh image is kept in $work"
        }
    }
    finally {
        if (-not $failed) {
            Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    exit ($failed ? 1 : 0)
}
finally {
    Pop-Location
}

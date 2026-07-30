[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$widgetDocs = Join-Path $repositoryRoot 'docs\widgets'
$errors = [System.Collections.Generic.List[string]]::new()

$widgets = Get-ChildItem -LiteralPath $repositoryRoot -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'manifest.json') }

if (-not $widgets) {
    $errors.Add('No widget directories were found.')
}

foreach ($widget in $widgets) {
    $manifestPath = Join-Path $widget.FullName 'manifest.json'
    try {
        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    } catch {
        $errors.Add("Invalid widget manifest: $manifestPath")
        continue
    }

    if ([string]::IsNullOrWhiteSpace($manifest.version)) {
        $errors.Add("Widget manifest has no version: $manifestPath")
    }

    $documentationPath = Join-Path $widgetDocs "$($widget.Name).md"
    if (-not (Test-Path -LiteralPath $documentationPath)) {
        $errors.Add("Missing widget documentation: $documentationPath")
    }
}

foreach ($requiredPath in @(
    (Join-Path $repositoryRoot 'README.md'),
    (Join-Path $repositoryRoot 'docs\installing-widgets.md'),
    (Join-Path $repositoryRoot 'docs\companion.md'),
    (Join-Path $repositoryRoot 'docs\releases.md')
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        $errors.Add("Missing release documentation: $requiredPath")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Release documentation is present for $($widgets.Count) widget(s)."

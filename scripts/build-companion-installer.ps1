param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $publishPath 'EdgeCompanion.Host.exe'))) {
    throw "The publish directory does not contain EdgeCompanion.Host.exe: $publishPath"
}

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Version must be a semantic version without a leading v: $Version"
}

$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

$compilerCandidates = @(
    $env:INNO_SETUP_COMPILER,
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

if (-not $compilerCandidates) {
    throw 'Inno Setup compiler was not found. Install Inno Setup 6 or set INNO_SETUP_COMPILER.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installerScript = Join-Path $repositoryRoot 'installer\edge-companion.iss'
$compiler = @($compilerCandidates)[0]

& $compiler `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishPath" `
    "/DOutputDir=$outputPath" `
    $installerScript |
    ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputPath "EdgeCompanion-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Expected installer was not created: $installerPath"
}

Write-Output $installerPath

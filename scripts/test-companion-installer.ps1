param(
    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,

    [string] $InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\XeneonEdgeWidgets\EdgeCompanion-SmokeTest')
)

$ErrorActionPreference = 'Stop'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$installPath = [System.IO.Path]::GetFullPath($InstallDirectory)
$protocolKey = 'HKCU:\Software\Classes\edgecompanion'
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$healthUrl = 'http://127.0.0.1:48620/api/v1/health'

function Invoke-Installer {
    $process = Start-Process -FilePath $installer -ArgumentList @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        "/DIR=$installPath"
    ) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Installer exited with code $($process.ExitCode)."
    }
}

function Wait-ForHealth([bool] $ExpectedHealthy) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
    do {
        $healthy = $false
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 1
            $healthy = $response.StatusCode -eq 200
        }
        catch {
            $healthy = $false
        }

        if ($healthy -eq $ExpectedHealthy) {
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Companion health did not become $ExpectedHealthy."
}

try {
    Invoke-Installer

    $executable = Join-Path $installPath 'EdgeCompanion.Host.exe'
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "Installed executable was not found: $executable"
    }

    $protocolCommand = (Get-ItemProperty -LiteralPath "$protocolKey\shell\open\command").'(default)'
    $expectedCommand = "`"$executable`" --start"
    if ($protocolCommand -ne $expectedCommand) {
        throw "Protocol command is incorrect. Expected '$expectedCommand', found '$protocolCommand'."
    }

    Start-Process -FilePath $executable -ArgumentList '--start' -WindowStyle Hidden
    Wait-ForHealth $true

    # Reinstall over a running copy to exercise the upgrade path.
    Invoke-Installer
    Start-Process -FilePath $executable -ArgumentList '--start' -WindowStyle Hidden
    Wait-ForHealth $true

    New-Item -Path $startupKey -Force | Out-Null
    New-ItemProperty -Path $startupKey -Name 'EdgeCompanion' -Value "`"$executable`" --start" -PropertyType String -Force | Out-Null

    & $executable --stop
    Wait-ForHealth $false

    $uninstaller = Join-Path $installPath 'unins000.exe'
    $uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART'
    ) -Wait -PassThru -WindowStyle Hidden
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($uninstallProcess.ExitCode)."
    }

    if (Test-Path -LiteralPath $protocolKey) {
        throw 'Protocol registration remained after uninstall.'
    }
    if ((Get-ItemProperty -Path $startupKey -Name 'EdgeCompanion' -ErrorAction SilentlyContinue).EdgeCompanion) {
        throw 'Windows startup registration remained after uninstall.'
    }
    if (Test-Path -LiteralPath $executable) {
        throw 'Companion executable remained after uninstall.'
    }

    Write-Output 'Companion installer smoke test passed.'
}
finally {
    try {
        $installedExecutable = Join-Path $installPath 'EdgeCompanion.Host.exe'
        if (Test-Path -LiteralPath $installedExecutable) {
            & $installedExecutable --stop
        }
    }
    catch {
        Write-Warning "Could not stop smoke-test companion: $($_.Exception.Message)"
    }
}

param(
    [string]$Version = "",
    [string]$IsccPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-ProjectVersion {
    param([string]$ProjectFilePath)

    $match = Select-String -Path $ProjectFilePath -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $match) {
        throw "Unable to read the version from the project file: $ProjectFilePath"
    }

    return $match.Matches[0].Groups[1].Value
}

function Resolve-Iscc {
    param([string]$RequestedPath)

    if ($RequestedPath) {
        if (-not (Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "Inno Setup compiler was not found: $RequestedPath"
        }

        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
        'C:\Program Files\Inno Setup 7\ISCC.exe'
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw "ISCC.exe was not found. Install Inno Setup 6 or 7, or pass -IsccPath C:\path\ISCC.exe."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repoRoot 'dotnet\src\StreamRecorder.WinForms\StreamRecorder.WinForms.csproj'
$packageScript = Join-Path $repoRoot 'scripts\package_winforms_release.ps1'
$issFile = Join-Path $repoRoot 'installer\StreamRecorder.iss'
$versionToUse = if ($Version) { $Version } else { Get-ProjectVersion -ProjectFilePath $projectFile }
$iscc = Resolve-Iscc -RequestedPath $IsccPath

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $packageScript -Version $versionToUse
if ($LASTEXITCODE -ne 0) {
    throw "Portable packaging failed. Exit code: $LASTEXITCODE"
}

$stageDir = Join-Path $repoRoot ("dotnet\target\release-package\StreamRecorder-v{0}-winforms-net48" -f $versionToUse)
$outputDir = Join-Path $repoRoot 'dotnet\target\release-package'
$legacyInstallerPath = Join-Path $outputDir ("StreamRecorder-v{0}-setup.exe" -f $versionToUse)
if (-not (Test-Path -LiteralPath (Join-Path $stageDir 'StreamRecorder.exe') -PathType Leaf)) {
    throw "The prepared portable package was not found: $stageDir"
}

Remove-Item -LiteralPath $legacyInstallerPath -Force -ErrorAction SilentlyContinue

& $iscc "/DVersion=$versionToUse" "/DStageDir=$stageDir" "/DOutputDir=$outputDir" $issFile
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed. Exit code: $LASTEXITCODE"
}

$installerPath = Join-Path $outputDir ("StreamRecorder-{0}-setup.exe" -f $versionToUse)
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "ISCC completed, but the installer was not found: $installerPath"
}

$stableInstallerPath = Join-Path $outputDir 'StreamRecorder-setup.exe'
Copy-Item -LiteralPath $installerPath -Destination $stableInstallerPath -Force

Write-Output $installerPath
Write-Output $stableInstallerPath

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
        throw "Nie można odczytać wersji z pliku projektu: $ProjectFilePath"
    }

    return $match.Matches[0].Groups[1].Value
}

function Resolve-Iscc {
    param([string]$RequestedPath)

    if ($RequestedPath) {
        if (-not (Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "Nie znaleziono kompilatora Inno Setup: $RequestedPath"
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

    throw "Nie znaleziono ISCC.exe. Zainstaluj Inno Setup 6 lub 7 albo przekaż -IsccPath C:\sciezka\ISCC.exe."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repoRoot 'dotnet\src\StreamRecorder.WinForms\StreamRecorder.WinForms.csproj'
$packageScript = Join-Path $repoRoot 'scripts\package_winforms_release.ps1'
$issFile = Join-Path $repoRoot 'installer\StreamRecorder.iss'
$versionToUse = if ($Version) { $Version } else { Get-ProjectVersion -ProjectFilePath $projectFile }
$iscc = Resolve-Iscc -RequestedPath $IsccPath

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $packageScript -Version $versionToUse
if ($LASTEXITCODE -ne 0) {
    throw "Pakowanie portable nie powiodło się. Kod: $LASTEXITCODE"
}

$stageDir = Join-Path $repoRoot ("dotnet\target\release-package\StreamRecorder-v{0}-winforms-net48" -f $versionToUse)
$outputDir = Join-Path $repoRoot 'dotnet\target\release-package'
$legacyInstallerPath = Join-Path $outputDir ("StreamRecorder-v{0}-setup.exe" -f $versionToUse)
if (-not (Test-Path -LiteralPath (Join-Path $stageDir 'StreamRecorder.exe') -PathType Leaf)) {
    throw "Nie znaleziono gotowej paczki portable: $stageDir"
}

Remove-Item -LiteralPath $legacyInstallerPath -Force -ErrorAction SilentlyContinue

& $iscc "/DVersion=$versionToUse" "/DStageDir=$stageDir" "/DOutputDir=$outputDir" $issFile
if ($LASTEXITCODE -ne 0) {
    throw "Kompilacja instalatora nie powiodła się. Kod: $LASTEXITCODE"
}

$installerPath = Join-Path $outputDir ("StreamRecorder-{0}-setup.exe" -f $versionToUse)
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "ISCC zakończył pracę, ale nie znaleziono instalatora: $installerPath"
}

$stableInstallerPath = Join-Path $outputDir 'StreamRecorder-setup.exe'
Copy-Item -LiteralPath $installerPath -Destination $stableInstallerPath -Force

Write-Output $installerPath
Write-Output $stableInstallerPath

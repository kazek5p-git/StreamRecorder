param(
    [string]$Version,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-ProjectVersion {
    param([string]$CargoTomlPath)

    $match = Select-String -Path $CargoTomlPath -Pattern '^version = "([^"]+)"$' | Select-Object -First 1
    if (-not $match) {
        throw "Unable to determine project version from $CargoTomlPath"
    }

    return $match.Matches[0].Groups[1].Value
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$cargoToml = Join-Path $repoRoot 'Cargo.toml'
$versionToUse = if ($Version) { $Version } else { Get-ProjectVersion -CargoTomlPath $cargoToml }

if (-not $SkipBuild) {
    Push-Location $repoRoot
    try {
        cargo build --release
    }
    finally {
        Pop-Location
    }
}

$releaseDir = Join-Path $repoRoot 'target\x86_64-pc-windows-msvc\release'
$packageRoot = Join-Path $repoRoot 'target\release-package'
$stageDir = Join-Path $packageRoot ("StreamRecorder-v{0}-portable-win64" -f $versionToUse)
$zipPath = "$stageDir.zip"

foreach ($required in @(
    (Join-Path $releaseDir 'streamrecorder.exe'),
    (Join-Path $releaseDir 'streamrecorder_guard.exe'),
    (Join-Path $repoRoot 'README.md')
)) {
    if (-not (Test-Path $required)) {
        throw "Required file not found: $required"
    }
}

Remove-Item $stageDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $stageDir | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageDir 'locale') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageDir 'Config') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageDir 'My recordings') | Out-Null

Copy-Item (Join-Path $releaseDir 'streamrecorder.exe') $stageDir
Copy-Item (Join-Path $releaseDir 'streamrecorder_guard.exe') $stageDir
Copy-Item (Join-Path $repoRoot 'locale\*') (Join-Path $stageDir 'locale') -Recurse -Force

$markdown = Get-Content (Join-Path $repoRoot 'README.md') -Raw
$htmlBody = ($markdown | ConvertFrom-Markdown).Html
$htmlDocument = @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>StreamRecorder README</title>
  <style>
    :root {
      color-scheme: light;
      font-family: "Segoe UI", sans-serif;
      line-height: 1.55;
    }
    body {
      margin: 0;
      background: #f3f5f7;
      color: #1d2733;
    }
    main {
      max-width: 900px;
      margin: 0 auto;
      padding: 32px 24px 48px;
      background: #ffffff;
      box-shadow: 0 12px 30px rgba(15, 23, 42, 0.08);
    }
    code {
      background: #eef2f7;
      padding: 0.1em 0.35em;
      border-radius: 4px;
    }
    pre code {
      display: block;
      padding: 16px;
      overflow-x: auto;
    }
    a {
      color: #005fb8;
    }
  </style>
</head>
<body>
  <main>
$htmlBody
  </main>
</body>
</html>
"@

Set-Content -Path (Join-Path $stageDir 'README.html') -Value $htmlDocument -Encoding UTF8
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -Force

Write-Output $zipPath

param(
    [string]$Version,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-ProjectVersion {
    param([string]$ProjectFilePath)

    $match = Select-String -Path $ProjectFilePath -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $match) {
        throw "Unable to determine project version from $ProjectFilePath"
    }

    return $match.Matches[0].Groups[1].Value
}

function Convert-MarkdownToHtml {
    param([string]$Markdown)

    if (Get-Command ConvertFrom-Markdown -ErrorAction SilentlyContinue) {
        return ($Markdown | ConvertFrom-Markdown).Html
    }

    if (Get-Command pwsh -ErrorAction SilentlyContinue) {
        $tempMarkdown = Join-Path $env:TEMP ("streamrecorder_winforms_readme_" + [Guid]::NewGuid().ToString("N") + ".md")
        try {
            Set-Content -LiteralPath $tempMarkdown -Value $Markdown -Encoding UTF8
            return pwsh -NoProfile -Command "Get-Content -LiteralPath '$tempMarkdown' -Raw | ConvertFrom-Markdown | Select-Object -ExpandProperty Html"
        }
        finally {
            Remove-Item -LiteralPath $tempMarkdown -Force -ErrorAction SilentlyContinue
        }
    }

    $escaped = [System.Net.WebUtility]::HtmlEncode($Markdown)
    return "<pre>$escaped</pre>"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repoRoot 'dotnet\src\StreamRecorder.WinForms\StreamRecorder.WinForms.csproj'
$readmePath = Join-Path $repoRoot 'dotnet\README.md'
$thirdPartyDir = Join-Path $repoRoot 'third_party'
$gpacSourceDir = Join-Path $thirdPartyDir 'GPAC'
$thirdPartyNoticesPath = Join-Path $thirdPartyDir 'THIRD-PARTY-NOTICES.txt'
$versionToUse = if ($Version) { $Version } else { Get-ProjectVersion -ProjectFilePath $projectFile }
$buildDir = Join-Path $repoRoot 'dotnet\src\StreamRecorder.WinForms\bin\Release\net48'
$packageRoot = Join-Path $repoRoot 'dotnet\target\release-package'
$stageDir = Join-Path $packageRoot ("StreamRecorder-v{0}-winforms-net48" -f $versionToUse)
$zipPath = "$stageDir.zip"

if (-not $SkipBuild) {
    Push-Location $repoRoot
    try {
        dotnet build $projectFile -c Release -p:Version=$versionToUse
    }
    finally {
        Pop-Location
    }
}

foreach ($required in @(
    (Join-Path $buildDir 'StreamRecorder.exe'),
    (Join-Path $buildDir 'StreamRecorder.exe.config'),
    (Join-Path $buildDir 'StreamRecorder.Core.dll'),
    (Join-Path $buildDir 'Tomlyn.dll'),
    (Join-Path $buildDir 'locales'),
    $readmePath
)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required file not found: $required"
    }
}

Remove-Item -LiteralPath $stageDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $stageDir | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageDir 'Config') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageDir 'My recordings') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageDir 'locales') | Out-Null

Get-ChildItem -LiteralPath $buildDir -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stageDir $_.Name) -Force
}

Copy-Item -Path (Join-Path $buildDir 'locales\*') -Destination (Join-Path $stageDir 'locales') -Recurse -Force

if (Test-Path -LiteralPath $gpacSourceDir) {
    $mp4BoxPath = Join-Path $gpacSourceDir 'MP4Box.exe'
    if (-not (Test-Path -LiteralPath $mp4BoxPath)) {
        throw "third_party\GPAC exists, but MP4Box.exe was not found: $mp4BoxPath"
    }

    $gpacTargetDir = Join-Path $stageDir 'Tools\GPAC'
    New-Item -ItemType Directory -Path $gpacTargetDir -Force | Out-Null
    Copy-Item -Path (Join-Path $gpacSourceDir '*') -Destination $gpacTargetDir -Recurse -Force

    if (Test-Path -LiteralPath $thirdPartyNoticesPath) {
        Copy-Item -LiteralPath $thirdPartyNoticesPath -Destination (Join-Path $stageDir 'THIRD-PARTY-NOTICES.txt') -Force
    }
}

$markdown = Get-Content -LiteralPath $readmePath -Raw
$htmlBody = Convert-MarkdownToHtml -Markdown $markdown
$htmlDocument = @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>StreamRecorder WinForms README</title>
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

Set-Content -LiteralPath (Join-Path $stageDir 'README.html') -Value $htmlDocument -Encoding UTF8
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -Force

Write-Output $zipPath

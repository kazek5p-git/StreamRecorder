param(
    [string]$Version,
    [string]$Runtime = "win-x64",
    [switch]$SkipPublish
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
$versionToUse = if ($Version) { $Version } else { Get-ProjectVersion -ProjectFilePath $projectFile }
$publishDir = Join-Path $repoRoot ("dotnet\publish\framework-dependent\{0}" -f $Runtime)
$packageRoot = Join-Path $repoRoot 'dotnet\target\release-package'
$stageDir = Join-Path $packageRoot ("StreamRecorder-v{0}-winforms-framework-dependent-{1}" -f $versionToUse, $Runtime)
$zipPath = "$stageDir.zip"

if (-not $SkipPublish) {
    Push-Location $repoRoot
    try {
        dotnet publish $projectFile -c Release -r $Runtime --self-contained false -p:Version=$versionToUse -o $publishDir
    }
    finally {
        Pop-Location
    }
}

foreach ($required in @(
    (Join-Path $publishDir 'StreamRecorder.exe'),
    (Join-Path $publishDir 'StreamRecorder.dll'),
    (Join-Path $publishDir 'StreamRecorder.deps.json'),
    (Join-Path $publishDir 'StreamRecorder.runtimeconfig.json'),
    (Join-Path $publishDir 'StreamRecorder.Core.dll'),
    (Join-Path $publishDir 'Tomlyn.dll'),
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

Get-ChildItem -LiteralPath $publishDir -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stageDir $_.Name) -Force
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

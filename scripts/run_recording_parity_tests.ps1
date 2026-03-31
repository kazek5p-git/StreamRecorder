param(
    [switch]$SkipBuild,
    [int]$DurationSeconds = 8,
    [int]$StartupTimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$runnerProject = Join-Path $repoRoot 'dotnet\tools\StreamRecorder.ParityRunner\StreamRecorder.ParityRunner.csproj'
$resultsRoot = Join-Path $repoRoot 'dotnet\target\parity-recording'
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$sessionRoot = Join-Path $resultsRoot $timestamp
$jsonRoot = Join-Path $sessionRoot 'json'

$cases = @(
    @{ Name = 'MP3 Slotex'; Url = 'http://s1.slotex.pl:7424/stream/1/'; ExpectedFormat = 'Mp3' },
    @{ Name = 'MP3 Radio Park'; Url = 'http://sluchaj.radiopark.com.pl:8055/fest'; ExpectedFormat = 'Mp3' },
    @{ Name = 'AAC Slotex'; Url = 'http://s1.slotex.pl:7298/;'; ExpectedFormat = 'AacRaw' },
    @{ Name = 'AAC Listen2MyRadio'; Url = 'http://uk20freenew.listen2myradio.com:13545/stream'; ExpectedFormat = 'AacRaw' },
    @{ Name = 'HLS AAC NORDA'; Url = 'http://ls.tkchopin.pl/norda/nordafm_aac_128/playlist.m3u8'; ExpectedFormat = 'AacRaw' },
    @{ Name = 'HLS MP3 NORDA'; Url = 'http://ls.tkchopin.pl/norda/nordafm_mp3_128.stream/playlist.m3u8'; ExpectedFormat = 'Mp3' },
    @{ Name = 'HTTPS MP3 Playground'; Url = 'https://playground.praa.sk/listen/playground/stream.mp3'; ExpectedFormat = 'Mp3' }
)

New-Item -ItemType Directory -Path $jsonRoot -Force | Out-Null

if (-not $SkipBuild) {
    Push-Location $repoRoot
    try {
        dotnet build $runnerProject -c Release
    }
    finally {
        Pop-Location
    }
}

$results = New-Object System.Collections.Generic.List[object]

foreach ($case in $cases) {
    $slug = ($case.Name -replace '[^A-Za-z0-9]+', '_').Trim('_')
    $caseRoot = Join-Path $sessionRoot $slug
    $jsonPath = Join-Path $jsonRoot ($slug + '.json')

    Write-Host "[case] $($case.Name)"

    $output = dotnet run --project $runnerProject -c Release --no-build -- `
        --name $case.Name `
        --url $case.Url `
        --expected-format $case.ExpectedFormat `
        --duration-seconds $DurationSeconds `
        --startup-timeout-seconds $StartupTimeoutSeconds `
        --output-root $caseRoot 2>&1

    $jsonText = $output | Select-Object -Last 1
    if (-not $jsonText) {
        throw "Runner produced no JSON output for case '$($case.Name)'."
    }

    Set-Content -LiteralPath $jsonPath -Value $jsonText -Encoding UTF8
    $result = $jsonText | ConvertFrom-Json
    $results.Add($result)

    $status = if ($result.Pass) { 'PASS' } else { 'FAIL' }
    $format = if ($result.FinalSnapshot.Format) { $result.FinalSnapshot.Format } else { '-' }
    $state = if ($result.FinalSnapshot.StateLabel) { $result.FinalSnapshot.StateLabel } else { '-' }
    $bytes = if ($result.FinalSnapshot.BytesWritten) { [int64]$result.FinalSnapshot.BytesWritten } else { 0 }
    Write-Host ("      {0} format={1} state={2} bytes={3}" -f $status, $format, $state, $bytes)
}

$summary = [pscustomobject]@{
    Timestamp = $timestamp
    DurationSeconds = $DurationSeconds
    StartupTimeoutSeconds = $StartupTimeoutSeconds
    Cases = $results
}

$summaryPath = Join-Path $sessionRoot 'summary.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$passCount = ($results | Where-Object { $_.Pass }).Count
$failCount = $results.Count - $passCount

Write-Host ''
Write-Host ("[summary] pass={0} fail={1}" -f $passCount, $failCount)
Write-Host ("[summary] results={0}" -f $summaryPath)

if ($failCount -gt 0) {
    exit 1
}

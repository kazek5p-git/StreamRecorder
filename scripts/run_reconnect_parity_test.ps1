param(
    [switch]$SkipBuild,
    [int]$DurationSeconds = 10,
    [int]$StartupTimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$runnerProject = Join-Path $repoRoot 'dotnet\tools\StreamRecorder.ParityRunner\StreamRecorder.ParityRunner.csproj'
$resultsRoot = Join-Path $repoRoot 'dotnet\target\parity-reconnect'
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$sessionRoot = Join-Path $resultsRoot $timestamp
New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null

if (-not $SkipBuild) {
    Push-Location $repoRoot
    try {
        dotnet build $runnerProject -c Release
    }
    finally {
        Pop-Location
    }
}

$probeListener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$probeListener.Start()
$port = ([System.Net.IPEndPoint]$probeListener.LocalEndpoint).Port
$probeListener.Stop()

$readyFile = Join-Path $sessionRoot 'server.ready'
$serverLog = Join-Path $sessionRoot 'server.log'

$serverJob = Start-Job -ArgumentList $port, $readyFile, $serverLog -ScriptBlock {
    param($Port, $ReadyFile, $ServerLog)

    $ErrorActionPreference = 'Stop'
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    $listener.Start()
    Set-Content -LiteralPath $ReadyFile -Value 'ready' -Encoding ASCII

    $connectionCount = 0
    try {
        while ($true) {
            $client = $listener.AcceptTcpClient()
            $connectionCount++
            Add-Content -LiteralPath $ServerLog -Value ("connection=" + $connectionCount) -Encoding UTF8

            try {
                $stream = $client.GetStream()
                $stream.ReadTimeout = 1000
                $requestBuffer = New-Object byte[] 2048
                try {
                    while ($stream.DataAvailable -or -not $stream.CanRead) {
                        break
                    }
                    [void]$stream.Read($requestBuffer, 0, $requestBuffer.Length)
                }
                catch {
                }

                $headers = [System.Text.Encoding]::ASCII.GetBytes(
                    "HTTP/1.1 200 OK`r`n" +
                    "Content-Type: audio/mpeg`r`n" +
                    "Cache-Control: no-cache`r`n" +
                    "Connection: close`r`n`r`n")
                $stream.Write($headers, 0, $headers.Length)

                $id3 = [byte[]](0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3F)
                $stream.Write($id3, 0, $id3.Length)

                $chunk = New-Object byte[] 4096
                for ($i = 0; $i -lt $chunk.Length; $i++) {
                    $chunk[$i] = [byte](0x20 + ($i % 90))
                }

                for ($part = 0; $part -lt 10; $part++) {
                    $stream.Write($chunk, 0, $chunk.Length)
                    $stream.Flush()
                    Start-Sleep -Milliseconds 120
                }
            }
            finally {
                $client.Close()
            }
        }
    }
    finally {
        $listener.Stop()
    }
}

try {
    $ready = $false
    for ($i = 0; $i -lt 50; $i++) {
        if (Test-Path -LiteralPath $readyFile) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 100
    }

    if (-not $ready) {
        throw 'Reconnect test server did not start in time.'
    }

    $url = "http://127.0.0.1:$port/stream.mp3"
    Write-Host "[case] Flaky local MP3 reconnect"
    $output = dotnet run --project $runnerProject -c Release --no-build -- `
        --name 'Flaky local MP3 reconnect' `
        --url $url `
        --expected-format Mp3 `
        --duration-seconds $DurationSeconds `
        --startup-timeout-seconds $StartupTimeoutSeconds `
        --output-root (Join-Path $sessionRoot 'runner') 2>&1

    $jsonText = $output | Select-Object -Last 1
    if (-not $jsonText) {
        throw 'Runner produced no JSON output.'
    }

    $jsonPath = Join-Path $sessionRoot 'result.json'
    Set-Content -LiteralPath $jsonPath -Value $jsonText -Encoding UTF8
    $result = $jsonText | ConvertFrom-Json

    $serverConnections = if (Test-Path -LiteralPath $serverLog) { (Get-Content -LiteralPath $serverLog).Count } else { 0 }
    $reconnectCount = if ($result.FinalSnapshot.ReconnectCount) { [int]$result.FinalSnapshot.ReconnectCount } else { 0 }
    $pass = $result.Pass -and $reconnectCount -ge 1 -and $serverConnections -ge 2

    Write-Host ("      runnerPass={0} reconnects={1} serverConnections={2} state={3} bytes={4}" -f `
        $result.Pass, `
        $reconnectCount, `
        $serverConnections, `
        $result.FinalSnapshot.StateLabel, `
        $result.FinalSnapshot.BytesWritten)

    $summary = [pscustomobject]@{
        Url = $url
        RunnerResult = $result
        ServerConnections = $serverConnections
        Pass = $pass
    }

    $summaryPath = Join-Path $sessionRoot 'summary.json'
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

    Write-Host ("[summary] results={0}" -f $summaryPath)

    if (-not $pass) {
        exit 1
    }
}
finally {
    Stop-Job -Job $serverJob -ErrorAction SilentlyContinue | Out-Null
    Receive-Job -Job $serverJob -ErrorAction SilentlyContinue | Out-Null
    Remove-Job -Job $serverJob -Force -ErrorAction SilentlyContinue | Out-Null
}

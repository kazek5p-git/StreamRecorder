using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamRecorder.Core.Compatibility;
using StreamRecorder.Core.Configuration;

namespace StreamRecorder.Core.Updates;

public sealed class UpdaterService
{
    private readonly HttpClient httpClient;

    public UpdaterService(string currentVersion)
    {
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"StreamRecorder/{currentVersion}");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(
        string currentVersion,
        string repository = AppDefaults.DefaultUpdateRepository,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return null;
        }

        var url = $"https://api.github.com/repos/{repository.Trim().Trim('/')}/releases/latest";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
        if (release is null || release.Draft || release.Prerelease)
        {
            return null;
        }

        var latest = release.TagName.TrimStart('v');
        var current = currentVersion.TrimStart('v');
        if (CompareVersions(latest, current) <= 0)
        {
            return null;
        }

        return new UpdateInfo
        {
            Version = release.TagName,
            HtmlUrl = release.HtmlUrl,
            Asset = ChoosePreferredAsset(release.Assets),
        };
    }

    public static int CompareVersions(string left, string right)
    {
        var leftParts = ParsedVersion.Parse(left);
        var rightParts = ParsedVersion.Parse(right);

        var maxLength = Math.Max(leftParts.NumericParts.Count, rightParts.NumericParts.Count);
        for (var index = 0; index < maxLength; index++)
        {
            var leftPart = index < leftParts.NumericParts.Count ? leftParts.NumericParts[index] : 0;
            var rightPart = index < rightParts.NumericParts.Count ? rightParts.NumericParts[index] : 0;
            var numericComparison = leftPart.CompareTo(rightPart);
            if (numericComparison != 0)
            {
                return numericComparison;
            }
        }

        if (leftParts.HasSuffix != rightParts.HasSuffix)
        {
            return leftParts.HasSuffix ? -1 : 1;
        }

        return string.Compare(leftParts.Suffix, rightParts.Suffix, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> DownloadUpdateAsync(AppPaths paths, UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var asset = update.Asset ?? throw new InvalidOperationException("No downloadable asset was found for the release.");
        var updatesDirectory = Path.Combine(paths.ConfigDirectory, "updates");
        Directory.CreateDirectory(updatesDirectory);

        var destination = Path.Combine(updatesDirectory, asset.Name);
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        using var response = await httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);
        return destination;
    }

    public Task InstallDownloadedUpdateAsync(
        AppPaths paths,
        string downloadedAssetPath,
        UpdateAsset asset,
        string restartExecutable,
        IReadOnlyList<string> restartArguments)
    {
        Directory.CreateDirectory(Path.Combine(paths.ConfigDirectory, "updates"));
        var scriptPath = Path.Combine(paths.ConfigDirectory, "updates", "apply_update.ps1");
        var script = asset.Kind == UpdateAssetKind.Zip
            ? BuildZipScript(paths, downloadedAssetPath, restartExecutable, restartArguments)
            : BuildInstallerScript(downloadedAssetPath);

        File.WriteAllText(scriptPath, script, Encoding.UTF8);
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = paths.RootDirectory,
        });

        if (process is null)
        {
            throw new InvalidOperationException("Failed to start the update installer script.");
        }

        return Task.CompletedTask;
    }

    private static UpdateAsset? ChoosePreferredAsset(IReadOnlyList<GitHubAsset>? assets)
    {
        return assets?
            .Select(MapAsset)
            .Where(static asset => asset is not null)
            .Cast<UpdateAsset>()
            .OrderByDescending(GetAssetScore)
            .FirstOrDefault();
    }

    private static UpdateAsset? MapAsset(GitHubAsset asset)
    {
        var lower = asset.Name.ToLowerInvariant();
        var kind = lower.EndsWith(".zip", StringComparison.Ordinal)
            ? UpdateAssetKind.Zip
            : lower.EndsWith(".exe", StringComparison.Ordinal)
                ? UpdateAssetKind.Exe
                : lower.EndsWith(".msi", StringComparison.Ordinal)
                    ? UpdateAssetKind.Msi
                    : (UpdateAssetKind?)null;

        return kind is null
            ? null
            : new UpdateAsset
            {
                Name = asset.Name,
                DownloadUrl = asset.BrowserDownloadUrl,
                Size = asset.Size,
                Kind = kind.Value,
            };
    }

    private static int GetAssetScore(UpdateAsset asset)
    {
        var lower = asset.Name.ToLowerInvariant();
        var score = asset.Kind switch
        {
            UpdateAssetKind.Zip => 300,
            UpdateAssetKind.Exe => 200,
            UpdateAssetKind.Msi => 180,
            _ => 0,
        };

        if (lower.Contains("portable", StringComparison.Ordinal))
        {
            score += 120;
        }
        if (lower.Contains("windows", StringComparison.Ordinal) || lower.Contains("win", StringComparison.Ordinal))
        {
            score += 90;
        }
        if (lower.Contains("x86_64", StringComparison.Ordinal)
            || lower.Contains("amd64", StringComparison.Ordinal)
            || lower.Contains("win64", StringComparison.Ordinal)
            || lower.Contains("64", StringComparison.Ordinal))
        {
            score += 70;
        }
        if (lower.Contains("setup", StringComparison.Ordinal) || lower.Contains("installer", StringComparison.Ordinal))
        {
            score -= 30;
        }

        return score;
    }

    private static string BuildZipScript(AppPaths paths, string downloadedAssetPath, string restartExecutable, IReadOnlyList<string> restartArguments)
    {
        var restartArgs = restartArguments.Count == 0
            ? "@()"
            : $"@({string.Join(", ", restartArguments.Select(static arg => $"'{EscapePowerShell(arg)}'"))})";

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            """
            $ErrorActionPreference = 'Stop'
            $archivePath = '{0}'
            $appRoot = '{1}'
            $restartExe = '{2}'
            $restartArgs = {3}
            $scriptPath = $MyInvocation.MyCommand.Path
            $logPath = Join-Path ([System.IO.Path]::GetDirectoryName($scriptPath)) 'apply_update.log'
            function Write-UpdateLog([string]$Message) {{
              $timestamp = Get-Date -Format o
              Add-Content -LiteralPath $logPath -Value ("[$timestamp] $Message") -Encoding UTF8
            }}
            function Wait-ForUnlockedFile([string]$Path, [int]$Attempts = 60) {{
              if (-not (Test-Path -LiteralPath $Path)) {{ return }}
              for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {{
                try {{
                  $stream = [System.IO.File]::Open($Path, 'Open', 'ReadWrite', 'None')
                  $stream.Close()
                  return
                }} catch {{
                  Start-Sleep -Milliseconds 500
                }}
              }}
              throw "File is still locked: $Path"
            }}
            function Copy-FileWithRetry([string]$Source, [string]$Destination, [int]$Attempts = 60) {{
              $destinationDir = Split-Path -Parent $Destination
              if ($destinationDir) {{ New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null }}
              $tempDestination = "$Destination.new"
              $lastError = $null
              for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {{
                try {{
                  Remove-Item -LiteralPath $tempDestination -Force -ErrorAction SilentlyContinue
                  Copy-Item -LiteralPath $Source -Destination $tempDestination -Force
                  Wait-ForUnlockedFile -Path $Destination -Attempts 1
                  if (Test-Path -LiteralPath $Destination) {{
                    Remove-Item -LiteralPath $Destination -Force
                  }}
                  Move-Item -LiteralPath $tempDestination -Destination $Destination -Force
                  return
                }} catch {{
                  $lastError = $_
                  Start-Sleep -Milliseconds 500
                }}
              }}
              throw $lastError
            }}
            function Copy-DirectoryContents([string]$SourceDir, [string]$DestinationDir) {{
              New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
              Get-ChildItem -LiteralPath $SourceDir -Force | ForEach-Object {{
                $target = Join-Path $DestinationDir $_.Name
                if ($_.PSIsContainer) {{
                  Copy-DirectoryContents -SourceDir $_.FullName -DestinationDir $target
                }} else {{
                  Copy-FileWithRetry -Source $_.FullName -Destination $target
                }}
              }}
            }}
            Start-Sleep -Seconds 2
            $extractRoot = Join-Path ([System.IO.Path]::GetDirectoryName($archivePath)) ('extract_' + [Guid]::NewGuid().ToString('N'))
            try {{
              Write-UpdateLog "Applying update from $archivePath to $appRoot"
              Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
              $sourceRoot = $extractRoot
              $children = Get-ChildItem -LiteralPath $extractRoot -Force
              if ($children.Count -eq 1 -and $children[0].PSIsContainer) {{ $sourceRoot = $children[0].FullName }}
              Get-ChildItem -LiteralPath $sourceRoot -Force | ForEach-Object {{
                if ($_.Name -eq 'Config') {{
                  $targetConfig = Join-Path $appRoot 'Config'
                  New-Item -ItemType Directory -Path $targetConfig -Force | Out-Null
                  Get-ChildItem -LiteralPath $_.FullName -Force | Where-Object {{ $_.Name -notin @('app.toml', 'streamrecorder.log') }} | ForEach-Object {{
                    if ($_.PSIsContainer) {{
                      Copy-DirectoryContents -SourceDir $_.FullName -DestinationDir (Join-Path $targetConfig $_.Name)
                    }} else {{
                      Copy-FileWithRetry -Source $_.FullName -Destination (Join-Path $targetConfig $_.Name)
                    }}
                  }}
                }} elseif ($_.PSIsContainer) {{
                  Copy-DirectoryContents -SourceDir $_.FullName -DestinationDir (Join-Path $appRoot $_.Name)
                }} else {{
                  Copy-FileWithRetry -Source $_.FullName -Destination (Join-Path $appRoot $_.Name)
                }}
              }}
              Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
              Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
              Write-UpdateLog "Update files copied successfully"
              Start-Sleep -Milliseconds 500
              Start-Process -FilePath $restartExe -ArgumentList $restartArgs -WorkingDirectory $appRoot
              Write-UpdateLog "Restarted application"
            }} catch {{
              Write-UpdateLog ("Update failed: " + $_.Exception.Message)
              if (Test-Path -LiteralPath $restartExe) {{
                Start-Sleep -Milliseconds 500
                Start-Process -FilePath $restartExe -ArgumentList $restartArgs -WorkingDirectory $appRoot
                Write-UpdateLog "Restarted previous application after failed update"
              }}
            }} finally {{
              Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
              Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
            }}
            """,
            EscapePowerShell(downloadedAssetPath),
            EscapePowerShell(paths.RootDirectory),
            EscapePowerShell(restartExecutable),
            restartArgs);
    }

    private static string BuildInstallerScript(string installerPath)
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            """
            $ErrorActionPreference = 'Stop'
            $installerPath = '{0}'
            $scriptPath = $MyInvocation.MyCommand.Path
            Start-Sleep -Seconds 2
            Start-Process -FilePath $installerPath
            Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
            """,
            EscapePowerShell(installerPath));
    }

    private static string EscapePowerShell(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    private sealed class ParsedVersion
    {
        public List<int> NumericParts { get; set; } = [];

        public string? Suffix { get; set; }

        public bool HasSuffix => !string.IsNullOrWhiteSpace(Suffix);

        public static ParsedVersion Parse(string value)
        {
            var trimmed = (value ?? string.Empty).Trim().TrimStart('v', 'V');
            if (trimmed.Length == 0)
            {
                return new ParsedVersion();
            }

            var hyphenIndex = trimmed.IndexOf('-');
            var numericPart = hyphenIndex >= 0 ? trimmed.Substring(0, hyphenIndex) : trimmed;
            var suffix = hyphenIndex >= 0 ? trimmed.Substring(hyphenIndex + 1) : null;

            var parts = numericPart
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(static segment => int.TryParse(segment, out var parsed) ? parsed : 0)
                .ToList();

            return new ParsedVersion
            {
                NumericParts = parts,
                Suffix = suffix,
            };
        }
    }
}

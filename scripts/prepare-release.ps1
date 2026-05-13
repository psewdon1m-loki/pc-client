param(
    [string]$Repository = "psewdon1m-loki/pc-client",
    [string]$Channel = "stable",
    [string]$Runtime = "win-x64",
    [string]$InstallerDirectory = "artifacts/installer",
    [string]$ReleaseDirectory = "artifacts/release",
    [string[]]$RuleSetIds = @("russia-smart", "global", "whitelist", "blacklist"),
    [string]$WatcherEndpoint = "https://loki-p-watcher.shmoza.net",
    [string]$WatcherSni = "loki-p-watcher.shmoza.net"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$projectPath = Join-Path $root "src/Client.App.Win/Client.App.Win.csproj"
[xml]$project = Get-Content -LiteralPath $projectPath
$version = $project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version was not found in $projectPath."
}

$installerName = "LokiClientSetup-$version-$Runtime.exe"
$bundleName = "LokiClientRelease-$version-$Runtime.zip"
$installerSource = Join-Path $root (Join-Path $InstallerDirectory $installerName)

if (-not (Test-Path -LiteralPath $installerSource)) {
    throw "Installer was not found: $installerSource. Run scripts/publish.ps1 and scripts/package-inno.ps1 first."
}

$releasePath = Join-Path $root $ReleaseDirectory
New-Item -ItemType Directory -Force -Path $releasePath | Out-Null

Get-ChildItem -LiteralPath $releasePath -File | Remove-Item -Force

$installerRelease = Join-Path $releasePath $installerName
Copy-Item -LiteralPath $installerSource -Destination $installerRelease -Force

$installerHash = (Get-FileHash -LiteralPath $installerRelease -Algorithm SHA256).Hash.ToLowerInvariant()
$publishedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$ruleSetAssets = @()

foreach ($ruleSetId in $RuleSetIds) {
    $ruleSetSource = Join-Path $root "src/Client.App.Win/Assets/rule-sets/$ruleSetId.json"
    if (-not (Test-Path -LiteralPath $ruleSetSource)) {
        continue
    }

    $ruleSetZipName = "$ruleSetId.zip"
    $ruleSetZipPath = Join-Path $releasePath $ruleSetZipName
    $ruleSetTemp = Join-Path ([System.IO.Path]::GetTempPath()) ("loki-rule-set-" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $ruleSetTemp | Out-Null
    try {
        Copy-Item -LiteralPath $ruleSetSource -Destination (Join-Path $ruleSetTemp "$ruleSetId.json") -Force
        Compress-Archive -Path (Join-Path $ruleSetTemp "$ruleSetId.json") -DestinationPath $ruleSetZipPath -Force
    }
    finally {
        Remove-Item -LiteralPath $ruleSetTemp -Recurse -Force -ErrorAction SilentlyContinue
    }

    $ruleSetHash = (Get-FileHash -LiteralPath $ruleSetZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $ruleSetAssets += [ordered]@{
        id = $ruleSetId
        version = $version
        url = "https://github.com/$Repository/releases/download/v$version/$ruleSetZipName"
        sha256 = $ruleSetHash
    }
}

$manifest = [ordered]@{
    channel = $Channel
    version = $version
    minimumVersion = $version
    publishedAt = $publishedAt
    installer = [ordered]@{
        url = "https://github.com/$Repository/releases/download/v$version/$installerName"
        sha256 = $installerHash
        mandatory = $false
    }
    ruleSets = $ruleSetAssets
    watcher = if ([string]::IsNullOrWhiteSpace($WatcherEndpoint)) {
        $null
    } else {
        [ordered]@{
            endpoint = $WatcherEndpoint
            sni = $WatcherSni
        }
    }
}

$manifestPath = Join-Path $releasePath "manifest.json"
$manifestJson = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

$bundlePath = Join-Path $releasePath $bundleName
$bundleTemp = Join-Path ([System.IO.Path]::GetTempPath()) ("loki-release-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $bundleTemp | Out-Null
try {
    Copy-Item -LiteralPath $installerRelease -Destination (Join-Path $bundleTemp $installerName) -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $bundleTemp "manifest.json") -Force
    foreach ($ruleSetId in $RuleSetIds) {
        $ruleSetZipPath = Join-Path $releasePath "$ruleSetId.zip"
        if (Test-Path -LiteralPath $ruleSetZipPath) {
            Copy-Item -LiteralPath $ruleSetZipPath -Destination (Join-Path $bundleTemp "$ruleSetId.zip") -Force
        }
    }
    Compress-Archive -Path (Join-Path $bundleTemp "*") -DestinationPath $bundlePath -Force
}
finally {
    Remove-Item -LiteralPath $bundleTemp -Recurse -Force -ErrorAction SilentlyContinue
}

$bundleHash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host "Prepared release assets in $releasePath"
Write-Host "$installerName sha256:$installerHash"
Write-Host "manifest.json sha256:$manifestHash"
Write-Host "$bundleName sha256:$bundleHash"

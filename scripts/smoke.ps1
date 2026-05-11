param(
    [string]$SubscriptionUrl = "https://loki-panel.shmoza.net:8000/sub/SzlYMlI3TDVNNFE4WjFQQiwxNzc4MzMyNjQ3lGVgfDSRJi",
    [switch]$TestSystemProxy
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".dotnet/dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

$argsList = @("--project", (Join-Path $root "tools/Client.Smoke/Client.Smoke.csproj"), "-c", "Release", "--", $SubscriptionUrl, "--allow-invalid-subscription-tls")
if ($TestSystemProxy) {
    $argsList += "--test-system-proxy"
}

& $dotnet run @argsList

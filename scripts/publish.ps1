param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "artifacts/publish/win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".dotnet/dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $userDotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
    $dotnet = if (Test-Path -LiteralPath $userDotnet) { $userDotnet } else { "dotnet" }
}

& $dotnet publish (Join-Path $root "src/Client.App.Win/Client.App.Win.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $root $Output)

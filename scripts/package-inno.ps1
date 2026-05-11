param(
    [string]$Script = "installer/LokiClient.iss"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    throw "ISCC.exe was not found. Install Inno Setup 6 and rerun scripts/package-inno.ps1."
}

& $iscc.Source (Join-Path $root $Script)


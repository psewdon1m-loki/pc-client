# Loki Proxy VPN client

Windows desktop client for Loki Proxy VPN. The client manages local proxy
state, Xray runtime assets, routing rule sets, telemetry commands, and
GitHub Releases based updates.

## Repository scope

This repository contains only the Windows client. The watcher/server side is a
separate repository and is not required for the client to start, connect, or
route traffic. Watcher integration is optional and non-blocking.

## Runtime data

Application binaries are installed per user into:

```text
%LOCALAPPDATA%\Programs\Loki Proxy VPN
```

User state is stored separately in:

```text
%LOCALAPPDATA%\LokiClient
```

The installer updates application files without deleting user profiles,
connections, routing rule overrides, telemetry identity, or local settings.

## Build

Use the bundled local .NET SDK when available:

```powershell
.\.dotnet\dotnet.exe test Client.sln --no-restore
.\scripts\publish.ps1
.\scripts\package-inno.ps1
```

The installer is written to:

```text
artifacts\installer\LokiClientSetup-<version>-win-x64.exe
```

## Production configuration

For production builds, create this local file before publishing:

```text
src\Client.App.Win\loki.env
```

Example:

```env
LOKI_UPDATE_MANIFEST_URL=https://github.com/psewdon1m-loki/pc-client/releases/latest/download/manifest.json
LOKI_UPDATE_CHANNEL=stable
LOKI_UPDATE_CHECK_INTERVAL_MINUTES=360
LOKI_UPDATE_PUBLIC_KEY_PEM=
```

`loki.env` is intentionally ignored by git, but the app project includes it in
publish output when the file exists.

## GitHub Releases update flow

The updater reads `manifest.json` from GitHub Releases. This file is the stable
entry point that tells the client which app installer, routing rule sets, and
watcher endpoint should be used.

Minimum release assets:

```text
manifest.json
LokiClientSetup-<version>-win-x64.exe
```

Optional remote routing assets:

```text
russia-smart.zip
global.zip
whitelist.zip
blacklist.zip
```

The installer already contains the default app assets, Xray binary, geo files,
and built-in routing rule sets. End users only need to download the installer.
Remote rule-set zip files are needed only when routing rules must be updated
independently from the application.

## Release manifest

Manifest files must be UTF-8 without BOM. The updater tolerates UTF-8 BOM, but
release artifacts should still be published without it.

Example:

```json
{
  "channel": "stable",
  "version": "<version>",
  "minimumVersion": "<version>",
  "publishedAt": "2026-05-11T00:00:00Z",
  "installer": {
    "url": "https://github.com/psewdon1m-loki/pc-client/releases/download/v<version>/LokiClientSetup-<version>-win-x64.exe",
    "sha256": "<installer-sha256>",
    "mandatory": false
  },
  "ruleSets": [],
  "watcher": null
}
```

## One-file release policy

For manual installation, publish the installer as the single user-facing file.
For automatic updates, keep `manifest.json` as a separate release asset because
the client needs a stable URL to discover new versions.

If release asset count becomes a problem, the next compatible step is an update
bundle zip: `manifest.json` stays separate, while installer and rule-set zips
are packed into one downloadable archive referenced by the manifest.

## Routing rule sets

Installed default rule sets live in:

```text
src\Client.App.Win\Assets\rule-sets
```

User/runtime rule sets live in:

```text
%LOCALAPPDATA%\LokiClient\assets\rule-sets
```

Supported rule-set ids:

```text
russia-smart
global
whitelist
blacklist
```

Each remote rule-set zip should contain a JSON file with either:

```json
{ "rules": [] }
```

or a raw JSON array of rules.

## Verification checklist

Before publishing a release:

```powershell
.\.dotnet\dotnet.exe test Client.sln --no-restore
.\scripts\publish.ps1
.\scripts\package-inno.ps1
Get-FileHash artifacts\installer\LokiClientSetup-<version>-win-x64.exe -Algorithm SHA256
```

Then update `manifest.json`, verify its SHA256 values, and upload it to the
GitHub Release together with the installer and optional remote rule-set zips.

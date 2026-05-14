# Loki Client Release Guide

This guide describes the production release process for the Windows client:
version bump, installer build, GitHub Release assets, update manifest, and
verification.

Run all commands from the `client/` directory unless another path is shown.

## Release Model

The client supports two distribution paths:

1. Manual install for new users:
   `LokiClientRelease-<version>-win-x64.zip`.

2. Automatic updates for installed users:
   `manifest.json` points to the installer and remote rule-set zip assets.
   The primary manifest URL should be served by Loki Watcher. GitHub Releases
   stay available as fallback.

User data is stored separately from app binaries:

```text
%LOCALAPPDATA%\Programs\Loki Proxy VPN   app binaries
%LOCALAPPDATA%\LokiClient                user data, settings, identity, rules
```

Installer updates must not delete `%LOCALAPPDATA%\LokiClient`. This keeps:

- connections and imported subscriptions;
- active routing mode and local rule-set updates;
- telemetry identity and client id;
- watcher/updater endpoint overrides;
- user settings such as auto updates and logs upload.

## Prerequisites

Install or verify:

- Windows 10/11 x64.
- .NET 8 SDK.
- Inno Setup 6 with `ISCC.exe` available in `PATH`.
- Git LFS configured for the client repository.
- Access to the GitHub repository `psewdon1m-loki/pc-client`.

Use the local SDK when present:

```powershell
.\.dotnet\dotnet.exe --info
```

If `.dotnet\dotnet.exe` is not present, the scripts try
`%USERPROFILE%\.dotnet\dotnet.exe`, then `dotnet` from `PATH`.

## Production Env

Before publishing, create:

```text
src\Client.App.Win\loki.env
```

Production example:

```env
LOKI_TELEMETRY_ENDPOINT=https://loki-p-watcher.shmoza.net
LOKI_TELEMETRY_SNI=loki-p-watcher.shmoza.net
LOKI_TELEMETRY_UPLOAD_INTERVAL_MINUTES=60
LOKI_TELEMETRY_COMMAND_POLL_SECONDS=300
LOKI_UPDATE_MANIFEST_URL=https://loki-p-watcher.shmoza.net/manifest.json
LOKI_UPDATE_FALLBACK_MANIFEST_URL=https://github.com/psewdon1m-loki/pc-client/releases/latest/download/manifest.json
LOKI_UPDATE_CHANNEL=stable
LOKI_UPDATE_CHECK_INTERVAL_MINUTES=360
LOKI_UPDATE_PUBLIC_KEY_PEM=
```

`loki.env` is ignored by git and must not be committed. The app project copies
it into publish output only when the file exists.

## Version Bump

Update both files to the same version.

In `src\Client.App.Win\Client.App.Win.csproj`:

```xml
<Version>0.1.60</Version>
<AssemblyVersion>0.1.60.0</AssemblyVersion>
<FileVersion>0.1.60.0</FileVersion>
<InformationalVersion>0.1.60</InformationalVersion>
```

In `installer\LokiClient.iss`:

```text
#define MyAppVersion "0.1.60"
```

Use plain semantic versions without `v` in project files. GitHub tags should
include `v`, for example `v0.1.60`.

## Pre-Release Checks

Check the working tree and confirm only intended source/docs changes are
present:

```powershell
git status --short
```

Run tests:

```powershell
.\.dotnet\dotnet.exe test Client.sln --no-restore
```

If local `.dotnet` is unavailable:

```powershell
dotnet test Client.sln --no-restore
```

Expected result:

```text
Passed! - Failed: 0
```

## Build Installer

Publish the self-contained Windows app:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

Output:

```text
artifacts\publish\win-x64
```

Build the Inno Setup installer:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-inno.ps1
```

Output:

```text
artifacts\installer\LokiClientSetup-<version>-win-x64.exe
```

If `ISCC.exe` is not found, install Inno Setup 6 or add it to `PATH`.

## Prepare GitHub Release Assets

Generate the release directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\prepare-release.ps1
```

Default production arguments are:

```text
Repository:      psewdon1m-loki/pc-client
Channel:         stable
Runtime:         win-x64
WatcherEndpoint: https://loki-p-watcher.shmoza.net
WatcherSni:      loki-p-watcher.shmoza.net
Rule sets:       russia-smart, global, whitelist, blacklist
```

Override when needed:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\prepare-release.ps1 `
  -Repository "psewdon1m-loki/pc-client" `
  -Channel "stable" `
  -WatcherEndpoint "https://loki-p-watcher.shmoza.net" `
  -WatcherSni "loki-p-watcher.shmoza.net"
```

Output:

```text
artifacts\release\manifest.json
artifacts\release\LokiClientSetup-<version>-win-x64.exe
artifacts\release\LokiClientRelease-<version>-win-x64.zip
artifacts\release\russia-smart.zip
artifacts\release\global.zip
artifacts\release\whitelist.zip
artifacts\release\blacklist.zip
```

The script prints SHA256 values for:

- installer;
- manifest;
- full release zip.

## GitHub Release

Create a GitHub Release in `psewdon1m-loki/pc-client`.

Use:

```text
tag:   v<version>
title: v<version>
```

Upload all files from:

```text
artifacts\release
```

Upload all of them, not only the full zip. The full zip is for manual download,
but automatic updates still need the individual assets referenced by
`manifest.json`:

- `manifest.json`
- `LokiClientSetup-<version>-win-x64.exe`
- `russia-smart.zip`
- `global.zip`
- `whitelist.zip`
- `blacklist.zip`
- `LokiClientRelease-<version>-win-x64.zip`

## Release Notes Template

Use this minimal release body:

```markdown
v<version>

Client release.

Assets:
- LokiClientRelease-<version>-win-x64.zip
- LokiClientSetup-<version>-win-x64.exe
- manifest.json
- routing rule-set zips
```

## Verify Published Release

After publishing, open these URLs in a browser or with PowerShell:

```text
https://github.com/psewdon1m-loki/pc-client/releases/download/v<version>/manifest.json
https://github.com/psewdon1m-loki/pc-client/releases/download/v<version>/LokiClientSetup-<version>-win-x64.exe
https://github.com/psewdon1m-loki/pc-client/releases/download/v<version>/russia-smart.zip
```

Verify local hashes:

```powershell
Get-FileHash .\artifacts\release\* -Algorithm SHA256
```

Check that `manifest.json` has the same hashes as the uploaded files.

Check zip contents:

```powershell
Expand-Archive .\artifacts\release\LokiClientRelease-<version>-win-x64.zip -DestinationPath $env:TEMP\loki-release-check -Force
Get-ChildItem $env:TEMP\loki-release-check
```

Expected files:

```text
LokiClientSetup-<version>-win-x64.exe
manifest.json
russia-smart.zip
global.zip
whitelist.zip
blacklist.zip
```

## Watcher Verification

Watcher is the primary source of update truth in production. After the GitHub
Release is published, verify:

```text
https://loki-p-watcher.shmoza.net/manifest.json
```

The manifest served by Watcher should target the same version and assets.

On the Watcher dashboard, check:

- latest client version;
- watcher endpoint;
- rule-set list and hashes;
- client update-state reports after a client checks for updates.

## Client Smoke Test

Install on a clean user profile or test VM:

1. Download `LokiClientRelease-<version>-win-x64.zip`.
2. Extract it.
3. Run `LokiClientSetup-<version>-win-x64.exe`.
4. Start Loki.
5. Import a connection or subscription.
6. Connect.
7. Confirm Windows system proxy is enabled.
8. Confirm traffic works through the proxy.
9. Confirm the client appears on Watcher dashboard.
10. Use Settings -> Request update.
11. Confirm update-state appears on Watcher dashboard.

Local traffic-path lab:

1. Connect Loki with the routing mode being tested.
2. From the workspace root, run `.\traffic-lab\run.ps1`.
3. Open the generated JSON/CSV report under `traffic-lab\artifacts`.
4. Confirm explicit/env profiles report
   `likelyReachedXray=true`.
5. Confirm direct profiles report `likelyReachedXray=false`.
6. Use system profiles to verify whether normal Windows system proxy consumers
   are actually reaching Xray.

Upgrade test:

1. Install previous release.
2. Import a connection.
3. Confirm `%LOCALAPPDATA%\LokiClient` contains data.
4. Install the new release.
5. Confirm connection data, telemetry identity, routing settings and client id
   remain unchanged.

## Updating Only Rule Sets

For production, rule-set updates should still be represented by a new manifest
served by Watcher. The client compares rule-set hashes and downloads changed
zip files.

Recommended process:

1. Update JSON files in `src\Client.App.Win\Assets\rule-sets`.
2. Bump client version if the app is also changing.
3. Build a normal release if an installer is needed.
4. If only rule sets changed, Watcher may serve a manifest that points to new
   rule-set zip assets without requiring app installation.

The current `prepare-release.ps1` expects an installer for the same version.
Use the full release process unless Watcher-side manifest management is handling
rule-only updates.

## Updating Watcher Endpoint

To move clients to a new watcher endpoint:

1. Publish a manifest with:

```json
"watcher": {
  "endpoint": "https://new-watcher.example.com",
  "sni": "new-watcher.example.com"
}
```

2. Clients receive it during auto update or Settings -> Request update.
3. Client writes the new endpoint into `%LOCALAPPDATA%\LokiClient\loki.env`.
4. Client reconfigures telemetry and future update checks use the new endpoint.

Keep the old watcher endpoint online long enough for active clients to migrate.

## Rollback

If a release is broken:

1. Do not delete the release immediately; clients may be downloading assets.
2. Publish or serve a newer manifest that points to the previous stable
   installer/rule sets.
3. Keep Watcher online so clients can receive the correction.
4. If the problem is only in rule sets, publish corrected rule-set zip files
   and update manifest hashes.

## Common Issues

`dotnet test` says no SDKs were found:

- Use `.\.dotnet\dotnet.exe` or `%USERPROFILE%\.dotnet\dotnet.exe`.
- Install .NET 8 SDK if needed.

`package-inno.ps1` cannot find `ISCC.exe`:

- Install Inno Setup 6.
- Add its install directory to `PATH`.

Auto updates do not see the release:

- Confirm `manifest.json` exists in the GitHub Release assets.
- Confirm Watcher serves `/manifest.json`.
- Confirm `LOKI_UPDATE_MANIFEST_URL` points to Watcher.
- Confirm `LOKI_UPDATE_FALLBACK_MANIFEST_URL` points to GitHub latest release.
- Confirm manifest `channel` matches `LOKI_UPDATE_CHANNEL`.

Rule sets show as missing:

- Upload individual `*.zip` rule-set assets.
- Confirm manifest hashes match uploaded files.
- Confirm each zip contains a valid JSON rule-set file.

Client id changed unexpectedly:

- Verify the installer did not delete `%LOCALAPPDATA%\LokiClient`.
- Do not run uninstall cleanup when testing updates unless a full removal is
  intended.

## Final Checklist

Before announcing a production release:

- Version bumped in `.csproj` and `.iss`.
- `src\Client.App.Win\loki.env` contains production endpoints.
- `dotnet test` passed.
- `publish.ps1` passed.
- `package-inno.ps1` passed.
- `prepare-release.ps1` passed.
- All files from `artifacts\release` uploaded to GitHub Release.
- GitHub Release tag is `v<version>`.
- Published manifest URLs return HTTP 200.
- Watcher `/manifest.json` returns the expected version.
- Test install works.
- Test update request reports to Watcher.

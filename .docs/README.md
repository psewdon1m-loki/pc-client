# Loki Windows Client

Minimal Windows client for Xray-based proxy connections with Russia-first routing defaults.

## MVP Scope

- Windows 10/11.
- .NET 8 WPF UI.
- Xray core only.
- `vless://` and subscription URL import.
- System proxy management.
- Russia routing preset: Russian/private/Ozon direct, ads and BitTorrent blocked, everything else through proxy.
- Local diagnostics bundle with secret redaction.
- Self-contained installer target.

See [mvp.md](mvp.md) for the product plan and stage checkpoints.

## STANDART Workflow

Development follows https://github.com/psewdon1m/STANDART.git:

- clear structure;
- reversible state changes;
- transparent stage results;
- modular implementation;
- defined terms;
- automated build/test/package checks;
- roadmap checkpoints;
- input and output verification per stage.

## Build

The client workspace expects a .NET 8 SDK. From `client/`, a local SDK can be used from `.dotnet/dotnet.exe`.

```powershell
.\.dotnet\dotnet.exe restore Client.sln
.\.dotnet\dotnet.exe build Client.sln
.\.dotnet\dotnet.exe test Client.sln
```

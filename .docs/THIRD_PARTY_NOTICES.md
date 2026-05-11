# Third-Party Notices

This repository bundles or references third-party components. Each component remains under its own license.

## Xray-core

- Project: XTLS/Xray-core
- Source: https://github.com/XTLS/Xray-core
- Bundled file: `src/Client.App.Win/Assets/xray/xray.exe`
- Version observed in smoke tests: `Xray 25.10.15`
- License: MPL-2.0

The Loki Client source code does not copy Xray-core source code. Xray is executed as an external process.

## Russia V2Ray Rules DAT

- Project: runetfreedom/russia-v2ray-rules-dat
- Source: https://github.com/runetfreedom/russia-v2ray-rules-dat
- Bundled files:
  - `src/Client.App.Win/Assets/geo/geoip.dat`
  - `src/Client.App.Win/Assets/geo/geosite.dat`

These files are routing data assets and are updated independently from application code. Before public distribution, confirm the upstream license/redistribution terms and keep this notice current.

## Microsoft .NET and NuGet Packages

The application is built with .NET 8 and uses NuGet packages such as:

- Microsoft.Data.Sqlite
- Microsoft.Extensions.Http
- xUnit
- Microsoft.NET.Test.Sdk
- Microsoft.AspNetCore.Mvc.Testing

These packages remain under their respective Microsoft or package-owner licenses.

## Inno Setup

- Project: Inno Setup
- Source: https://jrsoftware.org/isinfo.php
- Use: Windows installer compilation.

The generated installer is created with Inno Setup. Review Inno Setup licensing terms before commercial distribution.


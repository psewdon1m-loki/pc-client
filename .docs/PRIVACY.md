# Privacy Policy

Effective date: 2026-05-10

This policy describes what Loki Client stores locally and what it sends to Loki Watcher.

## Summary

Loki Client is a Windows proxy client. It stores proxy profiles locally, runs Xray locally and sends mandatory operational telemetry to Loki Watcher so the service can verify clients, detect regional blocking and monitor connection health.

The client must not collect browsing history, visited URLs, DNS query contents, clipboard contents or personal files.

## Local Data

The application stores the following data on the user's device:

- imported proxy profiles and subscription source URL;
- application settings;
- last-known-good Xray configuration;
- Xray and application logs;
- bundled and downloaded routing assets;
- telemetry identity and queued telemetry events.

Proxy secrets are required for connection and are stored locally. They must be redacted before any log text is queued or sent.

## Mandatory Telemetry

Operational telemetry is enabled by default and cannot be disabled in the client. It is required for panel registration, client verification before proxy activation, reachability checks and regional blocking diagnostics.

Mandatory telemetry may include:

- generated client identifier and public display identifier;
- Windows user name and machine name;
- Loki Client version, Windows version, CPU architecture and runtime;
- connection status, reachability status and sanitized error type;
- current routing mode;
- configured connection inventory without VLESS secrets, including profile name, protocol, host, port, transport, security mode, SNI and subscription flag;
- timestamped heartbeat and connection events;
- traffic counters measured while the proxy is connected;
- device type;
- update state, including installed client version, auto-update setting, active routing rule set, watcher/update endpoint configuration and last update-check result.

Traffic counters are operational counters and do not include visited domains, URLs or DNS queries.

## Optional Logs

Logs are enabled by default and can be changed from Settings -> Logs. This setting controls only application/Xray log lines attached to telemetry events. Disabling Logs does not disable mandatory operational telemetry, enrollment or verification.

Log lines must be redacted before upload to remove proxy secrets, UUIDs, private keys, short IDs and obvious credentials. Loki Watcher clears retained log-line payloads from old events after 30 days by default. Core operational telemetry events can be retained longer for service diagnostics and regional blocking analysis.

## IP and Region

The client does not directly send the user's original IP address. Loki Watcher derives the source IP from the incoming telemetry request and may derive an expected region from that IP.

The dashboard may show:

- original source IP;
- expected region;
- client display identifier;
- Windows user name and machine name;
- client status and reachability;
- routing mode and configured connection inventory;
- total reported traffic.

Raw IP and core telemetry retention should be defined by the operator's server-side retention policy. Deleting a client from Loki Watcher removes that client's server-side row, events and queued commands.

## Server Requests

The client sends telemetry every 1 hour by default. Loki Watcher can also queue commands such as `collect_now`, `check_updates` and watcher endpoint changes. The client polls for commands and sends fresh telemetry or update state when those commands are received.

If an operator deletes a client from Loki Watcher, the server removes its dashboard record, queued commands and stored events. If the same installed client later starts or sends telemetry again, it enrolls again and a fresh server-side record is created.

## Security Controls

Production telemetry endpoints must use HTTPS with a valid certificate. The endpoint should be configured through `LOKI_TELEMETRY_ENDPOINT` using the public telemetry hostname so TLS SNI, certificate name and routing host match.

After enrollment, telemetry requests are signed with HMAC SHA-256 using a per-install client secret. Dashboard access should be protected with `LOKI_WATCHER_DASHBOARD_USERNAME` and `LOKI_WATCHER_DASHBOARD_PASSWORD` before exposure outside localhost.

## User Control

The user can:

- disable optional log-line collection in Settings;
- uninstall the application.

Uninstall must not leave Windows system proxy enabled.

## Contact

Replace this section with the project owner's support and privacy contact before public distribution.

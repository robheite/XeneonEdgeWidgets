# Edge Companion

Edge Companion is one reusable Windows localhost application shared by widgets
that need capabilities unavailable to iCUE's HTML runtime. It provides narrowly
scoped modules for application integration, Windows network information, and
local network protocols.

It listens only on `http://127.0.0.1:48620`. Widgets that do not use a companion
module create no work for that module.

## Install

1. Download `edge-companion-win-x64-<version>.zip` from
   [GitHub Releases](https://github.com/robheite/XeneonEdgeWidgets/releases).
2. Extract it to a stable folder owned by your Windows account.
3. Run `EdgeCompanion.Host.exe`.
4. Leave the widget's companion URL set to `http://127.0.0.1:48620`.

The companion build is self-contained for Windows x64; users do not need to
install the .NET runtime separately.

Automatic startup and the final installer/protocol registration are still under
development. Until that feature is released, launch the executable manually
before using a companion-enabled widget.

## Security

- The HTTP service binds to loopback and is not exposed to the LAN.
- Actions are fixed and validated; the API is not a general command runner.
- An optional action token protects state-changing operations.
- Router credentials and NordVPN account credentials are not stored.

## Development

Run the companion from source:

```powershell
npm run companion:start
```

Run its test suite:

```powershell
npm run companion:test
```

The module architecture and API decisions are documented in
[Reusable iCUE Widget Companion](widget-companion/design.md).

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
2. Extract the ZIP and run `EdgeCompanion-Setup-<version>.exe`.
3. Complete the per-user installer. Administrator access is not required.
4. Leave the widget's companion URL set to `http://127.0.0.1:48620`.

The companion build is self-contained for Windows x64; users do not need to
install the .NET runtime separately. The initial public installer is not
code-signed, so Windows may display a SmartScreen warning.

The installer registers the private `edgecompanion://` Windows protocol.
Companion-enabled widgets use it only to launch the installed companion when it
is offline. The protocol does not accept commands or arbitrary arguments.

Widgets can enable or disable current-user Windows startup from their settings.
Startup remains disabled unless the user enables it. Uninstall **Edge
Companion** from Windows **Installed apps** to stop the companion, remove the
protocol registration, and remove its startup entry.

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

Build the installer after publishing a Windows x64 companion:

```powershell
.\scripts\build-companion-installer.ps1 `
  -PublishDirectory .\artifacts\companion `
  -Version 1.0.0 `
  -OutputDirectory .\installer\output
```

This requires Inno Setup 6. The public release workflow installs the pinned
compiler and tests install, in-place upgrade, launch, stop, and uninstall before
creating a release.

The module architecture and API decisions are documented in
[Reusable iCUE Widget Companion](widget-companion/design.md).

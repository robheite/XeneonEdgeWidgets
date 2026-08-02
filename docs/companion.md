# Edge Companion

Edge Companion is one reusable Windows localhost application shared by widgets
that need capabilities unavailable to iCUE's HTML runtime. It provides narrowly
scoped modules for application integration, Windows network information, and
local network protocols.

Its privileged API listens only on `http://127.0.0.1:48620`. Widgets that do not
use a companion module create no work for that module.

Wand Remote uses an unprivileged loopback origin at
`http://localhost:48620`. It shares the companion's single listener, but its
hostname creates a browser origin distinct from trusted APIs at `127.0.0.1`.
The proxy adapts Wand Remote for iCUE's embedded browser and cannot reach the
Companion's NordVPN, startup, or action-token APIs.

The Emby module proxies only local and private-network servers. It keeps Emby
authentication, library browsing, images, range-enabled video streaming, and
playback check-ins behind the same loopback listener. HTTP redirects are not
followed, preventing a configured LAN server from redirecting the companion to
an unapproved public address.

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
- A random per-user action token protects every state-changing operation and is
  bootstrapped automatically by installed widgets.
- iCUE loads widgets as local content. To keep that zero-setup action flow,
  the Companion also accepts the local-file browser origin. Do not open HTML
  files from downloads, email, or other untrusted sources while Edge Companion
  is running: a malicious local file could use the same browser privilege to
  request the action token. Normal websites and the isolated Wand proxy origin
  cannot access the privileged API.
- Router credentials and NordVPN account credentials are not stored.
- The Wand proxy permits only its fixed Wand and WeMod service hosts. It
  forwards pairing and live-channel credentials only to those allowlisted hosts
  and never logs or stores them.

## Local state

The companion stores its generated action token and an active NordVPN Pause
deadline under:

```text
%LOCALAPPDATA%\XeneonEdgeWidgets\EdgeCompanion\
```

Pause resumes remain scheduled after a companion or Windows restart. If the
saved deadline elapsed while the companion was offline, it requests a fastest
United States connection when the companion next starts. Uninstalling the
application leaves this small per-user state directory in place so an upgrade
or reinstall does not silently lose an active Pause or rotate the action token.

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

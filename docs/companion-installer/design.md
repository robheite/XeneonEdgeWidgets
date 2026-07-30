# Edge Companion Per-User Installer

**Status:** Approved

**Author:** Codex  **Date:** 2026-07-30

## Summary

Package Edge Companion as a self-contained Windows x64 application inside an
Inno Setup per-user installer. The installer places the companion at a stable
path, registers `edgecompanion://start`, adds standard Windows uninstall
metadata, supports in-place upgrades, and launches the companion after setup.
The existing release workflow will place the installer inside the separate
companion ZIP already promised by the repository.

This keeps installation approachable for public users without requiring
administrator privileges or a separate .NET runtime.

## Goals

- Install Edge Companion into a stable, current-user location.
- Require no administrator prompt and no separate .NET installation.
- Make the widget's **Start service** control work through
  `edgecompanion://start`.
- Support repeatable upgrades at the same location.
- Provide normal Windows uninstall behavior and clean owned registry entries.
- Preserve the widget-controlled `Start companion with Windows` setting.
- Keep the release contract of one widget ZIP and one companion ZIP.
- Build the installer through the manually dispatched release workflow.

## Non-goals

- Installing or importing widgets automatically.
- Running as a Windows service or before user sign-in.
- Installing for every Windows user.
- Automatically enabling Windows startup during setup.
- Adding a desktop shortcut.
- Implementing action-token bootstrap or Pause persistence; those remain phase
  4 work.
- Producing an ARM64 build in the first public release.

## Constraints

- The installed companion is a self-contained Windows x64 publish and is large
  enough that installer compression materially improves downloads.
- iCUE can open a registered URL protocol but cannot install the protocol
  handler itself.
- The startup module must point only to the stable installed executable.
- The installer and companion are initially unsigned, so Windows SmartScreen may
  warn users until a code-signing certificate and signing workflow are added.
- Release creation remains manual and must fail before publishing if installer
  compilation fails.

## Proposed design

### Installation model

Use Inno Setup with `PrivilegesRequired=lowest` and a fixed `AppId`. Install to:

```text
%LOCALAPPDATA%\Programs\XeneonEdgeWidgets\EdgeCompanion\
```

The installation is scoped to the current Windows user. It includes the
self-contained companion publish and an uninstaller registered in Windows
Installed Apps. Setup adds a Start Menu shortcut named **Edge Companion** and
launches the companion after a successful interactive install. It does not add
a desktop shortcut or enable startup automatically.

Using the same fixed `AppId`, publisher, and install directory allows newer
versions to upgrade the existing installation. Setup uses Inno Setup's
application-closing support to stop the installed companion when files need to
be replaced; it does not kill unrelated development copies by process name.

### Protocol handler

Setup owns these current-user entries:

```text
HKCU\Software\Classes\edgecompanion
HKCU\Software\Classes\edgecompanion\DefaultIcon
HKCU\Software\Classes\edgecompanion\shell\open\command
```

The protocol command is fixed to:

```text
"<installed path>\EdgeCompanion.Host.exe" --start
```

No component of the URL is forwarded as an application argument. The companion
recognizes only the exact `--start` flag, removes it before ASP.NET configuration
parsing, and starts normally. If an instance is already listening, the protocol
launch exits successfully instead of creating a competing server.

The protocol is intentionally start-only. It cannot execute arbitrary commands,
paths, or URLs.

### Startup and uninstall

The existing widget setting manages this exact value:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\EdgeCompanion
```

The installer leaves it disabled on first installation. Upgrades preserve it
because the installed executable path remains stable. Uninstall removes the Run
value, the protocol keys, application files, shortcuts, and uninstall metadata.

Uninstall does not remove widget packages or iCUE settings.

### Release workflow

The manual release job will:

1. test the companion and validate every widget;
2. publish Edge Companion self-contained for `win-x64`;
3. compile `installer/edge-companion.iss` with a pinned Inno Setup version;
4. smoke-test installer metadata and silent install/uninstall in the CI user
   profile;
5. place `EdgeCompanion-Setup-<tag>.exe` and a short installation README inside
   `edge-companion-win-x64-<tag>.zip`; and
6. create the existing two-asset GitHub Release.

Local development gets a script that accepts a published companion directory,
version, and output directory. It locates an installed Inno Setup compiler and
fails with a clear installation command when unavailable.

The release publish uses the Windows GUI subsystem so normal launches do not
leave a console window open. Development through `dotnet run` retains console
logging.

## Alternatives and tradeoffs

### Portable ZIP only

This avoids an installer tool but leaves users responsible for choosing a stable
folder, registering the protocol, and cleaning up startup and registry entries.
It does not provide a reliable public first-run experience.

### MSI or WiX

MSI is familiar to enterprise administrators but adds more authoring and
upgrade complexity for a current-user utility. It is unnecessary for the first
consumer release.

### MSIX

MSIX provides stronger identity and update concepts but complicates loopback,
protocol registration, signing, and sideloading for an unsigned public project.

### Self-installing companion command

An `--install` mode avoids an external installer compiler but makes the
application responsible for copying and replacing itself, uninstall metadata,
shortcuts, rollback, and in-use file handling. Established installer tooling is
safer.

### Inno Setup

Inno Setup supports non-admin per-user installs, registry entries, upgrades,
application closing, compression, and uninstall in one reviewed script. It adds
a build-time dependency and should be revisited if the project becomes
commercial because its publisher requests commercial users purchase a license.

## Risks

- **Unsigned installer warnings:** document the expected SmartScreen warning,
  publish checksums, and add Authenticode signing before describing the build as
  fully trusted.
- **Companion is running during upgrade:** let setup close only the installed
  executable whose files are being replaced.
- **Protocol hijacking:** install under the current user's Programs directory,
  quote the executable path, forward no URL payload, and restore the owned key
  on upgrades.
- **Stale startup value after uninstall:** explicitly delete the owned Run value
  during uninstall.
- **Downgrade:** disallow installing an older version over a newer version unless
  a maintainer deliberately adds a downgrade path.
- **CI installer drift:** pin the Inno Setup compiler version and record it in
  release logs.
- **Incomplete cleanup after a crashed uninstall:** rely on Inno Setup's
  uninstall log and keep all owned registry entries declared in the installer
  script.

## Rollout

1. Add the installer script and companion `--start` handling.
2. Add local installer build and verification scripts.
3. Build and install into a temporary test user location.
4. Verify protocol start, already-running behavior, startup preservation,
   upgrade, and uninstall cleanup.
5. Update companion and release documentation.
6. Add installer compilation and smoke tests to the manual release workflow.
7. Commit and update the existing draft pull request.

Backout removes the installer build from the workflow and retains the current
portable companion publish while leaving widget packages unchanged.

## Open questions

- None recommended for implementation. Use a per-user Inno Setup installer,
  leave startup off by default, and keep the companion installer inside the
  separate companion ZIP.

## Decision

Approved by the user on 2026-07-30. Implement the recommended per-user Inno
Setup installer, start-only URL protocol, stable install path, in-place
upgrades, and complete uninstall cleanup.

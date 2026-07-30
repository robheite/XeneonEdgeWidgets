# XENEON EDGE Widgets

A collection of independently installable HTML widgets for the CORSAIR XENEON
EDGE, backed where necessary by one reusable Windows companion application.

## Downloads

Published builds are available from
[GitHub Releases](https://github.com/robheite/XeneonEdgeWidgets/releases).
Each release contains two separate archives:

- `xeneon-edge-widgets-<version>.zip` contains the raw `.icuewidget` files.
- `edge-companion-win-x64-<version>.zip` contains the self-contained Windows
  companion installer.

The companion is only required by widgets that need access to Windows,
applications, or network protocols unavailable inside iCUE's HTML runtime.
Installing several companion-enabled widgets does not install several services;
they all use the same localhost companion.

See [Installing widgets](docs/installing-widgets.md) and
[Installing Edge Companion](docs/companion.md) for current instructions.

## Available widgets

| Widget | Companion required | Documentation |
| --- | --- | --- |
| NordVPN Edge | Yes | [Features and setup](docs/widgets/nordvpn-edge.md) |

## Repository structure

This is a main-branch monorepo:

```text
companion/        Shared modular .NET companion
nordvpn-edge/     Self-contained NordVPN widget
docs/widgets/     User documentation for each widget
scripts/          Development and release checks
```

Every widget lives in its own top-level folder and is validated and packaged
independently. Temporary branches are used for feature and release work; widgets
are not divided across permanent branches.

## Development

Requirements:

- Node.js 24.x
- .NET 8 SDK
- Inno Setup 6 (only when building the companion installer locally)
- iCUE 5.48 or later

Install the locked Node tooling:

```powershell
npm ci
```

Validate and package a widget:

```powershell
npm run widget:validate -- nordvpn-edge
npm run widget:package -- nordvpn-edge
```

Run companion tests and start it locally:

```powershell
npm run companion:test
npm run companion:start
```

Generated `.icuewidget` packages and .NET build output are intentionally not
committed.

## Releases

Releases are intentionally manual. A repository maintainer selects
**Actions → Create release → Run workflow**, supplies a new semantic version tag
such as `v1.0.0`, and chooses whether the release is a prerelease.

The workflow:

1. verifies that every widget has a documentation page;
2. validates and packages every widget folder;
3. runs the companion test suite;
4. builds and smoke-tests the per-user Windows x64 companion installer;
5. creates the two release ZIP files; and
6. creates the tag and GitHub Release.

It never runs automatically on a push or pull request. See
[Release process](docs/releases.md) for the checklist and asset layout.

## iCUE references

The repository includes a synchronized offline snapshot of the official widget
documentation under `docs/vendor/elgato-icue-widgets/` and reference copies of
the common tools shipped with the locally tested iCUE version.

Refresh the official documentation snapshot with:

```powershell
npm run docs:sync
```

[Official iCUE widget documentation](https://docs.elgato.com/icue/widgets/)

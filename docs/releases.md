# Release process

Releases are created only through the manually dispatched **Create release**
GitHub Actions workflow.

## Before releasing

1. Update each changed widget's manifest version.
2. Update that widget's page under `docs/widgets/`.
3. Update `docs/companion.md` for companion behavior, installation, or
   compatibility changes.
4. Update the repository README when widgets, requirements, asset names, or the
   installation model change.
5. Run:

   ```powershell
   npm ci
   npm run docs:verify
   npm run companion:test
   npm run widget:validate -- nordvpn-edge
   npm run widget:validate -- notes-edge
   npm run widget:validate -- wand-remote
   npm run widget:validate -- emby-edge
   ```

6. Merge the intended release commit into `main`.

## Create the release

1. Open the repository's **Actions** tab.
2. Select **Create release**.
3. Select **Run workflow** on `main`.
4. Enter a new semantic version tag beginning with `v`, such as `v1.0.0`.
5. Choose whether it is a prerelease and run the workflow.

The workflow does not respond to pushes, pull requests, or schedules.

## Release assets

Every release contains:

- `xeneon-edge-widgets-<version>.zip`
  - contains raw `.icuewidget` packages at the archive root;
  - includes every top-level folder with a valid widget `manifest.json`.
- `edge-companion-win-x64-<version>.zip`
  - contains the per-user `EdgeCompanion-Setup-<version>.exe` installer and a
    short README;
  - installs a self-contained .NET Windows x64 application;
  - does not require a separate .NET runtime installation.

GitHub-generated release notes describe commits since the previous release.
User-facing setup and compatibility details remain in the version-controlled
README and documentation.

## Failure behavior

No release is created if documentation verification, widget validation,
companion tests, packaging, installer compilation, or the installer
install/upgrade/uninstall smoke test fails. An existing release tag is never
replaced by the workflow.

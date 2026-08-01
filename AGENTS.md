# iCUE XENEON EDGE project guidance

This repository develops CORSAIR iCUE HTML widgets, with XENEON EDGE as the
primary target.

## Authoritative references

- Treat `docs/vendor/elgato-icue-widgets/` as the checked-in snapshot of the
  official iCUE widget documentation. Read the relevant snapshot before making
  assumptions about manifests, lifecycle events, controls, JavaScript
  expressions, global objects, or plugins.
- The snapshot's `manifest.json` records every source URL and sync time.
- Treat `docs/vendor/icue-5.48.58-common/common/` as a reference copy of the
  common tools and plugin wrappers shipped with locally installed iCUE 5.48.58.
- Re-check the live official docs and run `npm run docs:sync` when current API
  behavior matters or when updating the snapshot.

## Widget conventions

- Target `dashboard_lcd` for XENEON EDGE.
- Use the project-local CLI through npm scripts or `npx icuewidget`; do not
  depend on a globally installed CLI.
- Validate every widget with `npm run widget:validate -- <widget-directory>`.
- Package with `npm run widget:package -- <widget-directory>`.
- Keep exactly one generated package per widget at the repository root, using
  the CLI's unversioned `<widget-name>.icuewidget` filename. Do not create
  additional version-numbered package copies unless explicitly requested.
- Give every widget a distinctive, purpose-designed icon rather than a solid
  square or placeholder. iCUE renders selector icons as monochrome masks, so
  do not rely on color, gradients, shadows, or overlapping colors for meaning.
  Prefer a transparent SVG whose alpha silhouette and negative space remain
  recognizable as a single-color glyph at iCUE selector size, and set it as
  the manifest's `preview_icon`.
- Keep each widget self-contained. If it uses iCUE common tools or wrappers,
  copy the required files into that widget's own `common/` directory before
  packaging.
- Every widget `index.html` must include a `<title>tr('Widget Name')</title>`
  inside `<head>` and a matching `Widget Name` entry in `translation.json`.
  iCUE parses the head with Qt's strict XML parser: use the uppercase
  `<!DOCTYPE html>` declaration, and ensure every void element in the
  head (including `meta` and `link`) must use a self-closing `/>` form. A
  malformed head is reported by iCUE as the misleading `Missing Title Element`
  import error. Do not top-level-navigate a widget to an external page unless
  its head meets this same XML requirement; iCUE reparses the navigated page.
- Do not commit generated `.icuewidget` packages unless explicitly requested.
- Preserve the documented iCUE initialization and `onDataUpdated` lifecycle,
  and design for the XENEON EDGE `1688 × 696` landscape canvas.
- Treat iCUE's editor/configuration view as a scaled preview, not a smaller
  device canvas. Use `iCUE.isPreview` to fit the complete logical `1688 × 696`
  composition into the available preview viewport with one aspect-preserving
  scale, centered letterboxing, and no layout changes. Verify that the preview
  shows the whole widget without zooming, cropping, stretching, or changing
  the hardware layout. Apply this check to every future widget.

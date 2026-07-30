# Installing widgets

## Download

Open the repository's
[Releases](https://github.com/robheite/XeneonEdgeWidgets/releases) page and
download `xeneon-edge-widgets-<version>.zip` from the release you want. Extract
the archive; it contains one raw `.icuewidget` file for each published widget.

## Add a widget to iCUE

1. Open CORSAIR iCUE and select the XENEON EDGE.
2. Open the widgets or dashboard customization view.
3. Import the desired `.icuewidget` file.
4. Add the widget to a page, choose its size, and adjust its settings.

Install Edge Companion when the widget's documentation marks it as required.
HTML-only widgets do not need it.

## Updating

Download the newer widget archive and import the updated `.icuewidget` file.
Widget versions are recorded in their manifests and displayed during iCUE
validation and packaging.

Settings are managed by iCUE. Review the release notes before replacing a widget
when a release mentions settings or API compatibility changes.

## Removing

Remove the widget through iCUE. Removing one widget does not affect Edge
Companion or any other installed widget.

# Wand Remote

Wand Remote puts the Wand game-control interface on the XENEON EDGE panel, so
you can use it without switching iCUE out of panel mode.

## Requirements

- Windows 10 or 11
- CORSAIR iCUE 5.48 or later
- Wand for Windows, with Wand Remote access for your account
- Edge Companion from the same XENEON EDGE Widgets release

## Install and pair

1. Follow [Installing widgets](../installing-widgets.md) and import
   `wand-remote.icuewidget`.
2. Install and start [Edge Companion](../companion.md).
3. Add **Wand Remote** to an XENEON EDGE page. Its default settings use the
   Companion API at `http://127.0.0.1:48620` and the local Wand proxy at
   `http://localhost:48620`.
4. Open Wand on this PC and obtain its Remote PIN.
5. Enter the PIN on the panel. A successful pairing connects the panel to the
   active Wand session; start or select a game in Wand when you want its
   controls to appear.

The pairing is managed by Wand. The widget and Edge Companion do not request,
store, or display your Wand password.

## Settings

- **Companion service URL** — normally `http://127.0.0.1:48620`.
- **Wand proxy URL** — normally `http://localhost:48620`; the hostname deliberately differs from the trusted API origin.
- **Open Wand automatically** — opens the Remote as soon as the widget loads.

Leave both URLs at their defaults unless you intentionally run Edge Companion
on a different loopback port. Updated widgets automatically map the legacy
`127.0.0.1:48621` proxy setting to the shared `localhost:48620` listener.

## Troubleshooting

- **Edge Companion is offline:** start the installed Companion, then switch to
  another widget and back to reload Wand Remote.
- **Rotate-device screen:** install the current Wand Remote and Companion from
  the same release, then reload the widget. The Companion adapts Wand's
  phone-oriented orientation prompt for the XENEON EDGE landscape panel.
- **PIN is invalid or WeMod cannot connect:** make sure the Companion is from
  the same release, reload the widget, and use a newly generated Wand PIN.
- **Connected but no game controls:** leave the panel paired and make sure Wand
  has an active game or trainer session on this PC.

## Privacy and security

The Wand page is served through a fixed, loopback-only proxy. It can contact
only the Wand and WeMod hosts required for Remote assets, PIN pairing, and its
live control channel. That proxy is isolated from the Companion's privileged
API, so the Wand page cannot use NordVPN or Windows-startup controls.

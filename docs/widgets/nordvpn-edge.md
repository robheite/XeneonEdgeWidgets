# NordVPN Edge

NordVPN Edge is a responsive XENEON EDGE dashboard for NordVPN connection and
network state.

## Features

- protection and companion-service status;
- active NordVPN server number, city, and country;
- NordLynx adapter status;
- observed VPN exit IP;
- optional WAN identity when a supported read-only provider is available;
- live download and upload throughput;
- companion-managed Pause controls; and
- connection to NordVPN's fastest United States server.

## Requirements

- Windows 10 or 11
- CORSAIR iCUE 5.48 or later
- NordVPN for Windows
- Edge Companion

## Install

1. Follow [Installing widgets](../installing-widgets.md) and import
   `nordvpn-edge.icuewidget`.
2. Follow [Edge Companion](../companion.md) and start the companion.
3. Keep the default companion URL unless you intentionally changed its local
   configuration.
4. Add an action token to both the companion configuration and widget settings
   if action authorization is enabled.

## Settings

- Companion service URL
- Action token
- Start companion with Windows
- Pause duration
- Accent color and background opacity
- Comfortable or compact density
- Speed unit (`MB/s` or `Mb/s`)
- Show or hide WAN identity
- Show or hide the throughput chart

## WAN availability

The VPN exit IP can be observed through a public IP service. The WAN address
cannot be inferred through the same request because it follows the VPN route.
The router-neutral resolver uses read-only UPnP IGD and falls back to NAT-PMP.
It never creates a port mapping. When neither protocol is available, the widget
reports `WAN unavailable`.

## Privacy

The widget and companion do not require NordVPN account credentials. Public IP
lookups reveal the requesting IP address to the configured lookup provider, as
all public-IP services necessarily do.

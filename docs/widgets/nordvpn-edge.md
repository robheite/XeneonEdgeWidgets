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

The widget automatically obtains its per-user action token from the local
companion. No token needs to be copied into iCUE settings.

## Settings

- Companion service URL
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

The companion generates a random action token in the current user's local
application data. The iCUE widget retrieves it through the loopback API, and
state-changing requests are rejected without the token. A manually configured
`EDGE_COMPANION_TOKEN` remains available for advanced deployments.

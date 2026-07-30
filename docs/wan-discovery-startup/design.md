# Shared Companion WAN Discovery and Startup

**Status:** Draft

**Author:** Codex  **Date:** 2026-07-30

## Summary

Extend the reusable Edge Companion with router-neutral, read-only WAN discovery
and current-user Windows startup control. WAN discovery tries UPnP Internet
Gateway Device (IGD) first, then the NAT Port Mapping Protocol (NAT-PMP), and
returns an explicit unavailable result when neither protocol works. No router
credentials, model-specific adapters, external probe applications, port
mappings, or cloud services are used.

Keep all widgets and the companion together on the main branch as a monorepo.
Each widget remains independently packageable in its own directory, while one
modular companion supplies only the capabilities requested by installed
widgets.

## Goals

- Discover the gateway's IPv4 WAN address without knowing its manufacturer.
- Make discovery read-only and limited to the local network.
- Try UPnP IGD before NAT-PMP and expose which source succeeded.
- Return `unavailable` without repeated noisy errors when neither is supported.
- Let a widget setting enable or disable Edge Companion at Windows sign-in.
- Keep one companion installation usable by any combination of widgets.
- Preserve the existing loopback-only, versioned API and partial-result model.

## Non-goals

- Creating, deleting, or enumerating router port mappings.
- Supporting PCP in this iteration.
- Scraping router administration pages or storing router credentials.
- Bundling a split-tunnel helper or using an external non-VPN probe.
- Guaranteeing discovery when UPnP/NAT-PMP is disabled, LAN access is blocked by
  the VPN, or the network uses an unsupported gateway.
- Installing the companion from inside an iCUE widget.

## Constraints

- iCUE's HTML runtime cannot send SSDP or NAT-PMP UDP packets, inspect Windows
  routes, modify Windows startup, or execute NordVPN commands. The companion
  remains required for those capabilities.
- UPnP discovery uses UDP multicast and a local HTTP/SOAP request. NAT-PMP uses
  UDP to the active IPv4 gateway. VPN firewall or "invisible on LAN" features
  can block both.
- The widget receives settings during initialization and later through
  `onDataUpdated`; initialization alone must not unexpectedly rewrite startup
  configuration.
- Startup configuration is a state-changing Windows operation and uses the
  companion's existing action authorization.

## Proposed design

### Repository and modules

Use a single main branch with this layout:

```text
companion/                 Shared Edge Companion host and capability modules
nordvpn-edge/              Independently validated/packageable widget
future-widget/             Another independently packageable widget
docs/                      Shared architecture and per-feature designs
scripts/                   Shared build, package, install, and development tools
```

Branches remain temporary development/release branches, not permanent widget
partitions. Permanent widget branches would prevent users and maintainers from
combining widgets and shared companion changes in one coherent release.

The companion starts all lightweight modules but performs network discovery only
when a relevant endpoint is requested. Modules cache results and failures for a
short interval, so an unused widget capability has negligible ongoing work.

### WAN discovery flow

`RouterWanModule` becomes a router-neutral `WanDiscoveryModule` while preserving
the existing response fields used by the widget.

1. Identify active, non-loopback IPv4 interfaces and their IPv4 gateways.
2. For each candidate interface, send an SSDP `M-SEARCH` for UPnP IGD and
   WANIP/WANPPP connection services, bound to that interface.
3. Fetch only LAN-addressed device-description URLs from SSDP responses.
4. Resolve the advertised WAN connection control URL and invoke only
   `GetExternalIPAddress`.
5. If UPnP fails, send NAT-PMP public-address opcode `0` to the interface's
   gateway on UDP port `5351`.
6. Accept only well-formed IPv4 responses from the expected LAN gateway.
7. Return the first globally routable result with source `upnp-igd` or
   `nat-pmp`. Otherwise return `unavailable`.

Discovery never invokes UPnP mapping actions and never sends NAT-PMP mapping
opcodes. Per-attempt timeouts are short, total work is bounded, and the result
is cached. Description and control URLs are restricted to HTTP on private,
link-local, or same-subnet addresses to prevent SSDP responses from turning the
companion into an arbitrary URL fetcher.

The dashboard response remains compatible:

```json
{
  "routerWanIp": "203.0.113.10",
  "routerSource": "upnp-igd"
}
```

If no provider succeeds, both values remain null and the widget displays
`WAN unavailable`.

### Windows startup setting

Add an iCUE switch named `Start companion with Windows`, defaulting to off for
new installations. The companion exposes:

- `GET /api/v1/system/startup` for current startup state.
- `POST /api/v1/system/startup` with `{ "enabled": true | false }`.

The POST endpoint requires the existing action token and manages one exact
current-user startup entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The value launches the
installed companion executable with no widget-controlled path or arguments.
Enabling and disabling are idempotent.

The widget does not mutate startup during `onICUEInitialized`. It records the
initial setting, then sends the POST only when a later `onDataUpdated` changes
the switch. Success or failure appears in the existing action-status area. If
the companion is offline, the widget asks the user to start it and toggle the
setting again.

Development runs through `dotnet run` do not register that transient executable.
The startup endpoint reports `unsupported_install` until the companion is
published or installed at a stable path.

## Alternatives and tradeoffs

### Permanent branch per widget

This isolates widget histories but duplicates shared files and makes a combined
companion release difficult. A monorepo main branch retains independent widget
packages without fragmenting shared runtime work.

### Public-IP website

A request made inside the VPN observes the VPN exit and cannot independently
establish the WAN address.

### Router-specific APIs

These may be accurate but do not support a public widget across unknown router
models and often require credentials.

### Split-tunnel probe

This is router-independent but requires users to configure an additional
executable and can conflict with kill-switch behavior. It is explicitly excluded
from this design.

### Windows scheduled task or service

Both can support startup before user login but add elevation and installation
complexity. The per-user Run entry matches the current-user companion and needs
no administrator rights.

## Risks

- **UPnP is disabled:** NAT-PMP is attempted; otherwise the UI clearly reports
  unavailable.
- **Malicious SSDP response:** restrict fetched URLs to the local network,
  validate response sizes/content, and invoke only the fixed read action.
- **Multiple gateways:** prefer active interfaces with a default route and use
  bounded attempts rather than assuming one adapter.
- **CGNAT/private external address:** reject it as a public WAN result and
  report unavailable rather than presenting it as internet-routable.
- **Startup switch changed while offline:** show an inline failure and require a
  deliberate retry rather than silently applying it later.
- **Action token absent:** leave the switch unchanged and show that companion
  actions require configuration.

## Rollout

1. Add protocol parsers and unit tests for UPnP and NAT-PMP responses.
2. Add bounded live discovery and cache behavior to the WAN module.
3. Add startup state management and API tests.
4. Add the widget switch and `WAN unavailable` display.
5. Validate and package the widget, then exercise the live endpoint on the
   current network.
6. Add installer/published-build wiring for the stable companion executable.

Backout removes the startup value, restores the previous unavailable WAN
provider, and leaves other companion modules and widgets intact.

## Open questions

- Should the first public installer default companion startup to enabled while
  keeping the widget switch default off?

## Decision

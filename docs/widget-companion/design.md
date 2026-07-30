# Reusable iCUE Widget Companion

**Status:** Approved

**Author:** Codex  **Date:** 2026-07-30

## Summary

Create one Windows localhost service, tentatively named **Edge Companion**, that
provides versioned HTTP APIs to all widgets in this repository. The host owns
process execution, operating-system metrics, caching, and access to local
applications; small capability modules own integrations such as NordVPN. Widgets
remain self-contained HTML packages and communicate only with the loopback
service.

This separates privileged or Windows-specific work from iCUE's HTML runtime,
avoids one background process per widget, and gives future widgets a stable
integration surface.

## Goals

- Serve multiple widgets and combinations of widgets from one localhost process.
- Provide a versioned, discoverable API with independent capability modules.
- Bind only to loopback and reject untrusted browser origins.
- Share polling, caching, network throughput sampling, logging, and health state.
- Allow modules to expose read-only data and narrowly defined actions.
- Install and update as one Windows application with optional startup behavior.
- Keep widgets functional in loading, unavailable, partial-data, and error states.
- Support XENEON EDGE widget resizing through responsive layouts and iCUE
  customization properties.

## Non-goals

- A general-purpose command runner or proxy exposed to widgets.
- Remote LAN or internet access to the service.
- Storing NordVPN account credentials.
- Replacing iCUE's widget settings or packaging.
- Guessing the WAN IP from a request that follows the PC's VPN route.
- Supporting non-Windows platforms in the first version.

## Constraints

- iCUE widgets are HTML/JavaScript surfaces and do not expose arbitrary Windows
  process execution.
- XENEON EDGE's full canvas is 1688 × 696, but widgets may occupy smaller regions.
- NordVPN for Windows exposes documented connect and disconnect CLI actions, but
  its temporary Pause UI does not have a documented Windows CLI equivalent.
- The installed NordVPN application may change executable paths between updates.
- A correct ISP-facing WAN IP requires either a supported router API or a
  probe whose traffic is guaranteed not to traverse the PC VPN.
- The service must not accept arbitrary executable names, arguments, URLs, or
  filesystem paths from widget requests.

## Proposed design

### Process and module structure

Use a single .NET 8 Windows process. .NET provides a small self-contained HTTP
host, Windows service/startup integration, process management, structured
logging, and access to network adapter counters without adding a Node runtime to
the installed product.

The host contains:

- `EdgeCompanion.Host`: loopback HTTP server, configuration, origin policy,
  module lifecycle, health, caching, and logging.
- `EdgeCompanion.Contracts`: stable response envelopes, error codes, module
  metadata, and API-version types.
- `EdgeCompanion.Modules.SystemNetwork`: active adapter discovery and sampled
  upload/download byte rates.
- `EdgeCompanion.Modules.PublicIp`: public IP lookup with short caching and
  explicit source metadata.
- `EdgeCompanion.Modules.NordVpn`: NordVPN discovery, connection status, fastest
  US connection action, and pause orchestration.
- `EdgeCompanion.Modules.RouterWan`: optional provider interface for a
  router API or an external non-VPN probe.

Modules register fixed routes and fixed actions with the host. They cannot add
arbitrary cross-origin behavior. A module can be unavailable without taking down
the host or other modules.

### API shape

All APIs live under `/api/v1`.

- `GET /api/v1/health`: service version, uptime, and overall health.
- `GET /api/v1/capabilities`: installed modules, versions, availability, and
  supported actions.
- `GET /api/v1/network/throughput`: current upload/download rates and sampled
  adapter identity.
- `GET /api/v1/network/public-ip`: the PC's observed public IP and lookup source.
- `GET /api/v1/router/wan`: router WAN IP, source, confidence, and freshness, or
  an explicit `not_configured` result.
- `GET /api/v1/nordvpn/status`: connection state, server, country, city,
  protocol, and freshness.
- `POST /api/v1/nordvpn/actions/connect-fastest-us`: connects to NordVPN's best
  United States server using a fixed server group.
- `POST /api/v1/nordvpn/actions/pause`: pauses for an allowlisted duration.

Responses use a common envelope with `data`, `observedAt`, `source`, and an
optional structured `error`. Status endpoints may return partial data rather
than failing the entire combined widget.

The NordVPN widget should request module-specific endpoints in parallel. A
future optional aggregation endpoint may combine data for low-overhead polling,
but the host will not make every widget depend on one large shared response.

### Pause behavior

Because NordVPN documents Pause in the Windows UI but not as a CLI command, the
first supported implementation is:

1. record the intended previous state and a monotonic resume deadline;
2. invoke NordVPN's documented disconnect command;
3. show the service-managed paused state and remaining time;
4. invoke the documented fastest/recommended reconnect command at the deadline;
5. persist the deadline so a service restart does not silently lose the resume.

The widget must label this accurately as a companion-managed pause. Only
allowlisted durations (5, 15, 30, or 60 minutes) are accepted. If NordVPN later
documents a native pause API, the module can switch implementations without
changing the widget contract.

### Security model

- Listen on `127.0.0.1` only; never `0.0.0.0`.
- Use a fixed, configurable port with a conservative default.
- Allow `GET` from the iCUE widget environment and allow action requests only
  from approved local origins. Confirm the exact iCUE origin during device
  testing.
- Require a per-install random action token for state-changing requests. Store
  it with current-user ACLs and expose it to widgets through an iCUE setting
  during initial setup.
- Do not log tokens, public IP history, command output containing user data, or
  secrets.
- Use fixed command paths discovered from trusted installation locations and
  fixed argument templates. Validate every requested enum and numeric range.
- Rate-limit actions and serialize actions within each module.

### Widget resizing and customization

All widgets use intrinsic CSS layout plus container/window breakpoints rather
than assuming the full 1688 × 696 canvas.

The NordVPN widget has three compositions:

- **Wide:** protection, two-hop IP identity, and throughput in three columns.
- **Medium:** protection spans the first row; identity and throughput share the
  second row.
- **Compact:** protection and essential controls lead; identity becomes two
  concise rows; the chart hides before labels or actions become unreadable.

Minimum touch targets remain 44 × 44 pixels. Dynamic numbers use tabular figures.
Overflow and internal scrolling are avoided.

Initial iCUE customization properties:

- companion service URL and action token;
- accent color and background opacity;
- compact/comfortable density;
- pause duration;
- show/hide throughput chart;
- show/hide router identity;
- speed unit (`MB/s` or `Mb/s`);
- status detail level.

Settings remain presentation or connection configuration only. They never accept
commands, executable paths, or arbitrary API routes.

### Installation and offline start

The `.icuewidget` package cannot install or start a Windows executable. Ship a
Windows installer bundle containing the published Edge Companion application
and the packaged widget:

- install Edge Companion in a stable per-machine location;
- configure automatic startup;
- register the `edgecompanion://start` protocol;
- have that protocol start or wake the installed companion without accepting
  arbitrary arguments;
- place the `.icuewidget` package where the installer can offer it for iCUE
  import.

The widget declares iCUE's Link Provider and renders a compact service-status
control. When healthy it is a disabled `Service online` indicator. When offline
it becomes `Start service` and opens `edgecompanion://start`, then polls the
health endpoint for recovery. Browser fallback is intended only for development;
the installed protocol handler is the supported production path.

## Alternatives and tradeoffs

### One companion per widget

Simpler for the first widget, but duplicates ports, installation, startup,
logging, network polling, and security work. It becomes harder to update and
causes competing samples of the same system metrics.

### Node.js host

Matches the repository's JavaScript tooling and is quick to prototype. It adds a
runtime or larger packaged executable, and Windows service/process integration
is less direct. It remains a reasonable fallback if .NET packaging becomes a
material obstacle.

### PowerShell scripts invoked by each widget

PowerShell is useful for diagnostics but the widget cannot safely execute it
directly. Exposing a generic script bridge would create an unnecessarily broad
command surface.

### Query two public-IP services from the PC

Both requests normally follow the same VPN route and therefore cannot establish
the ISP-facing router address. Provider diversity does not create route
diversity.

### Read router WAN IP from the local gateway

This is the best local source when the router model and a stable,
authenticated API are known. It stays behind the optional `RouterWan` provider
because the model and supported interface have not yet been identified.

## Risks

- **NordVPN CLI output changes:** parse defensively, record the detected app
  version, and surface `unsupported_version` instead of guessing.
- **Reconnect differs from the prior server:** clearly define managed Pause as
  reconnecting to recommended/fastest unless reliable previous-server restore is
  supported.
- **iCUE origin is opaque or inconsistent:** validate on the actual device before
  finalizing CORS and token bootstrap.
- **Public-IP provider outage:** cache briefly, expose freshness, and support a
  small ordered provider list.
- **Wrong network adapter sampled:** select the active default-route adapter and
  return its name so the choice is observable.
- **Router credentials:** prefer a least-privileged API or external probe; never
  embed router admin credentials in the widget package.

## Rollout

1. Build the host, contracts, health endpoint, and system network module.
2. Add a mock NordVPN module and verify the widget across wide, medium, and
   compact sizes.
3. Implement documented NordVPN connect/disconnect and status discovery against
   the installed Windows version.
4. Add managed Pause with restart-safe scheduling and action-token protection.
5. Select and implement the router WAN provider after identifying the
   router model or a suitable non-VPN probe.
6. Package Edge Companion for current-user installation and optional startup.
7. Validate and package the widget; do not commit the generated `.icuewidget`
   unless requested.

Backout is straightforward: stop/uninstall Edge Companion. Widgets remain
installed and display a clear companion-unavailable state.

## Open questions

- Which router model is in use, and does it expose a supported local API?
- Should managed Pause reconnect to the fastest server or attempt to restore the
  exact previous server when that server remains available?
- Should Edge Companion run at user login or as a Windows service before login?
- What origin does iCUE 5.48.58 use for packaged widget requests on the actual
  XENEON EDGE?

## Decision

Approved by the user on 2026-07-30. Implement the reusable host with .NET 8; the
user authorized installing the missing .NET SDK. Preserve the proposed module
boundaries and `/api/v1` contract.

const state = {
  serviceUrl: "http://127.0.0.1:48620",
  actionToken: "",
  tokenPromise: null,
  startWithWindows: false,
  pauseMinutes: 5,
  accentColor: "#4d8dff",
  backgroundColor: "#07111f",
  backgroundOpacity: 100,
  density: "comfortable",
  speedUnit: "MBps",
  showRouterIdentity: true,
  showThroughputChart: true,
  polling: false,
  startupBusy: false,
  timer: null,
  history: {
    download: Array(30).fill(0),
    upload: Array(30).fill(0),
  },
};

const elements = {};
let layoutObserver;
let settingsInitialized = false;
let previousStartWithWindows = false;

function updateLayoutMode() {
  const preview = typeof iCUE !== "undefined" && Boolean(iCUE.isPreview);
  const effectiveWidth = window.innerWidth * (preview ? 1.75 : 1);
  const mode = effectiveWidth >= 1240 ? "wide" : effectiveWidth > 720 ? "medium" : "compact";
  document.body.classList.toggle("preview-mode", preview);
  document.body.classList.remove("layout-wide", "layout-medium", "layout-compact");
  document.body.classList.add(`layout-${mode}`);
}

function cacheElements() {
  [
    "status-dot", "service-button", "updated-at", "status-label", "server-name", "connection-detail", "server-location",
    "pause-button", "pause-label", "fastest-button", "action-message", "machine-ip",
    "machine-location", "router-ip", "router-source", "identity-verdict", "identity-note",
    "download-speed", "upload-speed", "download-line", "upload-line", "speed-unit",
  ].forEach((id) => {
    elements[id] = document.getElementById(id);
  });
}

function applySettings(settings = {}, allowStartupChange = false) {
  const requestedStartup = Object.prototype.hasOwnProperty.call(settings, "startWithWindows")
    ? Boolean(settings.startWithWindows)
    : Boolean(state.startWithWindows);
  const configureStartupNow = allowStartupChange
    && settingsInitialized
    && requestedStartup !== previousStartWithWindows;
  Object.assign(state, settings);
  state.actionToken = "";
  state.tokenPromise = null;
  state.startWithWindows = requestedStartup;
  previousStartWithWindows = requestedStartup;
  settingsInitialized = true;
  state.serviceUrl = String(state.serviceUrl || "").replace(/\/+$/, "");
  state.pauseMinutes = Number(state.pauseMinutes) || 5;
  document.documentElement.style.setProperty("--nord", state.accentColor || "#4d8dff");
  document.documentElement.style.setProperty("--widget-background", state.backgroundColor || "#07111f");
  const backgroundOpacity = Math.max(20, Math.min(100, Number(state.backgroundOpacity) || 100));
  document.documentElement.style.setProperty("--background-opacity", `${backgroundOpacity / 100}`);
  document.documentElement.style.setProperty("--panel-opacity", `${backgroundOpacity}%`);
  document.body.classList.toggle("density-compact", state.density === "compact");
  document.body.classList.toggle("hide-router", !state.showRouterIdentity);
  document.body.classList.toggle("hide-chart", !state.showThroughputChart);
  if (elements["pause-label"]) {
    elements["pause-label"].textContent = `Pause ${state.pauseMinutes} min`;
    elements["speed-unit"].textContent = state.speedUnit === "Mbps" ? "Mb/s" : "MB/s";
  }
  restartPolling();
  if (configureStartupNow) configureStartup(requestedStartup);
}

function restartPolling() {
  if (!elements["status-dot"]) return;
  clearInterval(state.timer);
  refreshStatus();
  state.timer = setInterval(refreshStatus, 2000);
}

async function ensureActionToken() {
  if (state.actionToken) return state.actionToken;
  if (!state.tokenPromise) {
    state.tokenPromise = fetch(`${state.serviceUrl}/api/v1/auth/token`, {
      cache: "no-store",
      signal: AbortSignal.timeout(4500),
    })
      .then(async (response) => {
        const result = await response.json().catch(() => ({}));
        if (!response.ok || !result.data?.token) {
          throw new Error(result.errors?.[0]?.message || "Unable to authorize widget actions");
        }
        state.actionToken = result.data.token;
        return state.actionToken;
      })
      .finally(() => {
        state.tokenPromise = null;
      });
  }
  return state.tokenPromise;
}

async function refreshStatus() {
  if (state.polling || !state.serviceUrl) return;
  state.polling = true;
  try {
    const response = await fetch(`${state.serviceUrl}/api/v1/nordvpn/dashboard`, {
      cache: "no-store",
      signal: AbortSignal.timeout(4500),
    });
    if (!response.ok) throw new Error(`Companion returned ${response.status}`);
    const payload = await response.json();
    renderStatus(payload.data);
  } catch (error) {
    renderOffline(error);
  } finally {
    state.polling = false;
  }
}

function renderStatus(data) {
  const connected = data.vpn?.state === "connected";
  const paused = data.vpn?.state === "paused";
  elements["status-dot"].className = `status-dot ${connected ? "connected" : ""}`;
  elements["status-label"].textContent = connected ? "Protected" : paused ? "Protection paused" : "Not connected";
  elements["status-label"].style.color = connected ? "var(--safe)" : "var(--warn)";
  elements["server-name"].textContent = connected ? "NordLynx" : paused ? "Paused" : "Disconnected";
  elements["connection-detail"].textContent = data.vpn?.protocol || "NordVPN is reachable.";
  const serverLocation = [
    data.vpn?.server,
    [data.vpn?.city, data.vpn?.country].filter(Boolean).join(", "),
  ].filter(Boolean).join(" · ");
  elements["server-location"].textContent = serverLocation;
  elements["server-location"].hidden = !serverLocation;
  elements["updated-at"].textContent = "Service online";
  elements["service-button"].className = "service-button is-online";
  elements["service-button"].disabled = true;
  elements["pause-button"].disabled = !connected;
  elements["fastest-button"].disabled = false;

  elements["machine-ip"].textContent = data.network?.machinePublicIp || "—";
  elements["machine-location"].textContent = data.network?.machineLocation || "Public IP unavailable";
  elements["router-ip"].textContent = data.network?.routerWanIp || "—";
  elements["router-source"].textContent = data.network?.routerSource || "WAN unavailable";

  const verified = Boolean(data.network?.routerWanIp && data.network?.machinePublicIp);
  const separated = verified && data.network.routerWanIp !== data.network.machinePublicIp;
  elements["identity-verdict"].textContent = separated ? "VPN verified" : verified ? "IPs match" : "Not verified";
  elements["identity-verdict"].className = `verdict ${separated ? "verified" : ""}`;
  elements["identity-note"].textContent = separated
    ? "The PC exits through a different public IP than the WAN."
    : verified
      ? "The PC and WAN report the same public IP; VPN protection is not confirmed by IP."
      : "WAN unavailable through UPnP IGD or NAT-PMP.";

  const speedMultiplier = state.speedUnit === "Mbps" ? 8 : 1;
  const download = normalizeSpeed(data.throughput?.downloadMBps) * speedMultiplier;
  const upload = normalizeSpeed(data.throughput?.uploadMBps) * speedMultiplier;
  elements["download-speed"].textContent = download.toFixed(2);
  elements["upload-speed"].textContent = upload.toFixed(2);
  pushHistory(download, upload);
}

function renderOffline(error) {
  elements["status-dot"].className = "status-dot error";
  elements["status-label"].textContent = "Status unavailable";
  elements["status-label"].style.color = "var(--danger)";
  elements["server-name"].textContent = "Companion offline";
  elements["connection-detail"].textContent = "Start the local NordVPN companion service.";
  elements["server-location"].textContent = "";
  elements["server-location"].hidden = true;
  elements["updated-at"].textContent = "Start service";
  elements["service-button"].className = "service-button is-offline";
  elements["service-button"].disabled = false;
  elements["pause-button"].disabled = true;
  elements["fastest-button"].disabled = true;
  elements["action-message"].textContent = error?.message || "Unable to reach companion";
}

function normalizeSpeed(value) {
  const number = Number(value);
  return Number.isFinite(number) && number >= 0 ? number : 0;
}

function startCompanionService() {
  elements["action-message"].textContent = "Requesting service start…";
  const launchUrl = "edgecompanion://start";
  if (
    window.plugins?.Linkprovider
    && typeof pluginLinkprovider_initialized !== "undefined"
    && pluginLinkprovider_initialized
  ) {
    window.plugins.Linkprovider.open(launchUrl);
  } else {
    window.open(launchUrl, "_blank");
  }
  setTimeout(refreshStatus, 1500);
}

function bindActivation(element, handler) {
  let handledPointerAt = 0;
  element.addEventListener("pointerup", (event) => {
    if (event.pointerType !== "touch" && event.pointerType !== "pen") return;
    handledPointerAt = Date.now();
    event.preventDefault();
    if (!element.disabled) handler();
  });
  element.addEventListener("click", () => {
    if (Date.now() - handledPointerAt < 750 || element.disabled) return;
    handler();
  });
}

function pushHistory(download, upload) {
  state.history.download.push(download);
  state.history.upload.push(upload);
  state.history.download.shift();
  state.history.upload.shift();
  const ceiling = Math.max(1, ...state.history.download, ...state.history.upload);
  elements["download-line"].setAttribute("d", makePath(state.history.download, ceiling));
  elements["upload-line"].setAttribute("d", makePath(state.history.upload, ceiling));
}

function makePath(values, ceiling) {
  return values.map((value, index) => {
    const x = (index / (values.length - 1)) * 520;
    const y = 122 - (value / ceiling) * 112;
    return `${index ? "L" : "M"}${x.toFixed(1)} ${y.toFixed(1)}`;
  }).join(" ");
}

async function sendAction(path, body) {
  elements["action-message"].textContent = "Working…";
  elements["pause-button"].disabled = true;
  elements["fastest-button"].disabled = true;
  try {
    const actionToken = await ensureActionToken();
    const response = await fetch(`${state.serviceUrl}/api${path}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Edge-Token": actionToken,
      },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(12000),
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.errors?.[0]?.message || `Action failed (${response.status})`);
    elements["action-message"].textContent = result.data?.message || "Done";
    setTimeout(refreshStatus, 500);
  } catch (error) {
    elements["action-message"].textContent = error.message;
    refreshStatus();
  }
}

async function configureStartup(enabled) {
  if (state.startupBusy) return;
  state.startupBusy = true;
  elements["action-message"].textContent = enabled
    ? "Enabling Windows startup…"
    : "Disabling Windows startup…";
  try {
    const actionToken = await ensureActionToken();
    const response = await fetch(`${state.serviceUrl}/api/v1/system/startup`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Edge-Token": actionToken,
      },
      body: JSON.stringify({ enabled }),
      signal: AbortSignal.timeout(5000),
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.errors?.[0]?.message || `Startup setting failed (${response.status})`);
    elements["action-message"].textContent = result.data?.enabled
      ? "Companion will start with Windows"
      : "Windows startup disabled";
  } catch (error) {
    elements["action-message"].textContent = error.message || "Unable to update Windows startup";
  } finally {
    state.startupBusy = false;
  }
}

document.addEventListener("DOMContentLoaded", () => {
  cacheElements();
  updateLayoutMode();
  layoutObserver = new ResizeObserver(updateLayoutMode);
  layoutObserver.observe(document.documentElement);
  bindActivation(elements["service-button"], startCompanionService);
  bindActivation(elements["pause-button"], () => {
    sendAction("/v1/nordvpn/actions/pause", { minutes: state.pauseMinutes });
  });
  bindActivation(elements["fastest-button"], () => {
    sendAction("/v1/nordvpn/actions/connect-fastest-us", {});
  });
  applySettings();
});

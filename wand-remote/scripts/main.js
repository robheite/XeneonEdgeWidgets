(function () {
  "use strict";

  const DEFAULT_SERVICE_URL = "http://127.0.0.1:48620";
  const DEFAULT_PROXY_URL = "http://localhost:48620";
  const LEGACY_PROXY_URLS = new Set(["http://127.0.0.1:48621", "http://localhost:48621"]);
  const PROXY_CACHE_KEY = "edge-panel-0.1.7";
  let initialized = false;
  let launchTimer = null;

  function isPreview() {
    return typeof iCUE !== "undefined" && Boolean(iCUE.isPreview);
  }

  function readAutoLaunch() {
    return typeof autoLaunch === "undefined" ? true : Boolean(autoLaunch);
  }

  function getServiceUrl() {
    const configured = typeof serviceUrl === "undefined" ? DEFAULT_SERVICE_URL : String(serviceUrl || DEFAULT_SERVICE_URL);
    return configured.replace(/\/+$/, "");
  }

  function getProxyUrl() {
    const configured = typeof proxyUrl === "undefined" ? DEFAULT_PROXY_URL : String(proxyUrl || DEFAULT_PROXY_URL);
    const normalized = configured.replace(/\/+$/, "");
    return LEGACY_PROXY_URLS.has(normalized.toLowerCase()) ? DEFAULT_PROXY_URL : normalized;
  }

  function updatePreviewLayout() {
    const preview = isPreview();
    document.body.classList.toggle("preview-mode", preview);
    if (!preview) return;

    const scale = Math.min(window.innerWidth / 1688, window.innerHeight / 696);
    document.documentElement.style.setProperty("--preview-scale", String(scale));
    document.documentElement.style.setProperty("--preview-offset-x", `${(window.innerWidth - (1688 * scale)) / 2}px`);
    document.documentElement.style.setProperty("--preview-offset-y", `${(window.innerHeight - (696 * scale)) / 2}px`);
  }

  function openRemote() {
    clearTimeout(launchTimer);
    document.getElementById("status").textContent = "Opening Wand Remote…";
    window.location.assign(`${getProxyUrl()}/wand/remote/?edgePanel=${PROXY_CACHE_KEY}`);
  }

  async function checkCompanion() {
    try {
      const response = await fetch(`${getServiceUrl()}/api/v1/health`, { cache: "no-store", signal: AbortSignal.timeout(2500) });
      return response.ok;
    } catch {
      return false;
    }
  }

  function startCompanion() {
    document.getElementById("status").textContent = "Starting Edge Companion…";
    if (window.plugins?.Linkprovider && typeof pluginLinkprovider_initialized !== "undefined" && pluginLinkprovider_initialized) {
      window.plugins.Linkprovider.open("edgecompanion://start");
    } else {
      window.open("edgecompanion://start", "_blank");
    }
    setTimeout(updateSettings, 1500);
  }

  function updateSettings() {
    clearTimeout(launchTimer);
    updatePreviewLayout();

    const preview = isPreview();
    const badge = document.getElementById("preview-badge");
    badge.hidden = !preview;

    if (preview) {
      document.getElementById("status").textContent = "Navigation paused for preview";
      document.getElementById("launch-title").textContent = "Ready for the panel";
      document.getElementById("launch-detail").textContent = "On XENEON EDGE, this widget opens Wand Remote in the panel webview. The editor keeps this complete 1688 × 696 preview visible.";
      return;
    }

    checkCompanion().then((online) => {
      const button = document.getElementById("launch-button");
      if (!online) {
        document.getElementById("status").textContent = "Edge Companion is offline";
        document.getElementById("launch-title").textContent = "Start the companion";
        document.getElementById("launch-detail").textContent = "The local companion safely adapts Wand Remote for iCUE. Start it, then the panel will connect.";
        button.querySelector("span").textContent = "Start Edge Companion";
        button.onclick = startCompanion;
        return;
      }
      button.querySelector("span").textContent = "Open Wand Remote";
      button.onclick = openRemote;
      document.getElementById("status").textContent = readAutoLaunch() ? "Opening automatically…" : "Ready to connect";
      if (initialized && readAutoLaunch()) launchTimer = setTimeout(openRemote, 900);
    });
  }

  function initialize() {
    initialized = true;
    updateSettings();
  }

  document.addEventListener("DOMContentLoaded", function () {
    document.getElementById("launch-button").onclick = openRemote;
    updatePreviewLayout();
    window.addEventListener("resize", updatePreviewLayout);
  });

  window.wandRemote = { initialize, updateSettings };
}());

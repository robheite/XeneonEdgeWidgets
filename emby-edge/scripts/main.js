const state = {
  serviceUrl: "http://127.0.0.1:48620",
  serverUrl: "http://192.168.1.11:8096/",
  session: null,
  users: [],
  views: [],
  currentView: null,
  history: [],
  item: null,
  playableItems: [],
  playbackInfo: null,
  playSessionId: null,
  playMethod: "Transcode",
  playStartedReported: false,
  streamOffsetSeconds: 0,
  progressTimer: null,
  playbackWatchdog: null,
  controlsTimer: null,
  lastAudibleVolume: 1,
  transitionPending: false,
  keyboardControlFocus: false,
  keyboardInteraction: false,
  stopping: false
};

const $ = id => document.getElementById(id);

function setting(name, fallback) {
  try {
    return typeof window[name] !== "undefined" ? window[name] : fallback;
  } catch {
    return fallback;
  }
}

function updateSettings() {
  const old = state.serverUrl;
  state.serviceUrl = String(setting("serviceUrl", state.serviceUrl)).replace(/\/$/, "");
  state.serverUrl = String(setting("embyServer", state.serverUrl)).replace(/\/$/, "") + "/";
  applyPreview();
  if (old !== state.serverUrl) logout(false);
  boot();
}

function applyPreview() {
  const preview = typeof iCUE !== "undefined" && iCUE.isPreview;
  document.documentElement.classList.toggle("preview-mode", preview);
  const app = $("app");
  if (!preview) {
    app.style.width = "100%";
    app.style.height = "100%";
    app.style.transform = "";
    return;
  }
  app.style.width = "1688px";
  app.style.height = "696px";
  const scale = Math.min(innerWidth / 1688, innerHeight / 696);
  app.style.transform = `translate(${(innerWidth - 1688 * scale) / 2}px,${(innerHeight - 696 * scale) / 2}px) scale(${scale})`;
}

async function api(path, options = {}) {
  const response = await fetch(`${state.serviceUrl}/api/v1/emby${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(state.session ? { "X-Emby-Token": state.session.token } : {}),
      ...(options.headers || {})
    },
    signal: AbortSignal.timeout(15000)
  });
  const type = response.headers.get("content-type") || "";
  const data = type.includes("json") ? await response.json() : response;
  if (!response.ok) throw new Error(data?.errors?.[0]?.message || data?.ResponseStatus?.Message || `Emby request failed (${response.status})`);
  return data;
}

async function boot() {
  const saved = JSON.parse(localStorage.getItem("emby-edge-session") || "null");
  if (saved?.serverUrl === state.serverUrl) state.session = saved;
  try {
    state.users = await api(`/public-users?serverUrl=${encodeURIComponent(state.serverUrl)}`);
    $("server-name").textContent = new URL(state.serverUrl).host;
    if (!state.session) return showLogin();
    await loadViews();
  } catch (error) {
    showStatus(error.message + " — start Edge Companion and check the server setting.");
  }
}

function showStatus(message) {
  $("status").textContent = message;
  $("status").hidden = false;
  $("browser").hidden = true;
  $("details").hidden = true;
}

function showLogin() {
  renderUsers();
  if (!$("login-dialog").open) $("login-dialog").showModal();
  showStatus("Choose an Emby user to continue.");
}

function renderUsers() {
  const root = $("public-users");
  root.hidden = !state.users.length;
  root.replaceChildren(...state.users.map((user, index) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "user-choice";
    button.textContent = user.Name;
    button.onclick = () => {
      document.querySelectorAll(".user-choice").forEach(choice => choice.classList.remove("selected"));
      button.classList.add("selected");
      state.selectedUser = user;
      $("username").value = user.Name;
      $("password").focus();
    };
    if (index === 0) setTimeout(() => button.click(), 0);
    return button;
  }));
}

async function login(event) {
  event.preventDefault();
  const username = $("username").value.trim();
  if (!username) return;
  try {
    const data = await api("/authenticate", {
      method: "POST",
      body: JSON.stringify({ serverUrl: state.serverUrl, username, password: $("password").value })
    });
    state.session = { userId: data.User.Id, userName: data.User.Name, token: data.AccessToken, serverUrl: state.serverUrl };
    localStorage.setItem("emby-edge-session", JSON.stringify(state.session));
    $("login-dialog").close();
    $("password").value = "";
    $("login-error").textContent = "";
    await loadViews();
  } catch (error) {
    $("login-error").textContent = error.message;
  }
}

function logout(reopen = true) {
  state.session = null;
  localStorage.removeItem("emby-edge-session");
  if (reopen) showLogin();
}

async function loadViews() {
  const data = await api(`/users/${state.session.userId}/views?serverUrl=${encodeURIComponent(state.serverUrl)}`);
  state.views = data.Items || [];
  $("user-name").textContent = state.session.userName;
  $("avatar").textContent = state.session.userName.slice(0, 1).toUpperCase();
  renderViews();
  await browse(state.views[0]?.Id || null, state.views[0]?.Name || "All media", false);
}

function renderViews() {
  const all = document.createElement("option");
  all.value = "";
  all.textContent = "All libraries";
  $("search-scope").replaceChildren(all, ...state.views.map(view => {
    const option = document.createElement("option");
    option.value = view.Id;
    option.textContent = view.Name;
    return option;
  }));
  $("libraries").replaceChildren(...state.views.map(view => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "library";
    button.textContent = view.Name;
    button.onclick = () => browse(view.Id, view.Name, false);
    button.dataset.id = view.Id;
    return button;
  }));
}

async function browse(parentId, title, push = true) {
  try {
    if (push && state.currentView) state.history.push(state.currentView);
    state.currentView = { id: parentId, title };
    document.querySelectorAll(".library").forEach(button => button.classList.toggle("active", button.dataset.id === parentId));
    const data = await api(`/users/${state.session.userId}/items?serverUrl=${encodeURIComponent(state.serverUrl)}${parentId ? `&parentId=${encodeURIComponent(parentId)}` : ""}`);
    renderItems(data.Items || [], title);
    $("back-button").hidden = !state.history.length;
  } catch (error) {
    showStatus(error.message);
  }
}

function imageUrl(id, width = 420) {
  return `${state.serviceUrl}/api/v1/emby/items/${id}/image?serverUrl=${encodeURIComponent(state.serverUrl)}&accessToken=${encodeURIComponent(state.session.token)}&width=${width}`;
}

function isPlayableItem(item) {
  return !item.IsFolder && (item.MediaType === "Video" || ["Movie", "Episode", "Video", "Trailer", "MusicVideo"].includes(item.Type));
}

function renderItems(items, title) {
  state.playableItems = items.filter(isPlayableItem);
  $("status").hidden = true;
  $("details").hidden = true;
  $("browser").hidden = false;
  $("view-title").textContent = title;
  $("item-count").textContent = `${items.length} items`;
  $("items").replaceChildren(...items.map(item => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "card";
    const progress = Math.max(0, Math.min(100, item.UserData?.PlayedPercentage || 0));
    button.innerHTML = `<div class="poster" style="background-image:url('${imageUrl(item.Id)}')">${progress ? `<span class="progress"><i style="width:${progress}%"></i></span>` : ""}</div><strong></strong><small></small>`;
    button.querySelector("strong").textContent = item.Name;
    button.querySelector("small").textContent = item.ProductionYear || item.Type || "";
    button.onclick = () => item.IsFolder ? browse(item.Id, item.Name) : showDetails(item.Id);
    return button;
  }));
  updateQueueControls();
}

async function search(event) {
  event.preventDefault();
  const term = $("search-input").value.trim();
  if (!term) return;
  const parentId = $("search-scope").value;
  const data = await api(`/users/${state.session.userId}/items?serverUrl=${encodeURIComponent(state.serverUrl)}&searchTerm=${encodeURIComponent(term)}${parentId ? `&parentId=${encodeURIComponent(parentId)}` : ""}`);
  state.history.push(state.currentView);
  renderItems(data.Items || [], `Results for “${term}”`);
  $("back-button").hidden = false;
}

async function showDetails(id) {
  const item = await api(`/users/${state.session.userId}/items/${id}?serverUrl=${encodeURIComponent(state.serverUrl)}`);
  state.item = item;
  $("browser").hidden = true;
  $("details").hidden = false;
  $("item-type").textContent = item.Type || item.MediaType || "VIDEO";
  $("item-title").textContent = item.Name;
  $("item-meta").textContent = [item.ProductionYear, item.OfficialRating, formatTicks(item.RunTimeTicks)].filter(Boolean).join("  •  ");
  $("item-overview").textContent = item.Overview || "No description available.";
  $("backdrop").style.backgroundImage = `url('${imageUrl(item.Id, 1100)}')`;
  $("watched-button").textContent = item.UserData?.Played ? "↶ Mark unwatched" : "✓ Mark watched";
  updateQueueControls();
  try {
    state.playbackInfo = await api(`/items/${id}/playback-info?serverUrl=${encodeURIComponent(state.serverUrl)}&userId=${encodeURIComponent(state.session.userId)}`, { method: "POST", body: "{}" });
    renderMediaOptions();
  } catch (error) {
    state.playbackInfo = { MediaSources: item.MediaSources || [] };
    renderMediaOptions();
    console.warn("Playback negotiation unavailable", error);
  }
}

function renderMediaOptions() {
  const sources = state.playbackInfo?.MediaSources || [];
  $("media-source").replaceChildren(...sources.map((source, index) => {
    const option = document.createElement("option");
    option.value = source.Id;
    option.textContent = mediaSourceLabel(source, index);
    return option;
  }));
  const singleSource = sources.length <= 1;
  $("media-source-label").hidden = singleSource;
  $("media-options").classList.toggle("single-source", singleSource);
  $("media-source").onchange = renderTracks;
  $("media-options").hidden = !sources.length;
  renderTracks();
}

function mediaSourceLabel(source, index) {
  const name = String(source.Name || "").trim();
  const itemName = String(state.item?.Name || "").trim();
  const itemYear = String(state.item?.ProductionYear || "");
  const numericName = /^\d+(?:\.\d+)?$/.test(name);
  const titlePattern = itemName ? new RegExp(escapeRegExp(itemName), "ig") : null;
  const descriptiveRemainder = name
    .replace(titlePattern || /$^/, "")
    .replace(itemYear, "")
    .replace(/[\s\-_.()[\]{}:]+/g, "");
  if (name && !numericName && descriptiveRemainder) return name;

  const video = (source.MediaStreams || []).find(stream => stream.Type === "Video");
  const height = Number(video?.Height || 0);
  const quality = height >= 2000 ? "4K" : height >= 1000 ? "1080p" : height >= 700 ? "720p" : height >= 550 ? "576p" : height > 0 ? `${height}p` : "";
  const codec = String(video?.Codec || "").toUpperCase();
  const container = String(source.Container || "").toUpperCase();
  return [...new Set([quality, codec, container].filter(Boolean))].join(" · ") || `Version ${index + 1}`;
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function renderTracks() {
  const source = selectedSource();
  const streams = source?.MediaStreams || [];
  const audio = streams.filter(stream => stream.Type === "Audio");
  const subtitles = streams.filter(stream => stream.Type === "Subtitle");
  $("audio-track").replaceChildren(...audio.map(track => trackOption(track, `Audio ${track.Index}`)));
  const off = document.createElement("option");
  off.value = "";
  off.textContent = "Off";
  off.selected = !subtitles.some(track => track.IsDefault);
  $("subtitle-track").replaceChildren(off, ...subtitles.map(track => trackOption(track, `Subtitle ${track.Index}`)));
  syncCaptionControl();
}

function trackOption(track, fallback) {
  const option = document.createElement("option");
  option.value = String(track.Index);
  option.textContent = [track.DisplayTitle, track.Language, track.Codec?.toUpperCase()].filter(Boolean).join(" · ") || fallback;
  option.selected = Boolean(track.IsDefault);
  option.dataset.default = String(Boolean(track.IsDefault));
  return option;
}

function selectedSource() {
  const sources = state.playbackInfo?.MediaSources || [];
  return sources.find(source => source.Id === $("media-source").value) || sources[0];
}

function formatTicks(ticks) {
  if (!ticks) return "";
  const minutes = Math.round(ticks / 600000000);
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}

function absolutePositionSeconds() {
  return state.streamOffsetSeconds + ($("player").currentTime || 0);
}

function playbackPayload(eventName, sessionId = state.playSessionId) {
  const source = selectedSource();
  return {
    QueueableMediaTypes: ["Video"],
    CanSeek: true,
    ItemId: state.item.Id,
    MediaSourceId: source?.Id || state.item.Id,
    AudioStreamIndex: numberOrNull($("audio-track").value),
    SubtitleStreamIndex: numberOrNull($("subtitle-track").value),
    IsPaused: $("player").paused,
    IsMuted: $("player").muted,
    PositionTicks: Math.round(absolutePositionSeconds() * 10000000),
    VolumeLevel: Math.round($("player").volume * 100),
    PlayMethod: state.playMethod,
    PlaySessionId: sessionId,
    EventName: eventName,
    PlaylistIndex: Math.max(0, state.playableItems.findIndex(item => item.Id === state.item.Id)),
    PlaylistLength: Math.max(1, state.playableItems.length)
  };
}

function numberOrNull(value) {
  return value === "" ? null : Number(value);
}

async function report(kind, eventName, sessionId = state.playSessionId) {
  if (!sessionId || state.stopping && kind !== "stopped") return;
  try {
    await api(`/playback/${kind}`, {
      method: "POST",
      body: JSON.stringify({
        serverUrl: state.serverUrl,
        accessToken: state.session.token,
        playback: playbackPayload(eventName, sessionId)
      })
    });
  } catch (error) {
    console.warn("Playback report failed", error);
  }
}

function setPlayerMessage(text, error = false) {
  const message = $("player-message");
  message.className = error ? "player-message error" : "player-message";
  message.textContent = text;
  message.hidden = false;
}

function playbackDiagnostics(player) {
  return `codec=${player.canPlayType('video/webm; codecs="vp8, vorbis"') || "unsupported"}, ready=${player.readyState}, network=${player.networkState}, error=${player.error?.code || "none"}, size=${player.videoWidth}x${player.videoHeight}`;
}

function webmStreamUrl(source, audio, subtitle, startSeconds = 0) {
  const startTicks = Math.round(startSeconds * 10000000);
  return `${state.serviceUrl}/api/v1/emby/videos/${state.item.Id}/stream.webm?serverUrl=${encodeURIComponent(state.serverUrl)}&accessToken=${encodeURIComponent(state.session.token)}&mediaSourceId=${encodeURIComponent(source.Id)}&playSessionId=${state.playSessionId}${audio ? `&audioStreamIndex=${audio}` : ""}${subtitle ? `&subtitleStreamIndex=${subtitle}` : ""}${startTicks > 0 ? `&startTimeTicks=${startTicks}` : ""}`;
}

function randomSessionId() {
  return crypto.randomUUID().replaceAll("-", "");
}

async function play(startSeconds = 0, autoplay = true, forceNewSession = false) {
  const source = selectedSource();
  if (!source) {
    showStatus("This item has no playable media source.");
    return;
  }
  const requestedPosition = typeof startSeconds === "number" ? Math.max(0, startSeconds) : 0;
  const replacingStream = Boolean(state.playSessionId);
  if (replacingStream) await stop(false);
  state.stopping = false;
  state.playMethod = "Transcode";
  state.playStartedReported = false;
  state.streamOffsetSeconds = requestedPosition;
  state.playSessionId = replacingStream || forceNewSession ? randomSessionId() : state.playbackInfo?.PlaySessionId || randomSessionId();
  const audio = $("audio-track").value;
  const subtitle = $("subtitle-track").value;
  const player = $("player");
  player.pause();
  player.removeAttribute("src");
  player.load();
  if (player.volume === 0) player.volume = state.lastAudibleVolume;
  $("playing-title").textContent = state.item.Name;
  $("player-shell").hidden = false;
  updateQueueControls();
  syncCaptionControl();
  syncVolumeControl();
  revealPlayerControls();
  if (!player.canPlayType('video/webm; codecs="vp8, vorbis"')) {
    setPlayerMessage(`This iCUE runtime does not report VP8/Vorbis support (${playbackDiagnostics(player)}).`, true);
    return;
  }
  setPlayerMessage("Preparing iCUE-compatible VP8 video and Vorbis audio…");
  player.src = webmStreamUrl(source, audio, subtitle, requestedPosition);
  clearTimeout(state.playbackWatchdog);
  state.playbackWatchdog = setTimeout(() => {
    if (!state.playStartedReported) setPlayerMessage(`The VP8/Vorbis stream did not produce video frames (${playbackDiagnostics(player)}).`, true);
  }, 10000);
  if (autoplay) {
    try {
      await player.play();
    } catch (error) {
      console.error("Emby play() failed", error);
      setPlayerMessage(`Playback could not start: ${error.message || error.name}; ${playbackDiagnostics(player)}.`, true);
    }
  } else {
    syncPlayControl();
    revealPlayerControls();
  }
}

async function stop(hidePlayer = true) {
  if (state.stopping) return;
  state.stopping = true;
  clearInterval(state.progressTimer);
  clearTimeout(state.playbackWatchdog);
  clearTimeout(state.controlsTimer);
  const sessionId = state.playSessionId;
  state.playSessionId = null;
  await report("stopped", "Stop", sessionId);
  const player = $("player");
  player.pause();
  player.removeAttribute("src");
  player.load();
  state.streamOffsetSeconds = 0;
  if (hidePlayer) $("player-shell").hidden = true;
  state.stopping = false;
}

function updateQueueControls() {
  const index = state.playableItems.findIndex(item => item.Id === state.item?.Id);
  $("previous-button").disabled = state.transitionPending || index <= 0;
  $("next-button").disabled = state.transitionPending || index < 0 || index >= state.playableItems.length - 1;
}

async function playAdjacent(direction) {
  if (state.transitionPending) return;
  const index = state.playableItems.findIndex(item => item.Id === state.item?.Id);
  const target = state.playableItems[index + direction];
  if (!target) return;
  revealPlayerControls();
  setTransitionPending(true);
  try {
    await stop(false);
    await showDetails(target.Id);
    await play(0);
  } catch (error) {
    console.error("Could not play adjacent title", error);
    setPlayerMessage(`Could not load ${target.Name}: ${error.message}`, true);
  } finally {
    setTransitionPending(false);
  }
}

function setTransitionPending(pending) {
  state.transitionPending = pending;
  $("toggle-play").disabled = pending;
  $("close-player").disabled = pending;
  updateQueueControls();
  syncCaptionControl();
}

async function togglePlayback() {
  const player = $("player");
  revealPlayerControls();
  if (player.paused) {
    try {
      await player.play();
    } catch (error) {
      setPlayerMessage(`Playback could not resume: ${error.message || error.name}.`, true);
    }
  } else {
    player.pause();
  }
}

function syncPlayControl() {
  const paused = $("player").paused;
  $("play-icon").textContent = paused ? "▶" : "❚❚";
  $("toggle-play").setAttribute("aria-label", paused ? "Play" : "Pause");
  $("toggle-play").dataset.state = paused ? "paused" : "playing";
}

function syncVolumeControl() {
  const player = $("player");
  const silent = player.muted || player.volume === 0;
  $("volume-slider").value = String(Math.round((player.muted ? 0 : player.volume) * 100));
  $("volume-icon").textContent = silent ? "🔇" : player.volume < 0.5 ? "🔉" : "🔊";
  $("mute-button").setAttribute("aria-label", silent ? "Unmute" : "Mute");
  $("mute-button").setAttribute("aria-pressed", String(silent));
}

function setVolume(event) {
  const player = $("player");
  const volume = Number(event.target.value) / 100;
  player.volume = volume;
  player.muted = volume === 0;
  if (volume > 0) state.lastAudibleVolume = volume;
  syncVolumeControl();
  revealPlayerControls();
}

function toggleMute() {
  const player = $("player");
  if (player.muted || player.volume === 0) {
    player.volume = state.lastAudibleVolume || 1;
    player.muted = false;
  } else {
    state.lastAudibleVolume = player.volume;
    player.muted = true;
  }
  syncVolumeControl();
  revealPlayerControls();
}

function syncCaptionControl() {
  const button = $("captions-button");
  const select = $("subtitle-track");
  const captions = [...select.options].filter(option => option.value !== "");
  const enabled = Boolean(select.value);
  button.disabled = state.transitionPending || captions.length === 0;
  button.setAttribute("aria-pressed", String(enabled));
  button.setAttribute("aria-label", enabled ? "Turn captions off" : "Turn captions on");
}

async function toggleCaptions() {
  if (state.transitionPending) return;
  const select = $("subtitle-track");
  const captions = [...select.options].filter(option => option.value !== "");
  if (!captions.length) return;
  const nextCaption = captions.find(option => option.dataset.default === "true") || captions[0];
  const activeSession = Boolean(state.playSessionId) && !$("player-shell").hidden;
  const wasPaused = $("player").paused;
  const position = absolutePositionSeconds();
  revealPlayerControls();
  if (!activeSession) {
    select.value = select.value ? "" : nextCaption.value;
    syncCaptionControl();
    return;
  }
  setTransitionPending(true);
  try {
    await stop(false);
    select.value = select.value ? "" : nextCaption.value;
    await play(position, !wasPaused, true);
  } finally {
    setTransitionPending(false);
  }
}

function revealPlayerControls() {
  const shell = $("player-shell");
  if (shell.hidden) return;
  shell.classList.remove("controls-hidden");
  clearTimeout(state.controlsTimer);
  scheduleControlsHide();
}

function scheduleControlsHide() {
  clearTimeout(state.controlsTimer);
  const player = $("player");
  const keyboardFocused = state.keyboardControlFocus && isPlayerControlFocused();
  if (player.paused || keyboardFocused) return;
  state.controlsTimer = setTimeout(() => {
    const stillKeyboardFocused = state.keyboardControlFocus && isPlayerControlFocused();
    if (!player.paused && !stillKeyboardFocused) $("player-shell").classList.add("controls-hidden");
  }, 2600);
}

function isPlayerControlFocused() {
  return $("player-controls").contains(document.activeElement) || document.activeElement === $("close-player");
}

function handlePointerActivity() {
  state.keyboardInteraction = false;
  state.keyboardControlFocus = false;
  revealPlayerControls();
}

async function handleEnded() {
  if (!$("next-button").disabled) await playAdjacent(1);
  else await stop();
}

async function toggleWatched() {
  const played = !state.item.UserData?.Played;
  await api(`/users/${state.session.userId}/items/${state.item.Id}/watched`, {
    method: "POST",
    body: JSON.stringify({ serverUrl: state.serverUrl, accessToken: state.session.token, played })
  });
  state.item.UserData = { ...(state.item.UserData || {}), Played: played };
  $("watched-button").textContent = played ? "↶ Mark unwatched" : "✓ Mark watched";
}

document.addEventListener("DOMContentLoaded", () => {
  const player = $("player");
  const shell = $("player-shell");
  $("login-form").addEventListener("submit", login);
  $("search-form").addEventListener("submit", search);
  $("user-button").onclick = () => logout();
  $("back-button").onclick = () => {
    const previous = state.history.pop();
    if (previous) browse(previous.id, previous.title, false);
  };
  $("details-back").onclick = () => {
    $("details").hidden = true;
    $("browser").hidden = false;
  };
  $("play-button").onclick = () => play();
  $("close-player").onclick = () => stop();
  $("previous-button").onclick = () => playAdjacent(-1);
  $("next-button").onclick = () => playAdjacent(1);
  $("toggle-play").onclick = togglePlayback;
  $("captions-button").onclick = toggleCaptions;
  $("mute-button").onclick = toggleMute;
  $("volume-slider").addEventListener("input", setVolume);
  $("subtitle-track").addEventListener("change", syncCaptionControl);
  $("watched-button").onclick = toggleWatched;
  document.addEventListener("keydown", () => { state.keyboardInteraction = true; }, true);
  shell.addEventListener("pointermove", handlePointerActivity);
  shell.addEventListener("pointerdown", handlePointerActivity);
  shell.addEventListener("touchstart", handlePointerActivity, { passive: true });
  shell.addEventListener("focusin", () => {
    state.keyboardControlFocus = state.keyboardInteraction;
    revealPlayerControls();
  });
  shell.addEventListener("focusout", () => setTimeout(() => {
    if (!isPlayerControlFocused()) state.keyboardControlFocus = false;
    scheduleControlsHide();
  }, 0));
  player.addEventListener("pause", () => {
    syncPlayControl();
    revealPlayerControls();
    report("progress", "Pause");
  });
  player.addEventListener("play", () => {
    syncPlayControl();
    scheduleControlsHide();
    if (state.playStartedReported) report("progress", "Unpause");
  });
  player.addEventListener("volumechange", syncVolumeControl);
  player.addEventListener("loadedmetadata", () => setPlayerMessage("VP8/Vorbis metadata received. Waiting for frames…"));
  player.addEventListener("canplay", () => setPlayerMessage("Video and audio buffered. Starting playback…"));
  player.addEventListener("timeupdate", async () => {
    if (state.playStartedReported || player.currentTime < .2 || !player.videoWidth) return;
    const reportedSessionId = state.playSessionId;
    state.playStartedReported = true;
    clearTimeout(state.playbackWatchdog);
    $("player-message").hidden = true;
    await report("started", "TimeUpdate", reportedSessionId);
    if (!reportedSessionId || state.playSessionId !== reportedSessionId) return;
    clearInterval(state.progressTimer);
    state.progressTimer = setInterval(() => report("progress", "TimeUpdate"), 10000);
  });
  player.addEventListener("stalled", () => setPlayerMessage("Emby is still preparing the VP8/Vorbis stream…"));
  player.addEventListener("error", () => setPlayerMessage(`The VP8/Vorbis Emby stream could not be decoded or reached (${playbackDiagnostics(player)}).`, true));
  player.addEventListener("seeked", () => report("progress", "TimeUpdate"));
  player.addEventListener("ended", handleEnded);
  addEventListener("resize", applyPreview);
  syncPlayControl();
  syncVolumeControl();
  updateQueueControls();
  updateSettings();
});

window.embyEdge = { updateSettings };

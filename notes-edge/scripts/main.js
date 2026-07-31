(() => {
  const STORAGE_KEY = "com.robert.notes-edge.lists.v1";
  const defaults = { listName: "Notes", showListName: true };
  const state = {
    ...defaults,
    store: { version: 1, lists: [] },
    listId: null,
    document: { listName: "Notes", notes: [] },
    showArchived: false,
    editingId: null,
    selected: new Set()
  };
  const el = {};
  let storageWarning = "";

  function property(name, fallback) {
    if (Object.prototype.hasOwnProperty.call(window, name) && window[name] !== undefined) return window[name];
    try {
      const value = Function(`return typeof ${name} !== "undefined" ? ${name} : undefined`)();
      return value === undefined ? fallback : value;
    } catch { return fallback; }
  }

  function associationKey() {
    return `com.robert.notes-edge.selection:${typeof uniqueId !== "undefined" ? uniqueId : "preview"}`;
  }

  function createId() {
    return typeof crypto.randomUUID === "function"
      ? crypto.randomUUID()
      : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
  }

  function readStore() {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { version: 1, lists: [] };
    try {
      const parsed = JSON.parse(raw);
      const valid = parsed?.version === 1
        && Array.isArray(parsed.lists)
        && parsed.lists.every(list =>
          typeof list?.id === "string"
          && typeof list.listName === "string"
          && Array.isArray(list.notes));
      if (valid) return parsed;
    } catch {}
    const recoveryKey = `${STORAGE_KEY}.recovery.${Date.now()}`;
    try { localStorage.setItem(recoveryKey, raw); } catch {}
    storageWarning = "Stored notes were damaged. A recovery copy was preserved in local storage.";
    return { version: 1, lists: [] };
  }

  function saveStore() {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state.store));
  }

  function rememberSelection() {
    localStorage.setItem(associationKey(), JSON.stringify({
      listId: state.listId,
      configuredName: state.listName
    }));
  }

  function message(text, isError = false) {
    el.notice.textContent = text || "";
    el.notice.style.color = isError ? "var(--danger)" : "";
  }

  function normalizeName(value) {
    return String(value || "").trim().slice(0, 60) || "Notes";
  }

  function findByName(name) {
    return state.store.lists.find(list =>
      list.listName.localeCompare(name, undefined, { sensitivity: "accent" }) === 0);
  }

  function createList(name) {
    const list = { id: createId(), listName: normalizeName(name), notes: [] };
    state.store.lists.push(list);
    saveStore();
    return list;
  }

  function useList(list, notice = "") {
    state.listId = list.id;
    state.document = list;
    rememberSelection();
    message(storageWarning || notice, Boolean(storageWarning));
    storageWarning = "";
    render();
  }

  function loadConfiguredList(forceConfigured = false) {
    state.store = readStore();
    let remembered = null;
    if (!forceConfigured) {
      try { remembered = JSON.parse(localStorage.getItem(associationKey()) || "null"); } catch {}
    }
    const rememberedList = remembered?.configuredName === state.listName
      ? state.store.lists.find(list => list.id === remembered.listId)
      : null;
    if (rememberedList) {
      useList(rememberedList);
      return;
    }

    const existing = findByName(state.listName);
    useList(
      existing || createList(state.listName),
      existing ? `Warning: “${existing.listName}” already exists. This widget will share that saved list.` : ""
    );
  }

  async function mutateCurrentList(mutation) {
    const run = async () => {
      const latest = readStore();
      const list = latest.lists.find(candidate => candidate.id === state.listId);
      if (!list) throw new Error("The selected list no longer exists.");
      mutation(list);
      localStorage.setItem(STORAGE_KEY, JSON.stringify(latest));
      state.store = latest;
      state.document = list;
    };
    if (navigator.locks?.request)
      return navigator.locks.request(STORAGE_KEY, run);
    return run();
  }

  function formatDue(value) {
    if (!value) return "";
    const hasTime = value.includes("T") && !value.includes("T00:00:00");
    const date = value.includes("T")
      ? new Date(value)
      : new Date(...value.split("-").map((part, index) => Number(part) - (index === 1 ? 1 : 0)));
    return new Intl.DateTimeFormat(undefined, {
      month: "short", day: "numeric", year: "numeric",
      ...(hasTime ? { hour: "numeric", minute: "2-digit" } : {})
    }).format(date);
  }

  function noteCard(note) {
    const card = document.createElement("article");
    card.className = `note-card${note.completed ? " completed" : ""}${note.deleted ? " archived" : ""}`;
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.className = "check";
    checkbox.checked = note.completed;
    checkbox.setAttribute("aria-label", `Mark ${note.text} complete`);
    checkbox.disabled = note.deleted;
    checkbox.addEventListener("change", () => updateNote(note, { completed: checkbox.checked }));

    const copy = document.createElement("div");
    copy.className = "note-copy";
    const text = document.createElement("div");
    text.className = "note-text";
    text.textContent = note.text;
    copy.append(text);
    if (note.dueAt) {
      const due = document.createElement("div");
      due.className = `due${new Date(note.dueAt) < new Date() && !note.completed ? " overdue" : ""}`;
      due.textContent = `Due ${formatDue(note.dueAt)}`;
      copy.append(due);
    }

    const actions = document.createElement("div");
    actions.className = "card-actions";
    if (note.deleted) {
      actions.append(actionButton("↶", "Restore note", () => updateNote(note, { deleted: false })));
    } else {
      actions.append(
        actionButton("✎", "Edit note", () => openEditor(note)),
        actionButton("trash", "Delete note", () => archiveIds([note.id]), true)
      );
    }
    card.append(checkbox, copy, actions);
    return card;
  }

  function actionButton(icon, label, handler, danger = false) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `icon-button${danger ? " danger" : ""}`;
    if (icon === "trash") {
      button.innerHTML = '<svg class="button-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M4 7h16M9 7V4h6v3m3 0-1 13H7L6 7m4 4v5m4-5v5"/></svg>';
    } else {
      button.textContent = icon;
    }
    button.title = label;
    button.setAttribute("aria-label", label);
    button.addEventListener("click", handler);
    return button;
  }

  function render() {
    el.listTitle.textContent = state.document.listName || state.listName;
    el.listTitle.hidden = !state.showListName;
    el.fileLabel.textContent = "Saved locally";
    el.archiveToggle.setAttribute("aria-pressed", String(state.showArchived));
    el.notesList.replaceChildren();
    const notes = state.document.notes.filter(note => note.deleted === state.showArchived);
    notes.sort((a, b) => Number(a.completed) - Number(b.completed) || b.createdAt.localeCompare(a.createdAt));
    notes.forEach(note => el.notesList.append(noteCard(note)));
    el.emptyState.hidden = notes.length !== 0;
    el.emptyCopy.textContent = state.showArchived ? "No archived notes." : "Nothing on this list yet.";
    const selectedIds = state.document.notes.filter(note => !note.deleted && note.completed).map(note => note.id);
    state.selected = new Set(selectedIds);
    el.deleteSelected.disabled = selectedIds.length === 0;
    el.addForm.hidden = state.showArchived;
    el.archiveToolbar.hidden = !state.showArchived;
    el.clearArchive.disabled = notes.length === 0;
  }

  async function addNote(event) {
    event.preventDefault();
    const text = el.newNote.value.trim();
    if (!text) return;
    const now = new Date().toISOString();
    try {
      await mutateCurrentList(list => list.notes.push({
        id: createId(),
        text,
        completed: false,
        deleted: false,
        dueAt: null,
        createdAt: now,
        updatedAt: now
      }));
      el.newNote.value = "";
      message("");
      render();
    } catch (error) {
      message(`Could not save locally: ${error.message}`, true);
    }
  }

  async function updateNote(note, changes) {
    try {
      await mutateCurrentList(list => {
        const latestNote = list.notes.find(item => item.id === note.id);
        if (!latestNote) throw new Error("The selected note no longer exists.");
        Object.assign(latestNote, changes, { updatedAt: new Date().toISOString() });
      });
      message("");
      render();
      return true;
    } catch (error) {
      message(`Could not save locally: ${error.message}`, true);
      render();
      return false;
    }
  }

  async function archiveIds(ids) {
    if (!ids.length) return;
    const selected = new Set(ids);
    const now = new Date().toISOString();
    try {
      await mutateCurrentList(list => list.notes.forEach(note => {
        if (selected.has(note.id)) Object.assign(note, { deleted: true, updatedAt: now });
      }));
      message("");
      render();
    } catch (error) {
      message(`Could not save locally: ${error.message}`, true);
    }
  }

  async function clearArchive(event) {
    event.preventDefault();
    try {
      await mutateCurrentList(list => {
        list.notes = list.notes.filter(note => !note.deleted);
      });
      el.clearDialog.close();
      message("Archive cleared.");
      render();
    } catch (error) {
      message(`Could not clear the archive: ${error.message}`, true);
    }
  }

  function openEditor(note) {
    state.editingId = note.id;
    el.editText.value = note.text;
    el.dueToggle.checked = Boolean(note.dueAt);
    el.dueFields.hidden = !note.dueAt;
    el.dueDate.required = Boolean(note.dueAt);
    if (note.dueAt) {
      if (!note.dueAt.includes("T")) {
        el.dueDate.value = note.dueAt;
        el.dueTime.value = "";
      } else {
        const date = new Date(note.dueAt);
        el.dueDate.value = date.toLocaleDateString("en-CA");
        el.dueTime.value = date.getHours() || date.getMinutes()
          ? `${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}` : "";
      }
    } else {
      el.dueDate.value = "";
      el.dueTime.value = "";
    }
    el.editDialog.showModal();
    el.editText.focus();
  }

  async function saveEdit(event) {
    event.preventDefault();
    const note = state.document.notes.find(item => item.id === state.editingId);
    if (!note) return;
    if (el.dueToggle.checked && !el.dueDate.value) {
      el.dueDate.setCustomValidity("A date is required when due date is turned on.");
      el.dueDate.reportValidity();
      return;
    }
    el.dueDate.setCustomValidity("");
    let dueAt = null;
    if (el.dueToggle.checked) {
      dueAt = el.dueTime.value
        ? new Date(`${el.dueDate.value}T${el.dueTime.value}:00`).toISOString()
        : el.dueDate.value;
    }
    if (await updateNote(note, { text: el.editText.value.trim(), dueAt }))
      el.editDialog.close();
  }

  function openOptions() {
    state.store = readStore();
    el.listFile.replaceChildren();
    el.listFile.add(new Option(`Use setup list name — ${state.listName}`, ""));
    state.store.lists
      .slice()
      .sort((a, b) => a.listName.localeCompare(b.listName))
      .forEach(list => {
        const activeCount = list.notes.filter(note => !note.deleted).length;
        const option = new Option(`${list.listName} (${activeCount})`, list.id);
        option.selected = list.id === state.listId;
        el.listFile.add(option);
      });
    el.optionsDialog.showModal();
  }

  function saveOptions(event) {
    event.preventDefault();
    state.store = readStore();
    const selected = el.listFile.value
      ? state.store.lists.find(list => list.id === el.listFile.value)
      : findByName(state.listName) || createList(state.listName);
    if (!selected) {
      message("The selected local list no longer exists.", true);
      return;
    }
    useList(
      selected,
      selected.listName === state.listName
        ? ""
        : `To reconnect automatically after replacement, set the widget’s List name to “${selected.listName}”.`
    );
    el.optionsDialog.close();
  }

  function updateSettings() {
    const previousName = state.listName;
    state.listName = normalizeName(property("listName", defaults.listName));
    state.showListName = Boolean(property("showListName", defaults.showListName));
    if (!el.listTitle) return;
    if (!state.listId || previousName !== state.listName) loadConfiguredList(previousName !== state.listName);
    else render();
  }

  document.addEventListener("DOMContentLoaded", () => {
    [
      "list-title", "file-label", "archive-toggle", "options-button", "add-form", "new-note",
      "delete-selected", "archive-toolbar", "clear-archive", "notice", "notes-list", "empty-state", "empty-copy", "edit-dialog",
      "edit-form", "edit-text", "due-toggle", "due-fields", "due-date", "due-time",
      "options-dialog", "options-form", "list-file", "clear-dialog", "clear-form"
    ].forEach(id => { el[id.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())] = document.getElementById(id); });
    el.addForm.addEventListener("submit", addNote);
    el.deleteSelected.addEventListener("click", () => archiveIds([...state.selected]));
    el.archiveToggle.addEventListener("click", () => { state.showArchived = !state.showArchived; render(); });
    el.clearArchive.addEventListener("click", () => el.clearDialog.showModal());
    el.clearForm.addEventListener("submit", clearArchive);
    el.optionsButton.addEventListener("click", openOptions);
    el.editForm.addEventListener("submit", saveEdit);
    el.optionsForm.addEventListener("submit", saveOptions);
    el.dueToggle.addEventListener("change", () => {
      el.dueFields.hidden = !el.dueToggle.checked;
      el.dueDate.required = el.dueToggle.checked;
      if (!el.dueToggle.checked) el.dueDate.setCustomValidity("");
    });
    document.querySelectorAll("[data-close]").forEach(button =>
      button.addEventListener("click", () => document.getElementById(button.dataset.close).close()));
    window.addEventListener("storage", event => {
      if (event.key !== STORAGE_KEY) return;
      const refreshed = readStore();
      const current = refreshed.lists.find(list => list.id === state.listId);
      state.store = refreshed;
      if (current) {
        state.document = current;
        render();
      }
    });
    updateSettings();
  });

  window.notesWidget = { updateSettings };
})();

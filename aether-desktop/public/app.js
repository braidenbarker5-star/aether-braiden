(() => {
  "use strict";

  const allowed = new Set([
    "devices.scan", "adb.connect", "ledger.list", "ledger.hash", "venice.open",
    "project.select", "project.files", "project.read", "project.save", "project.veniceContext", "project.build",
  ]);
  const $ = (id) => document.getElementById(id);
  const el = {
    veniceTab: $("venice-tab"), builderTab: $("builder-tab"), benchTab: $("bench-tab"), title: $("page-title"), status: $("page-status"),
    venice: $("venice-view"), builder: $("builder-view"), bench: $("bench-view"), openVenice: $("open-venice"),
    scan: $("scan-devices"), deviceCount: $("device-count"), deviceStatus: $("device-status"), devices: $("devices-list"),
    adbAddress: $("adb-address"), adbConnect: $("adb-connect"),
    attachProject: $("attach-project"), refreshProject: $("refresh-project"), sendVenice: $("send-venice"), buildProject: $("build-project"),
    projectStatus: $("builder-project-status"), fileCount: $("project-file-count"), tree: $("project-tree"), fileStatus: $("project-file-status"),
    editorTitle: $("editor-filename"), editor: $("project-editor"), dirty: $("editor-dirty"), saveFile: $("save-project-file"),
    buildStatus: $("build-project-status"), buildLog: $("project-build-log"),
    hash: $("hash-file"), hashStatus: $("hash-status"), hashResult: $("hash-result"),
    ledgerCount: $("ledger-count"), ledger: $("ledger-list"), ledgerPath: $("ledger-path"), refreshLedger: $("refresh-ledger"),
  };
  const pending = new Map();
  let sequence = 0;
  let nativeReady = false;
  let projectAttached = false;
  let currentFile = "";
  let originalContents = "";
  let projectBusy = false;

  function setStatus(label, online) {
    el.status.querySelector("span").textContent = label;
    el.status.querySelector(".status-dot").classList.toggle("offline", !online);
  }

  function nativeCall(action, args = {}, timeout = 120000) {
    if (!allowed.has(action)) return Promise.reject(new Error("Unsupported Aether action."));
    if (!window.chrome?.webview) return Promise.reject(new Error("The native Aether desktop host is unavailable."));
    const id = `aether-${Date.now()}-${++sequence}`;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        pending.delete(id);
        reject(new Error("The operation timed out. Check the build log and try again."));
      }, timeout);
      pending.set(id, { resolve, reject, timer, action });
      window.chrome.webview.postMessage({ channel: "aether.bench", id, action, ...args });
    });
  }

  function make(tag, value, className) {
    const item = document.createElement(tag);
    if (className) item.className = className;
    item.textContent = value == null ? "" : String(value);
    return item;
  }

  function renderDevices(devices) {
    el.devices.replaceChildren();
    if (!devices.length) {
      el.devices.append(make("div", "No phone interfaces found. Check USB mode, cable, and vendor drivers.", "empty-state"));
      return;
    }
    for (const device of devices) {
      const row = make("div", "", "record-row");
      const details = make("div", "", "record-details");
      details.append(make("strong", device.name || "USB device"));
      details.append(make("span", [device.transport, device.serial, device.vid && device.pid ? `VID:PID ${device.vid}:${device.pid}` : "", device.state].filter(Boolean).join(" · ")));
      row.append(details, make("span", device.type || "USB", "pill"));
      el.devices.append(row);
    }
  }

  function renderLedger(records) {
    const entries = Array.isArray(records) ? records : [];
    el.ledgerCount.textContent = `${entries.length} hash${entries.length === 1 ? "" : "es"}`;
    el.ledger.replaceChildren();
    if (!entries.length) {
      el.ledger.append(make("div", "No checksums yet. Choose a file to record SHA-256.", "empty-state"));
      return;
    }
    for (const entry of entries.slice(-20).reverse()) {
      const row = make("div", "", "ledger-row");
      row.append(make("strong", entry.name));
      row.append(make("code", entry.sha256));
      row.append(make("span", `${entry.bytes} bytes · ${entry.checkedAt || entry.checked_at || ""}`));
      el.ledger.append(row);
    }
  }

  function renderProjectTree(files) {
    el.tree.replaceChildren();
    if (!files.length) {
      el.tree.append(make("div", "No supported source files found. Check that this project contains source code or Gradle files.", "empty-state"));
      return;
    }
    for (const file of files) {
      const button = make("button", file.relativePath, "project-file");
      button.type = "button";
      button.title = file.relativePath;
      button.addEventListener("click", () => openProjectFile(file.relativePath));
      el.tree.append(button);
    }
  }

  function setProjectBusy(busy) {
    projectBusy = busy;
    el.attachProject.disabled = busy;
    el.refreshProject.disabled = busy || !projectAttached;
    el.buildProject.disabled = busy || !projectAttached;
    el.editor.disabled = busy || !currentFile;
    el.saveFile.disabled = busy || !currentFile || el.editor.value === originalContents;
    el.sendVenice.disabled = busy || !currentFile;
  }

  function updateResult(message) {
    const data = message.result || message;
    switch (message.action || message.event) {
      case "devices.scan": {
        const devices = Array.isArray(data.devices) ? data.devices : [];
        renderDevices(devices);
        el.deviceCount.textContent = `${devices.length} device${devices.length === 1 ? "" : "s"}`;
        el.deviceStatus.textContent = `Windows USB scan complete${data.adbAvailable ? " · ADB found" : " · ADB missing"}${data.fastbootAvailable ? " · fastboot found" : " · fastboot missing"}. Read-only; no device changes.`;
        break;
      }
      case "adb.connect":
        el.deviceStatus.textContent = data.message || "ADB attach finished.";
        request("devices.scan").catch(() => {});
        break;
      case "ledger.list":
        renderLedger(data.records);
        if (data.ledgerPath) el.ledgerPath.textContent = `Local ledger: ${data.ledgerPath}`;
        el.hashStatus.textContent = "Local checksum ledger loaded.";
        break;
      case "ledger.hash": {
        const record = data.record || {};
        el.hashResult.replaceChildren(make("strong", record.name), make("code", record.sha256));
        el.hashResult.hidden = false;
        el.hashStatus.textContent = `SHA-256 recorded locally · ${record.bytes} bytes.`;
        if (data.ledgerPath) el.ledgerPath.textContent = `Local ledger: ${data.ledgerPath}`;
        request("ledger.list").catch(() => {});
        break;
      }
      case "adb.connect":
        el.deviceStatus.textContent = data.message || "ADB connection request completed.";
        request("devices.scan").catch(() => {});
        break;
      case "project.select":
        projectAttached = true;
        el.projectStatus.textContent = data.projectName;
        el.fileStatus.textContent = `Attached locally: ${data.projectPath}. Nothing has been sent to Venice.`;
        setProjectBusy(false);
        request("project.files").catch((error) => { el.fileStatus.textContent = error.message; });
        break;
      case "project.files":
        renderProjectTree(data.files || []);
        el.fileCount.textContent = `${(data.files || []).length} project files`;
        el.projectStatus.textContent = data.projectName || "Project attached";
        break;
      case "project.read":
        currentFile = data.relativePath;
        originalContents = data.contents;
        el.editorTitle.textContent = currentFile;
        el.editor.value = data.contents;
        el.dirty.textContent = "Loaded";
        el.sendVenice.disabled = false;
        el.saveFile.disabled = true;
        el.editor.disabled = false;
        break;
      case "project.save":
        originalContents = el.editor.value;
        el.dirty.textContent = "Saved with backup";
        el.fileStatus.textContent = `Saved ${data.relativePath}. Previous contents are backed up under .aether-backup.`;
        el.saveFile.disabled = true;
        break;
      case "project.veniceContext":
        el.fileStatus.textContent = "Selected source and task copied to the clipboard. Open Venice, paste the context, review its proposed changes, then apply them here.";
        setProjectBusy(false);
        break;
      case "project.build":
        el.buildStatus.textContent = data.exitCode === 0 ? "Gradle build succeeded" : `Gradle exited with code ${data.exitCode}`;
        el.buildStatus.classList.toggle("error", data.exitCode !== 0);
        el.buildLog.textContent = data.output || "Gradle returned no output.";
        if (data.apkPath) el.apkResult.replaceChildren(make("strong", "Debug APK built"), make("code", data.apkPath));
        el.apkResult.hidden = !data.apkPath;
        setProjectBusy(false);
        break;
      case "venice.open":
        setStatus("Venice web agent", true);
        break;
    }
  }

  function onMessage(event) {
    const message = event.data;
    if (!message || typeof message !== "object") return;
    if (message.channel === "aether.bench.event") {
      nativeReady = true;
      setStatus("Aether ready", true);
      if (message.ledgerPath) el.ledgerPath.textContent = `Local ledger: ${message.ledgerPath}`;
      return;
    }
    if (message.channel === "aether.builder.event") {
      if (message.event === "build.output") {
        el.buildLog.textContent += message.line + "\n";
        el.buildLog.scrollTop = el.buildLog.scrollHeight;
      }
      return;
    }
    if (message.channel !== "aether.bench.result") return;
    const waiter = pending.get(message.id);
    if (waiter) { pending.delete(message.id); clearTimeout(waiter.timer); }
    if (!message.ok) {
      if (waiter) waiter.reject(new Error(message.error || "Operation failed."));
      return;
    }
    updateResult(message);
    if (waiter) waiter.resolve(message.result || message);
  }

  function request(action, data = {}, timeout = 120000) {
    if (!allowed.has(action)) return Promise.reject(new Error("Unsupported Aether action."));
    if (!window.chrome?.webview) return Promise.reject(new Error("Run AetherStandalone.exe to access native project and device tools."));
    const id = `aether-${Date.now()}-${Math.random().toString(36).slice(2)}`;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { pending.delete(id); reject(new Error("Operation timed out; review status and retry.")); }, timeout);
      pending.set(id, { resolve, reject, timer });
      window.chrome.webview.postMessage({ channel: "aether.bench", id, action, ...data });
    });
  }

  function showVenice() {
    el.venice.hidden = true;
    el.builder.hidden = true;
    el.bench.hidden = false;
    el.title.textContent = "Opening Venice agent";
    el.veniceTab.classList.add("active");
    el.builderTab.classList.remove("active");
    el.benchTab.classList.remove("active");
    el.deviceStatus.textContent = "Venice opens in its own native browser window. The App Builder stays open in Aether.";
    request("venice.open").catch((error) => { el.deviceStatus.textContent = error.message; });
  }

  function showBench() {
    el.venice.hidden = true;
    el.builder.hidden = true;
    el.bench.hidden = false;
    el.title.textContent = "Aether Bench";
    el.veniceTab.classList.remove("active");
    el.builderTab.classList.remove("active");
    el.benchTab.classList.add("active");
    request("ledger.list").catch((error) => { el.hashStatus.textContent = error.message; });
  }

  function showBuilder() {
    el.venice.hidden = true;
    el.bench.hidden = true;
    el.builder.hidden = false;
    el.title.textContent = "App Builder";
    el.veniceTab.classList.remove("active");
    el.builderTab.classList.add("active");
    el.benchTab.classList.remove("active");
    setStatus(projectAttached ? "Project attached locally" : "Attach a project folder", projectAttached);
  }

  function openProject() {
    setProjectBusy(true);
    el.fileStatus.textContent = "Choose a project folder to scan for source files.";
    request("project.select").catch((error) => { el.fileStatus.textContent = error.message; setProjectBusy(false); });
  }

  function openSource(relativePath) {
    request("project.read", { relativePath }).catch((error) => { el.fileStatus.textContent = error.message; });
  }

  function refreshFiles() {
    request("project.files").catch((error) => { el.fileStatus.textContent = error.message; });
  }

  async function saveSource() {
    if (!currentFile) return;
    el.saveFile.disabled = true;
    try { await request("project.save", { relativePath: currentFile, contents: el.editor.value }); }
    catch (error) { el.fileStatus.textContent = error.message; el.saveFile.disabled = false; }
  }

  async function handoffToVenice() {
    if (!currentFile) return;
    const task = window.prompt("Describe what you want Venice to build or change in the selected file:");
    if (!task) return;
    setProjectBusy(true);
    try { await request("project.veniceContext", { relativePath: currentFile, task }); }
    catch (error) { el.fileStatus.textContent = error.message; setProjectBusy(false); }
  }

  async function buildProject() {
    if (!projectAttached) return;
    if (!window.confirm("Run this local Android project’s Gradle wrapper with the assembleDebug task? Project Gradle scripts will execute on your computer.")) return;
    setProjectBusy(true);
    el.buildStatus.textContent = "Gradle assembleDebug running…";
    el.buildStatus.classList.remove("error");
    el.buildLog.textContent = "Starting the project Gradle wrapper…\n";
    el.apkResult.hidden = true;
    try { await request("project.build", {}, 10 * 60 * 1000); }
    catch (error) { el.buildStatus.textContent = error.message; el.buildStatus.classList.add("error"); setProjectBusy(false); }
  }

  async function scanDevices() {
    el.scan.disabled = true;
    el.deviceStatus.textContent = "Listing USB, ADB, and fastboot devices…";
    try { await request("devices.scan"); }
    catch (error) { el.deviceStatus.textContent = error.message; }
    finally { el.scan.disabled = false; }
  }

  async function attachAdb() {
    el.adbConnect.disabled = true;
    el.deviceStatus.textContent = "Requesting ADB connection…";
    try { await request("adb.connect", { address: el.adbAddress.value.trim() }); }
    catch (error) { el.deviceStatus.textContent = error.message; }
    finally { el.adbConnect.disabled = false; }
  }

  async function hashFile() {
    el.hash.disabled = true;
    el.hashStatus.textContent = "Choose a file to calculate SHA-256…";
    try { await request("ledger.hash"); }
    catch (error) { el.hashStatus.textContent = error.message; }
    finally { el.hash.disabled = false; }
  }

  el.veniceTab.addEventListener("click", showVenice);
  el.openVenice.addEventListener("click", showVenice);
  el.benchTab.addEventListener("click", showBench);
  el.builderTab.addEventListener("click", showBuilder);
  el.attachProject.addEventListener("click", openProject);
  el.refreshProject.addEventListener("click", refreshFiles);
  el.editor.addEventListener("input", () => {
    el.dirty.textContent = el.editor.value === originalContents ? "Saved" : "Unsaved changes";
    el.saveFile.disabled = !currentFile || el.editor.value === originalContents;
  });
  el.saveFile.addEventListener("click", saveSource);
  el.sendVenice.addEventListener("click", handoffToVenice);
  el.buildProject.addEventListener("click", buildProject);
  el.scan.addEventListener("click", scanDevices);
  el.adbConnect.addEventListener("click", attachAdb);
  el.hash.addEventListener("click", hashFile);
  el.refreshLedger.addEventListener("click", () => request("ledger.list").catch((error) => { el.hashStatus.textContent = error.message; }));
  if (window.chrome?.webview) {
    window.chrome.webview.addEventListener("message", onMessage);
    window.chrome.webview.postMessage({ channel: "aether.bench", id: "aether-init", action: "init" });
  } else setStatus("Open AetherStandalone.exe", false);
})();

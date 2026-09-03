"use strict";

const uiLogic = window.AcquisitionUiLogic;
const byId = id => document.getElementById(id);
const state = {
  csrf: "",
  authenticated: false,
  selection: uiLogic.createAgentSelection(),
  agents: [],
  deployments: [],
  configVersions: [],
  preview: null,
  previewRequest: null,
  previewFingerprint: "",
  activeConfigVersionId: "",
  assignmentTimer: null,
  socket: null,
  reconnectTimer: null,
  pollingTimer: null,
  refreshThrottle: null,
  currentView: "dashboard"
};

const viewMetadata = {
  dashboard: { eyebrow: "运行管理", title: "设备总览" },
  deployment: { eyebrow: "配置与部署", title: "部署向导" },
  configuration: { eyebrow: "配置与部署", title: "配置中心" },
  diagnostics: { eyebrow: "运行管理", title: "统一诊断" }
};

function setMessage(element, message, tone) {
  element.textContent = message || "";
  element.classList.remove("success", "warning");
  if (tone) element.classList.add(tone);
}

function setGlobalMessage(message) {
  const element = byId("globalMessage");
  element.textContent = message || "";
  element.hidden = !message;
}

function formatDate(value) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

function asArray(value) {
  if (Array.isArray(value)) return value;
  if (Array.isArray(value?.data)) return value.data;
  if (Array.isArray(value?.items)) return value.items;
  return [];
}

function firstValue(source, keys, fallback) {
  for (const key of keys) {
    if (source && source[key] !== undefined && source[key] !== null) return source[key];
  }
  return fallback;
}

function isSensitiveKey(key) {
  return /(?:password|accesscode|registrationkey|token|secret|connectionstring|hash|salt)/i.test(String(key));
}

function publicValue(key, value) {
  if (value === null || value === undefined || value === "") return "-";
  if (typeof value === "object" && value.displayValue !== undefined) return String(value.displayValue);
  if (isSensitiveKey(key)) {
    if (typeof value === "object" && value.isConfigured !== undefined) return value.isConfigured ? "已配置（已隐藏）" : "未配置";
    if (typeof value === "boolean") return value ? "已配置（已隐藏）" : "未配置";
    return "已隐藏";
  }
  if (typeof value === "boolean") return value ? "是" : "否";
  if (typeof value === "object") return JSON.stringify(maskSensitiveData(value));
  return String(value);
}

function maskSensitiveData(value) {
  if (Array.isArray(value)) return value.map(maskSensitiveData);
  if (!value || typeof value !== "object") return value;
  const safe = {};
  for (const [key, item] of Object.entries(value)) safe[key] = isSensitiveKey(key) ? "[已隐藏]" : maskSensitiveData(item);
  return safe;
}

async function api(url, options = {}) {
  const method = options.method || "GET";
  const headers = { Accept: options.responseType === "blob" ? "application/zip, application/octet-stream" : "application/json" };
  let body;
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(options.body);
  }
  if (state.csrf && !["GET", "HEAD", "OPTIONS"].includes(method)) headers["X-CSRF-TOKEN"] = state.csrf;

  const response = await fetch(url, { method, headers, body, credentials: "same-origin" });
  if (response.status === 401) {
    if (options.allowUnauthorized) return null;
    showLoggedOut("登录已失效，请重新登录。");
    throw new Error("登录已失效，请重新登录。");
  }
  if (!response.ok) {
    const errorBody = await response.json().catch(() => null);
    const message = errorBody?.error?.message || errorBody?.message || errorBody?.title || `请求失败 (${response.status})`;
    throw new Error(message);
  }
  if (options.responseType === "blob") return { blob: await response.blob(), response };
  if (response.status === 204) return null;
  return response.json();
}

async function ensureCsrf() {
  const result = await api("/api/auth/csrf", { allowUnauthorized: true });
  state.csrf = result?.token || "";
}

function showLoggedOut(message) {
  state.authenticated = false;
  byId("appShell").hidden = true;
  byId("loginView").hidden = false;
  if (message) setMessage(byId("loginMessage"), message);
  if (state.socket) state.socket.close();
  state.socket = null;
  clearTimeout(state.reconnectTimer);
  clearInterval(state.pollingTimer);
}

async function checkOperatorStatus() {
  try {
    const status = await api("/api/auth/operator/status", { allowUnauthorized: true });
    const authenticated = Boolean(status && firstValue(status, ["authenticated", "isAuthenticated", "signedIn"], false));
    if (!authenticated) {
      showLoggedOut();
      return;
    }
    await enterApplication(status);
  } catch (error) {
    showLoggedOut(error.message);
  }
}

async function enterApplication(status) {
  state.authenticated = true;
  byId("loginView").hidden = true;
  byId("appShell").hidden = false;
  byId("operatorName").textContent = firstValue(status, ["displayName", "operatorName", "name"], "已登录管理员");
  setMessage(byId("loginMessage"), "");
  state.refreshThrottle ||= uiLogic.createCoalescedThrottle(() => refreshFleet().catch(() => {}), 700);

  await Promise.allSettled([
    refreshFleet(),
    loadEffectiveConfiguration(),
    loadDeployments(),
    loadConfigVersions()
  ]);
  connectSignalR();
  clearInterval(state.pollingTimer);
  state.pollingTimer = setInterval(() => refreshFleet().catch(() => {}), 15000);
}

async function login(event) {
  event.preventDefault();
  const button = byId("loginButton");
  const accessCode = byId("accessCode").value;
  button.disabled = true;
  setMessage(byId("loginMessage"), "正在验证…", "warning");
  try {
    await ensureCsrf();
    const result = await api("/api/auth/operator/login", { method: "POST", body: { accessCode }, allowUnauthorized: true });
    byId("accessCode").value = "";
    if (!result) throw new Error("访问码无效或登录被拒绝。");
    await ensureCsrf();
    await checkOperatorStatus();
  } catch (error) {
    setMessage(byId("loginMessage"), error.message);
  } finally {
    button.disabled = false;
  }
}

async function logout() {
  try { await api("/api/auth/operator/logout", { method: "POST", body: {} }); }
  catch { /* 本地仍清理登录界面。 */ }
  state.selection.clear();
  updateSelectionUi();
  showLoggedOut("已退出管理员会话。");
}

function showView(name) {
  if (!viewMetadata[name]) return;
  state.currentView = name;
  document.querySelectorAll("[data-panel]").forEach(panel => { panel.hidden = panel.dataset.panel !== name; });
  document.querySelectorAll("[data-view]").forEach(button => {
    const active = button.dataset.view === name;
    button.classList.toggle("is-active", active);
    if (active) button.setAttribute("aria-current", "page");
    else button.removeAttribute("aria-current");
  });
  byId("pageEyebrow").textContent = viewMetadata[name].eyebrow;
  byId("pageTitle").textContent = viewMetadata[name].title;
  if (name === "deployment") Promise.allSettled([loadEffectiveConfiguration(), loadDeployments()]);
  if (name === "configuration") loadConfigVersions().catch(error => setMessage(byId("configMessage"), error.message));
  if (name === "diagnostics") updateDiagnosticAgentOptions();
}

function createMetric(label, value) {
  const box = document.createElement("div");
  box.className = "metric";
  const caption = document.createElement("span");
  caption.textContent = label;
  const number = document.createElement("strong");
  number.textContent = String(value ?? 0);
  box.append(caption, number);
  return box;
}

async function refreshFleet() {
  const search = byId("search").value.trim();
  const [summary, agentsResponse] = await Promise.all([
    api("/api/v1/fleet/summary"),
    api(`/api/v1/fleet/agents?page=1&pageSize=100&search=${encodeURIComponent(search)}`)
  ]);
  byId("summary").replaceChildren(
    createMetric("Agent 总数", firstValue(summary, ["agents", "agentCount"], 0)),
    createMetric("在线 Agent", firstValue(summary, ["onlineAgents", "onlineAgentCount"], 0)),
    createMetric("运行设备", firstValue(summary, ["runningDevices"], 0)),
    createMetric("等待任务", firstValue(summary, ["queueDepth", "pendingTasks"], 0)),
    createMetric("今日成功", firstValue(summary, ["todaySuccess"], 0)),
    createMetric("今日失败", firstValue(summary, ["todayFailure"], 0)),
    createMetric("未确认告警", firstValue(summary, ["activeAlerts"], 0))
  );
  state.agents = asArray(agentsResponse);
  renderAgents();
  updateDiagnosticAgentOptions();
  byId("lastRefresh").textContent = `最近刷新：${new Date().toLocaleString()}`;
}

function appendCell(row, value) {
  const cell = document.createElement("td");
  cell.textContent = value === null || value === undefined || value === "" ? "-" : String(value);
  row.append(cell);
  return cell;
}

function renderAgents() {
  const rows = byId("agentRows");
  rows.replaceChildren();
  const decorated = uiLogic.decorateAgentsWithSelection(state.agents, state.selection.selectedIds());
  const now = Date.now();
  for (const item of decorated) {
    const agentId = String(firstValue(item, ["id", "agentId"], ""));
    if (!agentId) continue;
    const heartbeat = firstValue(item, ["lastHeartbeatUtc", "lastHeartbeat"], null);
    const online = firstValue(item, ["isOnline", "online"], heartbeat ? now - new Date(heartbeat).getTime() <= 30000 : false);
    const row = document.createElement("tr");

    const selectionCell = document.createElement("td");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = item.isSelected;
    checkbox.setAttribute("aria-label", `选择 ${agentId}`);
    checkbox.addEventListener("change", () => {
      state.selection.setSelected(agentId, checkbox.checked);
      updateSelectionUi();
      invalidatePreview("发布目标已改变，请重新预览。", true);
    });
    selectionCell.append(checkbox);
    row.append(selectionCell);

    const statusCell = appendCell(row, online ? "在线" : "离线");
    const dot = document.createElement("span");
    dot.className = `dot${online ? " online" : ""}`;
    statusCell.prepend(dot);
    appendCell(row, firstValue(item, ["computerName"], "-"));
    appendCell(row, agentId);
    appendCell(row, firstValue(item, ["site"], "-"));
    appendCell(row, firstValue(item, ["agentVersion", "version"], "-"));
    appendCell(row, firstValue(item, ["pendingUploadCount", "queueDepth"], 0));
    appendCell(row, formatDate(heartbeat));

    const actionCell = document.createElement("td");
    for (const action of [
      { label: "启动", type: 0 },
      { label: "停止", type: 1 },
      { label: "刷新", type: 2 }
    ]) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "button ghost small";
      button.textContent = action.label;
      button.addEventListener("click", () => sendCommand(agentId, action.type, button));
      actionCell.append(button);
    }
    const diagnose = document.createElement("button");
    diagnose.type = "button";
    diagnose.className = "button secondary small";
    diagnose.textContent = "诊断";
    diagnose.addEventListener("click", () => {
      showView("diagnostics");
      byId("diagnosticAgent").value = agentId;
      loadDiagnostics().catch(error => setMessage(byId("diagnosticMessage"), error.message));
    });
    actionCell.append(diagnose);
    row.append(actionCell);
    rows.append(row);
  }
  byId("agentEmpty").hidden = state.agents.length !== 0;
  updateSelectionUi();
}

function updateSelectionUi() {
  const selected = state.selection.selectedIds();
  byId("selectedAgentCount").textContent = String(selected.length);
  byId("selectedAgentSummary").textContent = selected.length
    ? `已选择 ${selected.length} 台：${selected.join("、")}`
    : "尚未选择 Agent。请先在总览中明确勾选试点目标。";
}

async function sendCommand(agentId, type, button) {
  const original = button.textContent;
  button.disabled = true;
  try {
    await api(`/api/v1/agents/${encodeURIComponent(agentId)}/commands`, {
      method: "POST",
      body: { type, parametersJson: "{}", validForSeconds: 300 }
    });
    setGlobalMessage("命令任务已创建，等待 Agent 执行确认；这不代表命令已经成功。");
  } catch (error) {
    setGlobalMessage(error.message);
  } finally {
    button.textContent = original;
    button.disabled = false;
  }
}

function setLiveState(text, tone) {
  const live = byId("liveState");
  live.textContent = text;
  live.className = `status-pill ${tone}`;
}

async function connectSignalR() {
  if (!state.authenticated || state.socket?.readyState === WebSocket.OPEN || state.socket?.readyState === WebSocket.CONNECTING) return;
  clearTimeout(state.reconnectTimer);
  try {
    setLiveState("实时连接中", "neutral");
    const negotiate = await api("/hubs/fleet/negotiate?negotiateVersion=1", { method: "POST", body: undefined });
    const scheme = location.protocol === "https:" ? "wss" : "ws";
    const socket = new WebSocket(`${scheme}://${location.host}/hubs/fleet?id=${encodeURIComponent(negotiate.connectionToken)}`);
    state.socket = socket;
    socket.onopen = () => {
      socket.send(JSON.stringify({ protocol: "json", version: 1 }) + "\u001e");
      setLiveState("实时连接正常", "good");
    };
    socket.onmessage = event => {
      for (const frame of String(event.data).split("\u001e").filter(Boolean)) {
        try {
          const message = JSON.parse(frame);
          if (message.type === 1 && message.target === "fleetChanged") state.refreshThrottle.request();
        } catch { /* 忽略无法解析的 SignalR 帧。 */ }
      }
    };
    socket.onerror = () => socket.close();
    socket.onclose = () => {
      if (state.socket === socket) state.socket = null;
      setLiveState("轮询模式", "warning");
      if (state.authenticated) state.reconnectTimer = setTimeout(connectSignalR, 5000);
    };
  } catch {
    setLiveState("轮询模式", "warning");
    if (state.authenticated) state.reconnectTimer = setTimeout(connectSignalR, 5000);
  }
}

function appendDetail(container, label, value, source) {
  const wrapper = document.createElement("div");
  wrapper.className = "detail-row";
  const term = document.createElement("dt");
  term.textContent = label;
  const description = document.createElement("dd");
  description.textContent = value;
  const sourceNode = document.createElement("dd");
  sourceNode.className = "detail-source";
  sourceNode.textContent = source || "最终生效值";
  wrapper.append(term, description, sourceNode);
  container.append(wrapper);
}

function renderChecks(container, checks) {
  container.replaceChildren();
  for (const check of checks || []) {
    const status = String(firstValue(check, ["status", "state"], "unknown")).toLowerCase();
    const tone = /pass|ok|healthy|success|true/.test(status) ? "good" : /warn|unknown|pending/.test(status) ? "warning" : "danger";
    const item = document.createElement("div");
    item.className = `check-item ${tone}`;
    const marker = document.createElement("span");
    marker.textContent = tone === "good" ? "✓" : tone === "warning" ? "!" : "×";
    const text = document.createElement("span");
    text.textContent = firstValue(check, ["message", "summary", "label", "name"], String(check));
    item.append(marker, text);
    container.append(item);
  }
}

async function loadEffectiveConfiguration() {
  const message = byId("effectiveConfigMessage");
  setMessage(message, "正在读取最终配置…", "warning");
  try {
    const data = await api("/api/v1/system/effective-configuration");
    const container = byId("effectiveConfig");
    container.replaceChildren();
    const sources = data.sources || {};
    const database = data.database || {};
    const fields = [
      ["运行模式", "runMode", data.runMode],
      ["监听地址", "bindAddress", data.bindAddress],
      ["监听端口", "port", data.port],
      ["Agent 使用地址", "advertisedBaseUrl", data.advertisedBaseUrl],
      ["要求 HTTPS", "requireHttps", data.requireHttps],
      ["内网模式", "internalLanEnabled", data.internalLanEnabled],
      ["中心数据库", "database", [database.server, database.database].filter(Boolean).join(" / ") || "未配置"],
      ["数据库认证", "databaseAuthentication", database.authentication],
      ["数据库加密", "databaseEncryption", database.encryption],
      ["部署管理员访问码", "deploymentAdminAccessCode", data.deploymentAdminAccessCode],
      ["兼容注册码", "centerSharedCompatibilityKey", data.centerSharedCompatibilityKey]
    ];
    for (const [label, key, value] of fields) appendDetail(container, label, publicValue(key, value), sources[key] || sources[label]);
    const checks = Array.isArray(data.checks) ? data.checks : [
      { status: database.isValid ? "pass" : "fail", message: database.isValid ? "中心数据库配置有效。" : "中心数据库配置未通过校验。" },
      { status: data.deploymentAdminAccessCode?.isConfigured ? "pass" : "fail", message: data.deploymentAdminAccessCode?.isConfigured ? "部署管理员访问码已配置。" : "部署管理员访问码未配置。" },
      { status: data.requireHttps ? "pass" : "warning", message: data.requireHttps ? "管理与 Agent 通信要求 HTTPS。" : "当前未强制 HTTPS，仅适用于受控隔离内网。" }
    ];
    renderChecks(byId("effectiveChecks"), checks);
    setMessage(message, `读取时间：${new Date().toLocaleString()}`, "success");
  } catch (error) {
    setMessage(message, error.message);
  }
}

function deploymentRequest() {
  return uiLogic.buildDeploymentRequest({
    agentId: byId("deploymentAgentId").value.trim(),
    site: byId("deploymentSite").value.trim(),
    building: byId("deploymentBuilding").value.trim(),
    line: byId("deploymentLine").value.trim(),
    acquisitionDirectory: byId("acquisitionDirectory").value.trim(),
    autoStart: false
  });
}

function fileNameFromResponse(response, fallback) {
  const disposition = response.headers.get("Content-Disposition") || "";
  const encoded = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  if (encoded) {
    try { return decodeURIComponent(encoded); } catch { return fallback; }
  }
  const plain = disposition.match(/filename="?([^";]+)"?/i)?.[1];
  return plain || fallback;
}

function downloadBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

async function generateDeploymentPackage(event) {
  event.preventDefault();
  const payload = deploymentRequest();
  const message = byId("deploymentMessage");
  if (!/^[a-zA-Z0-9][a-zA-Z0-9._-]{1,99}$/.test(payload.agentId)) {
    setMessage(message, "设备编号只能包含字母、数字、点、短横线或下划线，长度 2～100。 ");
    return;
  }
  if (!payload.site) { setMessage(message, "厂区不能为空。"); return; }
  if (!uiLogic.isAbsoluteWindowsPath(payload.acquisitionDirectory)) {
    setMessage(message, "采集程序目录必须是目标电脑上的 Windows 绝对路径。");
    return;
  }
  const button = byId("generatePackage");
  button.disabled = true;
  setMessage(message, "正在生成自包含设备包…", "warning");
  try {
    const result = await api("/api/v1/deployments/package", { method: "POST", body: payload, responseType: "blob" });
    downloadBlob(result.blob, fileNameFromResponse(result.response, `${payload.agentId}-AgentSetup.zip`));
    setMessage(message, "设备包已生成并开始下载。安装是否成功仍需等待目标机首次心跳。", "success");
    await loadDeployments();
  } catch (error) {
    setMessage(message, error.message);
  } finally {
    button.disabled = false;
  }
}

async function loadDeployments() {
  try {
    const response = await api("/api/v1/deployments");
    state.deployments = asArray(response);
    const container = byId("deploymentRows");
    container.replaceChildren();
    for (const item of state.deployments) {
      const row = document.createElement("tr");
      appendCell(row, firstValue(item, ["agentId", "deviceId", "id"], "-"));
      appendCell(row, firstValue(item, ["site", "sitePath"], "-"));
      appendCell(row, firstValue(item, ["status", "state"], "已生成"));
      appendCell(row, formatDate(firstValue(item, ["createdAtUtc", "createdAt"], null)));
      appendCell(row, formatDate(firstValue(item, ["firstHeartbeatUtc", "enrolledAtUtc"], null)));
      container.append(row);
    }
    byId("deploymentEmpty").hidden = state.deployments.length !== 0;
  } catch (error) {
    setMessage(byId("deploymentMessage"), error.message);
  }
}

function addMachineRow(machine) {
  const currentIds = readMachineRows().map(item => Number(item.id));
  const nextId = [1, 2, 3, 4, 5, 6].find(id => !currentIds.includes(id));
  if (!machine && nextId === undefined) {
    setMessage(byId("machineValidationMessage"), "当前运行时最多支持 6 台机台。");
    return;
  }
  const value = machine || { id: nextId, name: `机台 ${nextId}`, monitorPath: "", successPath: "", errorPath: "" };
  const fragment = byId("machineRowTemplate").content.cloneNode(true);
  const row = fragment.querySelector(".machine-row");
  for (const input of row.querySelectorAll("[data-machine-field]")) input.value = value[input.dataset.machineField] ?? "";
  row.querySelector(".remove-machine").addEventListener("click", () => {
    row.remove();
    updateGeneratedPayload();
    invalidatePreview("机台配置已改变，请重新预览。", true);
  });
  row.addEventListener("input", () => {
    updateGeneratedPayload();
    invalidatePreview("机台配置已改变，请重新预览。", true);
  });
  byId("machineRows").append(row);
  updateGeneratedPayload();
}

function readMachineRows() {
  return Array.from(byId("machineRows").querySelectorAll(".machine-row")).map(row => {
    const result = {};
    for (const input of row.querySelectorAll("[data-machine-field]")) result[input.dataset.machineField] = input.value;
    return result;
  });
}

function updateGeneratedPayload() {
  const result = uiLogic.validateAndNormalizeMachines(readMachineRows());
  byId("configPayload").value = uiLogic.toPayloadJson(result.machines);
  setMessage(byId("machineValidationMessage"), result.errors.length ? result.errors[0].message : "", result.errors.length ? undefined : "success");
  return result;
}

function currentConfigFingerprint() {
  const result = uiLogic.validateAndNormalizeMachines(readMachineRows());
  return uiLogic.createPreviewFingerprint(result.machines, state.selection.selectedIds());
}

function invalidatePreview(message, showMessage) {
  const hadPreview = Boolean(state.previewFingerprint);
  state.preview = null;
  state.previewRequest = null;
  state.previewFingerprint = "";
  byId("publishConfig").disabled = true;
  byId("previewState").textContent = "预览已失效";
  byId("previewState").className = "status-pill warning";
  if (hadPreview) byId("previewCard").hidden = true;
  if (showMessage && hadPreview) setMessage(byId("configMessage"), message, "warning");
}

function buildConfigRequest() {
  const validation = uiLogic.validateAndNormalizeMachines(readMachineRows());
  const agentIds = state.selection.selectedIds();
  const name = byId("configName").value.trim();
  const minimumAgentVersion = byId("minimumAgentVersion").value.trim();
  if (validation.errors.length) throw new Error(validation.errors[0].message);
  if (!name) throw new Error("配置名称不能为空。");
  if (!agentIds.length) throw new Error("请先明确勾选至少一个 Agent。");
  return {
    name,
    minimumAgentVersion,
    agentIds,
    machines: validation.machines,
    payloadJson: uiLogic.toPayloadJson(validation.machines)
  };
}

function appendPreviewBlock(container, title, detail) {
  const block = document.createElement("article");
  block.className = "preview-agent";
  const heading = document.createElement("h3");
  heading.textContent = title;
  block.append(heading);
  if (Array.isArray(detail)) {
    const list = document.createElement("ul");
    for (const item of detail) {
      const entry = document.createElement("li");
      entry.textContent = typeof item === "string" ? item : JSON.stringify(maskSensitiveData(item));
      list.append(entry);
    }
    block.append(list);
  } else {
    const pre = document.createElement("pre");
    pre.textContent = typeof detail === "string" ? detail : JSON.stringify(maskSensitiveData(detail), null, 2);
    block.append(pre);
  }
  container.append(block);
}

function renderPreview(data) {
  const container = byId("previewContent");
  container.replaceChildren();
  const differences = asArray(data?.agentDiffs || data?.diffs || data?.targets);
  if (differences.length) {
    for (const difference of differences) {
      const agentId = firstValue(difference, ["agentId", "id", "target"], "Agent");
      const detail = firstValue(difference, ["changes", "differences", "diff", "summary"], difference);
      appendPreviewBlock(container, String(agentId), detail);
    }
  } else {
    appendPreviewBlock(container, "服务端预览结果", data || { message: "服务端未返回差异明细。" });
  }
  byId("previewCard").hidden = false;
}

async function previewConfig() {
  const button = byId("previewConfig");
  button.disabled = true;
  try {
    const request = buildConfigRequest();
    setMessage(byId("configMessage"), "正在生成逐 Agent 差异…", "warning");
    const result = await api("/api/v1/config-versions/preview", { method: "POST", body: request });
    state.preview = result || {};
    state.previewRequest = request;
    state.previewFingerprint = uiLogic.createPreviewFingerprint(request.machines, request.agentIds);
    renderPreview(result);
    byId("previewState").textContent = "预览有效";
    byId("previewState").className = "status-pill good";
    byId("publishConfig").disabled = false;
    setMessage(byId("configMessage"), "预览已生成。请核对后创建发布任务。", "success");
  } catch (error) {
    invalidatePreview();
    setMessage(byId("configMessage"), error.message);
  } finally {
    button.disabled = false;
  }
}

async function publishConfig() {
  const currentFingerprint = currentConfigFingerprint();
  if (!uiLogic.isPreviewCurrent(state.previewFingerprint, currentFingerprint) || !state.previewRequest) {
    invalidatePreview("表单或发布目标已改变，请重新预览。", true);
    return;
  }
  const button = byId("publishConfig");
  button.disabled = true;
  try {
    const request = uiLogic.withRequestId({
      ...state.previewRequest,
      previewToken: firstValue(state.preview, ["previewToken", "token", "id"], null),
      previewSha256: firstValue(state.preview, ["previewSha256", "sha256", "payloadSha256"], null),
      baseVersion: firstValue(state.preview, ["baseVersion", "expectedBaseVersion"], null)
    });
    const result = await api("/api/v1/config-versions", { method: "POST", body: request });
    setMessage(byId("configMessage"), "发布任务已创建，正在等待各 Agent 确认；当前尚不能视为已生效。", "success");
    invalidatePreview();
    await loadConfigVersions();
    const id = String(firstValue(result, ["id", "configVersionId"], ""));
    if (id) await loadAssignments(id);
  } catch (error) {
    setMessage(byId("configMessage"), error.message);
  }
}

async function loadConfigVersions() {
  const response = await api("/api/v1/config-versions");
  state.configVersions = asArray(response);
  renderConfigVersions();
}

function renderConfigVersions() {
  const container = byId("configVersions");
  container.replaceChildren();
  for (const version of state.configVersions) {
    const id = String(firstValue(version, ["id", "configVersionId"], ""));
    const number = firstValue(version, ["version", "versionNumber"], "-");
    const item = document.createElement("article");
    item.className = `version-item${id === state.activeConfigVersionId ? " is-active" : ""}`;
    const main = document.createElement("div");
    main.className = "item-main";
    const title = document.createElement("strong");
    title.textContent = `v${number} · ${firstValue(version, ["name"], "未命名配置")}`;
    const meta = document.createElement("small");
    meta.textContent = `${formatDate(firstValue(version, ["createdAtUtc", "createdAt"], null))} · ${firstValue(version, ["createdBy"], "-")}`;
    main.append(title, meta);
    const actions = document.createElement("div");
    actions.className = "item-actions";
    const results = document.createElement("button");
    results.type = "button";
    results.className = "button secondary small";
    results.textContent = "查看结果";
    results.addEventListener("click", () => loadAssignments(id).catch(error => setMessage(byId("configMessage"), error.message)));
    const rollback = document.createElement("button");
    rollback.type = "button";
    rollback.className = "button danger ghost small";
    rollback.textContent = "恢复此版本";
    rollback.addEventListener("click", () => rollbackVersion(id));
    actions.append(results, rollback);
    item.append(main, actions);
    container.append(item);
  }
  byId("configVersionsEmpty").hidden = state.configVersions.length !== 0;
}

async function loadAssignments(versionId, scheduleNext = true) {
  clearTimeout(state.assignmentTimer);
  state.activeConfigVersionId = String(versionId);
  renderConfigVersions();
  const response = await api(`/api/v1/config-versions/${encodeURIComponent(versionId)}/assignments`);
  const assignments = asArray(response);
  const container = byId("assignmentResults");
  container.replaceChildren();
  let hasPending = false;
  for (const assignment of assignments) {
    const presentation = uiLogic.assignmentPresentation(firstValue(assignment, ["state", "status"], null));
    hasPending ||= !presentation.terminal;
    const item = document.createElement("article");
    item.className = "assignment-item";
    const main = document.createElement("div");
    main.className = "item-main";
    const agent = document.createElement("strong");
    agent.textContent = firstValue(assignment, ["agentId", "targetId"], "未知 Agent");
    const detail = document.createElement("small");
    detail.textContent = `${presentation.label} · ${formatDate(firstValue(assignment, ["acknowledgedAtUtc", "updatedAtUtc", "createdAtUtc"], null))}`;
    main.append(agent, detail);
    const badge = document.createElement("span");
    badge.className = `status-pill ${presentation.key === "Applied" ? "good" : presentation.key === "Pending" ? "neutral" : presentation.key === "RolledBack" ? "warning" : "danger"}`;
    badge.textContent = presentation.label;
    item.append(main, badge);
    const message = firstValue(assignment, ["message", "detail"], "");
    if (message) {
      const note = document.createElement("p");
      note.className = "muted";
      note.textContent = String(message);
      item.append(note);
    }
    container.append(item);
  }
  byId("assignmentEmpty").hidden = assignments.length !== 0;
  if (assignments.length === 0) byId("assignmentEmpty").textContent = "该版本尚无 Agent 分配记录。";
  if (hasPending && scheduleNext) state.assignmentTimer = setTimeout(() => loadAssignments(versionId, true).catch(() => {}), 3000);
}

async function rollbackVersion(versionId) {
  const agentIds = state.selection.selectedIds();
  if (!agentIds.length) {
    setMessage(byId("configMessage"), "请先在总览明确勾选要恢复的 Agent。");
    return;
  }
  if (!window.confirm(`将以历史版本创建新的恢复任务，目标 ${agentIds.length} 台。是否继续？`)) return;
  try {
    const result = await api(`/api/v1/config-versions/${encodeURIComponent(versionId)}/rollback`, {
      method: "POST", body: uiLogic.withRequestId({ agentIds })
    });
    setMessage(byId("configMessage"), "恢复任务已创建；只有 Agent 返回 Applied 后，恢复配置才算生效。", "success");
    await loadConfigVersions();
    const newId = String(firstValue(result, ["id", "configVersionId"], ""));
    if (newId) await loadAssignments(newId);
  } catch (error) {
    setMessage(byId("configMessage"), error.message);
  }
}

function updateDiagnosticAgentOptions() {
  const select = byId("diagnosticAgent");
  const previous = select.value;
  const options = [document.createElement("option")];
  options[0].value = "";
  options[0].textContent = "请选择";
  const agents = [...state.agents].sort((left, right) => String(firstValue(left, ["id", "agentId"], "")).localeCompare(String(firstValue(right, ["id", "agentId"], ""))));
  for (const agent of agents) {
    const id = String(firstValue(agent, ["id", "agentId"], ""));
    if (!id) continue;
    const option = document.createElement("option");
    option.value = id;
    option.textContent = `${id} · ${firstValue(agent, ["computerName"], "未知电脑")}`;
    options.push(option);
  }
  select.replaceChildren(...options);
  if (agents.some(agent => String(firstValue(agent, ["id", "agentId"], "")) === previous)) select.value = previous;
}

function flattenDiagnosticData(source, prefix, depth, output) {
  if (!source || typeof source !== "object" || depth > 2) return;
  for (const [key, value] of Object.entries(source)) {
    if (["checks", "recentErrors", "logs"].includes(key)) continue;
    const label = prefix ? `${prefix}.${key}` : key;
    if (value && typeof value === "object" && !Array.isArray(value) && !isSensitiveKey(key)) flattenDiagnosticData(value, label, depth + 1, output);
    else output.push([label, publicValue(key, value)]);
  }
}

async function loadDiagnostics() {
  const agentId = byId("diagnosticAgent").value;
  const message = byId("diagnosticMessage");
  if (!agentId) { setMessage(message, "请先选择 Agent。"); return; }
  setMessage(message, "正在读取诊断快照…", "warning");
  try {
    const data = await api(`/api/v1/agents/${encodeURIComponent(agentId)}/diagnostics`);
    const observedAt = firstValue(data, ["observedAtUtc", "timestampUtc", "generatedAtUtc"], null);
    byId("diagnosticObservedAt").textContent = observedAt ? `观察时间：${formatDate(observedAt)}` : "观察时间：服务端未提供";
    const details = [];
    flattenDiagnosticData(data, "", 0, details);
    const container = byId("diagnosticDetails");
    container.replaceChildren();
    for (const [label, value] of details) appendDetail(container, label, value, "诊断快照");
    renderChecks(byId("diagnosticChecks"), data.checks || []);
    byId("exportDiagnostics").disabled = false;
    setMessage(message, "诊断快照已读取；未知项未按正常处理。", "success");
  } catch (error) {
    byId("exportDiagnostics").disabled = true;
    setMessage(message, error.message);
  }
}

async function exportDiagnostics() {
  const agentId = byId("diagnosticAgent").value;
  if (!agentId) return;
  const button = byId("exportDiagnostics");
  button.disabled = true;
  try {
    const result = await api(`/api/v1/agents/${encodeURIComponent(agentId)}/diagnostics/export`, { responseType: "blob" });
    downloadBlob(result.blob, fileNameFromResponse(result.response, `${agentId}-diagnostics.zip`));
    setMessage(byId("diagnosticMessage"), "脱敏诊断文件已开始下载。", "success");
  } catch (error) {
    setMessage(byId("diagnosticMessage"), error.message);
  } finally {
    button.disabled = false;
  }
}

function wireEvents() {
  byId("loginForm").addEventListener("submit", login);
  byId("logoutButton").addEventListener("click", logout);
  document.querySelectorAll("[data-view]").forEach(button => button.addEventListener("click", () => showView(button.dataset.view)));
  document.querySelectorAll("[data-go-view]").forEach(button => button.addEventListener("click", () => showView(button.dataset.goView)));
  byId("refreshDashboard").addEventListener("click", () => refreshFleet().catch(error => setGlobalMessage(error.message)));
  byId("searchButton").addEventListener("click", () => refreshFleet().catch(error => setGlobalMessage(error.message)));
  byId("search").addEventListener("keydown", event => {
    if (event.key === "Enter") { event.preventDefault(); refreshFleet().catch(error => setGlobalMessage(error.message)); }
  });
  byId("clearSelection").addEventListener("click", () => {
    state.selection.clear();
    renderAgents();
    invalidatePreview("发布目标已清空，请重新选择并预览。", true);
  });
  byId("refreshEffectiveConfig").addEventListener("click", loadEffectiveConfiguration);
  byId("deploymentForm").addEventListener("submit", generateDeploymentPackage);
  byId("addMachine").addEventListener("click", () => addMachineRow());
  byId("configName").addEventListener("input", () => invalidatePreview("配置名称已改变，请重新预览。", true));
  byId("minimumAgentVersion").addEventListener("input", () => invalidatePreview("最低版本已改变，请重新预览。", true));
  byId("previewConfig").addEventListener("click", previewConfig);
  byId("publishConfig").addEventListener("click", publishConfig);
  byId("loadDiagnostics").addEventListener("click", () => loadDiagnostics().catch(error => setMessage(byId("diagnosticMessage"), error.message)));
  byId("diagnosticAgent").addEventListener("change", () => {
    byId("exportDiagnostics").disabled = true;
    byId("diagnosticDetails").replaceChildren();
    byId("diagnosticChecks").replaceChildren();
    byId("diagnosticObservedAt").textContent = "尚未读取。";
  });
  byId("exportDiagnostics").addEventListener("click", exportDiagnostics);
}

async function initialize() {
  wireEvents();
  addMachineRow({ id: 1, name: "机台 1", monitorPath: "", successPath: "", errorPath: "" });
  updateSelectionUi();
  byId("previewState").textContent = "尚未预览";
  try { await ensureCsrf(); }
  catch { state.csrf = ""; }
  await checkOperatorStatus();
}

initialize();
